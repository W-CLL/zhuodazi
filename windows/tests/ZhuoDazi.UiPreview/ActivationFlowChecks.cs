using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZhuoDazi;
using ZhuoDazi.Services;

internal static class ActivationFlowChecks
{
    internal static void RunPublicConfigurationCheck(string output)
    {
        Directory.CreateDirectory(output);
        RemoteConfigService.PreviewSiteSettingsUri = null;
        DeskPetHttp.HandlerFactory = () => new PublicSettingsOnlyHandler();
        try
        {
            var remote = new RemoteConfigService(new SettingsStore(output));
            if (!Task.Run(() => remote.RefreshAsync()).GetAwaiter().GetResult())
                throw new InvalidOperationException("The production RemoteConfigService could not fetch public site settings.");
            var result = JsonSerializer.Serialize(new
            {
                userAgent = $"ZhuoDazi/{UpdateService.CurrentVersion}",
                title = remote.Current.ActivationTitle,
                description = remote.Current.ActivationDescription,
                replacementTitle = remote.Current.ActivationReplacementTitle,
                hasQr = !string.IsNullOrWhiteSpace(remote.Current.ActivationQrCodeDataUrl)
            });
            Console.WriteLine(result);
            File.WriteAllText(Path.Combine(output, "public-config-check.json"), result);
        }
        finally { DeskPetHttp.HandlerFactory = null; }
    }

