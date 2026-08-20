using System.Net;
using System.Net.Http;

namespace ZhuoDazi.Services;

internal static class DeskPetHttp
{
    internal static SocketsHttpHandler CreateHandler()
    {
        return new SocketsHttpHandler
        {
            UseProxy = false,
            ConnectTimeout = TimeSpan.FromSeconds(20),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        };
    }

    internal static HttpClient CreateClient(TimeSpan timeout)
    {
        return new HttpClient(CreateHandler())
        {
            Timeout = timeout,
            DefaultRequestVersion = HttpVersion.Version20,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower
        };
    }

    internal static bool TryCreateCompanionFileUrl(string downloadPath, out Uri? uri)
        => TryCreateHttpsFileUrl(downloadPath, "/api/companion/deliveries/", "/file", out uri);

    internal static bool TryCreateTrialVisitFileUrl(string downloadPath, out Uri? uri)
        => TryCreateHttpsFileUrl(downloadPath, "/api/trial/visit-stickers/", "/file", out uri);

    private static bool TryCreateHttpsFileUrl(string downloadPath, string prefix, string suffix, out Uri? uri)
    {
        uri = null;
        if (string.IsNullOrWhiteSpace(downloadPath) || string.IsNullOrEmpty(prefix) || string.IsNullOrEmpty(suffix))
            return false;
        if (!Uri.TryCreate(DeskPetApi.BaseUrl, UriKind.Absolute, out var origin)) return false;
        if (!Uri.TryCreate(origin, downloadPath, out var resolved)) return false;
        if (!resolved.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) return false;
        if (!resolved.Host.Equals(DeskPetApi.Host, StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.IsNullOrEmpty(resolved.Query) || !string.IsNullOrEmpty(resolved.Fragment)) return false;

        var path = resolved.AbsolutePath;
        if (!path.StartsWith(prefix, StringComparison.Ordinal)
            || !path.EndsWith(suffix, StringComparison.Ordinal))
            return false;

        var id = path[prefix.Length..^suffix.Length];
        if (!Guid.TryParse(id, out _)) return false;
        uri = resolved;
        return true;
    }
}
