using System.Drawing;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using ZhuoDazi.Models;
using ZhuoDazi.Services;
using ZhuoDazi.Interop;
using Forms = System.Windows.Forms;
using Point = System.Windows.Point;

namespace ZhuoDazi;

public sealed class AppController : IDisposable
{
    private readonly SettingsStore _store = new();
    private readonly LicenseService _licenses;
    private readonly StartupRegistrationService _startupRegistration = new();
    private readonly GifLibraryService _library = new();
    private readonly Random _random = new();
    private readonly DispatcherTimer _randomTimer = new();
    private readonly DispatcherTimer _reminderTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _idleTimer = new() { Interval = TimeSpan.FromSeconds(32) };
    private readonly DispatcherTimer _theaterTimer = new();
    private IReadOnlyList<string> _libraryFiles = [];
    private string? _activeLibraryPetPath;
    private PetWindow? _petWindow;
    private PetWindow? _companionWindow;
    private SettingsWindow? _settingsWindow;
    private Forms.NotifyIcon? _tray;
    private Forms.ContextMenuStrip? _trayMenu;
    private CancellationTokenSource? _theaterCancellation;
    private DateOnly? _lastMidnightSecret;
    private bool _theaterActive;
    private bool _disposed;

    public AppSettings Settings { get; }
    public UpdateService Updates { get; }
    public FeedbackService Feedback { get; }
    public event Action? StateChanged;
    public bool IsExiting { get; private set; }
    public LibraryDefinition? ActiveLibrary => Settings.Libraries.FirstOrDefault(item => item.Id == Settings.ActiveLibraryId);
    public InteractionWordPackDefinition? ActiveInteractionWordPack => Settings.InteractionWordPacks
        .FirstOrDefault(item => item.Id == Settings.ActiveInteractionWordPackId);
    public string LibraryName => ActiveLibrary?.Name ?? "月薪喵";
    public string LibraryPath => ActiveLibrary?.Path ?? "内置：月薪喵";
    public int LibraryCount => _libraryFiles.Count;
    public int InteractionWordCount => ActiveInteractionWordPack?.WordCount ?? 0;
    public string LicenseSummary => _licenses.Summary;

    public AppController(LicenseService licenses)
    {
        _licenses = licenses;
        Settings = _store.Load();
        Updates = new UpdateService(_store, _licenses);
        Feedback = new FeedbackService(_licenses);
        Updates.StateChanged += OnUpdateStateChanged;
        _randomTimer.Tick += (_, _) => RandomizePet();
        _reminderTimer.Tick += (_, _) => CheckReminders();
        _idleTimer.Tick += (_, _) => _petWindow?.ShowReaction(GetInteractionWord("idle", "你忙你的，我负责陪你。"));
        _theaterTimer.Tick += async (_, _) =>
        {
            _theaterTimer.Stop();
            await RunTheaterAsync(false);
        };
    }

    public void Start()
    {
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
        Save();
        if (Settings.AutoCheckUpdates)
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
            timer.Tick += async (_, _) =>
            {
                timer.Stop();
                try { await Updates.CheckAsync(false); } catch { }
            };
            timer.Start();
        }
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

    public bool ShowActivation(Window? owner = null)
    {
        var activationWindow = new ActivationWindow(_licenses);
        if (owner is not null) activationWindow.Owner = owner;
        var activated = activationWindow.ShowDialog() == true;
        if (activated)
        {
            StateChanged?.Invoke();
            _petWindow?.ShowReaction("新的邀请码已经绑定完成。");
        }
        return activated;
    }

    public void ShowTrayMenu() => _trayMenu?.Show(Forms.Cursor.Position);

    public string? CurrentPetPath()
    {
        if (Settings.RandomPetEnabled) return _activeLibraryPetPath;
        return Settings.Pets.FirstOrDefault(item => item.Id == Settings.ActivePetId)?.Path;
    }

