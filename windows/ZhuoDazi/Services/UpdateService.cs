using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using NSec.Cryptography;

namespace ZhuoDazi.Services;

public enum UpdatePhase
{
    Idle,
    Checking,
    Current,
    Available,
    Downloading,
    Downloaded,
    Error
}

public sealed record UpdateState(UpdatePhase Phase, string Message, UpdateManifest? Manifest = null, int Progress = 0);

public sealed class UpdateManifest
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = string.Empty;

    [JsonPropertyName("notes")]
    public string Notes { get; set; } = string.Empty;

    [JsonPropertyName("signatureAlgorithm")]
    public string SignatureAlgorithm { get; set; } = string.Empty;

    [JsonPropertyName("signature")]
    public string Signature { get; set; } = string.Empty;
}

public sealed class UpdateService : IDisposable
{
    private const string ManifestUrl = "https://8.134.130.155/api/update/latest?platform=windows&architecture=x64";
    private const int MaxManifestBytes = 512 * 1024;
    private const long MaxDownloadBytes = 300L * 1024 * 1024;
    private const int DownloadBufferSize = 1024 * 1024;
    private const string PublicKeySpki = "MCowBQYDK2VwAyEANjBEMMQ5TY+0ECNoRqQy9780eoVOzkKpzFDq2TwLytU=";
    private static readonly JsonSerializerOptions SignedJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly SettingsStore _store;
    private readonly LicenseService _licenses;
    private readonly HttpClient _httpClient = new(new SocketsHttpHandler
    {
        UseProxy = false,
        ConnectTimeout = TimeSpan.FromSeconds(20),
        PooledConnectionLifetime = TimeSpan.FromMinutes(5)
    })
    {
        Timeout = TimeSpan.FromMinutes(10),
        DefaultRequestVersion = HttpVersion.Version20,
        DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower
    };
    private string? _downloadedPath;

    public UpdateState State { get; private set; } = new(UpdatePhase.Idle, "可以检查更新");
    public event Action<UpdateState>? StateChanged;

    public UpdateService(SettingsStore store, LicenseService licenses)
    {
        _store = store;
        _licenses = licenses;
    }