    internal static void Run(string directory, string output)
    {
        Directory.CreateDirectory(output);
        var licenseId = Guid.NewGuid().ToString();
        var authorized = false;
        var identityVerified = false;
        string? expectedInstallation = null;
        var title = "扫码联系作者 · 最新激活指引";
        var description = "这是后台动态下发的激活说明。\n复制下方设备码发给作者，作者可为指定设备直接激活；一组购买码仍可绑定两台设备。";
        var replacementTitle = "更换激活码 · 单独指引";
        var replacementDescription = "这是后台单独下发的更换激活码说明。";
        var qrCodeDataUrl = QrDataUrl(Colors.Red);
        var wechatId = "support_123";
        var xianyuUrl = "";
        var fixturePath = Environment.GetEnvironmentVariable("ZHUODAZI_ACTIVATION_CONFIG_FIXTURE");
        if (!string.IsNullOrWhiteSpace(fixturePath))
        {
            using var fixture = JsonDocument.Parse(File.ReadAllText(fixturePath));
            var root = fixture.RootElement;
            if (root.TryGetProperty("publicSiteSettings", out var nested)) root = nested;
            var activation = root.GetProperty("activation");
            title = activation.GetProperty("title").GetString()!;
            description = activation.GetProperty("description").GetString()!;
            replacementTitle = activation.GetProperty("replacementTitle").GetString()!;
            replacementDescription = activation.GetProperty("replacementDescription").GetString()!;
            qrCodeDataUrl = activation.GetProperty("qrCodeDataUrl").GetString()!;
            wechatId = root.GetProperty("wechatId").GetString()!;
            xianyuUrl = root.TryGetProperty("xianyuUrl", out var xianyu) ? xianyu.GetString()! : "";
        }
        var trialResponse = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var delayTrial = true;
        DeskPetHttp.HandlerFactory = () => new ResponseHandler(async request =>
        {
            object payload;
            if (request.RequestUri!.AbsolutePath == "/api/public/site-settings")
                payload = new { wechatId, xianyuUrl, activation = new { title, description,
                    replacementTitle, replacementDescription, qrCodeDataUrl } };
            else if (request.RequestUri.AbsolutePath == "/api/trial")
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
                identityVerified = body.RootElement.GetProperty("installationId").GetString() == expectedInstallation
                    && body.RootElement.GetProperty("credential").GetString()?.Length >= 40;
                if (delayTrial) await trialResponse.Task;
                payload = new { allowed = !authorized, remainingSeconds = authorized ? 0 : 86400,
                    license = authorized ? new { licenseId, activatedAt = "2026-10-16T08:00:00Z", deviceCount = 1 } : null };
            }
            else throw new InvalidOperationException("Unexpected test request: " + request.RequestUri.AbsolutePath);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };
        });
        using var licenses = new LicenseService(directory);
        expectedInstallation = licenses.InstallationId;
        var sharedConfig = new RemoteConfigService(new SettingsStore(directory));
        if (!Task.Run(() => sharedConfig.RefreshAsync()).GetAwaiter().GetResult())
            throw new InvalidOperationException("The controller's startup configuration did not load.");
        var window = new ActivationWindow(licenses, false, null, false, sharedConfig)
        {
            WindowStartupLocation = WindowStartupLocation.Manual, Left = -30000, Top = -30000, ShowActivated = false
        };
        if (((TextBlock)window.FindName("ContactTitleText")).Text != title)
            throw new InvalidOperationException("The first frame must use the controller's latest in-memory configuration.");
        Exception? failure = null;
        window.Loaded += async (_, _) =>
        {
            try
            {
                await WaitFor(() => ((TextBlock)window.FindName("ContactTitleText")).Text == title && identityVerified,
                    "first activation copy and the delayed trial request");
                await WaitForRefreshToFinish(window, "_configurationRefreshing", "initial configuration refresh completion");
                if (((TextBlock)window.FindName("ContactDescriptionText")).Text != description
                    || ((TextBlock)window.FindName("WeChatIdText")).Text != wechatId
                    || ((TextBox)window.FindName("DeviceCodeText")).Text != licenses.InstallationId)
                    throw new InvalidOperationException("The activation window did not display backend copy and the full device identity.");
                if (string.IsNullOrWhiteSpace(fixturePath)) AssertQrColor(window, Colors.Red);
                Capture(window, Path.Combine(output, "activation-first-frame.png"));
                var code = (TextBox)window.FindName("CodeTextBox");
                code.Text = "ABC234";
                var status = (TextBlock)window.FindName("StatusText");
                status.Text = "保留正在输入的状态";
                title = "在线修改后的首次激活说明";
                description = "激活验证仍在等待，卡片配置也应立即更新。";
                qrCodeDataUrl = QrDataUrl(Colors.Blue);
                ((Button)window.FindName("SyncActivationButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await WaitFor(() => ((TextBlock)window.FindName("ContactTitleText")).Text == title,
                    "manual copy refresh while trial verification is delayed");
                await WaitForRefreshToFinish(window, "_configurationRefreshing", "manual configuration refresh completion");
                if (((TextBlock)window.FindName("ContactDescriptionText")).Text != description
                    || code.Text != "ABC234" || status.Text != "保留正在输入的状态")
                    throw new InvalidOperationException("A delayed activation check must not block copy refresh or reset input/status.");
                AssertQrColor(window, Colors.Blue);
                title = "主控制器刷新后同步到已打开卡片";
                await sharedConfig.RefreshAsync();
                await WaitFor(() => ((TextBlock)window.FindName("ContactTitleText")).Text == title,
                    "shared controller configuration appearing in the open card");
                delayTrial = false;
                trialResponse.TrySetResult(true);
                await WaitForRefreshToFinish(window, "_refreshing", "delayed trial verification completion");
                Capture(window, Path.Combine(output, "activation-window.png"));
                authorized = true;
                ((Button)window.FindName("SyncActivationButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await WaitFor(() => licenses.IsActivated, "verified backend activation and modal completion");
            }
            catch (Exception error)
            {
                failure = error;
                window.Close();
            }
        };
        var result = window.ShowDialog();
        if (failure is not null) throw failure;
        if (result != true || licenses.LicenseId != licenseId || !identityVerified)
            throw new InvalidOperationException("The activation window did not complete a verified backend activation.");
        var closedTitle = ((TextBlock)window.FindName("ContactTitleText")).Text;
        title = "关闭后到达的配置";
        qrCodeDataUrl = "";
        Task.Run(() => sharedConfig.RefreshAsync()).GetAwaiter().GetResult();
        if (((TextBlock)window.FindName("ContactTitleText")).Text != closedTitle)
            throw new InvalidOperationException("A closed activation card must unsubscribe from later configuration updates.");

        var replacementWindow = new ActivationWindow(licenses, true, null, false, sharedConfig)
        {
            WindowStartupLocation = WindowStartupLocation.Manual, Left = -30000, Top = -30000, ShowActivated = false
        };
        if (((TextBlock)replacementWindow.FindName("ContactTitleText")).Text != replacementTitle)
            throw new InvalidOperationException("The replacement card must use its own latest copy before the first render.");
        failure = null;
        replacementWindow.Loaded += async (_, _) =>
        {
            try
            {
                await WaitFor(() => ((TextBlock)replacementWindow.FindName("ContactTitleText")).Text == replacementTitle,
                    "replacement-specific contact copy");
                if (((TextBlock)replacementWindow.FindName("ContactDescriptionText")).Text != replacementDescription)
                    throw new InvalidOperationException("The replacement flow reused the first-activation copy.");
                replacementWindow.DialogResult = false;
            }
            catch (Exception error)
            {
                failure = error;
                replacementWindow.Close();
            }
        };
        replacementWindow.ShowDialog();
        if (failure is not null) throw failure;
        File.WriteAllText(Path.Combine(output, "activation-check.txt"),
            "PASS: first-frame shared configuration, open-card text/QR updates, independent copy refresh during delayed activation, retained code/status, closed-card unsubscribe, separate first/replacement copy, WeChat sync, full device code, offline QR fallback, verified manual activation, modal completion; no production requests.");
    }

    private static void Capture(Window window, string path)
    {
        window.UpdateLayout();
        var image = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        image.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static string QrDataUrl(Color color)
    {
        var pixels = Enumerable.Range(0, 4).SelectMany(_ => new[] { color.B, color.G, color.R, color.A }).ToArray();
        var image = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, pixels, 8);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var output = new MemoryStream();
        encoder.Save(output);
        return "data:image/png;base64," + Convert.ToBase64String(output.ToArray());
    }

    private static void AssertQrColor(ActivationWindow window, Color expected)
    {
        var image = (BitmapSource)((Image)window.FindName("ActivationQrImage")).Source;
        var pixels = new byte[4];
        new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0)
            .CopyPixels(new Int32Rect(0, 0, 1, 1), pixels, 4, 0);
        if (pixels[0] != expected.B || pixels[1] != expected.G || pixels[2] != expected.R)
            throw new InvalidOperationException("The activation QR image did not update with its backend configuration.");
    }

    private static Task WaitForRefreshToFinish(ActivationWindow window, string fieldName, string stage)
    {
        // Configuration Changed is raised before the awaiting window reaches its
        // finally block. Wait for the real operation to finish before simulating
        // another click; response arrival alone is not completion on a busy STA.
        var field = typeof(ActivationWindow).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Missing activation refresh state: {fieldName}");
        return WaitFor(() => field.GetValue(window) is false, stage);
    }

    private static async Task WaitFor(Func<bool> condition, string stage)
    {
        Program.Stage($"activation: {stage}");
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (condition()) return;
            await Task.Delay(50);
        }
        throw new TimeoutException($"Activation window test timed out waiting for {stage}.");
    }

    private sealed class ResponseHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => respond(request);
    }

    private sealed class PublicSettingsOnlyHandler : HttpMessageHandler
    {
        private readonly HttpMessageInvoker _transport = new(DeskPetHttp.CreateHandler(allowAutoRedirect: false));

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method != HttpMethod.Get || request.RequestUri?.AbsoluteUri != DeskPetApi.SiteSettings)
                throw new InvalidOperationException("The live check only permits GET of public site settings.");
            return _transport.SendAsync(request, cancellationToken);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _transport.Dispose();
            base.Dispose(disposing);
        }
    }
}
