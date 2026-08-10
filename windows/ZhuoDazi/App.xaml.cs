using System.Threading;
using System.Windows;
using System.Windows.Threading;
using ZhuoDazi.Services;

namespace ZhuoDazi;

public partial class App : System.Windows.Application
{
    private const string ShowSettingsSignalName = "Local\\ZhuoDazi.Native.ShowSettings";
    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _showSettingsSignal;
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
            try { EventWaitHandle.OpenExisting(ShowSettingsSignalName).Set(); } catch { }
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
                    else freeModeMessage = "五分钟完整体验已结束，基础陪伴仍可免费使用。";
                }
                catch
                {
                    freeModeMessage = "已进入免费版，基础陪伴仍可继续使用。";
                }
            }
            Controller = new AppController(_licenseService);
            Controller.Start();
            if (freeModeMessage is not null) Controller.RefreshPremiumAccess(freeModeMessage);
            if (e.Args.Any(arg => arg.Equals("--settings", StringComparison.OrdinalIgnoreCase)))
                Controller.ShowSettings();
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
        _signalThread?.Join(TimeSpan.FromSeconds(1));
        _showSettingsSignal?.Dispose();
        Controller?.Dispose();
        _licenseService?.Dispose();
        if (_singleInstanceMutex is not null)
        {
            try { _singleInstanceMutex.ReleaseMutex(); } catch (ApplicationException) { }
            _singleInstanceMutex.Dispose();
        }
        base.OnExit(e);
    }

    private bool ShowActivation(string? status = null)
    {
        if (_licenseService is null) return false;
        _activationWindow = new ActivationWindow(_licenseService, false, status);
        var activated = _activationWindow.ShowDialog() == true;
        _activationWindow = null;
        return activated;
    }

    private void ScheduleTrialCheck(int remainingSeconds)
    {
        _trialTimer?.Stop();
        _trialTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(Math.Clamp(remainingSeconds, 1, 300))
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
        Controller?.RefreshPremiumAccess("五分钟完整体验结束啦，基础陪伴继续免费营业。");
        ShowActivation("激活后可继续使用小剧场、提醒、互动词包和外部 GIF 资源库。");
        Controller?.RefreshPremiumAccess();
    }

    private void StartSettingsSignalListener()
    {
        _showSettingsSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSettingsSignalName);
        _signalThread = new Thread(() =>
        {
            while (!_stopping)
            {
                _showSettingsSignal.WaitOne();
                if (!_stopping) Dispatcher.BeginInvoke(() =>
                {
                    if (_activationWindow?.IsVisible == true)
                    {
                        _activationWindow.Activate();
                        return;
                    }
                    Controller?.ShowSettings();
                });
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
