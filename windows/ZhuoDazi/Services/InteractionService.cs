using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using ZhuoDazi.Models;

namespace ZhuoDazi.Services;

public sealed class InteractionService : IDisposable
{
    private const string ProfileUrl = LicenseService.ServiceBaseUrl + "/api/interactions/profile";
    private const string EventsUrl = LicenseService.ServiceBaseUrl + "/api/interactions/events";
    private const string BatchUrl = LicenseService.ServiceBaseUrl + "/api/content/batch";
    private const string OfflinePackUrl = LicenseService.ServiceBaseUrl + "/api/content/offline-pack";
    private const int MaximumResponseBytes = 20 * 1024 * 1024;
    private const int TargetCacheSize = 60;
    private const int EventBatchSize = 50;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly object _sync = new();
    private readonly LicenseService _licenses;
    private readonly InteractionCacheStore _cacheStore;
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _networkGate = new(1, 1);
    private InteractionCacheDocument _state;
    private int _profileRevision;
    private bool _disposed;

    public InteractionService(SettingsStore store, LicenseService licenses)
        : this(store, licenses, new SocketsHttpHandler
        {
            UseProxy = false,
            ConnectTimeout = TimeSpan.FromSeconds(20)
        })
    {
    }

    internal InteractionService(SettingsStore store, LicenseService licenses, HttpMessageHandler handler)
    {
        _licenses = licenses;
        _cacheStore = new InteractionCacheStore(store.InteractionsPath);
        _state = _cacheStore.Load();
        _httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(35),
            DefaultRequestVersion = HttpVersion.Version20,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower
        };
    }

    public int CachedContentCount
    {
        get { lock (_sync) return _state.Items.Count; }
    }

    public int PendingEventCount
    {
        get { lock (_sync) return _state.PendingEvents.Count; }
    }

    public bool ShouldFlush => PendingEventCount >= 10;

    public string StatusSummary
    {
        get
        {
            lock (_sync)
            {
                var catalog = _state.CatalogVersion == 0 ? "尚未同步" : $"目录 v{_state.CatalogVersion}";
                return $"{catalog} · {_state.Items.Count} 条可用 · {_state.PendingEvents.Count} 条待上传";
            }
        }
    }

    public bool IsMoodPromptDue(DateTimeOffset now)
    {
        lock (_sync)
        {
            return !DateTimeOffset.TryParse(_state.NextMoodPromptAt, out var next) || now >= next;
        }
    }

    public void MarkMoodPrompted(DateTimeOffset now)
    {
        lock (_sync)
        {
            _state.NextMoodPromptAt = (now + InteractionScheduler.NextMoodCooldown()).ToString("O");
            _state.LastPromptType = "mood";
            SaveLocked();
        }
    }

    public InteractionContentItem? TakeNextContent()
    {
        lock (_sync)
        {
            if (_state.Items.Count == 0) return null;
            var candidates = _state.Items
                .Where(item => !item.Type.Equals(_state.LastPromptType, StringComparison.Ordinal))
                .ToList();
            if (candidates.Count == 0) candidates = [.. _state.Items];
            var selected = candidates[RandomNumberGenerator.GetInt32(candidates.Count)];
            _state.Items.RemoveAll(item => item.Id.Equals(selected.Id, StringComparison.Ordinal));
            _state.Shown.RemoveAll(item => item.Id.Equals(selected.Id, StringComparison.Ordinal));
            _state.Shown.Insert(0, new InteractionShownContent
            {
                Id = selected.Id,
                ShownAt = DateTimeOffset.UtcNow.ToString("O")
            });
            _state.LastPromptType = selected.Type;
            AddEventLocked(new InteractionEventRecord
            {
                Type = "content_shown",
                ContentId = selected.Id
            });
            SaveLocked();
            return selected;
        }
    }

    public void RecordMood(string mood)
    {
        if (mood is not ("happy" or "okay" or "low")) return;
        lock (_sync)
        {
            AddEventLocked(new InteractionEventRecord { Type = "mood_response", Mood = mood });
            SaveLocked();
        }
    }

    public void RecordJoke(string contentId)
    {
        if (!ContentEnvelopeVerifier.IsValidItemId(contentId)) return;
        lock (_sync)
        {
            AddEventLocked(new InteractionEventRecord { Type = "joke_revealed", ContentId = contentId });
            SaveLocked();
        }
    }

    public void RecordQuiz(string contentId, bool correct)
    {
        if (!ContentEnvelopeVerifier.IsValidItemId(contentId)) return;
        lock (_sync)
        {
            AddEventLocked(new InteractionEventRecord
            {
                Type = "quiz_answered",
                ContentId = contentId,
                Correct = correct
            });
            SaveLocked();
        }
    }

    public void MarkProfileDirty(string mode, bool promptsEnabled)
    {
        lock (_sync)
        {
            _state.InteractionMode = NormalizeMode(mode);
            _state.PromptsEnabled = promptsEnabled;
            _state.ProfileDirty = true;
            _profileRevision += 1;
            SaveLocked();
        }
    }

    internal async Task<InteractionProfile> SyncProfileAsync(
        string localMode,
        bool localPromptsEnabled,
        CancellationToken cancellationToken = default)
    {
        await _networkGate.WaitAsync(cancellationToken);
        try
        {
            while (true)
            {
                bool dirty;
                int revision;
                string mode;
                bool promptsEnabled;
                lock (_sync)
                {
                    dirty = _state.ProfileDirty;
                    revision = _profileRevision;
                    mode = dirty ? _state.InteractionMode : NormalizeMode(localMode);
                    promptsEnabled = dirty ? _state.PromptsEnabled : localPromptsEnabled;
                }
                using var request = dirty
                    ? JsonRequest(HttpMethod.Patch, ProfileUrl, new
                    {
                        mode,
                        promptsEnabled
                    })
                    : new HttpRequestMessage(HttpMethod.Get, ProfileUrl);
                Authorize(request);
                var response = await SendAsync<InteractionProfileEnvelope>(request, cancellationToken);
                var profile = response.Profile
                    ?? throw new InvalidOperationException("互动设置响应无效。");
                profile.Mode = NormalizeMode(profile.Mode);
                lock (_sync)
                {
                    if (_profileRevision != revision) continue;
                    _state.InteractionMode = profile.Mode;
                    _state.PromptsEnabled = profile.PromptsEnabled;
                    _state.ProfileDirty = false;
                    SaveLocked();
                    return profile;
                }
            }
        }
        finally
        {
            _networkGate.Release();
        }
    }

    public async Task<int> RefillAsync(CancellationToken cancellationToken = default)
    {
        await _networkGate.WaitAsync(cancellationToken);
        try
        {
            var totalAdded = 0;
            for (var attempt = 0; attempt < 2; attempt++)
            {
                string[] exclusions;
                int cachedCount;
                lock (_sync)
                {
                    cachedCount = _state.Items.Count;
                    exclusions = BuildExclusionsLocked();
                }
                if (cachedCount >= TargetCacheSize) break;
                using var request = JsonRequest(HttpMethod.Post, BatchUrl, new
                {
                    types = new[] { "joke", "math", "trivia" },
                    limit = 30,
                    excludeIds = exclusions
                });
                Authorize(request);
                var envelope = await SendAsync<SignedContentEnvelope>(request, cancellationToken);
                var payload = ContentEnvelopeVerifier.Validate(envelope, "batch");
                var added = ApplyPayload(payload, replace: false);
                totalAdded += added;
                if (added == 0) break;
            }
            return totalAdded;
        }
        finally
        {
            _networkGate.Release();
        }
    }

    public async Task<int> DownloadOfflinePackAsync(CancellationToken cancellationToken = default)
    {
        await _networkGate.WaitAsync(cancellationToken);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, OfflinePackUrl);
            Authorize(request);
            var envelope = await SendAsync<SignedContentEnvelope>(request, cancellationToken);
            var payload = ContentEnvelopeVerifier.Validate(envelope, "offline-pack");
            ApplyPayload(payload, replace: true);
            return CachedContentCount;
        }
        finally
        {
            _networkGate.Release();
        }
    }

    public async Task FlushEventsAsync(CancellationToken cancellationToken = default)
    {
        await _networkGate.WaitAsync(cancellationToken);
        try
        {
            while (true)
            {
                InteractionEventRecord[] batch;
                lock (_sync) batch = _state.PendingEvents.Take(EventBatchSize).ToArray();
                if (batch.Length == 0) return;
                using var request = JsonRequest(HttpMethod.Post, EventsUrl, new { events = batch });
                Authorize(request);
                _ = await SendAsync<JsonElement>(request, cancellationToken);
                var sentIds = batch.Select(item => item.EventId).ToHashSet(StringComparer.OrdinalIgnoreCase);
                lock (_sync)
                {
                    _state.PendingEvents.RemoveAll(item => sentIds.Contains(item.EventId));
                    SaveLocked();
                }
            }
        }
        finally
        {
            _networkGate.Release();
        }
    }

    private int ApplyPayload(SignedContentPayload payload, bool replace)
    {
        lock (_sync)
        {
            if (payload.CatalogVersion < _state.CatalogVersion) return 0;
            var disabled = payload.DisabledIds.ToHashSet(StringComparer.Ordinal);
            var shown = _state.Shown.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
            var existing = replace
                ? new Dictionary<string, InteractionContentItem>(StringComparer.Ordinal)
                : _state.Items.Where(item => !disabled.Contains(item.Id))
                    .ToDictionary(item => item.Id, StringComparer.Ordinal);
            var added = 0;
            foreach (var item in payload.Items)
            {
                if (disabled.Contains(item.Id) || shown.Contains(item.Id)) continue;
                if (!existing.TryGetValue(item.Id, out var current))
                {
                    existing[item.Id] = item;
                    added += 1;
                }
                else if (item.Revision > current.Revision)
                {
                    existing[item.Id] = item;
                }
            }
            _state.Items = existing.Values.OrderBy(item => item.Type, StringComparer.Ordinal)
                .ThenBy(item => item.Id, StringComparer.Ordinal).ToList();
            _state.CatalogVersion = payload.CatalogVersion;
            _state.CatalogUpdatedAt = payload.CatalogUpdatedAt;
            SaveLocked();
            return added;
        }
    }

    private string[] BuildExclusionsLocked()
    {
        return _state.Items.Select(item => item.Id)
            .Concat(_state.Shown.Select(item => item.Id))
            .Distinct(StringComparer.Ordinal)
            .Take(500)
            .ToArray();
    }

    private void AddEventLocked(InteractionEventRecord item)
    {
        _state.PendingEvents.Add(item);
        if (_state.PendingEvents.Count > 1000)
            _state.PendingEvents.RemoveRange(0, _state.PendingEvents.Count - 1000);
    }

    private void SaveLocked() => _cacheStore.Save(_state);

    private static string NormalizeMode(string mode)
        => mode is "quiet" or "standard" or "lively" ? mode : "standard";

    private void Authorize(HttpRequestMessage request)
    {
        _licenses.Authorize(request);
        request.Headers.TryAddWithoutValidation("X-DeskPet-Platform", "windows");
    }

    private static HttpRequestMessage JsonRequest(HttpMethod method, string url, object body)
    {
        var request = new HttpRequestMessage(method, url)
        {
            Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(body, JsonOptions))
        };
        request.Content.Headers.ContentType = new("application/json") { CharSet = "utf-8" };
        return request;
    }

    private async Task<T> SendAsync<T>(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
        }
        catch (Exception error)
        {
            var message = NetworkConnectionErrors.Format(error, "连接互动服务超时，请稍后重试。");
            throw new InvalidOperationException(message, error);
        }

        using (response)
        {
            if (response.Content.Headers.ContentLength is > MaximumResponseBytes)
                throw new InvalidOperationException("互动服务响应过大。");
            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            if (bytes.Length > MaximumResponseBytes)
                throw new InvalidOperationException("互动服务响应过大。");
            if (!response.IsSuccessStatusCode)
            {
                var fallback = response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                    ? "设备绑定已失效，请重新绑定。"
                    : "互动请求失败，请稍后重试。";
                throw new InvalidOperationException(TryReadError(bytes) ?? fallback);
            }
            try
            {
                return JsonSerializer.Deserialize<T>(bytes, JsonOptions)
                    ?? throw new JsonException();
            }
            catch (JsonException error)
            {
                throw new InvalidOperationException("互动服务返回的数据无效。", error);
            }
        }
    }

    private static string? TryReadError(byte[] bytes)
    {
        try { return JsonSerializer.Deserialize<InteractionErrorResponse>(bytes, JsonOptions)?.Message; }
        catch { return null; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _httpClient.Dispose();
        _networkGate.Dispose();
    }
}
