using System.Net;
using System.Net.Http;
using System.Text.Json;
using ZhuoDazi.Models;

namespace ZhuoDazi.Services;

public sealed class InteractionService : IDisposable
{
    private const string ProfileUrl = DeskPetApi.InteractionProfile;
    private const string EventsUrl = DeskPetApi.InteractionEvents;
    private const string BatchUrl = DeskPetApi.ContentBatch;
    private const int MaximumResponseBytes = 20 * 1024 * 1024;
    private const int EventBatchSize = 50;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly object _sync = new();
    private readonly LicenseService _licenses;
    private readonly InteractionCacheStore _cacheStore;
    private readonly HttpClient _httpClient;
    private readonly byte[]? _signingPublicKey;
    private readonly SemaphoreSlim _networkGate = new(1, 1);
    private readonly SemaphoreSlim _contentGate = new(1, 1);
    private InteractionCacheDocument _state;
    private int _profileRevision;
    private bool _disposed;

    public InteractionService(SettingsStore store, LicenseService licenses)
        : this(store, licenses, DeskPetHttp.HandlerFactory?.Invoke() ?? DeskPetHttp.CreateHandler())
    {
    }

    internal InteractionService(SettingsStore store, LicenseService licenses, HttpMessageHandler handler,
        byte[]? signingPublicKey = null)
    {
        _licenses = licenses;
        _signingPublicKey = signingPublicKey?.ToArray();
        _cacheStore = new InteractionCacheStore(store.InteractionsPath);
        _state = _cacheStore.Load();
        _httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(35),
            DefaultRequestVersion = HttpVersion.Version20,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower
        };
    }

    public int PendingEventCount
    {
        get { lock (_sync) return _state.PendingEvents.Count; }
    }

    public bool ShouldFlush => PendingEventCount >= 10;

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

    public void RecordContentShown(InteractionContentItem item)
    {
        if (!ContentEnvelopeVerifier.IsValidItem(item)) return;
        lock (_sync)
        {
            _state.Shown.RemoveAll(shown => shown.Id.Equals(item.Id, StringComparison.Ordinal));
            _state.Shown.Insert(0, new InteractionShownContent
            {
                Id = item.Id,
                ShownAt = DateTimeOffset.UtcNow.ToString("O")
            });
            _state.LastPromptType = item.Type;
            AddEventLocked(new InteractionEventRecord
            {
                Type = "content_shown",
                ContentId = item.Id
            });
            SaveLocked();
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

    public async Task<InteractionContentItem?> FetchNextContentAsync(
        IReadOnlyCollection<string>? types = null, CancellationToken cancellationToken = default)
    {
        // A fresh question must not wait for background profile/event uploads to drain.
        await _contentGate.WaitAsync(cancellationToken);
        try
        {
            var requestedTypes = (types ?? new[] { "joke", "math", "trivia", "riddle", "tip", "care" })
                .Distinct(StringComparer.Ordinal).ToArray();
            Random.Shared.Shuffle(requestedTypes);
            string[] exclusions;
            lock (_sync)
            {
                requestedTypes = requestedTypes.OrderBy(type => type == _state.LastPromptType ? 1 : 0).ToArray();
                exclusions = BuildExclusionsLocked();
            }
            using var request = JsonRequest(HttpMethod.Post, BatchUrl, new
            {
                types = requestedTypes,
                limit = 1,
                excludeIds = exclusions
            });
            request.Headers.CacheControl = new() { NoCache = true, NoStore = true };
            Authorize(request);
            var envelope = await SendAsync<SignedContentEnvelope>(request, cancellationToken);
            var payload = ContentEnvelopeVerifier.Validate(envelope, "batch", _signingPublicKey);
            lock (_sync)
            {
                if (payload.CatalogVersion < _state.CatalogVersion || payload.Items.Count > 1)
                    throw new InvalidOperationException("互动内容响应无效，请稍后重试。");
                _state.CatalogVersion = payload.CatalogVersion;
                _state.CatalogUpdatedAt = payload.CatalogUpdatedAt;
                SaveLocked();
                var item = payload.Items.SingleOrDefault();
                return item is not null
                    && requestedTypes.Contains(item.Type, StringComparer.Ordinal)
                    && !payload.DisabledIds.Contains(item.Id, StringComparer.Ordinal)
                    && !_state.Shown.Any(shown => shown.Id.Equals(item.Id, StringComparison.Ordinal))
                    ? item : null;
            }
        }
        finally
        {
            _contentGate.Release();
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

    private string[] BuildExclusionsLocked()
    {
        return _state.Shown.Select(item => item.Id)
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
        catch (HttpRequestException error)
        {
            var message = NetworkConnectionErrors.Format(error, "连接互动服务失败，请检查网络。");
            System.Diagnostics.Trace.TraceError($"Network request failed: {error}");
            throw new InvalidOperationException(message, error);
        }
        catch (TaskCanceledException error) when (!cancellationToken.IsCancellationRequested)
        {
            // 超时（非用户取消）
            System.Diagnostics.Trace.TraceWarning($"Network request timeout: {error.Message}");
            throw new InvalidOperationException("连接互动服务超时，请稍后重试。", error);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 用户主动取消
            throw;
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
        try
        {
            var error = JsonSerializer.Deserialize<InteractionErrorResponse>(bytes, JsonOptions);
            return error?.Error ?? error?.Message;
        }
        catch { return null; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _httpClient.Dispose();
        // In-flight requests still release these managed gates after HTTP cancellation.
    }
}
