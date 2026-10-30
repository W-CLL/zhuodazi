using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using ZhuoDazi.Models;

namespace ZhuoDazi.Services;

internal sealed class ResourcePackClient : IDisposable
{
    private const int MaxPackageBytes = 50 * 1024 * 1024;
    private readonly HttpClient _http;
    private readonly Uri _origin;
    private readonly string _downloadDirectory;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    internal ResourcePackClient(Uri origin, string dataDirectory, bool localPreview)
    {
        _origin = origin;
        _downloadDirectory = Path.Combine(dataDirectory, "shop-resource-downloads");
        if (localPreview && (origin.Scheme != "http" || origin.Host != "127.0.0.1"
            || origin.AbsolutePath != "/" || origin.UserInfo.Length != 0 || origin.Query.Length != 0))
            throw new InvalidOperationException("本地资源小铺只能连接配置的本机后台。");
        HttpMessageHandler transport = new HttpClientHandler
            { AllowAutoRedirect = false, UseCookies = false, UseProxy = !localPreview };
        _http = new HttpClient(transport) { Timeout = TimeSpan.FromSeconds(90) };
    }

    internal async Task<List<ShopResourcePack>> GetCatalogAsync(CancellationToken token)
    {
        using var response = await _http.GetAsync(new Uri(_origin, "/api/public/resource-packs"),
            HttpCompletionOption.ResponseHeadersRead, token);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException("暂时无法获取资源目录，请稍后刷新。");
        using var memory = new MemoryStream();
        await using var input = await response.Content.ReadAsStreamAsync(token);
        await CopyBoundedAsync(input, memory, 2 * 1024 * 1024, token);
        var catalog = JsonSerializer.Deserialize<ResourcePackCatalog>(memory.ToArray(), JsonOptions)
            ?? throw new InvalidOperationException("资源目录格式无效。");
        return (catalog.Packs ?? []).Where(IsValid).ToList();
    }

    internal async Task<string> DownloadAsync(ShopResourcePack pack, CancellationToken token)
    {
        if (!IsValid(pack)) throw new InvalidOperationException("资源信息无效，请刷新目录。");
        // Build the path from the validated ID; never follow a server-supplied external URL.
        using var response = await _http.GetAsync(new Uri(_origin, $"/resource-packs/{pack.Id}/download"),
            HttpCompletionOption.ResponseHeadersRead, token);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException("资源已下架或暂时无法下载，请刷新目录。");
        if (response.Content.Headers.ContentLength is { } length && length != pack.Size)
            throw new InvalidOperationException("资源大小已变化，请刷新目录。");
        Directory.CreateDirectory(_downloadDirectory);
        var file = Path.Combine(_downloadDirectory, $"{Guid.NewGuid():N}.zip");
        try
        {
            await using (var output = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await using var input = await response.Content.ReadAsStreamAsync(token);
                await CopyBoundedAsync(input, output, pack.Size, token);
                if (output.Length != pack.Size) throw new InvalidOperationException("资源下载不完整，请重试。");
            }
            await using var read = File.OpenRead(file);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(read, token));
            if (!hash.Equals(pack.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("资源完整性校验失败，请重新下载。");
            return file;
        }
        catch { try { File.Delete(file); } catch { } throw; }
    }

    private static bool IsValid(ShopResourcePack item) => Guid.TryParse(item.Id, out _)
        && item.Category is "interaction-words" or "theater-scripts"
        && item.Size is > 0 and <= MaxPackageBytes && item.FileCount is > 0 and <= 80
        && item.Sha256.Length == 64 && item.Sha256.All(Uri.IsHexDigit);

    private static async Task CopyBoundedAsync(Stream input, Stream output, long limit, CancellationToken token)
    {
        var buffer = new byte[65536];
        long total = 0;
        int count;
        while ((count = await input.ReadAsync(buffer, token)) > 0)
        {
            total += count;
            if (total > limit) throw new InvalidOperationException("资源超过允许大小。");
            await output.WriteAsync(buffer.AsMemory(0, count), token);
        }
    }

    public void Dispose() => _http.Dispose();
}
