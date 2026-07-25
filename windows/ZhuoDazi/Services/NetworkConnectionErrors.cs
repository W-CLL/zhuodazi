using System.Net.Http;

namespace ZhuoDazi.Services;

internal static class NetworkConnectionErrors
{
    internal const string SecureConnectionMessage =
        "无法建立安全连接。请暂时关闭 VPN，或在 VPN 中将 8.134.130.155 设置为直连后重试。";

    internal static string Format(Exception error, string timeoutMessage)
    {
        if (error is TaskCanceledException) return timeoutMessage;
        if (error is HttpRequestException && error.ToString().Contains("SSL connection", StringComparison.OrdinalIgnoreCase))
            return SecureConnectionMessage;
        return error.Message;
    }
}
