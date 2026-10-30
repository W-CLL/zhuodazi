using System.Threading;
using System.Windows;
using System.Windows.Threading;
using ZhuoDazi.Services;

namespace ZhuoDazi;

public partial class App : System.Windows.Application
{
    private const int UninstallCanceledExitCode = 20;
    private const int UninstallHelperFailureExitCode = 21;
    private const string ShowSettingsSignalName = "Local\\ZhuoDazi.Native.ShowSettings";
    private const string ShowFakeAdSignalName = "Local\\ZhuoDazi.Native.ShowFakeAd";
    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _showSettingsSignal;
    private EventWaitHandle? _showFakeAdSignal;
    private Thread? _signalThread;
    private LicenseService? _licenseService;
    private DispatcherTimer? _trialTimer;
    private volatile bool _stopping;
    private bool _localMarketMode;
    private bool _petMarketDebugMode;
    private bool _appPreviewMode;
    private bool _uninstallFeedbackMode;
    private string _instanceSuffix = string.Empty;
    internal AppController? Controller { get; private set; }

    public App()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            if (_uninstallFeedbackMode)
            {
                args.Handled = true;
                Shutdown(UninstallHelperFailureExitCode);
                return;
            }
            if (_localMarketMode || _petMarketDebugMode || _appPreviewMode)
            {
                var title = _appPreviewMode ? "桌搭子完整本地预览" : _petMarketDebugMode ? "桌宠小铺调试" : "本地付款测试";
                System.Windows.MessageBox.Show($"{title}遇到错误，请关闭窗口后重试。", title, MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true;
                return;
            }
            WriteCrashLog(args.Exception);
            System.Windows.MessageBox.Show(args.Exception.Message, "桌搭子运行失败", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (!_uninstallFeedbackMode && !_localMarketMode && !_petMarketDebugMode && !_appPreviewMode
                && args.ExceptionObject is Exception error) WriteCrashLog(error);
        };
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        var localMarketArgument = Array.FindIndex(e.Args, argument => argument.Equals("--local-market-config", StringComparison.OrdinalIgnoreCase));
        var petMarketArgument = Array.FindIndex(e.Args, argument => argument.Equals("--pet-market-config", StringComparison.OrdinalIgnoreCase));
        var appPreviewArgument = Array.FindIndex(e.Args, argument => argument.Equals("--app-preview-config", StringComparison.OrdinalIgnoreCase));
        var uninstallFeedbackArgument = Array.FindIndex(e.Args, argument => argument.Equals("--uninstall-feedback", StringComparison.OrdinalIgnoreCase));
        _localMarketMode = localMarketArgument >= 0;
        _petMarketDebugMode = petMarketArgument >= 0;
        _appPreviewMode = appPreviewArgument >= 0;
        _uninstallFeedbackMode = uninstallFeedbackArgument >= 0;
        base.OnStartup(e);
        if (_uninstallFeedbackMode)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Shutdown(RunUninstallFeedback(e.Args, uninstallFeedbackArgument));
            return;
        }
        if (_appPreviewMode)
        {
            try
            {
                if (_localMarketMode || _petMarketDebugMode)
                    throw new InvalidOperationException("完整预览不能与独立小铺或付款测试模式同时启动。");
                if (appPreviewArgument + 1 >= e.Args.Length)
                    throw new InvalidOperationException("请提供完整本地预览配置文件的绝对路径。");
                var config = PetMarketDebugConfig.Load(e.Args[appPreviewArgument + 1]);
                var preview = new AppPreviewEnvironment(config.Origin, config.DataDirectory);
                _instanceSuffix = ".AppPreview";
                _singleInstanceMutex = new Mutex(true, "Local\\ZhuoDazi.AppPreview.Singleton", out var ownsPreviewMutex);
                if (!ownsPreviewMutex)
                {
                    try { EventWaitHandle.OpenExisting(ShowSettingsSignalName + _instanceSuffix).Set(); } catch { }
                    Shutdown();
                    return;
                }

                // Install the deny-by-default transport before constructing any service.
                // Production URLs are never rewritten or sent to the local test server.
                DeskPetHttp.HandlerFactory = () => preview.CreateHandler();
                config.DataDirectory = preview.DataDirectory;
                var store = new SettingsStore(preview.DataDirectory);
                var settings = store.Load();
                settings.StartWithWindows = false;
                settings.AutoCheckUpdates = false;
                settings.Onboarding.Dismissed = true;
                settings.Onboarding.UpgradeNoticePending = false;
                store.Save(settings);
                _licenseService = new LicenseService(preview.DataDirectory);
                Controller = new AppController(_licenseService, store, localPreview: true);
                Controller.ConfigurePetMarketDebug(config, fullAppPreview: true);
                StartSettingsSignalListener();
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                MainWindow = Controller.StartFullAppPreview();
            }
            catch (Exception error)
            {
                var message = error is InvalidOperationException ? error.Message : "完整本地预览无法启动，请检查本地配置和目录权限。";
                System.Windows.MessageBox.Show(message, "桌搭子完整本地预览", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
            }
            return;
        }
        if (_petMarketDebugMode)
        {
            try
            {
                if (petMarketArgument + 1 >= e.Args.Length)
                    throw new InvalidOperationException("请提供桌宠小铺调试配置文件的绝对路径。");
                var config = PetMarketDebugConfig.Load(e.Args[petMarketArgument + 1]);
                _singleInstanceMutex = new Mutex(true, "Local\\ZhuoDazi.PetMarket.Debug.Singleton", out var ownsMarketMutex);
                if (!ownsMarketMutex)
                {
                    System.Windows.MessageBox.Show("桌宠小铺调试窗口已经打开。", "桌宠小铺调试", MessageBoxButton.OK, MessageBoxImage.Information);
                    Shutdown();
                    return;
                }
                ShutdownMode = ShutdownMode.OnMainWindowClose;
                var store = new SettingsStore(config.DataDirectory);
                _licenseService = new LicenseService(config.DataDirectory);
                Controller = new AppController(_licenseService, store, localPreview: true);
                Controller.ConfigurePetMarketDebug(config);
                Controller.StartPetMarketDebugPreview();
                MainWindow = Controller.ShowPetMarketForDebug();
            }
            catch (Exception error)
            {
                var message = error is InvalidOperationException ? error.Message : "桌宠小铺调试无法启动，请检查本地配置和目录权限。";
                System.Windows.MessageBox.Show(message, "桌宠小铺调试启动失败", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
            }
            return;
        }
        if (_localMarketMode)
        {
            try
            {
                if (localMarketArgument + 1 >= e.Args.Length)
                    throw new InvalidOperationException("请提供本地市场配置文件的绝对路径。");
                var config = LocalMarketConfig.Load(e.Args[localMarketArgument + 1]);
                _singleInstanceMutex = new Mutex(true, "Local\\ZhuoDazi.LocalMarket.Debug.Singleton", out var ownsLocalMutex);
                if (!ownsLocalMutex)
                {
                    System.Windows.MessageBox.Show("本地付款测试窗口已经打开。", "本地付款测试", MessageBoxButton.OK, MessageBoxImage.Information);
                    Shutdown();
                    return;
                }
                ShutdownMode = ShutdownMode.OnMainWindowClose;
                MainWindow = new LocalMarketWindow(config);
                MainWindow.Show();
            }
            catch (Exception error)
            {
                var message = error is InvalidOperationException ? error.Message : "本地付款测试无法启动，请检查本地配置和目录权限。";
                System.Windows.MessageBox.Show(message, "本地付款测试启动失败", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
            }
            return;
        }
        var dailyPreview = e.Args.Contains("--daily-preview", StringComparer.OrdinalIgnoreCase);
        _instanceSuffix = dailyPreview ? ".DailyPreview" : string.Empty;
        _singleInstanceMutex = new Mutex(true, "Local\\ZhuoDazi.Native.Singleton" + _instanceSuffix, out var ownsMutex);
        if (!ownsMutex)
        {
            try
            {
                var signal = e.Args.Any(arg => arg.Equals("--fake-ad", StringComparison.OrdinalIgnoreCase))
                    ? ShowFakeAdSignalName
                    : ShowSettingsSignalName;
                EventWaitHandle.OpenExisting(signal + _instanceSuffix).Set();
            }
            catch { }
            Shutdown();
            return;
        }

        try
        {
            var configArgument = Array.FindIndex(e.Args, argument => argument.Equals("--daily-config-url", StringComparison.OrdinalIgnoreCase));
            if (dailyPreview && configArgument >= 0)
            {
                if (configArgument + 1 >= e.Args.Length || !Uri.TryCreate(e.Args[configArgument + 1], UriKind.Absolute, out var configUri))
                    throw new ArgumentException("本地预览配置地址无效。");
                RemoteConfigService.PreviewSiteSettingsUri = configUri;
            }
            StartSettingsSignalListener();
            var store = dailyPreview ? CreateDailyPreviewStore() : new SettingsStore();
            _licenseService = new LicenseService(store.DataDirectory);
            if (!dailyPreview)
            {
                try
                {
                    var batchStore = new InstallBatchStore(store.DataDirectory);
                    var batch = batchStore.GetOrCreate();
                    _ = ReportInstalledStartAsync(_licenseService.InstallEventIdentity, batchStore, batch);
                }
                catch { }
            }
            Controller = new AppController(_licenseService, store, dailyPreview);
            Controller.TrialVerificationRequested += async () => await VerifyTrialAsync();
            Controller.Start(synchronizeStartupRegistration: !dailyPreview);
            var verification = VerifyTrialAsync();
            if (e.Args.Any(arg => arg.Equals("--settings", StringComparison.OrdinalIgnoreCase)))
                Controller.ShowSettings();
            if (dailyPreview) Controller.ShowDailyJournal();
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

    private static int RunUninstallFeedback(string[] arguments, int argumentIndex)
    {
        if (argumentIndex < 0 || argumentIndex + 1 >= arguments.Length || string.IsNullOrWhiteSpace(arguments[argumentIndex + 1]))
            return UninstallHelperFailureExitCode;

        try
        {
            var completionPath = Path.GetFullPath(arguments[argumentIndex + 1]);
            var temporaryDirectory = Path.GetFullPath(Path.GetTempPath())
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!completionPath.StartsWith(temporaryDirectory, StringComparison.OrdinalIgnoreCase))
                return UninstallHelperFailureExitCode;
            var batchStore = new InstallBatchStore();
            var batch = batchStore.LoadExisting();
            using var licenses = batch is null ? null : LicenseService.TryOpenExisting(batchStore.DataDirectory);
            var identity = licenses?.InstallEventIdentity;
            using var service = new UninstallFeedbackService();
            var window = new UninstallFeedbackWindow(async (choice, cancellationToken) =>
            {
                if (identity is null || batch is null) return null;
                return await service.SubmitIntentAsync(identity, batch, choice, cancellationToken);
            });
            window.ShowDialog();
            if (window.Outcome != UninstallFeedbackOutcome.Proceed) return UninstallCanceledExitCode;
            if (window.Receipt is not null)
            {
                try { UninstallFeedbackService.WriteCompletionPayloadTemplate(completionPath, window.Receipt); }
                catch { }
            }
            return 0;
        }
        catch
        {
            return UninstallHelperFailureExitCode;
        }
    }

    private static async Task ReportInstalledStartAsync(
        InstallEventIdentity identity,
        InstallBatchStore store,
        InstallBatchRecord batch)
    {
        try
        {
            using var service = new UninstallFeedbackService();
            await service.ReportInstalledStartAsync(identity, store, batch);
        }
        catch { }
    }

    private async Task VerifyTrialAsync()
    {
        if (_stopping || _licenseService is null || Controller is null || _licenseService.IsActivated || Controller.IsTrialVerificationPending) return;
        Controller.SetTrialVerificationState(true);
        string? status = null;
        try
        {
            var trial = await _licenseService.CheckTrialAsync();
            Controller.RefreshDeviceActivity();
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
            Controller.RefreshDeviceActivity();
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
        _showSettingsSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSettingsSignalName + _instanceSuffix);
        _showFakeAdSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowFakeAdSignalName + _instanceSuffix);
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

    private static SettingsStore CreateDailyPreviewStore()
    {
        var installed = new SettingsStore();
        var preview = new SettingsStore(Path.Combine(installed.DataDirectory, "windows-daily-preview"));
        Directory.CreateDirectory(preview.DataDirectory);
        var licensePath = Path.Combine(preview.DataDirectory, "license.dat");
        var installedLicensePath = Path.Combine(installed.DataDirectory, "license.dat");
        if (!File.Exists(licensePath) && File.Exists(installedLicensePath))
            File.Copy(installedLicensePath, licensePath);
        if (!File.Exists(preview.SettingsPath))
        {
            var settings = installed.Load();
            settings.DailyFrequencyPreset = "relaxed";
            settings.StartWithWindows = false;
            settings.AutoCheckUpdates = false;
            settings.Onboarding.Dismissed = true;
            settings.Onboarding.UpgradeNoticePending = false;
            preview.Save(settings);
        }
        return preview;
    }
}
