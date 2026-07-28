using System.Net.Http;

namespace ZhuoDazi.Services;

internal static class NetworkConnectionErrors
{
    internal const string SecureConnectionMessage =
        "无法建立安全连接。应用会使用电脑的直接网络，请检查网络连接后重试。";

    internal static string Format(Exception error, string timeoutMessage)
    {
        if (error is TaskCanceledException) return timeoutMessage;
        if (error is HttpRequestException) return SecureConnectionMessage;
        return error.Message;
    }
}
