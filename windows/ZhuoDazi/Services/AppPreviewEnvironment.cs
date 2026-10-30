using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace ZhuoDazi.Services;

/// <summary>Local-only transport and storage for the complete Windows application preview.</summary>
internal sealed class AppPreviewEnvironment
{
    internal Uri Origin { get; }
    internal string DataDirectory { get; }

    internal AppPreviewEnvironment(Uri origin, string sourceDataDirectory)
    {
        if (!origin.IsAbsoluteUri || origin.Scheme != Uri.UriSchemeHttp || origin.Host != "127.0.0.1"
            || origin.Port is < 1 or > 65535 || origin.AbsolutePath != "/"
            || origin.UserInfo.Length != 0 || origin.Query.Length != 0 || origin.Fragment.Length != 0)
            throw new InvalidOperationException("完整本地预览只允许配置的 http://127.0.0.1:端口/。");
        if (string.IsNullOrWhiteSpace(sourceDataDirectory) || !Path.IsPathFullyQualified(sourceDataDirectory)
            || sourceDataDirectory.StartsWith(@"\\", StringComparison.Ordinal)
            || sourceDataDirectory.StartsWith("//", StringComparison.Ordinal))
            throw new InvalidOperationException("完整本地预览必须使用隔离的本机绝对目录。");
        var source = Path.GetFullPath(sourceDataDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (source.Length == 0 || source.Equals(Path.GetPathRoot(source)?.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("完整本地预览不能使用磁盘根目录。");
        DataDirectory = Path.Combine(source, "app-preview");
        var production = Path.GetFullPath(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "poko-desktop-pet"));
        if (source.Equals(production, StringComparison.OrdinalIgnoreCase)
            || source.StartsWith(production + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("完整本地预览不能使用正式用户数据目录。");

        // Reject junctions in the ancestry as well as the preview directory itself.
        // This keeps an existing local directory from redirecting preview writes to user data.
        for (var directory = new DirectoryInfo(DataDirectory); directory is not null; directory = directory.Parent)
        {
            if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("完整本地预览目录不能经过符号链接或目录联接。");
        }
        Directory.CreateDirectory(DataDirectory);
        Origin = origin;
    }

    internal HttpMessageHandler CreateHandler() => CreateGuardedHandler(CreateLoopbackTransport());

    // The injected transport is used only by focused, socket-free boundary checks.
    internal HttpMessageHandler CreateGuardedHandler(HttpMessageHandler transport)
        => new OriginGuard(Origin, transport);

    private HttpMessageHandler CreateLoopbackTransport()
    {
        return new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            UseCookies = false,
            ConnectTimeout = TimeSpan.FromSeconds(8),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectCallback = async (context, cancellationToken) =>
            {
                if (context.DnsEndPoint.Host != "127.0.0.1" || context.DnsEndPoint.Port != Origin.Port)
                    throw new HttpRequestException("已阻止本地预览连接非配置的本机服务。");
                var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                try
                {
                    await socket.ConnectAsync(IPAddress.Loopback, Origin.Port, cancellationToken);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            }
        };
    }

    private sealed class OriginGuard(Uri origin, HttpMessageHandler transport) : DelegatingHandler(transport)
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var target = request.RequestUri;
            if (target is null || !target.IsAbsoluteUri || target.Scheme != Uri.UriSchemeHttp
                || target.Host != "127.0.0.1" || target.Port != origin.Port
                || target.UserInfo.Length != 0 || target.Fragment.Length != 0
                || (request.Headers.Host is { } host && !host.Equals(origin.Authority, StringComparison.OrdinalIgnoreCase)))
                throw new HttpRequestException("完整本地预览仅连接配置的本机小铺，云端服务未连接。");
            return base.SendAsync(request, cancellationToken);
        }
    }
}
