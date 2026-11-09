using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ZhuoDazi.Services;

internal sealed class LocalPaymentTestEnvironment
{
    internal static bool IsTestBuild
    {
        get
        {
#if ALIPAY_LOCAL_TEST
            return true;
#else
            return false;
#endif
        }
    }

    public string Profile { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string SigningPublicKeySpki { get; set; } = string.Empty;
    internal Uri Origin { get; private set; } = null!;
    internal byte[] SigningPublicKey { get; private set; } = [];
    internal string DataDirectory { get; private set; } = string.Empty;

    internal static LocalPaymentTestEnvironment Load(string path)
    {
        if (!File.Exists(path) || new FileInfo(path).Length > 16 * 1024)
            throw new InvalidOperationException("请将完整测试包解压后运行，缺少 payment-test.json 配置。");
        var config = JsonSerializer.Deserialize<LocalPaymentTestEnvironment>(File.ReadAllText(path),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? throw new InvalidOperationException("测试配置无效。");
        if (!Regex.IsMatch(config.Profile, "^[a-zA-Z0-9_-]{8,64}$") || config.BaseUrl != "http://127.0.0.1:18788/")
            throw new InvalidOperationException("完整支付测试只允许指定的本机服务和独立测试档案。");
        var spki = Convert.FromBase64String(config.SigningPublicKeySpki);
        if (spki.Length != 44 || !spki.AsSpan(0, 12).SequenceEqual(Convert.FromHexString("302A300506032B6570032100")))
            throw new InvalidOperationException("本地资源签名公钥无效。");
        config.SigningPublicKey = spki[^32..];
        config.Origin = new Uri(config.BaseUrl);
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ZhuoDazi", "AlipayLocalTest", config.Profile);
        config.DataDirectory = new AppPreviewEnvironment(config.Origin, root).DataDirectory;
        return config;
    }

    internal HttpMessageHandler CreateHandler() => new LocalApiRouter(Origin,
        new AppPreviewEnvironment(Origin, Path.GetDirectoryName(DataDirectory)!).CreateHandler());

    // The isolated license/settings store is installed before any service is constructed.
    // Known application API requests are routed to loopback; no other destination is allowed.
    internal sealed class LocalApiRouter(Uri origin, HttpMessageHandler transport) : DelegatingHandler(transport)
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var target = request.RequestUri;
            if (target is null || !target.IsAbsoluteUri || target.UserInfo.Length != 0 || target.Fragment.Length != 0
                || request.Headers.Host is not null || !target.AbsolutePath.StartsWith("/api/", StringComparison.Ordinal))
                throw new HttpRequestException("完整测试版仅连接本机 API。");
            if (target.Scheme == "https" && target.Host == DeskPetApi.Host && target.IsDefaultPort)
                request.RequestUri = new Uri(origin, target.PathAndQuery);
            else if (target.Scheme != "http" || target.Host != "127.0.0.1" || target.Port != origin.Port)
                throw new HttpRequestException("完整测试版已阻止外部 API 请求。");
            return base.SendAsync(request, token);
        }
    }
}