    public string GetInteractionWord(string action, string fallback)
    {
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

    public void SetTheaterConfig(bool enabled, int intervalSeconds)
    {
        Settings.TheaterEnabled = enabled;
        Settings.TheaterIntervalSeconds = intervalSeconds is 60 or 180 or 300 or 600 or 1800
            ? intervalSeconds : 300;
        RestartTheaterTimer();
        SaveAndRefresh();
    }

    public void SetClickThrough(bool value)
    {
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
        Settings.RandomPetEnabled = false;
        RestartRandomTimer();
        SaveAndRefresh();
        return pet;
    }

    public void SelectPet(string? id)
    {
        Settings.ActivePetId = id;
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
        RestartRandomTimer();
        SaveAndRefresh();
        return library;
    }

    public void SelectLibrary(string? id)
    {
        if (id is not null && !Settings.Libraries.Any(item => item.Id == id))
            throw new InvalidOperationException("所选资源库不存在。");
        Settings.ActiveLibraryId = id;
        RefreshLibrary(true);
        RestartRandomTimer();
        SaveAndRefresh();
    }

    public void DeleteLibrary(string id)
    {
        var library = Settings.Libraries.FirstOrDefault(item => item.Id == id);
        if (library is null) return;
        Settings.Libraries.Remove(library);
        if (Settings.ActiveLibraryId == id) Settings.ActiveLibraryId = null;
        RefreshLibrary(true);
        RestartRandomTimer();
        SaveAndRefresh();
    }

    public void SetRandomPetConfig(bool enabled, int intervalSeconds)
    {
        Settings.RandomPetEnabled = enabled;
        Settings.RandomPetIntervalSeconds = intervalSeconds is 30 or 60 or 300 or 600 or 1800 ? intervalSeconds : 300;
        if (enabled && _activeLibraryPetPath is null) _activeLibraryPetPath = _library.Pick(_libraryFiles);
        RestartRandomTimer();
        SaveAndRefresh();
    }

    public void RandomizePet()
    {
        if (_theaterActive) return;
        if (_libraryFiles.Count == 0) return;
        Settings.RandomPetEnabled = true;
        _activeLibraryPetPath = _library.Pick(_libraryFiles, _activeLibraryPetPath);
        RestartRandomTimer();
        SaveAndRefresh();
        _petWindow?.ShowReaction(GetInteractionWord("switch", "换班完成，新选手登场。"));
    }

    public void StartTheater() => _ = RunTheaterAsync(true);

    public InteractionWordPackDefinition ImportInteractionWords(string filePath)
    {
        if (Settings.InteractionWordPacks.Count >= 5)
            throw new InvalidOperationException("最多只能上传 5 个互动词包。");
        var words = InteractionWordsService.Parse(filePath);
        var baseName = Path.GetFileNameWithoutExtension(filePath);
        var name = baseName;
        var suffix = 2;
        while (Settings.InteractionWordPacks.Any(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            name = $"{baseName} {suffix++}";

        var pack = new InteractionWordPackDefinition
        {
            Id = $"words-{Guid.NewGuid():N}",
            Name = name,
            Words = words
        };
        Settings.InteractionWordPacks.Add(pack);
        Settings.ActiveInteractionWordPackId = pack.Id;
        SaveAndRefresh();
        return pack;
    }

    public void SelectInteractionWordPack(string? id)
    {
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
    {
        if (Settings.TheaterScripts.Count >= 10)
            throw new InvalidOperationException("最多只能导入 10 个小剧场剧本。");
        var script = TheaterScriptService.Parse(filePath);
        var baseName = script.Name;
        var suffix = 2;
        while (Settings.TheaterScripts.Any(item => item.Name.Equals(script.Name, StringComparison.OrdinalIgnoreCase)))
            script.Name = $"{baseName} {suffix++}";
        Settings.TheaterScripts.Add(script);
        SaveAndRefresh();
        return script;
    }

    public void DeleteTheaterScript(string id)
    {
        Settings.TheaterScripts.RemoveAll(item => item.Id == id);
        SaveAndRefresh();
    }

    public void SaveReminder(ReminderDefinition reminder)
    {
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

    public void TogglePetVisibility()
    {
        if (_petWindow is null) return;
        if (_petWindow.IsVisible) _petWindow.Hide(); else _petWindow.Show();
        RefreshTray();
    }

    public void Exit()
    {
        IsExiting = true;
        _theaterCancellation?.Cancel();
        if (_companionWindow?.IsLoaded == true) _companionWindow.Close();
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
        if (!Settings.RandomPetEnabled || _libraryFiles.Count < 2) return;
        _randomTimer.Interval = TimeSpan.FromSeconds(Settings.RandomPetIntervalSeconds);
        _randomTimer.Start();
    }

    private void RestartTheaterTimer()
    {
        _theaterTimer.Stop();
        if (_disposed || IsExiting || !Settings.TheaterEnabled) return;
        _theaterTimer.Interval = TimeSpan.FromSeconds(Settings.TheaterIntervalSeconds);
        _theaterTimer.Start();
    }

    private async Task RunTheaterAsync(bool manual)
    {
        if (_theaterActive || _petWindow is not { IsVisible: true } main)
        {
            if (!_theaterActive) RestartTheaterTimer();
            return;
        }

        var originalPetPath = CurrentPetPath();
        var theaterScript = Settings.TheaterScripts.Count == 0
            ? null : Settings.TheaterScripts[_random.Next(Settings.TheaterScripts.Count)];
        var actorA = !string.IsNullOrWhiteSpace(originalPetPath) && File.Exists(originalPetPath)
            ? originalPetPath : _library.Pick(_libraryFiles);
        var actorB = _library.Pick(_libraryFiles, actorA);
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
        CheckMidnightSecret();
        var now = DateTime.Now;
        var due = Settings.Reminders.Where(item => item.Enabled && item.At.HasValue && item.LocalTime <= now).ToArray();
        if (due.Length == 0) return;
        foreach (var reminder in due)
        {
            SetClickThrough(false);
            _petWindow?.ShowReaction(reminder.Message);
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

    private void RefreshTray()
    {
        _trayMenu?.Dispose();
        _trayMenu = new Forms.ContextMenuStrip();
        _trayMenu.Items.Add("打开设置", null, (_, _) => ShowSettings());
        _trayMenu.Items.Add(_petWindow?.IsVisible == true ? "隐藏桌宠" : "显示桌宠", null, (_, _) => TogglePetVisibility());
        _trayMenu.Items.Add("随机换一只", null, (_, _) => RandomizePet()).Enabled = _libraryFiles.Count > 0;
        _trayMenu.Items.Add("上演小剧场", null, (_, _) => StartTheater()).Enabled = _libraryFiles.Count > 1 && !_theaterActive;
        _trayMenu.Items.Add(new Forms.ToolStripSeparator());
        _trayMenu.Items.Add(new Forms.ToolStripMenuItem("始终置顶", null, (_, _) => SetAlwaysOnTop(!Settings.AlwaysOnTop)) { Checked = Settings.AlwaysOnTop });
        _trayMenu.Items.Add(new Forms.ToolStripMenuItem("鼠标穿透", null, (_, _) => SetClickThrough(!Settings.ClickThrough)) { Checked = Settings.ClickThrough });
        _trayMenu.Items.Add(new Forms.ToolStripSeparator());
        _trayMenu.Items.Add("退出桌搭子", null, (_, _) => Exit());
        if (_tray is not null) _tray.ContextMenuStrip = _trayMenu;
    }

    private void OnUpdateStateChanged(UpdateState state)
    {
        if (state.Phase != UpdatePhase.Available || state.Manifest is not { } manifest
            || manifest.Version.Equals(Settings.IgnoredUpdateVersion, StringComparison.OrdinalIgnoreCase)) return;

        void Notify()
        {
            _petWindow?.ShowReaction($"发现新版本 v{manifest.Version}");
            _tray?.ShowBalloonTip(5000, "桌搭子可以更新", $"新版本 v{manifest.Version} 已发布，双击托盘图标查看。", Forms.ToolTipIcon.Info);
        }

        var dispatcher = System.Windows.Application.Current.Dispatcher;
        if (dispatcher.CheckAccess()) Notify(); else dispatcher.BeginInvoke(Notify);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _randomTimer.Stop();
        _reminderTimer.Stop();
        _idleTimer.Stop();
        _theaterTimer.Stop();
        _theaterCancellation?.Cancel();
        Updates.StateChanged -= OnUpdateStateChanged;
        Updates.Dispose();
        Feedback.Dispose();
        if (_tray is not null) _tray.Visible = false;
        _tray?.Dispose();
        _trayMenu?.Dispose();
    }
}
