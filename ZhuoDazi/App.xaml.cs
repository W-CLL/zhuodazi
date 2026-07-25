using System.Threading;
using System.Windows;
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

    protected override void OnStartup(StartupEventArgs e)
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
            if (!_licenseService.IsActivated)
            {
                _activationWindow = new ActivationWindow(_licenseService);
                var activated = _activationWindow.ShowDialog() == true;
                _activationWindow = null;
                if (!activated)
                {
                    Shutdown();
                    return;
                }
            }
            Controller = new AppController(_licenseService);
            Controller.Start();
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
