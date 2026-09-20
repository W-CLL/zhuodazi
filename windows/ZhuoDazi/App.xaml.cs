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
            Controller = new AppController(_licenseService);
            Controller.TrialVerificationRequested += async () => await VerifyTrialAsync();
            Controller.Start();
            var verification = VerifyTrialAsync();
            if (e.Args.Any(arg => arg.Equals("--settings", StringComparison.OrdinalIgnoreCase)))
                Controller.ShowSettings();
            await verification;
            if (!_stopping && e.Args.Any(arg => arg.Equals("--fake-ad", StringComparison.OrdinalIgnoreCase)))
                Controller.ShowFakeAdWindow();
        }
        catch (Exception error)
        {
            WriteCrashLog(error);
            System.Windows.MessageBox.Show(error.ToString(), "桌搭子启动失败", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private async Task VerifyTrialAsync()
    {
        if (_stopping || _licenseService is null || Controller is null || _licenseService.IsActivated || Controller.IsTrialVerificationPending) return;
        Controller.SetTrialVerificationState(true);
        string? status = null;
        try
        {
            var trial = await _licenseService.CheckTrialAsync();
            if (_stopping) return;
            if (_licenseService.IsActivated) return;
            if (trial.Allowed) ScheduleTrialCheck(trial.RemainingSeconds);
            else status = "七天完整体验结束啦，基础陪伴继续。";
        }
        catch
        {
            if (_stopping) return;
            if (_licenseService.IsTrialActive)
            {
                ScheduleTrialCheck(Math.Min(_licenseService.RemainingTrialSeconds, 3600));
                status = "暂时连不上服务器，按本机尚未到期的体验继续；可在设置重试。";
            }
            else status = "暂时连不上服务器，基础陪伴和本地引导可用；可在设置重试体验验证。";
        }
        finally
        {
            if (!_stopping)
            {
                Controller.SetTrialVerificationState(false, status);
                Controller.RefreshPremiumAccess();
            }
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
        if (_stopping || _licenseService is null || _licenseService.IsActivated || Controller is null) return;
        if (Controller.IsTrialVerificationPending) { ScheduleTrialCheck(60); return; }
        Controller.SetTrialVerificationState(true);
        var expired = false;
        string? status = null;
        try
        {
            var trial = await _licenseService.CheckTrialAsync();
            if (_stopping || _licenseService.IsActivated) return;
            if (trial.Allowed) ScheduleTrialCheck(trial.RemainingSeconds);
            else expired = true;
        }
        catch
        {
            if (_stopping || _licenseService.IsActivated) return;
            if (_licenseService.IsTrialActive)
                ScheduleTrialCheck(Math.Min(_licenseService.RemainingTrialSeconds, 3600));
            else
            {
                status = "暂时无法验证体验，基础陪伴和本地引导仍可用；可以在设置重试。";
                ScheduleTrialCheck(60);
            }
        }
        finally
        {
            if (!_stopping)
            {
                Controller.SetTrialVerificationState(false, status);
                Controller.RefreshPremiumAccess();
            }
        }
        if (_stopping || _licenseService.IsActivated || !expired) return;
        Controller.RefreshPremiumAccess("七天完整体验结束啦，基础陪伴继续。");
        if (!Controller.IsGuideActive)
            Controller.ShowActivation(status: "刚才试过的互动、小剧场和摸鱼模式，激活后都可以继续使用。", trialEnded: true);
        Controller.RefreshPremiumAccess();
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
                    if (Windows.OfType<ActivationWindow>().FirstOrDefault(window => window.IsVisible) is { } activationWindow)
                    {
                        activationWindow.Activate();
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
