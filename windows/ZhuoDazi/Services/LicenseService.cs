using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ZhuoDazi.Services;

public sealed record TrialStatus(bool Allowed, int RemainingSeconds);

public sealed class LicenseService : IDisposable
{
    internal const string ServiceBaseUrl = "https://in.desktoppet.online";
    private const string ActivationUrl = ServiceBaseUrl + "/api/activate";
    private const string TrialUrl = ServiceBaseUrl + "/api/trial";
    private static readonly byte[] OptionalEntropy = Encoding.UTF8.GetBytes("ZhuoDazi.Native.License.v1");
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(25) };
    private readonly string _licensePath;
    private LicenseRecord _record;
    private bool _trialActive;

    public bool IsActivated => Guid.TryParse(_record.LicenseId, out _);
    public string LicenseId => IsActivated ? _record.LicenseId! : string.Empty;
    public string InstallationId => _record.InstallationId;
    public string Summary => IsActivated ? $"此设备已完成绑定 · {LicenseId[^8..]}" : "此设备尚未绑定";

    public LicenseService()
    {
        var dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "poko-desktop-pet");
        _licensePath = Path.Combine(dataDirectory, "license.dat");
        _record = Load() ?? CreatePendingRecord();
        if (!IsValid(_record))
        {
            _record = CreatePendingRecord();
        }
        Save();
    }

    public async Task ActivateAsync(
        string activationCode,
        bool replacingExisting = false,
        CancellationToken cancellationToken = default)
    {
        var code = new string((activationCode ?? string.Empty)
            .Trim().ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
        if (code.Length != 6) throw new InvalidOperationException("请输入有效的 6 位激活码。");

        var candidate = replacingExisting ? CreatePendingRecord() : _record;
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            code,
            installationId = candidate.InstallationId,
            credential = candidate.Credential,
            appVersion = UpdateService.CurrentVersion
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, ActivationUrl)
        {
            Content = new ByteArrayContent(payload)
        };
        request.Content.Headers.ContentType = new("application/json") { CharSet = "utf-8" };
        request.Headers.UserAgent.ParseAdd($"ZhuoDazi/{UpdateService.CurrentVersion}");
        request.Headers.TryAddWithoutValidation("X-DeskPet-Platform", "windows");
        request.Headers.TryAddWithoutValidation("X-DeskPet-Architecture", "x64");
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (Exception error)
        {
            var message = NetworkConnectionErrors.Format(error, "连接激活服务超时。");
            throw new InvalidOperationException(message, error);
        }
        using (response)
        {
            var responseBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            if (responseBytes.Length > 32 * 1024) throw new InvalidOperationException("激活服务响应无效。");
            if (!response.IsSuccessStatusCode)
            {
                var message = TryReadError(responseBytes) ?? "激活码验证失败，请检查后重试。";
                throw new InvalidOperationException(message);
            }
            var result = JsonSerializer.Deserialize<ActivationResponse>(responseBytes, JsonOptions);
            if (result is null || !Guid.TryParse(result.LicenseId, out _))
                throw new InvalidOperationException("激活服务返回的授权无效。");
            candidate.LicenseId = result.LicenseId;
            candidate.ActivatedAt = result.ActivatedAt;
            _record = candidate;
            Save();
        }
    }

    public async Task<TrialStatus> CheckTrialAsync(CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            installationId = _record.InstallationId,
            credential = _record.Credential,
            appVersion = UpdateService.CurrentVersion
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, TrialUrl)
        {
            Content = new ByteArrayContent(payload)
        };
        request.Content.Headers.ContentType = new("application/json") { CharSet = "utf-8" };
        request.Headers.UserAgent.ParseAdd($"ZhuoDazi/{UpdateService.CurrentVersion}");
        request.Headers.TryAddWithoutValidation("X-DeskPet-Platform", "windows");
        request.Headers.TryAddWithoutValidation("X-DeskPet-Architecture", "x64");
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (Exception error)
        {
            throw new InvalidOperationException(NetworkConnectionErrors.Format(error, "暂时无法获取试用状态，请稍后重试。"), error);
        }
        using (response)
        {
            var responseBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            if (responseBytes.Length > 32 * 1024) throw new InvalidOperationException("试用服务响应无效。");
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(TryReadError(responseBytes) ?? "试用时间校验失败，请稍后重试。");
            var result = JsonSerializer.Deserialize<TrialResponse>(responseBytes, JsonOptions);
            if (result is null || result.RemainingSeconds is < 0 or > 600)
                throw new InvalidOperationException("试用服务返回的数据无效。");
            _trialActive = result.Allowed && result.RemainingSeconds > 0;
            return new TrialStatus(_trialActive, result.RemainingSeconds);
        }
    }

    public void Authorize(HttpRequestMessage request)
    {
        if (IsActivated)
            request.Headers.Authorization = new("Bearer", $"{_record.LicenseId}.{_record.Credential}");
        else if (_trialActive)
            request.Headers.Authorization = new("Trial", $"{_record.InstallationId}.{_record.Credential}");
        else
            throw new InvalidOperationException("此设备尚未完成绑定。");
        request.Headers.TryAddWithoutValidation("X-DeskPet-Version", UpdateService.CurrentVersion);
        request.Headers.UserAgent.ParseAdd($"ZhuoDazi/{UpdateService.CurrentVersion}");
    }

    private LicenseRecord? Load()
    {
        try
        {
            if (!File.Exists(_licensePath)) return null;
            var protectedBytes = File.ReadAllBytes(_licensePath);
            var json = ProtectedData.Unprotect(protectedBytes, OptionalEntropy, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<LicenseRecord>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private void Save()
    {
        var directory = Path.GetDirectoryName(_licensePath)!;
        Directory.CreateDirectory(directory);
        var json = JsonSerializer.SerializeToUtf8Bytes(_record, JsonOptions);
        var protectedBytes = ProtectedData.Protect(json, OptionalEntropy, DataProtectionScope.CurrentUser);
        var temporaryPath = $"{_licensePath}.{Environment.ProcessId}.tmp";
        File.WriteAllBytes(temporaryPath, protectedBytes);
        File.Move(temporaryPath, _licensePath, true);
    }

    private static LicenseRecord CreatePendingRecord() => new()
    {
        Version = 1,
        InstallationId = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant(),
        Credential = Base64UrlEncode(RandomNumberGenerator.GetBytes(32))
    };

    private static bool IsValid(LicenseRecord record)
    {
        if (record.Version != 1 || record.InstallationId.Length != 32 || record.Credential.Length != 43) return false;
        if (!record.InstallationId.All(Uri.IsHexDigit)) return false;
        if (!record.Credential.All(character => char.IsLetterOrDigit(character) || character is '-' or '_')) return false;
        return string.IsNullOrWhiteSpace(record.LicenseId) || Guid.TryParse(record.LicenseId, out _);
    }

    private static string Base64UrlEncode(byte[] value) => Convert.ToBase64String(value)
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string? TryReadError(byte[] responseBytes)
    {
        try
        {
            return JsonSerializer.Deserialize<ErrorResponse>(responseBytes, JsonOptions)?.Error;
        }
        catch
        {
            return null;
        }
    }

    public void Dispose() => _httpClient.Dispose();

    private sealed class LicenseRecord
    {
        [JsonPropertyName("version")]
        public int Version { get; set; }

        [JsonPropertyName("installationId")]
        public string InstallationId { get; set; } = string.Empty;

        [JsonPropertyName("credential")]
        public string Credential { get; set; } = string.Empty;

        [JsonPropertyName("licenseId")]
        public string? LicenseId { get; set; }

        [JsonPropertyName("activatedAt")]
        public string? ActivatedAt { get; set; }
    }

    private sealed class ActivationResponse
    {
        [JsonPropertyName("licenseId")]
        public string LicenseId { get; set; } = string.Empty;

        [JsonPropertyName("activatedAt")]
        public string? ActivatedAt { get; set; }
    }

    private sealed class TrialResponse
    {
        [JsonPropertyName("allowed")]
        public bool Allowed { get; set; }

        [JsonPropertyName("remainingSeconds")]
        public int RemainingSeconds { get; set; }
    }

    private sealed class ErrorResponse
    {
        [JsonPropertyName("error")]
        public string? Error { get; set; }
    }
}
