using System.IO;
using System.Net;
using System.Net.Http;
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
        DeskPetHttp.HandlerFactory = () => new ResponseHandler(async request =>
        {
            object payload;
            if (request.RequestUri!.AbsolutePath == "/api/public/site-settings")
                payload = new { wechatId = "support_123", activation = new { title, description,
                    replacementTitle, replacementDescription, qrCodeDataUrl = "" } };
            else if (request.RequestUri.AbsolutePath == "/api/trial")
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
                identityVerified = body.RootElement.GetProperty("installationId").GetString() == expectedInstallation
                    && body.RootElement.GetProperty("credential").GetString()?.Length >= 40;
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
        var window = new ActivationWindow(licenses)
        {
            WindowStartupLocation = WindowStartupLocation.Manual, Left = -30000, Top = -30000, ShowActivated = false
        };
        Exception? failure = null;
        window.Loaded += async (_, _) =>
        {
            try
            {
                await WaitFor(() => ((TextBlock)window.FindName("ContactTitleText")).Text == title && identityVerified);
                if (((TextBlock)window.FindName("ContactDescriptionText")).Text != description
                    || ((TextBlock)window.FindName("WeChatIdText")).Text != "support_123"
                    || ((TextBox)window.FindName("DeviceCodeText")).Text != licenses.InstallationId)
                    throw new InvalidOperationException("The activation window did not display backend copy and the full device identity.");
                window.UpdateLayout();
                var image = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                image.Render(window);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(image));
                using (var stream = File.Create(Path.Combine(output, "activation-window.png"))) encoder.Save(stream);
                await Task.Delay(100);
                authorized = true;
                ((Button)window.FindName("SyncActivationButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await WaitFor(() => licenses.IsActivated);
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

        var replacementWindow = new ActivationWindow(licenses, replacingExisting: true)
        {
            WindowStartupLocation = WindowStartupLocation.Manual, Left = -30000, Top = -30000, ShowActivated = false
        };
        failure = null;
        replacementWindow.Loaded += async (_, _) =>
        {
            try
            {
                await WaitFor(() => ((TextBlock)replacementWindow.FindName("ContactTitleText")).Text == replacementTitle);
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
            "PASS: separate first/replacement copy, WeChat sync, full device code, offline QR fallback, verified manual activation, modal completion; no production requests.");
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (condition()) return;
            await Task.Delay(50);
        }
        throw new TimeoutException("Activation window test timed out.");
    }

    private sealed class ResponseHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => respond(request);
    }
}
