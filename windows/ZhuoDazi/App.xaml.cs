using System.Threading;
using System.Windows;
using System.Windows.Threading;
using ZhuoDazi.Services;

namespace ZhuoDazi;

public partial class App : System.Windows.Application
{
    private const string ShowSettingsSignalName = "Local\\ZhuoDazi.Native.ShowSettings";
    private const string ShowFakeAdSignalName = "Local\\ZhuoDazi.Native.ShowFakeAd";
    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _showSettingsSignal;
    private EventWaitHandle? _showFakeAdSignal;
    private Thread? _signalThread;
    private ActivationWindow? _activationWindow;
    private LicenseService? _licenseService;
    private DispatcherTimer? _trialTimer;
    private volatile bool _stopping;
    internal AppController? Controller { get; private set; }

    public App()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            WriteCrashLog(args.Exception);
            System.Windows.MessageBox.Show(args.Exception.Message, "桌搭子运行失败", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception error) WriteCrashLog(error);
        };
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _singleInstanceMutex = new Mutex(true, "Local\\ZhuoDazi.Native.Singleton", out var ownsMutex);
        if (!ownsMutex)
        {
            try
            {
                var signal = e.Args.Any(arg => arg.Equals("--fake-ad", StringComparison.OrdinalIgnoreCase))
                    ? ShowFakeAdSignalName
                    : ShowSettingsSignalName;
                EventWaitHandle.OpenExisting(signal).Set();
            }
            catch { }
            Shutdown();
            return;
        }

        try
        {
            StartSettingsSignalListener();
            _licenseService = new LicenseService();
            string? freeModeMessage = null;
            if (!_licenseService.IsActivated)
            {
                try
                {
                    var trial = await _licenseService.CheckTrialAsync();
                    if (trial.Allowed) ScheduleTrialCheck(trial.RemainingSeconds);
                    else freeModeMessage = "七天完整体验结束啦，基础陪伴继续。";
                }
                catch
                {
                    freeModeMessage = "先用基础陪伴就好，桌搭子还在。";
                }
            }
            Controller = new AppController(_licenseService);
            Controller.Start();
            if (freeModeMessage is not null) Controller.RefreshPremiumAccess(freeModeMessage);
            if (e.Args.Any(arg => arg.Equals("--settings", StringComparison.OrdinalIgnoreCase)))
                Controller.ShowSettings();
            if (e.Args.Any(arg => arg.Equals("--fake-ad", StringComparison.OrdinalIgnoreCase)))
                Controller.ShowFakeAdWindow();
        }
        catch (Exception error)
        {
            WriteCrashLog(error);
            System.Windows.MessageBox.Show(error.ToString(), "桌搭子启动失败", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _stopping = true;
        _trialTimer?.Stop();
        _showSettingsSignal?.Set();
        _showFakeAdSignal?.Set();
        _signalThread?.Join(TimeSpan.FromSeconds(1));
        _showSettingsSignal?.Dispose();
        _showFakeAdSignal?.Dispose();
        Controller?.Dispose();
        _licenseService?.Dispose();
        if (_singleInstanceMutex is not null)
        {
            try { _singleInstanceMutex.ReleaseMutex(); } catch (ApplicationException) { }
            _singleInstanceMutex.Dispose();
        }
        base.OnExit(e);
    }

    private bool ShowActivation(string? status = null, bool trialEnded = false)
    {
        if (_licenseService is null) return false;
        _activationWindow = new ActivationWindow(_licenseService, false, status, trialEnded);
        var activated = _activationWindow.ShowDialog() == true;
        _activationWindow = null;
        return activated;
    }

    private void ScheduleTrialCheck(int remainingSeconds)
    {
        _trialTimer?.Stop();
        _trialTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(Math.Clamp(remainingSeconds, 1, 24 * 60 * 60))
        };
        _trialTimer.Tick += TrialTimer_Tick;
        _trialTimer.Start();
    }

    private async void TrialTimer_Tick(object? sender, EventArgs e)
    {
        _trialTimer?.Stop();
        if (_licenseService is null || _licenseService.IsActivated) return;
        try
        {
            var trial = await _licenseService.CheckTrialAsync();
            if (trial.Allowed)
            {
                ScheduleTrialCheck(trial.RemainingSeconds);
                return;
            }
        }
        catch
        {
            // The full trial ends locally if its expiry cannot be confirmed.
        }

        _licenseService.EndTrial();
        Controller?.RefreshPremiumAccess("七天完整体验结束啦，基础陪伴继续。");
        ShowActivation("刚才试过的互动、小剧场和摸鱼模式，激活后都可以继续使用。", trialEnded: true);
        Controller?.RefreshPremiumAccess();
    }

    private void StartSettingsSignalListener()
    {
        _showSettingsSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSettingsSignalName);
        _showFakeAdSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowFakeAdSignalName);
        _signalThread = new Thread(() =>
        {
            var handles = new WaitHandle[] { _showSettingsSignal, _showFakeAdSignal };
            while (!_stopping)
            {
                var signaled = WaitHandle.WaitAny(handles, TimeSpan.FromMilliseconds(400));
                if (_stopping) return;
                if (signaled == 0) Dispatcher.BeginInvoke(() =>
                {
                    if (_activationWindow?.IsVisible == true)
                    {
                        _activationWindow.Activate();
                        return;
                    }
                    Controller?.ShowSettings();
                });
                else if (signaled == 1) Dispatcher.BeginInvoke(() => Controller?.ShowFakeAdWindow());
            }
        })
        {
            IsBackground = true,
            Name = "ZhuoDazi settings signal"
        };
        _signalThread.Start();
    }

    private static void WriteCrashLog(Exception error)
    {
        try
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "poko-desktop-pet", "logs");
            Directory.CreateDirectory(directory);
            File.AppendAllText(Path.Combine(directory, "native-crash.log"), $"[{DateTimeOffset.Now:O}]\n{error}\n\n");
        }
        catch { }
    }
}