    public async Task<UpdateState> CheckAsync(bool manual = true, CancellationToken cancellationToken = default)
    {
        SetState(new(UpdatePhase.Checking, "正在检查更新…"));
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, ManifestUrl);
            _licenses.Authorize(request);
            request.Headers.Accept.ParseAdd("application/json");
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            EnsureAuthorizedResponse(response);
            if (await IsNoReleaseResponseAsync(response, cancellationToken))
                return SetState(new(UpdatePhase.Current, "当前已经是最新版本"));
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is > MaxManifestBytes)
                throw new InvalidOperationException("更新清单文件过大。");
            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            if (bytes.Length > MaxManifestBytes) throw new InvalidOperationException("更新清单文件过大。");
            var manifest = JsonSerializer.Deserialize<UpdateManifest>(bytes)
                ?? throw new InvalidOperationException("更新清单格式无效。");
            ValidateManifest(manifest);
            if (CompareVersions(manifest.Version, CurrentVersion) <= 0)
                return SetState(new(UpdatePhase.Current, "当前已经是最新版本"));
            return SetState(new(UpdatePhase.Available, $"发现新版本 v{manifest.Version}", manifest));
        }
        catch (Exception error)
        {
            var message = NetworkConnectionErrors.Format(error, "检查更新超时");
            var state = SetState(new(UpdatePhase.Error, message));
            if (manual) throw new InvalidOperationException(message, error);
            return state;
        }
    }

    public async Task<UpdateState> DownloadAsync(IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        var manifest = State.Manifest ?? throw new InvalidOperationException("请先检查更新。");
        Directory.CreateDirectory(_store.UpdatesDirectory);
        var finalPath = Path.Combine(_store.UpdatesDirectory, $"ZhuoDazi-Setup-{manifest.Version}.exe");
        var temporaryPath = $"{finalPath}.part";
        SetState(new(UpdatePhase.Downloading, "正在下载新版本…", manifest));
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, manifest.Url);
            _licenses.Authorize(request);
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            EnsureAuthorizedResponse(response);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength;
            if (total is > MaxDownloadBytes) throw new InvalidOperationException("更新文件超过 300 MB。");
            string digest;
            var stopwatch = Stopwatch.StartNew();
            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var output = new FileStream(temporaryPath, new FileStreamOptions
            {
                Mode = FileMode.Create,
                Access = FileAccess.Write,
                Share = FileShare.None,
                BufferSize = DownloadBufferSize,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
                PreallocationSize = total.GetValueOrDefault()
            }))
            using (var hash = System.Security.Cryptography.IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = GC.AllocateUninitializedArray<byte>(DownloadBufferSize);
                long received = 0;
                var lastProgress = -1;
                while (true)
                {
                    var read = await input.ReadAsync(buffer, cancellationToken);
                    if (read == 0) break;
                    received += read;
                    if (received > MaxDownloadBytes) throw new InvalidOperationException("更新文件超过 300 MB。");
                    hash.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    if (total is > 0)
                    {
                        var percent = Math.Min(99, (int)(received * 100 / total.Value));
                        if (percent >= lastProgress + 2)
                        {
                            lastProgress = percent;
                            var megabytesPerSecond = received / 1024d / 1024d
                                / Math.Max(stopwatch.Elapsed.TotalSeconds, 0.1);
                            progress?.Report(percent);
                            SetState(new(
                                UpdatePhase.Downloading,
                                $"正在下载 {percent}% · {megabytesPerSecond:0.0} MB/s",
                                manifest,
                                percent));
                        }
                    }
                }
                await output.FlushAsync(cancellationToken);
                digest = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            }

            if (!CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(digest), Encoding.ASCII.GetBytes(manifest.Sha256.ToLowerInvariant())))
                throw new InvalidOperationException("更新文件校验失败，已取消更新。");
            File.Move(temporaryPath, finalPath, true);
            _downloadedPath = finalPath;
            progress?.Report(100);
            return SetState(new(UpdatePhase.Downloaded, "更新已下载并通过校验", manifest, 100));
        }
        catch (Exception error)
        {
            try { File.Delete(temporaryPath); } catch { }
            var message = NetworkConnectionErrors.Format(error, "下载更新超时");
            SetState(new(UpdatePhase.Error, message, manifest));
            throw new InvalidOperationException(message, error);
        }
    }

    public void InstallDownloaded()
    {
        if (string.IsNullOrWhiteSpace(_downloadedPath) || !File.Exists(_downloadedPath))
            throw new InvalidOperationException("已下载的更新文件不存在。");
        Process.Start(new ProcessStartInfo
        {
            FileName = _downloadedPath,
            Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS",
            UseShellExecute = true
        });
    }

    public static string CurrentVersion => typeof(UpdateService).Assembly.GetName().Version?.ToString(3) ?? "2.4.5";

    internal static int CompareVersions(string left, string right)
    {
        static int[] Parse(string value) => value.Split('-', 2)[0].Split('.')
            .Select(part => int.TryParse(part, out var number) ? number : 0).Concat([0, 0, 0, 0]).Take(4).ToArray();
        var a = Parse(left);
        var b = Parse(right);
        for (var index = 0; index < a.Length; index++)
        {
            var comparison = a[index].CompareTo(b[index]);
            if (comparison != 0) return comparison;
        }
        return 0;
    }

    internal static void ValidateManifest(UpdateManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(manifest.Version) || CompareVersions(manifest.Version, "0.0.0") <= 0)
            throw new InvalidOperationException("更新清单版本号无效。");
        if (!Uri.TryCreate(manifest.Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new InvalidOperationException("更新下载地址无效。");
        if (manifest.Sha256.Length != 64 || !manifest.Sha256.All(Uri.IsHexDigit))
            throw new InvalidOperationException("更新清单缺少有效的 SHA-256。");
        if (!manifest.SignatureAlgorithm.Equals("ed25519", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("更新清单签名算法无效。");

        byte[] signature;
        try { signature = Convert.FromBase64String(manifest.Signature); }
        catch { throw new InvalidOperationException("更新清单签名格式无效。"); }
        if (signature.Length != 64) throw new InvalidOperationException("更新清单签名格式无效。");

        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            version = manifest.Version,
            url = manifest.Url,
            sha256 = manifest.Sha256.ToLowerInvariant(),
            notes = manifest.Notes ?? string.Empty
        }, SignedJsonOptions);
        var spki = Convert.FromBase64String(PublicKeySpki);
        var publicKey = spki[^32..];
        var algorithm = SignatureAlgorithm.Ed25519;
        var importedKey = PublicKey.Import(algorithm, publicKey, KeyBlobFormat.RawPublicKey);
        if (!algorithm.Verify(importedKey, payload, signature))
            throw new InvalidOperationException("更新清单签名验证失败。");
    }

    private UpdateState SetState(UpdateState state)
    {
        State = state;
        StateChanged?.Invoke(state);
        return state;
    }

    private static void EnsureAuthorizedResponse(HttpResponseMessage response)
    {
        if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
            throw new InvalidOperationException("设备绑定已失效，暂时无法更新。");
    }

    private static async Task<bool> IsNoReleaseResponseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.StatusCode != System.Net.HttpStatusCode.NotFound) return false;
        if (response.Content.Headers.ContentLength is > MaxManifestBytes) return false;

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (bytes.Length > MaxManifestBytes) return false;
        try
        {
            using var document = JsonDocument.Parse(bytes);
            return document.RootElement.TryGetProperty("code", out var code)
                && code.ValueKind == JsonValueKind.String
                && code.GetString() == "NO_RELEASE";
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public void Dispose() => _httpClient.Dispose();
}
