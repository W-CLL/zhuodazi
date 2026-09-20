using System.Drawing;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using ZhuoDazi.Models;
using ZhuoDazi.Services;
using ZhuoDazi.Interop;
using Forms = System.Windows.Forms;
using Point = System.Windows.Point;

namespace ZhuoDazi;

public sealed partial class AppController : IDisposable
{
    private readonly SettingsStore _store;
    private readonly LicenseService _licenses;
    private readonly AnalyticsService _analytics;
    private readonly StartupRegistrationService _startupRegistration = new();
    private readonly GifLibraryService _library = new();
    private readonly Random _random = new();
    private readonly DispatcherTimer _randomTimer = new();
    private readonly DispatcherTimer _reminderTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _idleTimer = new() { Interval = TimeSpan.FromSeconds(32) };
    private readonly DispatcherTimer _theaterTimer = new();
    private readonly DispatcherTimer _interactionTimer = new();
    private readonly DispatcherTimer _interactionSyncTimer = new() { Interval = TimeSpan.FromMinutes(15) };
    private readonly DispatcherTimer _companionTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    private readonly DispatcherTimer _trialDisplayTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly CompanionVisitorQueue _visitorQueue = new();
    private readonly RemoteConfigService _remoteConfig;
    private IReadOnlyList<string> _libraryFiles = [];
    private string? _activeLibraryPetPath;
    private PetWindow? _petWindow;
    private PetWindow? _companionWindow;
    private SettingsWindow? _settingsWindow;
    private FakeAdWindow? _fakeAdWindow;
    private Forms.NotifyIcon? _tray;
    private Forms.ContextMenuStrip? _trayMenu;
    private CancellationTokenSource? _theaterCancellation;
    private DateOnly? _lastMidnightSecret;
    private bool _theaterActive;
    private bool _interactionActive;
    private bool _interactionWasManual;
    private bool _interactionSyncing;
    private bool _interactionSyncRequested;
    private bool _companionSyncing;
    private bool _trialVisitBusy;
    private bool _autoUpdateStarted;
    private Task _remoteInitialization = Task.CompletedTask;
    private bool _disposed;

    public AppSettings Settings { get; }
    public UpdateService Updates { get; }
    public FeedbackService Feedback { get; }
    public InteractionService Interactions { get; }
    public CompanionService Companions { get; }
    public RemoteConfig RemoteConfig => _remoteConfig.Current;
    public event Action? StateChanged;
    public event Action? TrialClockChanged;
    public bool IsExiting { get; private set; }
    public bool HasPremiumAccess => _licenses.HasPremiumAccess;
    public bool HasActivatedLicense => _licenses.IsActivated;
    public bool IsTrialActive => _licenses.IsTrialActive;
    public int RemainingTrialSeconds => _licenses.RemainingTrialSeconds;
    public LibraryDefinition? ActiveLibrary => HasPremiumAccess
        ? Settings.Libraries.FirstOrDefault(item => item.Id == Settings.ActiveLibraryId)
        : null;
    public InteractionWordPackDefinition? ActiveInteractionWordPack => HasPremiumAccess
        ? Settings.InteractionWordPacks.FirstOrDefault(item => item.Id == Settings.ActiveInteractionWordPackId)
        : null;
    public string LibraryName => ActiveLibrary?.Name ?? "月薪喵";
    public string LibraryPath => ActiveLibrary?.Path ?? "内置：月薪喵";
    public int LibraryCount => _libraryFiles.Count;
    public int InteractionWordCount => ActiveInteractionWordPack?.WordCount ?? 0;
    public string LicenseSummary => _licenses.IsActivated ? _licenses.Summary
        : IsTrialVerificationPending ? "桌宠已就位 · 正在验证完整体验，基础陪伴和本地引导可用"
        : TrialVerificationStatus ?? (IsTrialActive
            ? $"完整体验还剩 {FormatTrialClock(RemainingTrialSeconds)}"
            : "基础陪伴中 · 桌宠会一直在");
    public string InteractionStatus => Interactions.StatusSummary;
    public bool IsQuiet => Settings.QuietUntilUtc is { } until && until > DateTimeOffset.UtcNow;
    public bool CanShowVisits => !IsGuideActive && !GuideBusy && !IsQuiet && !_theaterActive && !_interactionActive && !Settings.ClickThrough;
    public string CurrentPetName => Settings.Pets.FirstOrDefault(p => p.Path == CurrentPetPath())?.Name
        ?? (CurrentPetPath() is { } path ? Path.GetFileNameWithoutExtension(path) : "基础矢量桌宠");
    public string CurrentPetSource => CurrentPetPath() is null ? "内置基础形象 · 无 GIF"
        : Settings.Pets.Any(p => p.Path == CurrentPetPath()) ? "我的自定义 GIF" : $"来自 {LibraryName} 图鉴";
    public string QuietStatus => IsQuiet ? $"已暂停主动打扰，{Settings.QuietUntilUtc!.Value.LocalDateTime:HH:mm} 恢复" : "主动陪伴正常 · 设置自动保存";

    public AppController(LicenseService licenses, SettingsStore? store = null)
    {
        _store = store ?? new SettingsStore();
        _licenses = licenses;
        _analytics = new AnalyticsService(_licenses, _store.DataDirectory);
        Settings = _store.Load();
        _remoteConfig = new RemoteConfigService(_store);
        Updates = new UpdateService(_store, _licenses);
        Feedback = new FeedbackService(_licenses);
        Interactions = new InteractionService(_store, _licenses);
        Companions = new CompanionService(_store, _licenses);
        _visitorQueue.Restore(Companions.Inbox);
        Updates.StateChanged += OnUpdateStateChanged;
        _randomTimer.Tick += (_, _) => RandomizePet();
        _reminderTimer.Tick += (_, _) => CheckReminders();
        _idleTimer.Tick += (_, _) =>
        {
            if (IsGuideActive || IsQuiet || !Settings.DailySpeechEnabled || _interactionActive || _theaterActive || _visitorQueue.IsShowing) return;
            if (_petWindow is not { IsVisible: true }) return;
            _petWindow.ShowReaction(GetInteractionWord("idle", "你忙你的，我负责把角落占住。"));
        };
        _theaterTimer.Tick += async (_, _) =>
        {
            _theaterTimer.Stop();
            await RunTheaterAsync(false);
        };
        _interactionTimer.Tick += async (_, _) =>
        {
            _interactionTimer.Stop();
            await PresentRandomInteractionAsync(false);
        };
        _interactionSyncTimer.Tick += async (_, _) =>
        {
            _interactionSyncTimer.Stop();
            await RunInteractionSyncAsync();
        };
        _companionTimer.Tick += async (_, _) =>
        {
            _companionTimer.Stop();
            await PollCompanionAsync();
        };
        _trialDisplayTimer.Tick += (_, _) => RefreshTrialDisplay();

    }

    public void Start()
    {
        _remoteInitialization = RefreshRemoteConfigAsync();
        try { _startupRegistration.SetEnabled(Settings.StartWithWindows); } catch { }
        NormalizeReminderTimes();
        RefreshLibrary(true);
        _petWindow = new PetWindow(this);
        PositionPetWindow(_petWindow);
        _petWindow.RefreshAppearance(CurrentPetPath());
        _petWindow.Show();
        CreateTray();
        RestartRandomTimer();
        _reminderTimer.Start();
        _idleTimer.Start();
        RestartTheaterTimer();
        RestartInteractionTimer();
        if (HasPremiumAccess)
        {
            _interactionSyncTimer.Interval = TimeSpan.FromSeconds(5);
            _interactionSyncTimer.Start();
        }
        if (_licenses.IsActivated || _licenses.IsTrialActive)
        {
            _companionTimer.Start();
            if (_licenses.IsActivated) _ = RefreshCompanionAsync();
        }
        Save();
        RefreshTrialDisplay();
        StartOnboardingIfNeeded();
        _ = _analytics.TrackStartupAsync();
        MaybeCheckUpdates();
        _ = ShowQueuedVisitsAsync();
    }

    public async Task RefreshRemoteConfigAsync()
    {
        if (await _remoteConfig.RefreshAsync()) ApplyRemoteDefaultsIfNeeded();
        RestartInteractionTimer();
        RestartTheaterTimer();
        _petWindow?.RefreshAppearance(CurrentPetPath());
        RefreshTray();
        StateChanged?.Invoke();
        MaybeCheckUpdates();
    }

