using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Markup;
using System.Xml.Linq;
using ZhuoDazi;
using ZhuoDazi.Models;
using ZhuoDazi.Services;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // Every service request is intercepted in memory. This harness never opens a network socket.
        DeskPetHttp.HandlerFactory = () => new OfflineHandler();
        var directory = Path.Combine(Path.GetTempPath(), "ZhuoDazi-OfflinePreview", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        if (args.Contains("--guide-check"))
        {
            // Automated checks raise their own routed events. Ignore real mouse/keyboard input
            // in this test process so a click on an overlapping test pet cannot answer early.
            System.Windows.Input.InputManager.Current.PreProcessInput += (_, input) =>
            {
                if (input.StagingItem.Input.Device is System.Windows.Input.MouseDevice
                    or System.Windows.Input.KeyboardDevice) input.Cancel();
            };
        }
        XNamespace ns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var source = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "App.xaml.source")).Root!;
        var resources = new XElement(ns + "ResourceDictionary", source.Element(ns + "Application.Resources")!.Elements());
        resources.SetAttributeValue(XNamespace.Xmlns + "x", x.NamespaceName);
        app.Resources = (ResourceDictionary)XamlReader.Parse(resources.ToString());
        using var license = new LicenseService(directory);
        license.CheckTrialAsync().GetAwaiter().GetResult();
        System.Threading.SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext(app.Dispatcher));
        using var controller = new AppController(license, new SettingsStore(directory));
        controller.Settings.RemoteDefaultsApplied = true;
        controller.Settings.RandomPetEnabled = false;
        var pet = new PetDefinition
        {
            Id = "offline-preview-pet", Name = "月薪喵 · 下班倒计时",
            Path = Path.Combine(AppContext.BaseDirectory, "resources", "pet-libraries", "yuexinmiao", "017-90d741d6.gif")
        };
        controller.Settings.Pets.Add(pet);
        controller.Settings.ActivePetId = pet.Id;
        if (args.Contains("--guide-check") || args.Contains("--guide-preview"))
        {
            var petWindow = new PetWindow(controller);
            petWindow.RefreshAppearance(pet.Path);
            petWindow.Place(new Point(180, Math.Max(0, SystemParameters.WorkArea.Bottom - petWindow.Height)));
            typeof(AppController).GetField("_petWindow", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(controller, petWindow);
            petWindow.Show();
            if (args.Contains("--guide-check"))
                app.Dispatcher.BeginInvoke(async () =>
                {
                    try
                    {
                        await GuideFlowChecks.Run(controller, petWindow, args.SkipWhile(a => a != "--guide-check").Skip(1).FirstOrDefault() ?? directory);
                        await OverlayFlowChecks.Run(controller, petWindow, args.SkipWhile(a => a != "--guide-check").Skip(1).FirstOrDefault() ?? directory);
                        await RenderSettings(controller, args.SkipWhile(a => a != "--guide-check").Skip(1).FirstOrDefault() ?? directory);
                        app.Shutdown(0);
                    }
                    catch (Exception error)
                    {
                        File.WriteAllText(Path.Combine(directory, "guide-failure.txt"), error.ToString());
                        Console.Error.WriteLine(error);
                        app.Shutdown(1);
                    }
                });
            else
            {
                controller.StartOnboardingIfNeeded();
                typeof(AppController).GetMethod("CreateTray", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(controller, null);
            }
            app.Run();
            return;
        }
        controller.ShowHall();
        var window = app.Windows.OfType<SettingsWindow>().Single();
        window.Title = "桌搭子 · 离线界面验收（独立临时数据）";
        // Exercise the real tray menu without starting pet timers or touching startup registration.
        typeof(AppController).GetMethod("CreateTray", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(controller, null);
        // The settings close button hides the window. Exit through the real tray command.
        app.Run();
    }

    private static async Task RenderSettings(AppController controller, string output)
    {
        controller.ShowSettings();
        var window = Application.Current.Windows.OfType<SettingsWindow>().Single();
        var tabs = (System.Windows.Controls.TabControl)window.FindName("MainTabs");
        foreach (var index in new[] { 1, 2, 3, 4, 7, 8 })
        {
            tabs.SelectedIndex = index;
            window.UpdateLayout();
            // Allow the async GIF preview loader and layout dispatcher to finish before capture.
            await Task.Delay(500);
            window.UpdateLayout();
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
                (int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight),
                96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            bitmap.Render(window);
            var png = new System.Windows.Media.Imaging.PngBitmapEncoder();
            png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(output, $"settings-{index}.png"));
            png.Save(stream);
        }
        window.Hide();
    }

    private sealed class OfflineHandler : HttpMessageHandler
    {
        private static bool _joined;
        private static string _name = "桌角的朋友";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(250, cancellationToken);
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/trial")
                return Json(new { allowed = true, remainingSeconds = 604800, expiresAt = DateTimeOffset.UtcNow.AddDays(7).ToString("O") });
            if (path == "/api/companion" && request.Method == HttpMethod.Patch)
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                _name = body.RootElement.GetProperty("displayName").GetString() ?? _name;
            }
            if (path == "/api/companion") return Profile();
            if (path == "/api/companion/hall")
            {
                if (request.Method == HttpMethod.Patch)
                {
                    using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                    _joined = body.RootElement.GetProperty("enabled").GetBoolean();
                    return Profile();
                }
                var people = new[] { "小鱼同学", "一颗快乐的土豆", "今天准时下班", "奶茶半糖" }
                    .Select((name, i) => new { id = $"trial:offline-{i}", displayName = name, online = true, lastSeenAt = DateTimeOffset.UtcNow.ToString("O") }).ToArray();
                return Json(new { enabled = _joined, people = _joined ? people : [] });
            }
            if (path.StartsWith("/api/companion/hall/deliveries/")) return Json(new { recipientName = "小鱼同学" });
            if (path == "/api/feedback") return Json(new
            {
                quota = new { active = 1, maximum = 3, remaining = 2 },
                items = new[] { new { id = "preview", type = "suggestion", title = "希望可以暂时安静一下", content = "工作时希望暂停主动消息，提醒仍然保留。", status = "resolved", adminNote = string.Concat(Enumerable.Repeat("已经加入暂停 1 小时功能，时间到后恢复原来的节奏。提醒不受影响，来访会排队保留。", 12)), createdAt = "2026-09-18T08:00:00Z", updatedAt = "2026-09-18T10:00:00Z" } }
            });
            if (path == "/api/update/latest") return Json(new { error = "No release available" }, HttpStatusCode.NotFound);
            return Json(new { error = "离线界面验收中，此操作不会连接服务器。" }, HttpStatusCode.ServiceUnavailable);
        }

        private static HttpResponseMessage Profile() => Json(new { displayName = _name, pairingCode = "", partner = (object?)null, hallEnabled = _joined, online = _joined });
        private static HttpResponseMessage Json(object value, HttpStatusCode status = HttpStatusCode.OK)
            => new(status) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
    }
}
