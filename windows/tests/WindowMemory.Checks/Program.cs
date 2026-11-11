using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZhuoDazi;
using ZhuoDazi.Controls;
using ZhuoDazi.Services;

internal static class Program
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly List<object> Measurements = [];

    [STAThread]
    private static int Main(string[] args)
    {
        var baseline = args.Contains("--baseline");
        var root = Path.Combine(Path.GetTempPath(), "zhuodazi-window-memory-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var assembly = typeof(AppController).Assembly;
        assembly.GetType("ZhuoDazi.Services.DeskPetHttp")!.GetProperty("HandlerFactory", BindingFlags.Static | BindingFlags.NonPublic)!
            .SetValue(null, (Func<HttpMessageHandler>)(() => new OfflineHandler()));
        var app = new App();
        app.InitializeComponent();
        var store = new SettingsStore(root);
        var settings = store.Load();
        settings.Onboarding.Dismissed = true;
        settings.Onboarding.UpgradeNoticePending = false;
        settings.AutoCheckUpdates = false;
        settings.StartWithWindows = false;
        settings.RemoteDefaultsApplied = true;
        settings.RandomPetEnabled = false;
        settings.RandomMovementEnabled = false;
        settings.MouseInteractionEnabled = false;
        settings.RandomInteractionsEnabled = false;
        settings.DailySpeechEnabled = false;
        settings.TheaterEnabled = false;
        settings.HideRecoveryHintSeen = true;
        var petPath = Path.Combine(AppContext.BaseDirectory, "resources/pet-libraries/default/017-90d741d6.gif");
        Require(File.Exists(petPath), "Published default GIF is missing.");
        settings.Pets.Add(new() { Id = "memory-pet", Name = "Memory fixture", Path = petPath });
        settings.ActivePetId = "memory-pet";
        store.Save(settings);
        var controller = new AppController(new LicenseService(root), store, localPreview: true);
        try
        {
            var configuration = Path.Combine(root, "preview.json");
            File.WriteAllText(configuration, JsonSerializer.Serialize(new {
                baseUrl = "http://127.0.0.1:9", dataDirectory = root,
                accountId = Guid.NewGuid().ToString(), licenseId = Guid.NewGuid().ToString(),
                installationId = Guid.NewGuid().ToString("N"), credential = new string('a', 64),
                signingPublicKeySpki = Convert.ToBase64String(new byte[32])
            }));
            var configType = assembly.GetType("ZhuoDazi.Services.PetMarketDebugConfig")!;
            var config = configType.GetMethod("Load", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [configuration]);
            typeof(AppController).GetMethod("ConfigurePetMarketDebug", Hidden)!.Invoke(controller, [config, true]);
            controller.Start(synchronizeStartupRegistration: false);
            var pet = Field<PetWindow>(controller, "_petWindow");
            Park(pet);
            Pump(500);
            Snapshot("pet-only");
            controller.ShowSettings();
            var window = Field<SettingsWindow>(controller, "_settingsWindow");
            Park(window);
            Pump(400);
            Snapshot("settings-open");
            var stats = (Panel)window.FindName("DailyStatsPanel");
            if (!baseline) Require(stats.Children.Count == 0, "Inactive daily page was eagerly built.");
            MeasureRefresh(controller, "visible-status-updates");
            window.ShowDailyJournalTab();
            Pump(200);
            Require(stats.Children.Count > 0, "Daily page did not render on selection.");
            var dailyCells = ((Panel)window.FindName("DailyCalendarGrid")).Children.Cast<UIElement>().ToArray();
            MeasureRefresh(controller, "daily-status-updates");
            if (!baseline) Require(((Panel)window.FindName("DailyCalendarGrid")).Children.Cast<UIElement>().SequenceEqual(dailyCells), "Unchanged journal rebuilt its calendar.");
            controller.Journal.RecordMood("happy");
            Pump(120);
            Require(!((Panel)window.FindName("DailyCalendarGrid")).Children.Cast<UIElement>().SequenceEqual(dailyCells), "A new mood did not refresh the calendar.");
            var draft = (TextBox)window.FindName("FeedbackContentText");
            draft.Text = "Keep this unsent feedback while the window is hidden.";
            window.Close(); // Production close-to-tray behavior, not a test substitute.
            Pump(200);
            Snapshot("settings-closed");
            var oldCells = ((Panel)window.FindName("DailyCalendarGrid")).Children.Cast<UIElement>().ToArray();
            MeasureRefresh(controller, "hidden-status-updates");
            if (!baseline)
            {
                Require(!Field<DispatcherTimer>(window, "_hallClock").IsEnabled, "Hidden settings clock is still active.");
                Require(((Panel)window.FindName("DailyCalendarGrid")).Children.Cast<UIElement>().SequenceEqual(oldCells), "Hidden daily UI rebuilt.");
            }
            controller.TogglePetVisibility();
            Pump(200);
            Snapshot("all-hidden");
            if (!baseline)
            {
                Require(((Image)pet.FindName("PetImage")).Source is null, "Hidden pet retained its bitmap.");
                Require(!Field<DispatcherTimer>(pet, "_motionTimer").IsEnabled, "Hidden pet kept its motion timer.");
                var move = pet.MoveToAsync(new Point(10, 10), 100, CancellationToken.None);
                Require(move.IsCompleted, "Hidden scripted motion hangs without its timer.");
                pet.LeaveScriptedMode();
            }
            controller.TogglePetVisibility();
            Pump(200);
            Require(((Image)pet.FindName("PetImage")).Source is not null, "Showing the pet did not restore its GIF.");
            controller.ShowSettings();
            Pump(200);
            Require(draft.Text.StartsWith("Keep this"), "Closing settings discarded an unsent draft.");
            Require(stats.Children.Count > 0, "Reopening daily page did not restore its display.");
            window.WindowState = WindowState.Minimized;
            Pump(100);
            MeasureRefresh(controller, "minimized-status-updates");
            if (!baseline) Require(!Field<DispatcherTimer>(window, "_hallClock").IsEnabled, "Minimized settings clock is still active.");
            window.WindowState = WindowState.Normal;
            Pump(100);
            var tabs = (TabControl)window.FindName("MainTabs");
            tabs.SelectedIndex = 0;
            Pump(100);
            if (!baseline) Require(stats.Children.Count == 0, "Leaving daily page retained generated controls.");
            for (var cycle = 0; cycle < 5; cycle++)
            {
                window.Close(); controller.TogglePetVisibility(); Pump(40);
                controller.TogglePetVisibility(); controller.ShowSettings(); Pump(40);
            }
            Snapshot("after-five-reopens");
            if (!baseline)
            {
                var petList = (ListBox)window.FindName("PetList");
                var libraryList = (ListBox)window.FindName("LibraryList");
                var originalPets = petList.ItemsSource;
                var originalLibraries = libraryList.ItemsSource;
                Field<Action>(controller, "StateChanged")(); Pump(50);
                Require(ReferenceEquals(originalPets, petList.ItemsSource) && ReferenceEquals(originalLibraries, libraryList.ItemsSource), "Unchanged lists rebound their items.");
                controller.Settings.Pets[0].Name = "Updated fixture name";
                Field<Action>(controller, "StateChanged")(); Pump(50);
                Require(!ReferenceEquals(originalPets, petList.ItemsSource), "Changed pet metadata was not refreshed.");
                var preview = (AnimatedGifImage)window.FindName("CurrentPetPreview");
                Require(preview.Source is not null, "Settings preview was not visible.");
                window.WindowState = WindowState.Minimized; Pump(50);
                Require(preview.Source is null, "Minimized settings kept its animated preview.");
                window.WindowState = WindowState.Normal; Pump(50);
                Require(preview.Source is not null, "Restored settings did not reload its preview.");
                window.Close();
                controller.SetSize(110);
                controller.ShowSettings(); Pump(50);
                Require(((Slider)window.FindName("SizeSlider")).Value == 110, "Reopened settings showed stale controller state.");
                CheckThumbnails(root);
                // The shop's payment timer must outlive page/window visibility.
                window.ShowPetMarketTab(); Pump(150);
                var shop = Field<object>(window, "_petMarketView");
                var market = Field<object>(shop, "_pets");
                window.Close(); Pump(100);
                Require(Field<DispatcherTimer>(market, "_timer").IsEnabled, "Hiding settings stopped payment polling.");
            }
            var output = JsonSerializer.Serialize(new { Baseline = baseline, Measurements, DataDirectory = root }, new JsonSerializerOptions { WriteIndented = true });
            Console.WriteLine(output);
            if (args.FirstOrDefault(a => a.StartsWith("--output=")) is { } file) File.WriteAllText(file[9..], output);
            Console.WriteLine("Real window lifecycle and isolated memory measurements completed.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { controller.Exit(); controller.Dispose(); }
    }

    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Hidden)!.GetValue(target)!;
    private static void Park(Window window) { window.Left = -32000; window.Top = -32000; window.ShowInTaskbar = false; window.ShowActivated = false; }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }
    private static void MeasureRefresh(AppController controller, string stage)
    {
        var callback = Field<Action>(controller, "StateChanged");
        var allocated = GC.GetTotalAllocatedBytes(true);
        for (var i = 0; i < 30; i++) { callback(); Pump(20); }
        Measurements.Add(new { Stage = stage, Updates = 30, AllocatedBytes = GC.GetTotalAllocatedBytes(true) - allocated });
    }
    private static void Snapshot(string stage)
    {
        // Benchmark-only stabilization, identical for both builds. Never trim the working set.
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); Pump(120);
        using var process = Process.GetCurrentProcess();
        Measurements.Add(new { Stage = stage, PrivateBytes = process.PrivateMemorySize64, WorkingSetBytes = process.WorkingSet64, ManagedBytes = GC.GetTotalMemory(false) });
    }

    private static void CheckThumbnails(string root)
    {
        foreach (var (width, height) in new[] { (1200, 800), (600, 1400) })
        {
            var path = Path.Combine(root, $"cover-{width}.png");
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, new byte[width * height * 4], width * 4)));
            using (var file = File.Create(path)) encoder.Save(file);
            var thumbnail = new ThumbnailImage { FilePath = path };
            var window = new Window { Content = thumbnail, Width = 170, Height = 120, ShowActivated = false, ShowInTaskbar = false };
            Park(window);
            try
            {
                Require(thumbnail.Source is null, "Unloaded thumbnail decoded eagerly.");
                window.Show(); Pump(50);
                var bitmap = thumbnail.Source as BitmapSource ?? throw new InvalidOperationException("Thumbnail did not load.");
                var limit = (int)Math.Ceiling(160 * Math.Max(1, VisualTreeHelper.GetDpi(thumbnail).DpiScaleX));
                Require(Math.Max(bitmap.PixelWidth, bitmap.PixelHeight) <= limit, "Thumbnail retained full-resolution pixels.");
                Require(Math.Abs((double)bitmap.PixelWidth / bitmap.PixelHeight - (double)width / height) < 0.02, "Thumbnail aspect ratio changed.");
                using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                window.Hide(); Require(thumbnail.Source is null, "Hidden thumbnail retained pixels.");
                window.Show(); Require(thumbnail.Source is not null, "Thumbnail did not reload.");
                window.WindowState = WindowState.Minimized; Pump(50);
                Require(thumbnail.Source is null, "Minimized thumbnail retained pixels.");
                window.WindowState = WindowState.Normal; Pump(50);
                Require(thumbnail.Source is not null, "Restored thumbnail did not reload.");
                thumbnail.FilePath = Path.Combine(root, "missing.png");
                Require(thumbnail.Source is null, "Missing thumbnail kept previous cover.");
            }
            finally { window.Close(); }
        }
    }
    private sealed class OfflineHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("{}") });
    }
}