    public void ShowHall()
    {
        ShowSettings();
        _settingsWindow?.ShowHallTab();
    }

    public void SetDailySpeech(bool enabled)
    {
        Settings.DailySpeechEnabled = enabled;
        if (!enabled) _petWindow?.HideSpeech();
        SaveAndRefresh();
    }

    public void PauseForOneHour()
    {
        DismissGuide();
        Settings.QuietUntilUtc = DateTimeOffset.UtcNow.AddHours(1);
        CancelTheater();
        _visitorQueue.PauseActive();
        if (_interactionActive && !_interactionWasManual) _petWindow?.DismissCurrentInteraction();
        _petWindow?.HideSpeech();
        RestartInteractionTimer();
        RestartTheaterTimer();
        SaveAndRefresh();
    }

    public void ResumeCompanionship()
    {
        Settings.QuietUntilUtc = null;
        RestartInteractionTimer();
        RestartTheaterTimer();
        SaveAndRefresh();
        _ = ShowQueuedVisitsAsync();
    }

    public void StopCurrentPerformance()
    {
        if (IsGuideActive) StopGuideDemo();
        CancelTheater();
        _visitorQueue.CloseActive();
        _petWindow?.HideSpeech();
    }

    private void ApplyRemoteDefaultsIfNeeded()
    {
        if (Settings.RemoteDefaultsApplied) return;
        var config = RemoteConfig;
        Settings.Personality = config.Personality;
        Settings.InteractionMode = config.InteractionMode;
        Settings.TheaterIntervalSeconds = config.TheaterIntervalSeconds;
        Settings.RemoteDefaultsApplied = true;
        Save();
    }

    private void MaybeCheckUpdates()
    {
        if (_autoUpdateStarted || !Settings.AutoCheckUpdates || !RemoteConfig.AutoUpdates) return;
        _autoUpdateStarted = true;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        timer.Tick += async (_, _) =>
        {
            timer.Stop();
            try { await Updates.CheckAsync(false); } catch { }
        };
        timer.Start();
    }

    public void ShowSettings()
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(this);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }
        _settingsWindow.Show();
        if (_settingsWindow.WindowState == WindowState.Minimized) _settingsWindow.WindowState = WindowState.Normal;
        _settingsWindow.Activate();
    }

    public bool ShowActivation(Window? owner = null, string? status = null, bool trialEnded = false)
    {
        var previousInbox = Companions.Inbox;
        var previousWasTrialIdentity = !_licenses.IsActivated;
        var previousInstallation = _licenses.InstallationId;
        var activationWindow = new ActivationWindow(_licenses, _licenses.IsActivated, status, trialEnded);
        if (owner is not null) activationWindow.Owner = owner;
        var activated = activationWindow.ShowDialog() == true;
        if (activated)
        {
            var activationMessage = _licenses.ActivationSuccessMessage;
            try
            {
                if (previousWasTrialIdentity && previousInstallation == _licenses.InstallationId)
                    Companions.Inbox.ImportPendingFrom(previousInbox);
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                activationMessage += " 旧来访记录暂时无法恢复，原文件已保留。";
            }
            _visitorQueue.Restore(Companions.Inbox);
            _petWindow?.ShowReaction(activationMessage);
            RefreshPremiumAccess();
        }
        StateChanged?.Invoke();
        return activated;
    }

    public bool RequestPremiumAccess(string feature, Window? owner = null)
    {
        if (HasPremiumAccess) return true;
        if (IsTrialVerificationPending) { ShowActionMessage("正在验证完整体验，请稍候再试。基础陪伴和本地引导已经可以使用。"); return false; }
        return ShowActivation(owner ?? _settingsWindow, $"{feature}可以在完整体验里接着用，桌宠会一直在。");
    }

    public void RefreshPremiumAccess(string? message = null)
    {
        _visitorQueue.Restore(Companions.Inbox);
        RefreshLibrary(!IsGuideActive && !_theaterActive);
        RestartRandomTimer();
        RestartTheaterTimer();
        RestartInteractionTimer();
        _interactionSyncTimer.Stop();
        if (HasPremiumAccess) ScheduleInteractionSync();
        _companionTimer.Stop();
        if (_licenses.IsActivated || _licenses.IsTrialActive)
        {
            _companionTimer.Start();
            if (_licenses.IsActivated) _ = RefreshCompanionAsync();
        }
        if (!HasPremiumAccess && _fakeAdWindow is not null) _fakeAdWindow.Close();
        if (!IsGuideActive && !string.IsNullOrWhiteSpace(message)) _petWindow?.ShowReaction(message);
        RefreshTrialDisplay();
        SaveAndRefresh();
    }

    private string? PickDemoVisitGif()
    {
        var current = CurrentPetPath();
        var candidates = _libraryFiles
            .Where(path => !string.Equals(path, current, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(path, _activeLibraryPetPath, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (candidates.Count == 0)
            candidates = _library.Scan(null)
                .Where(path => !string.Equals(path, current, StringComparison.OrdinalIgnoreCase))
                .ToList();
        if (candidates.Count == 0) return _libraryFiles.FirstOrDefault() ?? current;
        return candidates[_random.Next(candidates.Count)];
    }

    public void ShowTrayMenu() => _trayMenu?.Show(Forms.Cursor.Position);

    public void ShowFakeAdWindow()
    {
        if (!RemoteConfig.FishMode)
        {
            _petWindow?.ShowReaction("摸鱼广告暂时关掉了。");
            return;
        }
        if (!RequestPremiumAccess("摸鱼模式")) return;
        if (_fakeAdWindow is null)
        {
            _fakeAdWindow = new FakeAdWindow(this);
            _fakeAdWindow.Closed += (_, _) => _fakeAdWindow = null;
        }
        _fakeAdWindow.Show();
        if (_fakeAdWindow.WindowState == WindowState.Minimized) _fakeAdWindow.WindowState = WindowState.Normal;
        _fakeAdWindow.Activate();
    }

    public string? CurrentPetPath()
    {
        if (Settings.RandomPetEnabled) return _activeLibraryPetPath;
        if (Settings.SelectedLibraryPetPath is { } selected && _libraryFiles.Contains(selected, StringComparer.OrdinalIgnoreCase) && File.Exists(selected)) return selected;
        return Settings.Pets.FirstOrDefault(item => item.Id == Settings.ActivePetId)?.Path;
    }

    public string GetInteractionWord(string action, string fallback)
    {
        if ((IsGuideActive || !Settings.DailySpeechEnabled || IsQuiet) && !action.StartsWith("theater", StringComparison.Ordinal)) return string.Empty;
        var activeWords = ActiveInteractionWordPack?.Words;
        if (activeWords is null || !activeWords.TryGetValue(action, out var words) || words.Count == 0) return fallback;
        return words[_random.Next(words.Count)];
    }

    public void SetSize(double value)
    {
        Settings.Size = Math.Clamp((int)Math.Round(value), 140, 300);
        SaveAndRefresh();
    }

    public void SetOpacity(double value)
    {
        Settings.Opacity = Math.Clamp((int)Math.Round(value), 20, 100);
        SaveAndRefresh();
    }

    public void SetAlwaysOnTop(bool value)
    {
        Settings.AlwaysOnTop = value;
        SaveAndRefresh();
    }

    public void SetStartWithWindows(bool enabled)
    {
        _startupRegistration.SetEnabled(enabled);
        Settings.StartWithWindows = enabled;
        SaveAndRefresh();
    }

    public void SetMirrored(bool value)
    {
        Settings.Mirrored = value;
        SaveAndRefresh();
    }

    public void SetPersonality(string personality)
    {
        Settings.RemoteDefaultsApplied = true;
        Settings.Personality = personality is "lively" or "shy" or "clingy" or "chaotic"
            ? personality : "lively";
        _petWindow?.RefreshBehavior();
        RestartTheaterTimer();
        SaveAndRefresh();
    }

    public void SetMouseInteraction(bool enabled)
    {
        Settings.MouseInteractionEnabled = enabled;
        _petWindow?.RefreshBehavior();
        SaveAndRefresh();
    }

    public void SetRandomMovement(bool enabled)
    {
        Settings.RandomMovementEnabled = enabled;
        _petWindow?.RefreshBehavior();
        SaveAndRefresh();
    }

    public void SetInteractionConfig(bool enabled, string mode)
    {
        if (!RequestPremiumAccess("随机互动")) return;
        Settings.RemoteDefaultsApplied = true;
        Settings.RandomInteractionsEnabled = enabled;
        Settings.InteractionMode = mode is "quiet" or "standard" or "lively" ? mode : "standard";
        Interactions.MarkProfileDirty(Settings.InteractionMode, enabled);
        RestartInteractionTimer();
        SaveAndRefresh();
        ScheduleInteractionSync();
    }

    public void StartRandomInteraction()
    {
        if (!PrepareManualScene()) return;
        if (RequestPremiumAccess("互动内容")) _ = PresentRandomInteractionAsync(true);
    }

    public async Task RefreshCompanionAsync(CancellationToken cancellationToken = default)
    {
        if (!HasPremiumAccess) return;
        await Companions.RefreshProfileAsync(cancellationToken);
        RefreshTray();
        StateChanged?.Invoke();
    }

    public async Task<IReadOnlyList<CompanionHallPerson>> RefreshCompanionHallAsync(
        CancellationToken cancellationToken = default)
    {
        if (!RemoteConfig.CompanionHall) return [];
        if (!HasPremiumAccess) return [];
        var people = await Companions.RefreshHallAsync(cancellationToken);
        StateChanged?.Invoke();
        return people;
    }

    public async Task SetCompanionHallEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        if (!RemoteConfig.CompanionHall) throw new InvalidOperationException("桌宠大厅暂时关闭。");
        if (!HasPremiumAccess) throw new InvalidOperationException("试用期或正式激活后可以加入桌宠大厅。");
        await Companions.SetHallEnabledAsync(enabled, cancellationToken);
        StateChanged?.Invoke();
    }

    public async Task UpdateCompanionNameAsync(string displayName, CancellationToken cancellationToken = default)
    {
        if (!HasPremiumAccess) throw new InvalidOperationException("试用期或正式激活后可以设置大厅昵称。");
        if (string.IsNullOrWhiteSpace(displayName) || displayName.Trim().EnumerateRunes().Count() > 12)
            throw new InvalidOperationException("昵称请填写 1–12 个字符。");
        await Companions.UpdateNameAsync(displayName, cancellationToken);
        RefreshTray();
        StateChanged?.Invoke();
    }

    public async Task PairCompanionAsync(string code, CancellationToken cancellationToken = default)
    {
        if (!_licenses.IsActivated) throw new InvalidOperationException("激活完整版本后可以使用搭子联机。");
        await Companions.PairAsync(code, cancellationToken);
        RefreshTray();
        StateChanged?.Invoke();
    }

    public async Task UnpairCompanionAsync(CancellationToken cancellationToken = default)
    {
        if (!_licenses.IsActivated) return;
        await Companions.UnpairAsync(cancellationToken);
        RefreshTray();
        StateChanged?.Invoke();
    }

    public async Task PlayTrialVisitAsync(string category)
    {
        if (!PrepareManualScene()) return;
        if (!RemoteConfig.TrialVisits)
        {
            _petWindow?.ShowReaction("体验来访暂时关掉了。");
            return;
        }
        if (!IsTrialActive)
        {
            ShowActivation(_settingsWindow, "演示来访在有效体验期可用；体验期也能去大厅，正式激活后还可绑定私人搭子。");
            return;
        }
        if (_trialVisitBusy || _visitorQueue.IsShowing)
        {
            _petWindow?.ShowReaction("来访还在演，稍等一下。");
            return;
        }
        _trialVisitBusy = true;
        try
        {
            var visit = await Companions.PlayTrialVisitAsync(category);
            visit = visit with { SenderName = $"演示来访 · {visit.SenderName}" };
            _visitorQueue.EnqueueFirst(visit);
            await ShowQueuedVisitsAsync(true);
            if (!IsGuideActive && _petWindow is { IsVisible: true })
                _petWindow.ShowReaction("刚才是演示来访。体验期可去桌宠大厅；正式激活后还可绑定私人搭子。");
        }
        catch (Exception error)
        {
            _petWindow?.ShowReaction(NetworkConnectionErrors.ForUser(error, "暂时叫不来，请稍后重试。"));
        }
        finally
        {
            _trialVisitBusy = false;
        }
    }

    public async Task SendCurrentGifToCompanionAsync(CancellationToken cancellationToken = default)
    {
        if (!_licenses.IsActivated)
        {
            ShowActivation(_settingsWindow, "绑定一位熟人后，可以把当前 GIF 发到对方桌角。");
            return;
        }
        var path = CurrentPetPath();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new InvalidOperationException("当前桌宠没有可发送的 GIF。");
        var recipient = await Companions.SendCurrentGifAsync(path, cancellationToken);
        _petWindow?.ShowReaction($"已经去找 {recipient} 啦。");
    }

    public async Task SendCurrentGifToHallAsync(
        string recipientId,
        string message,
        CancellationToken cancellationToken = default)
    {
        if (!HasPremiumAccess)
        {
            throw new InvalidOperationException("试用期或正式激活后可以向大厅用户发送 GIF。");
        }
        var path = CurrentPetPath();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new InvalidOperationException("当前桌宠没有可发送的 GIF。");
        var recipient = await Companions.SendCurrentGifToHallAsync(path, recipientId, message, cancellationToken);
        _petWindow?.ShowReaction($"已经给 {recipient} 发去一只表情啦。");
    }

    public async Task<int> SyncInteractionContentAsync(CancellationToken cancellationToken = default)
    {
        await _remoteInitialization;
        if (!HasPremiumAccess) throw new InvalidOperationException("激活后可同步互动内容。");
        var profile = await Interactions.SyncProfileAsync(
            Settings.InteractionMode,
            Settings.RandomInteractionsEnabled,
            cancellationToken);
        ApplyInteractionProfile(profile);
        await Interactions.FlushEventsAsync(cancellationToken);
        var added = await Interactions.RefillAsync(cancellationToken);
        StateChanged?.Invoke();
        return added;
    }

    public async Task<int> DownloadInteractionPackAsync(CancellationToken cancellationToken = default)
    {
        if (!HasPremiumAccess) throw new InvalidOperationException("激活后可下载互动内容包。");
        var count = await Interactions.DownloadOfflinePackAsync(cancellationToken);
        StateChanged?.Invoke();
        return count;
    }

    public void SetTheaterConfig(bool enabled, int intervalSeconds)
    {
        if (!RequestPremiumAccess("小剧场")) return;
        Settings.RemoteDefaultsApplied = true;
        Settings.TheaterEnabled = enabled;
        Settings.TheaterIntervalSeconds = intervalSeconds is 60 or 180 or 300 or 600 or 1800
            ? intervalSeconds : 300;
        RestartTheaterTimer();
        SaveAndRefresh();
    }

    public void SetClickThrough(bool value)
    {
        if (value) DismissGuide();
        Settings.ClickThrough = value;
        SaveAndRefresh();
        if (!value)
        {
            _petWindow?.Show();
            _petWindow?.Activate();
        }
    }

    public void SavePosition(double left, double top)
    {
        Settings.Position = new WindowPosition { X = left, Y = top };
        Save();
    }

    public PetDefinition AddPet(string sourcePath)
    {
        if (Settings.Pets.Count >= 3) throw new InvalidOperationException("最多只能添加 3 个自定义桌宠。");
        var copiedPath = _store.ImportPet(sourcePath);
        var pet = new PetDefinition
        {
            Id = $"pet-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
            Name = Path.GetFileNameWithoutExtension(sourcePath),
            Path = copiedPath
        };
        Settings.Pets.Add(pet);
        Settings.ActivePetId = pet.Id;
        Settings.SelectedLibraryPetPath = null;
        Settings.RandomPetEnabled = false;
        RestartRandomTimer();
        SaveAndRefresh();
        return pet;
    }

    public void SelectPet(string? id)
    {
        Settings.ActivePetId = id;
        Settings.SelectedLibraryPetPath = null;
        Settings.RandomPetEnabled = false;
        RestartRandomTimer();
        SaveAndRefresh();
    }

    public void DeletePet(string id)
    {
        var pet = Settings.Pets.FirstOrDefault(item => item.Id == id);
        if (pet is null) return;
        Settings.Pets.Remove(pet);
        if (Settings.ActivePetId == id) Settings.ActivePetId = Settings.Pets.FirstOrDefault()?.Id;
        try
        {
            var petsRoot = Path.GetFullPath(_store.PetsDirectory) + Path.DirectorySeparatorChar;
            var fullPath = Path.GetFullPath(pet.Path);
            if (fullPath.StartsWith(petsRoot, StringComparison.OrdinalIgnoreCase)) File.Delete(fullPath);
        }
        catch { }
        SaveAndRefresh();
    }

    public LibraryDefinition AddLibraryDirectory(string directory)
    {
        if (!HasPremiumAccess) throw new InvalidOperationException("激活后可绑定外部 GIF 资源库。");
        if (!Directory.Exists(directory))
            throw new InvalidOperationException("GIF 资源库目录不存在。");
        if (Settings.Libraries.Count >= 3) throw new InvalidOperationException("最多只能绑定 3 个 GIF 资源库目录。");
        var fullPath = Path.GetFullPath(directory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (Settings.Libraries.Any(item => string.Equals(
            Path.GetFullPath(item.Path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            fullPath,
            StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("该资源库目录已经绑定。");

        var library = new LibraryDefinition
        {
            Id = $"library-{Guid.NewGuid():N}",
            Name = Path.GetFileName(fullPath) is { Length: > 0 } name ? name : fullPath,
            Path = fullPath
        };
        Settings.Libraries.Add(library);
        Settings.ActiveLibraryId = library.Id;
        RefreshLibrary(true);
        Settings.SelectedLibraryPetPath = _activeLibraryPetPath;
        RestartRandomTimer();
        SaveAndRefresh();
        return library;
    }

    public void SelectLibrary(string? id)
    {
        if (id is not null && !RequestPremiumAccess("外部 GIF 资源库")) return;
        if (id is not null && !Settings.Libraries.Any(item => item.Id == id))
            throw new InvalidOperationException("所选资源库不存在。");
        Settings.ActiveLibraryId = id;
        RefreshLibrary(true);
        Settings.SelectedLibraryPetPath = _activeLibraryPetPath;
        RestartRandomTimer();
        SaveAndRefresh();
    }

    public void DeleteLibrary(string id)
    {
        var library = Settings.Libraries.FirstOrDefault(item => item.Id == id);
        if (library is null) return;
        Settings.Libraries.Remove(library);
        if (Settings.ActiveLibraryId == id) Settings.ActiveLibraryId = null;
        RefreshLibrary(!IsGuideActive);
        RestartRandomTimer();
        SaveAndRefresh();
    }

    public void SetRandomPetConfig(bool enabled, int intervalSeconds)
    {
        if (!enabled && Settings.RandomPetEnabled) Settings.SelectedLibraryPetPath = _activeLibraryPetPath;
        Settings.RandomPetEnabled = enabled;
        Settings.RandomPetIntervalSeconds = intervalSeconds is 30 or 60 or 300 or 600 or 1800 ? intervalSeconds : 30;
        if (enabled && _activeLibraryPetPath is null) _activeLibraryPetPath = _library.Pick(_libraryFiles);
        RestartRandomTimer();
        SaveAndRefresh();
    }

    public void RandomizePet()
    {
        if (IsGuideActive || GuideBusy || _theaterActive || _interactionActive)
        {
            RestartRandomTimer();
            return;
        }
        if (_libraryFiles.Count == 0) return;
        _activeLibraryPetPath = _library.Pick(_libraryFiles, _activeLibraryPetPath);
        Settings.SelectedLibraryPetPath = _activeLibraryPetPath;
        RestartRandomTimer();
        SaveAndRefresh();
        _petWindow?.ShowReaction(GetInteractionWord("switch", "换班了，上一位把零食吃完就跑。"));
    }

    public void StartTheater()
    {
        if (!PrepareManualScene()) return;
        if (RequestPremiumAccess("小剧场")) _ = RunTheaterAsync(true);
    }

    public InteractionWordPackDefinition ImportInteractionWords(string filePath)
        => ImportInteractionWords([filePath])[0];

    public IReadOnlyList<InteractionWordPackDefinition> ImportInteractionWords(IEnumerable<string> filePaths)
    {
        if (!RequestPremiumAccess("互动词包导入")) return [];
        var paths = filePaths.ToArray();
        if (paths.Length == 0) return [];
        if (Settings.InteractionWordPacks.Count + paths.Length > 5)
            throw new InvalidOperationException("最多只能导入 5 个互动词包。");

        var imported = new List<InteractionWordPackDefinition>();
        foreach (var filePath in paths)
        {
            var baseName = Path.GetFileNameWithoutExtension(filePath);
            var name = baseName;
            var suffix = 2;
            while (Settings.InteractionWordPacks.Concat(imported).Any(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                name = $"{baseName} {suffix++}";
            imported.Add(new InteractionWordPackDefinition
            {
                Id = $"words-{Guid.NewGuid():N}",
                Name = name,
                Words = InteractionWordsService.Parse(filePath)
            });
        }

        Settings.InteractionWordPacks.AddRange(imported);
        Settings.ActiveInteractionWordPackId = imported[^1].Id;
        SaveAndRefresh();
        return imported;
    }

    public void SelectInteractionWordPack(string? id)
    {
        if (id is not null && !RequestPremiumAccess("互动词包")) return;
        if (id is not null && !Settings.InteractionWordPacks.Any(item => item.Id == id))
            throw new InvalidOperationException("所选互动词包不存在。");
        Settings.ActiveInteractionWordPackId = id;
        SaveAndRefresh();
    }

    public void DeleteInteractionWordPack(string id)
    {
        var pack = Settings.InteractionWordPacks.FirstOrDefault(item => item.Id == id);
        if (pack is null) return;
        Settings.InteractionWordPacks.Remove(pack);
        if (Settings.ActiveInteractionWordPackId == id) Settings.ActiveInteractionWordPackId = null;
        SaveAndRefresh();
    }

    public TheaterScriptDefinition ImportTheaterScript(string filePath)
        => ImportTheaterScripts([filePath])[0];

    public IReadOnlyList<TheaterScriptDefinition> ImportTheaterScripts(IEnumerable<string> filePaths)
    {
        if (!RequestPremiumAccess("小剧场剧本导入")) return [];
        var paths = filePaths.ToArray();
        if (paths.Length == 0) return [];
        if (Settings.TheaterScripts.Count + paths.Length > 10)
            throw new InvalidOperationException("最多只能导入 10 个小剧场剧本。");

        var imported = new List<TheaterScriptDefinition>();
        foreach (var filePath in paths)
        {
            var script = TheaterScriptService.Parse(filePath);
            var baseName = script.Name;
            var suffix = 2;
            while (Settings.TheaterScripts.Concat(imported).Any(item => item.Name.Equals(script.Name, StringComparison.OrdinalIgnoreCase)))
                script.Name = $"{baseName} {suffix++}";
            imported.Add(script);
        }

        Settings.TheaterScripts.AddRange(imported);
        SaveAndRefresh();
        return imported;
    }

    public void DeleteTheaterScript(string id)
    {
        Settings.TheaterScripts.RemoveAll(item => item.Id == id);
        SaveAndRefresh();
    }

    public void SaveReminder(ReminderDefinition reminder)
    {
        if (!RequestPremiumAccess("提醒")) return;
        reminder.LocalTime = ReminderSchedule.ValidateAndAdvance(reminder.LocalTime, reminder.Enabled, reminder.RepeatDaily, DateTime.Now);
        var existing = Settings.Reminders.FindIndex(item => item.Id == reminder.Id);
        if (existing < 0 && Settings.Reminders.Count >= 20) throw new InvalidOperationException("最多只能添加 20 个提醒。");
        reminder.Message = string.Join(' ', reminder.Message.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (reminder.Message.Length == 0) reminder.Message = "休息一下吧";
        if (reminder.Message.Length > 40) reminder.Message = reminder.Message[..40];
        if (existing >= 0) Settings.Reminders[existing] = reminder;
        else Settings.Reminders.Add(reminder);
        SaveAndRefresh();
    }

    public void DeleteReminder(string id)
    {
        Settings.Reminders.RemoveAll(item => item.Id == id);
        SaveAndRefresh();
    }

    public void SetAutoCheckUpdates(bool enabled)
    {
        Settings.AutoCheckUpdates = enabled;
        SaveAndRefresh();
    }

    public void IgnoreUpdate(string version)
    {
        Settings.IgnoredUpdateVersion = version;
        SaveAndRefresh();
    }

    /// <summary>
    /// 请求终止正在进行的小剧场。脚本运动依赖主窗口的帧循环，
    /// 窗口一旦隐藏帧循环就停摆，必须先取消演出才能让 RunTheaterAsync 正常收尾。
    /// </summary>
    private void CancelTheater()
    {
        if (!_theaterActive) return;
        try { _theaterCancellation?.Cancel(); } catch (ObjectDisposedException) { }
    }

    public void TogglePetVisibility()
    {
        if (_petWindow is null) return;
        if (_petWindow.IsVisible)
        {
            DismissGuide();
            CancelTheater();
            _visitorQueue.PauseActive();
            _petWindow.Hide();
            if (!Settings.HideRecoveryHintSeen)
            {
                _tray?.ShowBalloonTip(5000, "桌宠已隐藏", "从托盘图标打开菜单，选择“显示桌宠”就能找回。退出应用需选择“退出桌搭子”。", Forms.ToolTipIcon.Info);
                Settings.HideRecoveryHintSeen = true;
                Save();
            }
        }
        else _petWindow.Show();
        RefreshTray();
    }

    public void Exit()
    {
        IsExiting = true;
        DismissGuide();
        _theaterCancellation?.Cancel();
        if (_companionWindow?.IsLoaded == true) _companionWindow.Close();
        _visitorQueue.PauseActive();
        _fakeAdWindow?.Close();
        _settingsWindow?.Close();
        _petWindow?.Close();
        System.Windows.Application.Current.Shutdown();
    }

    private void RefreshLibrary(bool pickNew)
    {
        _libraryFiles = _library.Scan(ActiveLibrary?.Path);
        if (pickNew || !_libraryFiles.Contains(_activeLibraryPetPath, StringComparer.OrdinalIgnoreCase))
            _activeLibraryPetPath = _library.Pick(_libraryFiles, _activeLibraryPetPath);
    }

    private void RestartRandomTimer()
    {
        _randomTimer.Stop();
        if (IsGuideActive || GuideBusy || !Settings.RandomPetEnabled || _libraryFiles.Count < 2) return;
        _randomTimer.Interval = TimeSpan.FromSeconds(Settings.RandomPetIntervalSeconds);
        _randomTimer.Start();
    }

    private void RestartTheaterTimer()
    {
        _theaterTimer.Stop();
        if (_disposed || IsExiting || IsGuideActive || GuideBusy || IsQuiet || !HasPremiumAccess || !Settings.TheaterEnabled) return;
        _theaterTimer.Interval = TimeSpan.FromSeconds(Settings.TheaterIntervalSeconds);
        _theaterTimer.Start();
    }

    private void RestartInteractionTimer()
    {
        _interactionTimer.Stop();
        if (_disposed || IsExiting || IsGuideActive || GuideBusy || IsQuiet || !HasPremiumAccess || !Settings.RandomInteractionsEnabled) return;
        _interactionTimer.Interval = InteractionScheduler.NextDelay(Settings.InteractionMode);
        _interactionTimer.Start();
    }

    private async Task PresentRandomInteractionAsync(bool manual)
    {
        _interactionTimer.Stop();
        if (!HasPremiumAccess) return;
        if (_interactionActive || _theaterActive || _petWindow is not { IsVisible: true } pet)
        {
            RestartInteractionTimer();
            return;
        }
        if (IsGuideActive || GuideBusy || !manual && (IsQuiet || Settings.ClickThrough || !Settings.RandomInteractionsEnabled))
        {
            RestartInteractionTimer();
            return;
        }
        if (manual && Settings.ClickThrough) SetClickThrough(false);

        _interactionActive = true;
        _interactionWasManual = manual;
        try
        {
            var moodDue = Interactions.IsMoodPromptDue(DateTimeOffset.UtcNow);
            if (moodDue && (Interactions.CachedContentCount == 0 || _random.Next(4) == 0))
            {
                ShowMoodInteraction(pet);
                return;
            }
            if (Interactions.CachedContentCount == 0) await Interactions.RefillAsync();
            if (IsExiting || !manual && IsQuiet || _theaterActive || _petWindow is not { IsVisible: true })
            {
                FinishInteraction();
                return;
            }

            var item = Interactions.TakeNextContent();
            if (item is null)
            {
                if (moodDue) ShowMoodInteraction(pet);
                else
                {
                    if (manual) pet.ShowReaction("稍后再来找我玩吧。");
                    FinishInteraction();
                }
                return;
            }
            ShowContentInteraction(pet, item);
        }
        catch (Exception error)
        {
            if (manual) pet.ShowReaction(error.Message);
            FinishInteraction();
        }
    }

    private void ShowMoodInteraction(PetWindow pet)
    {
        Interactions.MarkMoodPrompted(DateTimeOffset.UtcNow);
        pet.ShowInteraction(
            "随手问候",
            "今天心情怎么样？",
            [
                new("开心", "happy"),
                new("还可以", "okay"),
                new("不咋地", "low")
            ],
            choice =>
            {
                if (choice is not null)
                {
                    Interactions.RecordMood(choice.Value);
                    pet.ShowReaction(choice.Value switch
                    {
                        "happy" => "那就把这份开心多留一会儿。",
                        "low" => "先不用硬撑，我在这儿陪你一会儿。",
                        _ => "平平稳稳也很好，慢慢来。"
                    });
                }
                FinishInteraction();
            });
    }

    private void ShowContentInteraction(PetWindow pet, InteractionContentItem item)
    {
        if (item.Type == "joke")
        {
            pet.ShowInteraction(
                "冷笑话时间",
                item.Prompt,
                [new("看答案", "reveal", true)],
                choice =>
                {
                    if (choice is null)
                    {
                        FinishInteraction();
                        return;
                    }
                    Interactions.RecordJoke(item.Id);
                    ShowAnswerInteraction(pet, "答案", FormatContentAnswer(item), null);
                });
            return;
        }

        if (item.Type is "tip" or "care")
        {
            var noticeTitle = item.Type == "tip" ? "生活小贴士" : "关心你一下";
            var button = item.Type == "tip" ? "记下了" : "我知道了";
            pet.ShowInteraction(
                noticeTitle,
                $"{item.Prompt}\n\n{FormatContentAnswer(item)}",
                [new(button, "acknowledge", true)],
                _ => FinishInteraction());
            return;
        }

        var title = item.Type switch
        {
            "math" => "来道数学题",
            "riddle" => "脑筋急转弯",
            _ => "趣味知识"
        };
        if (item.Choices.Count > 0)
        {
            var choices = item.Choices.Select((value, index) =>
                new PetInteractionChoice(value, index.ToString())).ToList();
            pet.ShowInteraction(title, item.Prompt, choices, choice =>
            {
                if (choice is null)
                {
                    FinishInteraction();
                    return;
                }
                var selected = item.Choices[int.Parse(choice.Value)];
                var correct = AnswersMatch(selected, item.Answer);
                Interactions.RecordQuiz(item.Id, correct);
                ShowAnswerInteraction(
                    pet,
                    correct ? "答对了" : "答案揭晓",
                    FormatContentAnswer(item),
                    correct);
            });
            return;
        }

        pet.ShowInteraction(
            title,
            item.Prompt,
            [new("查看答案", "reveal", true)],
            choice =>
            {
                if (choice is null)
                {
                    FinishInteraction();
                    return;
                }
                pet.ShowInteraction(
                    "答案",
                    FormatContentAnswer(item),
                    [new("答对了", "correct", true), new("没答对", "wrong")],
                    result =>
                    {
                        if (result is not null)
                            Interactions.RecordQuiz(item.Id, result.Value == "correct");
                        FinishInteraction();
                    });
            });
    }

    private void ShowAnswerInteraction(PetWindow pet, string title, string message, bool? correct)
    {
        pet.ShowInteraction(
            title,
            message,
            [new(correct == true ? "收下这分" : "知道了", "done", true)],
            _ => FinishInteraction());
    }

    private static string FormatContentAnswer(InteractionContentItem item)
    {
        var answer = string.IsNullOrWhiteSpace(item.Answer) ? "答案暂缺" : item.Answer.Trim();
        var explanation = item.Explanation.Trim();
        return explanation.Length == 0 || explanation.Equals(answer, StringComparison.Ordinal)
            ? answer
            : $"{answer}\n{explanation}";
    }

    private static bool AnswersMatch(string selected, string answer)
        => selected.Trim().Equals(answer.Trim(), StringComparison.OrdinalIgnoreCase);

    private void FinishInteraction()
    {
        _interactionActive = false;
        RestartInteractionTimer();
        StateChanged?.Invoke();
        if (Interactions.ShouldFlush) _ = FlushInteractionEventsAsync();
    }

    private async Task FlushInteractionEventsAsync()
    {
        try { await Interactions.FlushEventsAsync(); }
        catch { }
        StateChanged?.Invoke();
    }

    private void ScheduleInteractionSync()
    {
        if (!HasPremiumAccess) return;
        _interactionSyncRequested = true;
        if (_interactionSyncing) return;
        _interactionSyncTimer.Stop();
        if (_disposed || IsExiting) return;
        _interactionSyncTimer.Interval = TimeSpan.FromSeconds(2);
        _interactionSyncTimer.Start();
    }

    private async Task RunInteractionSyncAsync()
    {
        await _remoteInitialization;
        if (_disposed || IsExiting || !HasPremiumAccess) return;
        if (_interactionSyncing)
        {
            _interactionSyncRequested = true;
            return;
        }
        _interactionSyncing = true;
        _interactionSyncRequested = false;
        try
        {
            var profile = await Interactions.SyncProfileAsync(
                Settings.InteractionMode,
                Settings.RandomInteractionsEnabled);
            ApplyInteractionProfile(profile);
            await Interactions.FlushEventsAsync();
            await Interactions.RefillAsync();
        }
        catch { }
        finally
        {
            _interactionSyncing = false;
            StateChanged?.Invoke();
            if (!_disposed && !IsExiting)
            {
                _interactionSyncTimer.Interval = _interactionSyncRequested
                    ? TimeSpan.FromSeconds(2)
                    : TimeSpan.FromMinutes(15);
                _interactionSyncTimer.Start();
            }
        }
    }

    private void ApplyInteractionProfile(InteractionProfile profile)
    {
        var changed = Settings.InteractionMode != profile.Mode
            || Settings.RandomInteractionsEnabled != profile.PromptsEnabled;
        Settings.InteractionMode = profile.Mode;
        Settings.RandomInteractionsEnabled = profile.PromptsEnabled;
        RestartInteractionTimer();
        if (changed) Save();
    }

    private async Task RunTheaterAsync(bool manual)
    {
        if (!HasPremiumAccess) return;
        if (IsGuideActive || GuideBusy || !manual && IsQuiet) return;
        if (_theaterActive || _interactionActive || _visitorQueue.IsShowing || _petWindow is not { IsVisible: true } main)
        {
            if (!_theaterActive) RestartTheaterTimer();
            return;
        }

        var originalPetPath = CurrentPetPath();
        var theaterScript = Settings.TheaterScripts.Count == 0
            ? null : Settings.TheaterScripts[_random.Next(Settings.TheaterScripts.Count)];
        var actorA = !string.IsNullOrWhiteSpace(originalPetPath) && File.Exists(originalPetPath)
            ? originalPetPath : PickTemporaryPet();
        var actorB = PickTemporaryPet(actorA);
        if (actorA is null || actorB is null || string.Equals(actorA, actorB, StringComparison.OrdinalIgnoreCase))
        {
            if (manual) main.ShowReaction("小剧场还缺一位演员，再准备一个 GIF 吧。");
            RestartTheaterTimer();
            return;
        }

        _theaterActive = true;
        _randomTimer.Stop();
        _idleTimer.Stop();
        _theaterCancellation = new CancellationTokenSource();
        var cancellationToken = _theaterCancellation.Token;
        var originalPosition = new Point(main.Left, main.Top);

        try
        {
            main.RefreshAppearance(actorA);
            main.EnterScriptedMode();
            var companion = new PetWindow(this, true);
            _companionWindow = companion;
            companion.RefreshAppearance(actorB);
            companion.EnterScriptedMode();

            var area = main.GetWorkingArea();
            var stageY = Math.Max(area.Top, area.Bottom - main.Height);
            var gap = Math.Min(90, Math.Max(32, area.Width * 0.05));
            var pairWidth = main.Width + companion.Width + gap;
            var leftX = Math.Max(area.Left, area.Left + (area.Width - pairWidth) / 2);
            var rightX = Math.Min(area.Right - companion.Width, leftX + main.Width + gap);
            var mainStage = new Point(leftX, stageY);
            var companionStage = new Point(rightX, stageY);

            companion.Place(new Point(area.Right + 8, stageY));
            companion.Show();
            await Task.WhenAll(
                main.MoveToAsync(mainStage, 260, cancellationToken),
                companion.MoveToAsync(companionStage, 310, cancellationToken));

            main.ShowReaction(GetTheaterLine("theater_open", true, 0, theaterScript));
            await Task.Delay(1550, cancellationToken);
            companion.ShowReaction(GetTheaterLine("theater_reply", false, 0, theaterScript));
            await Task.Delay(1650, cancellationToken);

            var closeShift = Math.Min(52, gap * 0.42);
            var mainClose = new Point(mainStage.X + closeShift, mainStage.Y);
            var companionClose = new Point(companionStage.X - closeShift, companionStage.Y);
            await Task.WhenAll(
                main.MoveToAsync(mainClose, 220, cancellationToken),
                companion.MoveToAsync(companionClose, 220, cancellationToken));
            main.ShowReaction(GetTheaterLine("theater_middle", true, 1, theaterScript));
            await Task.Delay(1650, cancellationToken);
            companion.ShowReaction(GetTheaterLine("theater_middle_reply", false, 1, theaterScript));
            await Task.Delay(1750, cancellationToken);
            await Task.WhenAll(
                main.MoveToAsync(mainStage, 235, cancellationToken),
                companion.MoveToAsync(companionStage, 235, cancellationToken));

            var hop = Math.Min(64, Math.Max(34, area.Height * 0.08));
            await Task.WhenAll(
                main.MoveToAsync(new Point(mainStage.X, Math.Max(area.Top, stageY - hop)), 330, cancellationToken),
                companion.MoveToAsync(new Point(companionStage.X, Math.Max(area.Top, stageY - hop * 0.72)), 300, cancellationToken));
            await Task.WhenAll(
                main.MoveToAsync(mainStage, 360, cancellationToken),
                companion.MoveToAsync(companionStage, 350, cancellationToken));

            main.ShowReaction(GetTheaterLine("theater_challenge", true, 2, theaterScript));
            await Task.Delay(1600, cancellationToken);
            companion.ShowReaction(GetTheaterLine("theater_challenge_reply", false, 2, theaterScript));
            await Task.Delay(1650, cancellationToken);
            await Task.WhenAll(
                main.MoveToAsync(companionStage, 440, cancellationToken),
                companion.MoveToAsync(mainStage, 410, cancellationToken));

            main.ShowReaction(GetTheaterLine("theater_twist", true, 3, theaterScript));
            await Task.Delay(1650, cancellationToken);
            companion.ShowReaction(GetTheaterLine("theater_twist_reply", false, 3, theaterScript));
            await Task.Delay(1750, cancellationToken);

            var danceRise = Math.Max(area.Top, stageY - hop * 0.65);
            var mainOuter = new Point(Math.Min(area.Right - main.Width, companionStage.X + Math.Min(70, gap)), danceRise);
            var companionOuter = new Point(Math.Max(area.Left, mainStage.X - Math.Min(70, gap)), danceRise);
            await Task.WhenAll(
                main.MoveToAsync(mainOuter, 390, cancellationToken),
                companion.MoveToAsync(companionOuter, 390, cancellationToken));
            await Task.WhenAll(
                main.MoveToAsync(new Point(companionStage.X, stageY), 360, cancellationToken),
                companion.MoveToAsync(new Point(mainStage.X, stageY), 360, cancellationToken));

            main.ShowReaction(GetTheaterLine("theater_finish", true, 4, theaterScript));
            await Task.Delay(1450, cancellationToken);
            companion.ShowReaction(GetTheaterLine("theater_finish", false, 4, theaterScript));
            await Task.Delay(1800, cancellationToken);
            var returnPosition = ClampToArea(originalPosition, area, main.Width, main.Height);
            await main.MoveToAsync(returnPosition, 340, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (_companionWindow?.IsLoaded == true) _companionWindow.Close();
            _companionWindow = null;
            if (!IsExiting)
            {
                main.LeaveScriptedMode();
                main.RefreshAppearance(CurrentPetPath());
                SavePosition(main.Left, main.Top);
            }
            _theaterCancellation?.Dispose();
            _theaterCancellation = null;
            _theaterActive = false;
            if (!IsExiting)
            {
                _idleTimer.Start();
                RestartRandomTimer();
                RestartTheaterTimer();
            }
        }
    }

    private string TheaterLine(bool mainActor, int index, TheaterScriptDefinition? script)
    {
        if (script?.Scenes.ElementAtOrDefault(index) is { } scene)
            return mainActor ? scene.Main : scene.Companion;
        var pair = (Settings.Personality, index) switch
        {
            ("shy", 0) => ("那个……要不要一起排练一下？", "好呀，我站远一点，不吓到你。"),
            ("shy", 1) => ("我先靠近一点点，你别突然转身。", "放心，我会把脚步放轻。"),
            ("shy", 2) => ("那就比谁跳得更轻。", "输了的人负责假装没输。"),
            ("shy", 3) => ("咦，我们怎么换到对面了？", "这样也能看见你，挺好的。"),
            ("shy", _) => ("谢谢你陪我演完。", "下次开场，我还在这里等你。"),
            ("clingy", 0) => ("终于来了，快站到我旁边。", "只留这么一点缝，够不够？"),
            ("clingy", 1) => ("再靠近一点，舞台这么大做什么。", "再近就要共用一个像素了。"),
            ("clingy", 2) => ("跳起来也不许走丢。", "那你要跟紧我。"),
            ("clingy", 3) => ("换了位置，距离还是一样。", "这才叫完美走位。"),
            ("clingy", _) => ("演完也先别散场。", "好，我陪你多站一会儿。"),
            ("chaotic", 0) => ("临时通知：舞台规则刚刚被我吃掉了。", "太好了，我带来了不存在的剧本。"),
            ("chaotic", 1) => ("第一幕：两个演员争夺同一块地板。", "我宣布地板归会跳的那位。"),
            ("chaotic", 2) => ("三、二、一，方向不重要！", "收到，我往所有方向出发。"),
            ("chaotic", 3) => ("位置交换成功，身份也交换吗？", "从现在起你负责演我。"),
            ("chaotic", _) => ("演出结束，事故非常成功。", "谢幕！请忽略地上的剧本碎片。"),
            (_, 0) => ("新搭档，准备好了吗？", "随时可以，今天演哪一出？"),
            (_, 1) => ("靠近一点，我们先对个暗号。", "暗号是：今天也要有精神。"),
            (_, 2) => ("先跳一下，再交换位置！", "好，看谁落地更稳。"),
            (_, 3) => ("换位成功，要不要再来一轮？", "当然，舞台还没热够呢。"),
            _ => ("配合满分，今天先演到这里。", "收到，掌声留到下次继续。")
        };
        return mainActor ? pair.Item1 : pair.Item2;
    }

    private string GetTheaterLine(string action, bool mainActor, int index, TheaterScriptDefinition? script)
    {
        var line = TheaterLine(mainActor, index, script);
        return script is null ? GetInteractionWord(action, line) : line;
    }

    private static Point ClampToArea(Point point, Rect area, double width, double height)
        => new(
            Math.Clamp(point.X, area.Left, Math.Max(area.Left, area.Right - width)),
            Math.Clamp(point.Y, area.Top, Math.Max(area.Top, area.Bottom - height)));

    private void NormalizeReminderTimes()
    {
        var now = DateTime.Now;
        foreach (var reminder in Settings.Reminders.Where(item => item.Enabled && item.At.HasValue))
        {
            var local = reminder.LocalTime;
            if (local > now) continue;
            if (!reminder.RepeatDaily) reminder.Enabled = false;
            else
            {
                while (local <= now) local = local.AddDays(1);
                reminder.LocalTime = local;
            }
        }
    }

    private void CheckReminders()
    {
        if (Settings.QuietUntilUtc is { } until && until <= DateTimeOffset.UtcNow) ResumeCompanionship();
        CheckMidnightSecret();
        if (!HasPremiumAccess) return;
        var now = DateTime.Now;
        var due = Settings.Reminders.Where(item => item.Enabled && item.At.HasValue && item.LocalTime <= now).ToArray();
        if (due.Length == 0) return;
        // A cancelled short play cleans up asynchronously. Keep the reminder due until
        // the next tick so that its newly displayed bubble cannot be cleared by that cleanup.
        if (GuideBusy) { DismissGuide(); return; }
        DismissGuide();
        // Preserve due reminders through question/answer cards; ordinary deferred
        // status text may be superseded, but a reminder must not be consumed unseen.
        if (_petWindow is { IsInteractionVisible: true }) return;
        foreach (var reminder in due)
        {
            SetClickThrough(false);
            _petWindow?.ShowReminder(reminder.Message, reminder.ExpressionPath);
            _tray?.ShowBalloonTip(4000, "桌搭子提醒", reminder.Message, Forms.ToolTipIcon.Info);
            if (reminder.RepeatDaily)
            {
                var next = reminder.LocalTime;
                while (next <= now) next = next.AddDays(1);
                reminder.LocalTime = next;
            }
            else reminder.Enabled = false;
        }
        SaveAndRefresh();
    }

    private void CheckMidnightSecret()
    {
        if (IsGuideActive || IsQuiet || !Settings.DailySpeechEnabled) return;
        var now = DateTime.Now;
        var today = DateOnly.FromDateTime(now);
        if (now.Hour != 0 || now.Minute > 2 || _lastMidnightSecret == today) return;
        _lastMidnightSecret = today;
        _petWindow?.ShowReaction("零点彩蛋已送达：今天也辛苦啦。");
        _petWindow?.PlaySecretAnimation();
    }

    private void SaveAndRefresh()
    {
        Save();
        _petWindow?.RefreshAppearance(CurrentPetPath());
        RefreshTray();
        StateChanged?.Invoke();
    }

    private void Save() => _store.Save(Settings);

    private void PositionPetWindow(Window window)
    {
        if (Settings.Position is { } position)
        {
            var savedArea = SystemParameters.WorkArea;
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = Math.Clamp(position.X, savedArea.Left - window.Width + 80, savedArea.Right - 80);
            window.Top = Math.Clamp(position.Y, savedArea.Top, savedArea.Bottom - 80);
            return;
        }
        var area = SystemParameters.WorkArea;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = area.Right - window.Width - 28;
        window.Top = area.Bottom - window.Height - 20;
    }

    private void CreateTray()
    {
        Icon icon;
        try
        {
            icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? SystemIcons.Application;
        }
        catch
        {
            icon = SystemIcons.Application;
        }
        _tray = new Forms.NotifyIcon { Icon = icon, Text = "桌搭子", Visible = true };
        _tray.DoubleClick += (_, _) => ShowSettings();
        RefreshTray();
    }

    private string? PickTemporaryPet(string? excluded = null)
    {
        var candidates = _libraryFiles
            .Where(item => !string.Equals(item, excluded, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return candidates.Length == 0 ? null : candidates[_random.Next(candidates.Length)];
    }

    private void RefreshTrialDisplay()
    {
        if (_tray is not null) _tray.Text = TrayTooltip();
        if (IsTrialActive)
        {
            if (!_trialDisplayTimer.IsEnabled) _trialDisplayTimer.Start();
        }
        else if (_trialDisplayTimer.IsEnabled)
        {
            _trialDisplayTimer.Stop();
        }
        TrialClockChanged?.Invoke();
    }

    private string TrayTooltip()
    {
        if (Settings.ClickThrough) return "桌搭子 · 鼠标穿透中 · Ctrl+Shift+P 关闭";
        if (IsTrialActive) return $"桌搭子 · 完整体验还剩 {FormatTrialClock(RemainingTrialSeconds)}";
        return "桌搭子";
    }

    private static string FormatTrialClock(int seconds)
    {
        seconds = Math.Max(0, seconds);
        var days = seconds / 86400;
        var hours = seconds % 86400 / 3600;
        var minutes = seconds % 3600 / 60;
        var remainder = seconds % 60;
        if (days > 0) return $"{days}天 {hours}:{minutes:00}:{remainder:00}";
        return hours > 0 ? $"{hours}:{minutes:00}:{remainder:00}" : $"{minutes}:{remainder:00}";
    }

    private void RefreshTray()
    {
        _trayMenu?.Dispose();
        _trayMenu = new Forms.ContextMenuStrip();
        var partner = Companions.Profile?.Partner;
        _trayMenu.Items.Add("陪伴设置", null, (_, _) => ShowSettings());
        if (RemoteConfig.CompanionHall) _trayMenu.Items.Add("桌宠大厅", null, (_, _) => ShowHall());
        _trayMenu.Items.Add(IsQuiet ? "恢复主动陪伴" : "暂停主动打扰 1 小时", null, (_, _) =>
        {
            if (IsQuiet) ResumeCompanionship(); else PauseForOneHour();
        });
        _trayMenu.Items.Add("停止当前剧场 / 关闭来访", null, (_, _) => StopCurrentPerformance());
        _trayMenu.Items.Add(_petWindow?.IsVisible == true ? "隐藏桌宠" : "显示桌宠", null, (_, _) => TogglePetVisibility());
        _trayMenu.Items.Add("随机换一只", null, (_, _) => RandomizePet()).Enabled = _libraryFiles.Count > 0;
        _trayMenu.Items.Add(new Forms.ToolStripSeparator());
        if (IsTrialActive)
        {
            if (RemoteConfig.TrialVisits)
            {
                _trayMenu.Items.Add("演示来访 · 女友", null, async (_, _) => await PlayTrialVisitAsync("girlfriend"));
                _trayMenu.Items.Add("演示来访 · 好友", null, async (_, _) => await PlayTrialVisitAsync("friend"));
                _trayMenu.Items.Add("演示来访 · 搭子", null, async (_, _) => await PlayTrialVisitAsync("companion"));
            }
        }
        else
        {
            var sendLabel = partner is null ? "发给搭子（先绑定）" : $"发给 {partner.DisplayName}";
            _trayMenu.Items.Add(sendLabel, null, async (_, _) =>
            {
                try { await SendCurrentGifToCompanionAsync(); }
                catch (Exception error)
                {
                    _petWindow?.ShowReaction(NetworkConnectionErrors.ForUser(error, "暂时发送不了，请稍后重试。"));
                }
            }).Enabled = _licenses.IsActivated && partner is not null && File.Exists(CurrentPetPath());
        }
        _trayMenu.Items.Add("陪我玩一会", null, (_, _) => StartRandomInteraction()).Enabled = !_theaterActive;
        _trayMenu.Items.Add("看一场小剧场", null, (_, _) => StartTheater()).Enabled = _libraryFiles.Count > 1 && !_theaterActive;
        if (RemoteConfig.FishMode)
        {
            var fishModeLabel = _licenses.IsActivated
                ? "摸鱼广告"
                : IsTrialActive ? "摸鱼广告（体验中）" : "摸鱼广告（激活后继续）";
            _trayMenu.Items.Add(fishModeLabel, null, (_, _) => ShowFakeAdWindow());
        }
        _trayMenu.Items.Add(new Forms.ToolStripSeparator());
        _trayMenu.Items.Add(new Forms.ToolStripMenuItem("始终置顶", null, (_, _) => SetAlwaysOnTop(!Settings.AlwaysOnTop)) { Checked = Settings.AlwaysOnTop });
        var clickThroughItem = new Forms.ToolStripMenuItem(
            Settings.ClickThrough ? "鼠标穿透（开）" : "鼠标穿透",
            null,
            (_, _) => SetClickThrough(!Settings.ClickThrough))
        {
            Checked = Settings.ClickThrough
        };
        _trayMenu.Items.Add(clickThroughItem);
        _trayMenu.Items.Add(new Forms.ToolStripSeparator());
        _trayMenu.Items.Add("退出桌搭子", null, (_, _) => Exit());
        if (_tray is not null)
        {
            _tray.ContextMenuStrip = _trayMenu;
            _tray.Text = TrayTooltip();
        }
    }

    private void OnUpdateStateChanged(UpdateState state)
    {
        if (!RemoteConfig.AutoUpdates) return;
        if (state.Phase != UpdatePhase.Available || state.Manifest is not { } manifest
            || manifest.Version.Equals(Settings.IgnoredUpdateVersion, StringComparison.OrdinalIgnoreCase)) return;

        void Notify()
        {
            if (IsQuiet || IsGuideActive) return;
            _petWindow?.ShowReaction($"发现新版本 v{manifest.Version}");
            _tray?.ShowBalloonTip(5000, "桌搭子可以更新", $"新版本 v{manifest.Version} 已发布，双击托盘图标查看。", Forms.ToolTipIcon.Info);
        }

        var dispatcher = System.Windows.Application.Current.Dispatcher;
        if (dispatcher.CheckAccess()) Notify(); else dispatcher.BeginInvoke(Notify);
    }

    private Task ShowQueuedVisitsAsync(bool manual = false)
        => _visitorQueue.ShowQueuedAsync(
            () => !_disposed && !IsExiting && !IsGuideActive && !GuideBusy && (manual || CanShowVisits) && _petWindow is { IsVisible: true },
            PresentVisitAsync, oneOnly: manual);

    private async Task PresentVisitAsync(CompanionVisit visit, CancellationToken cancellationToken)
    {
        if (_petWindow is not { IsVisible: true } main) throw new OperationCanceledException();
        var visitor = new PetWindow(this, true);
        try
        {
            visitor.RefreshAppearance(visit.FilePath);
            visitor.EnterScriptedMode();
            var area = main.GetWorkingArea();
            var left = main.Left - visitor.Width - 12;
            if (left < area.Left) left = main.Left + main.Width + 12;
            left = Math.Clamp(left, area.Left, Math.Max(area.Left, area.Right - visitor.Width));
            var top = Math.Clamp(main.Top, area.Top, Math.Max(area.Top, area.Bottom - visitor.Height));
            visitor.Place(new Point(left, top));
            visitor.Show();
            visitor.ShowReaction(string.IsNullOrWhiteSpace(visit.Message)
                ? $"{visit.SenderName} 来串门啦" : $"{visit.SenderName}：{visit.Message}");
            await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
        }
        finally { if (visitor.IsLoaded) visitor.Close(); }
    }

    private async Task PollCompanionAsync()
    {
        if (_disposed || IsExiting || !(_licenses.IsActivated || _licenses.IsTrialActive))
            return;
        if (_companionSyncing)
        {
            _companionTimer.Start();
            return;
        }

        _companionSyncing = true;
        try
        {
            _visitorQueue.Restore(Companions.Inbox);
            var visits = await Companions.ReceiveAsync();
            _visitorQueue.Enqueue(visits);
            if (!_visitorQueue.IsShowing && _visitorQueue.HasPending)
                _ = ShowQueuedVisitsAsync();
        }
        catch { }
        finally
        {
            _companionSyncing = false;
            if (!_disposed && !IsExiting && _visitorQueue.HasPending)
                _ = ShowQueuedVisitsAsync();
            if (!_disposed && !IsExiting) _companionTimer.Start();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DismissGuide();
        _randomTimer.Stop();
        _reminderTimer.Stop();
        _idleTimer.Stop();
        _theaterTimer.Stop();
        _interactionTimer.Stop();
        _interactionSyncTimer.Stop();
        _companionTimer.Stop();
        _trialDisplayTimer.Stop();
        _theaterCancellation?.Cancel();
        Updates.StateChanged -= OnUpdateStateChanged;
        Updates.Dispose();
        Feedback.Dispose();
        Interactions.Dispose();
        Companions.Dispose();
        _analytics.Dispose();
        if (_tray is not null) _tray.Visible = false;
        _tray?.Dispose();
        _trayMenu?.Dispose();
    }
}
