using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using ZhuoDazi.Models;
using ZhuoDazi.Services;
using Forms = System.Windows.Forms;
using WpfMessageBox = System.Windows.MessageBox;
using WpfOpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace ZhuoDazi;

public partial class SettingsWindow : Window
{
    private readonly AppController _controller;
    private bool _refreshing;
    private bool _feedbackLoading;
    private bool _interactionContentLoading;
    private bool _companionLoading;
    private bool _hallLoading;
    private DateTimeOffset _hallSendAllowedAt;
    private readonly System.Windows.Threading.DispatcherTimer _hallClock = new() { Interval = TimeSpan.FromSeconds(1) };
    private string? _editingReminderId;

    public SettingsWindow(AppController controller)
    {
        _controller = controller;
        _refreshing = true;
        InitializeComponent();
        MainTabs.SelectionChanged += MainTabs_SelectionChanged;
        _controller.StateChanged += Controller_StateChanged;
        _controller.TrialClockChanged += Controller_TrialClockChanged;
        _controller.Updates.StateChanged += Updates_StateChanged;
        Loaded += (_, _) =>
        {
            RefreshAll();
        };
        Closing += OnClosing;
        _hallClock.Tick += (_, _) => RefreshHallSendButton();
        _hallClock.Start();
        Closed += (_, _) => _hallClock.Stop();
        var nextReminder = ReminderSchedule.DefaultTime(DateTime.Now);
        ReminderDatePicker.SelectedDate = nextReminder.Date;
        ReminderHourCombo.ItemsSource = Enumerable.Range(0, 24)
            .Select(hour => hour.ToString("00", CultureInfo.InvariantCulture)).ToList();
        ReminderMinuteCombo.ItemsSource = Enumerable.Range(0, 60)
            .Select(minute => minute.ToString("00", CultureInfo.InvariantCulture)).ToList();
        SetReminderTime(nextReminder.TimeOfDay);
        RefreshAll();
    }

    public void ShowInteractionTab()
    {
        MainTabs.SelectedIndex = 1;
        Show();
        Activate();
    }

    public void ShowHallTab()
    {
        MainTabs.SelectedItem = HallTab;
        Show();
        Activate();
    }

    public void ShowUpdateTab()
    {
        MainTabs.SelectedItem = UpdatesTab;
        Show();
        Activate();
    }

    private void Controller_StateChanged()
    {
        if (!Dispatcher.CheckAccess()) Dispatcher.BeginInvoke(RefreshAll);
        else RefreshAll();
    }

    private void Controller_TrialClockChanged()
    {
        if (!Dispatcher.CheckAccess()) Dispatcher.BeginInvoke(RefreshLicenseBanner);
        else RefreshLicenseBanner();
    }

    private void RefreshLicenseBanner()
    {
        if (LicenseBannerText is null || LicenseStatusText is null) return;
        GuideButton.Content = _controller.GuideActionLabel;
        RetryTrialButton.Visibility = _controller.HasActivatedLicense ? Visibility.Collapsed : Visibility.Visible;
        RetryTrialButton.IsEnabled = !_controller.IsTrialVerificationPending;
        LicenseBannerText.Text = _controller.LicenseSummary;
        LicenseStatusText.Text = _controller.LicenseSummary;
        if (TodayActivateButton is not null)
        {
            TodayActivateButton.Visibility = _controller.HasActivatedLicense ? Visibility.Collapsed : Visibility.Visible;
            TodayActivateButton.Content = _controller.IsTrialActive ? "体验中，也可现在激活" : "继续完整体验";
        }
    }

    private void Updates_StateChanged(UpdateState state)
    {
        if (!Dispatcher.CheckAccess()) Dispatcher.BeginInvoke(() => RenderUpdateState(state));
        else RenderUpdateState(state);
    }

    /// <summary>
    /// 重建整个设置窗的显示。_refreshing 必须在 finally 里复位：
    /// 一旦它卡在 true，所有控件的写回守卫都会静默失效，整个窗口"点了没反应"。
    /// </summary>
    private void RefreshAll()
    {
        _refreshing = true;
        try
        {
            RefreshAllCore();
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void RefreshAllCore()
    {
        var settings = _controller.Settings;
        var premium = _controller.HasPremiumAccess;
        SizeSlider.Value = settings.Size;
        SizeValue.Text = $"{settings.Size} px";
        OpacitySlider.Value = settings.Opacity;
        OpacityValue.Text = $"{settings.Opacity}%";
        CurrentPetPreview.FilePath = _controller.CurrentPetPath();
        CurrentPetNameText.Text = _controller.CurrentPetName;
        CurrentPetSourceText.Text = _controller.CurrentPetSource;
        DailySpeechCheck.IsChecked = settings.DailySpeechEnabled;
        QuietStatusText.Text = _controller.QuietStatus;
        PauseCompanionshipButton.Content = _controller.IsQuiet ? "恢复主动陪伴" : "暂停 1 小时";
        GuideButton.Content = _controller.GuideActionLabel;
        RetryTrialButton.Visibility = _controller.HasActivatedLicense ? Visibility.Collapsed : Visibility.Visible;
        RetryTrialButton.IsEnabled = !_controller.IsTrialVerificationPending;
        LicenseBannerText.Text = _controller.LicenseSummary;
        var announcement = _controller.RemoteConfig.Announcement;
        AnnouncementBanner.Visibility = string.IsNullOrWhiteSpace(announcement) ? Visibility.Collapsed : Visibility.Visible;
        AnnouncementText.Text = announcement;
        FishModeRow.Visibility = _controller.RemoteConfig.FishMode ? Visibility.Visible : Visibility.Collapsed;
        HallTab.Visibility = _controller.RemoteConfig.CompanionHall ? Visibility.Visible : Visibility.Collapsed;
        TodayActivateButton.Visibility = _controller.HasActivatedLicense ? Visibility.Collapsed : Visibility.Visible;
        TodayActivateButton.Content = _controller.IsTrialActive ? "体验中，也可现在激活" : "继续完整体验";
        TopmostCheck.IsChecked = settings.AlwaysOnTop;
        StartWithWindowsCheck.IsChecked = settings.StartWithWindows;
        MirrorCheck.IsChecked = settings.Mirrored;
        ClickThroughButton.Content = settings.ClickThrough ? "关闭鼠标穿透" : "开启鼠标穿透";
        foreach (var item in PersonalityCombo.Items.OfType<ComboBoxItem>())
            if (item.Tag?.ToString() == settings.Personality) item.IsSelected = true;
        MouseInteractionCheck.IsChecked = settings.MouseInteractionEnabled;
        RandomMovementCheck.IsChecked = settings.RandomMovementEnabled;
        RandomInteractionCheck.IsChecked = settings.RandomInteractionsEnabled;
        RandomInteractionCheck.IsEnabled = premium;
        InteractionModeCombo.IsEnabled = premium && settings.RandomInteractionsEnabled;
        TheaterEnabledCheck.IsEnabled = premium;
        TheaterIntervalCombo.IsEnabled = premium;
        foreach (var item in InteractionModeCombo.Items.OfType<ComboBoxItem>())
            if (item.Tag?.ToString() == settings.InteractionMode) item.IsSelected = true;
        TheaterEnabledCheck.IsChecked = settings.TheaterEnabled;
        foreach (var item in TheaterIntervalCombo.Items.OfType<ComboBoxItem>())
            if (item.Tag?.ToString() == settings.TheaterIntervalSeconds.ToString(CultureInfo.InvariantCulture)) item.IsSelected = true;

        PetList.ItemsSource = null;
        PetList.ItemsSource = settings.Pets;
        PetList.SelectedItem = settings.Pets.FirstOrDefault(item => item.Id == settings.ActivePetId);
        PetCountText.Text = $"已添加 {settings.Pets.Count}/3 个自定义桌宠";

        var libraryItems = new List<LibraryListItem>
        {
            new(null, "月薪喵", "内置桌宠图鉴", true)
        };
        libraryItems.AddRange(settings.Libraries.Select(item => new LibraryListItem(
            item.Id, item.Name, item.Path, false)));
        LibraryList.ItemsSource = libraryItems;
        // 配置里的 ActiveLibraryId 可能指向已被删除的目录，此时回落到内置图鉴，
        // 不能用 First()——找不到会抛 InvalidOperationException 打断整个刷新。
        LibraryList.SelectedItem = premium
            ? libraryItems.FirstOrDefault(item => item.Id == settings.ActiveLibraryId) ?? libraryItems[0]
            : libraryItems[0];
        LibrarySummary.Text = premium
            ? $"已绑定 {settings.Libraries.Count}/3 个目录 · 当前 {_controller.LibraryName} · {_controller.LibraryCount} 个 GIF"
            : $"正在使用内置图鉴 · {_controller.LibraryCount} 个 GIF";
        LibraryPathText.Text = _controller.LibraryPath;
        DeleteLibraryButton.IsEnabled = settings.ActiveLibraryId is not null;
        RandomPetCheck.IsChecked = settings.RandomPetEnabled;
        foreach (var item in RandomIntervalCombo.Items.OfType<ComboBoxItem>())
            if (item.Tag?.ToString() == settings.RandomPetIntervalSeconds.ToString(CultureInfo.InvariantCulture)) item.IsSelected = true;

        var wordPackItems = new List<WordPackListItem>
        {
            new(null, "日常悄悄话", "小搭子的问候与碎碎念", true)
        };
        wordPackItems.AddRange(settings.InteractionWordPacks.Select(item => new WordPackListItem(
            item.Id, item.Name, $"{item.WordCount} 条互动台词", false)));
        WordPackList.ItemsSource = wordPackItems;
        WordPackList.SelectedItem = premium
            ? wordPackItems.FirstOrDefault(item => item.Id == settings.ActiveInteractionWordPackId) ?? wordPackItems[0]
            : wordPackItems[0];
        WordPackSummary.Text = $"已收藏 {settings.InteractionWordPacks.Count}/5 套 · 当前 {(_controller.ActiveInteractionWordPack?.Name ?? "日常悄悄话")}";
        DeleteWordPackButton.IsEnabled = settings.ActiveInteractionWordPackId is not null;
        InteractionContentStatusText.Text = premium
            ? _controller.InteractionStatus
            : "完整体验里可以补充线上内容和导入词包";
        SyncInteractionContentButton.IsEnabled = !_interactionContentLoading;
        DownloadInteractionPackButton.IsEnabled = !_interactionContentLoading;

        var selectedTheaterScriptId = (TheaterScriptList.SelectedItem as TheaterScriptListItem)?.Id;
        var theaterScriptItems = settings.TheaterScripts.Select(item => new TheaterScriptListItem(
            item.Id, item.Name, $"{item.Scenes.Count} 轮对白")).ToList();
        TheaterScriptList.ItemsSource = theaterScriptItems;
        TheaterScriptList.SelectedItem = theaterScriptItems.FirstOrDefault(item => item.Id == selectedTheaterScriptId);
        TheaterScriptSummary.Text = settings.TheaterScripts.Count == 0
            ? "尚未导入剧本，演出将使用内置性格对白"
            : $"已导入 {settings.TheaterScripts.Count}/10 个剧本 · 每次演出随机抽取";
        DeleteTheaterScriptButton.IsEnabled = TheaterScriptList.SelectedItem is not null;

        var selectedReminderId = _editingReminderId;
        var reminderItems = settings.Reminders
            .OrderBy(item => item.LocalTime)
            .Select(item => new ReminderListItem(item)).ToList();
        ReminderList.ItemsSource = reminderItems;
        ReminderList.SelectedItem = reminderItems.FirstOrDefault(item => item.Id == selectedReminderId);
        ReminderCountText.Text = premium
            ? $"已保存 {settings.Reminders.Count}/20 个提醒"
            : "完整体验里可以让桌宠到点来叫你";
        ReminderEmptyState.Visibility = settings.Reminders.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        var companionProfile = _controller.Companions.Profile;
        if (!_companionLoading && !CompanionNameText.IsKeyboardFocusWithin)
            CompanionNameText.Text = companionProfile?.DisplayName ?? string.Empty;
        CompanionCodeText.Text = companionProfile?.PairingCode ?? string.Empty;
        CompanionStatusText.Text = !_controller.HasActivatedLicense
            ? "绑定一位熟人后，可以把当前 GIF 发到对方桌角。"
            : companionProfile is null
                ? "正在连接搭子服务…"
                : companionProfile.Partner is { } partner
                ? $"已和 {partner.DisplayName} 绑定"
                : "电脑和手机是同一对搭子码，不用重新绑定。分享给对方，或输入对方的搭子码";
        CompanionPartnerText.Text = companionProfile?.Partner is { } currentPartner
            ? $"{currentPartner.DisplayName} · 收到的 GIF 会作为独立桌宠出现"
            : "尚未绑定";
        CompanionEmptyHint.Visibility = companionProfile?.Partner is null ? Visibility.Visible : Visibility.Collapsed;
        var sendPreviewPath = _controller.CurrentPetPath();
        CompanionPreviewImage.FilePath = sendPreviewPath;
        CompanionSendPreview.Visibility = companionProfile?.Partner is not null && File.Exists(sendPreviewPath)
            ? Visibility.Visible : Visibility.Collapsed;
        var companionEnabled = _controller.HasActivatedLicense && !_companionLoading;
        CompanionNameText.IsEnabled = companionEnabled;
        SaveCompanionNameButton.IsEnabled = companionEnabled;
        CompanionCodeText.IsEnabled = companionEnabled;
        CopyCompanionCodeButton.IsEnabled = companionEnabled && companionProfile is not null;
        RefreshCompanionButton.IsEnabled = companionEnabled;
        PairCompanionPanel.Visibility = companionProfile?.Partner is null && _controller.HasActivatedLicense
            ? Visibility.Visible : Visibility.Collapsed;
        PairCompanionButton.IsEnabled = companionEnabled;
        PairedCompanionActions.Visibility = companionProfile?.Partner is not null
            ? Visibility.Visible : Visibility.Collapsed;
        SendCompanionGifButton.IsEnabled = companionEnabled && File.Exists(_controller.CurrentPetPath());
        UnpairCompanionButton.IsEnabled = companionEnabled;
        CompanionActivateButton.Visibility = _controller.HasActivatedLicense
            ? Visibility.Collapsed : Visibility.Visible;
        CompanionActivateButton.Content = _controller.IsTrialActive ? "体验结束后继续使用" : "继续完整体验后使用";

        var hallAllowed = premium && _controller.RemoteConfig.CompanionHall;
        var hallEnabled = hallAllowed && companionProfile?.HallEnabled == true;
        if (!_hallLoading && !HallNicknameText.IsKeyboardFocusWithin)
            HallNicknameText.Text = companionProfile?.DisplayName ?? string.Empty;
        HallNicknameText.IsEnabled = hallAllowed && !_hallLoading;
        SaveHallNicknameButton.IsEnabled = hallAllowed && !_hallLoading;
        CompanionHallEnabledCheck.IsChecked = companionProfile?.HallEnabled == true;
        CompanionHallEnabledCheck.IsEnabled = hallAllowed && !_hallLoading && companionProfile is not null;
        RefreshHallButton.IsEnabled = hallAllowed && !_hallLoading;
        RefreshHallButton.Content = _hallLoading ? "正在连接…" : "刷新大厅";
        CompanionHallStatusText.Text = !hallAllowed
            ? "试用已结束。正式激活后可以继续加入大厅并发送表情。"
            : _hallLoading ? "正在连接大厅…"
            : hallEnabled ? "已加入大厅 · 昵称与在线状态对大厅用户可见"
            : "尚未加入 · 勾选上方选项后才会公开在线并接收来访。";
        HallCountText.Text = $"{_controller.Companions.HallPeople.Count} 人在线";
        var selectedHallId = (CompanionHallList.SelectedItem as HallListItem)?.Id;
        var hallItems = _controller.Companions.HallPeople.Select(person => new HallListItem(person)).ToList();
        CompanionHallList.ItemsSource = hallItems;
        CompanionHallList.SelectedItem = hallItems.FirstOrDefault(item => item.Id == selectedHallId);
        CompanionHallList.IsEnabled = hallEnabled && !_hallLoading;
        CompanionHallEmptyText.Visibility = hallItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        CompanionHallEmptyText.Text = _hallLoading ? "正在寻找此刻在线的朋友…"
            : !hallAllowed ? "大厅支持有效试用与正式激活设备。"
            : !hallEnabled ? "先加入大厅，再看看谁在这里。"
            : "暂时没有其他人在场。保持加入状态，稍后刷新看看。";
        CompanionHallMessageText.IsEnabled = hallEnabled && !_hallLoading;
        HallPreviewImage.FilePath = _controller.CurrentPetPath();
        HallPetNameText.Text = _controller.CurrentPetName + " · " + _controller.CurrentPetSource;
        HallSendTargetText.Text = CompanionHallList.SelectedItem is HallListItem selected
            ? $"发送给 {selected.DisplayName}" : "先选择一位在线用户";
        RefreshHallSendButton();

        AutoUpdateCheck.IsChecked = settings.AutoCheckUpdates;
        AutoUpdateCheck.Visibility = _controller.RemoteConfig.AutoUpdates ? Visibility.Visible : Visibility.Collapsed;
        CurrentVersionText.Text = $"当前版本 v{UpdateService.CurrentVersion}";
        LicenseStatusText.Text = _controller.LicenseSummary;
        RenderUpdateState(_controller.Updates.State);
    }

    private void RenderUpdateState(UpdateState state)
    {
        UpdateStatusText.Text = state.Message;
        UpdateNotesText.Text = state.Manifest?.Notes?.Trim() is { Length: > 0 } notes
            ? notes : "此版本暂未提供详细更新说明。";
        UpdateNotesPanel.Visibility = state.Manifest is null ? Visibility.Collapsed : Visibility.Visible;
        UpdateProgress.Visibility = state.Phase is UpdatePhase.Downloading or UpdatePhase.Downloaded
            ? Visibility.Visible : Visibility.Collapsed;
        UpdateProgress.Value = state.Progress;
        CheckUpdateButton.IsEnabled = state.Phase is not (UpdatePhase.Checking or UpdatePhase.Downloading);
        DownloadUpdateButton.Visibility = state.Phase == UpdatePhase.Available ? Visibility.Visible : Visibility.Collapsed;
        InstallUpdateButton.Visibility = state.Phase == UpdatePhase.Downloaded ? Visibility.Visible : Visibility.Collapsed;
        IgnoreUpdateButton.Visibility = state.Phase == UpdatePhase.Available ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_controller.IsExiting) return;
        e.Cancel = true;
        Hide();
    }

    private void SizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (SizeValue is null) return;
        SizeValue.Text = $"{(int)e.NewValue} px";
        if (!_refreshing) _controller.SetSize(e.NewValue);
    }

    private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (OpacityValue is null) return;
        OpacityValue.Text = $"{(int)e.NewValue}%";
        if (!_refreshing) _controller.SetOpacity(e.NewValue);
    }

    private void TopmostCheck_Click(object sender, RoutedEventArgs e)
    {
        if (!_refreshing) _controller.SetAlwaysOnTop(TopmostCheck.IsChecked == true);
    }

    private void StartWithWindowsCheck_Click(object sender, RoutedEventArgs e)
    {
        if (_refreshing) return;
        try { _controller.SetStartWithWindows(StartWithWindowsCheck.IsChecked == true); }
        catch (Exception error)
        {
            RefreshAll();
            WpfMessageBox.Show(this, NetworkConnectionErrors.ForUser(error, "暂时无法修改开机启动设置。"), "设置开机启动失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void MirrorCheck_Click(object sender, RoutedEventArgs e)
    {
        if (!_refreshing) _controller.SetMirrored(MirrorCheck.IsChecked == true);
    }

    private void ClickThroughButton_Click(object sender, RoutedEventArgs e)
        => _controller.SetClickThrough(!_controller.Settings.ClickThrough);

    private void OpenFakeAd_Click(object sender, RoutedEventArgs e) => _controller.ShowFakeAdWindow();

    private void PersonalityCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshing || PersonalityCombo.SelectedItem is not ComboBoxItem item) return;
        _controller.SetPersonality(item.Tag?.ToString() ?? "lively");
    }

    private void StartTheater_Click(object sender, RoutedEventArgs e) => _controller.StartTheater();

    private void MouseInteractionCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!_refreshing) _controller.SetMouseInteraction(MouseInteractionCheck.IsChecked == true);
    }

    private void RandomMovementCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!_refreshing) _controller.SetRandomMovement(RandomMovementCheck.IsChecked == true);
    }

    private void RandomInteractionCheck_Changed(object sender, RoutedEventArgs e)
        => ApplyInteractionSettings();

    private void InteractionModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_refreshing) ApplyInteractionSettings();
    }

    private void ApplyInteractionSettings()
    {
        if (_refreshing || InteractionModeCombo.SelectedItem is not ComboBoxItem item) return;
        _controller.SetInteractionConfig(
            RandomInteractionCheck.IsChecked == true,
            item.Tag?.ToString() ?? "standard");
    }

    private void TryInteraction_Click(object sender, RoutedEventArgs e)
        => _controller.StartRandomInteraction();

    private async void SyncInteractionContent_Click(object sender, RoutedEventArgs e)
    {
        if (!_controller.RequestPremiumAccess("在线互动内容", this)) return;
        if (_interactionContentLoading) return;
        SetInteractionContentLoading(true, "正在找新趣事…");
        try
        {
            var added = await _controller.SyncInteractionContentAsync();
            RefreshAll();
            InteractionContentStatusText.Text = $"{_controller.InteractionStatus} · 本次新增 {added} 条";
        }
        catch (Exception error)
        {
            InteractionContentStatusText.Text = _controller.InteractionStatus;
            WpfMessageBox.Show(this, NetworkConnectionErrors.ForUser(error, "暂时没找到新趣事，稍后再试。"), "稍后再试", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            SetInteractionContentLoading(false);
        }
    }

    private async void DownloadInteractionPack_Click(object sender, RoutedEventArgs e)
    {
        if (!_controller.RequestPremiumAccess("互动内容包", this)) return;
        if (_interactionContentLoading) return;
        SetInteractionContentLoading(true, "正在下载离线内容包…");
        try
        {
            var count = await _controller.DownloadInteractionPackAsync();
            RefreshAll();
            InteractionContentStatusText.Text = $"{_controller.InteractionStatus} · 离线包共 {count} 条";
        }
        catch (Exception error)
        {
            InteractionContentStatusText.Text = _controller.InteractionStatus;
            WpfMessageBox.Show(this, NetworkConnectionErrors.ForUser(error, "暂时无法下载离线内容，请稍后重试。"), "下载离线内容失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            SetInteractionContentLoading(false);
        }
    }

    private void SetInteractionContentLoading(bool loading, string? status = null)
    {
        _interactionContentLoading = loading;
        SyncInteractionContentButton.IsEnabled = !loading;
        DownloadInteractionPackButton.IsEnabled = !loading;
        if (status is not null) InteractionContentStatusText.Text = status;
    }

    private void TheaterEnabledCheck_Changed(object sender, RoutedEventArgs e) => ApplyTheaterSettings();

    private void TheaterIntervalCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_refreshing) ApplyTheaterSettings();
    }

    private void ApplyTheaterSettings()
    {
        if (_refreshing) return;
        var seconds = TheaterIntervalCombo.SelectedItem is ComboBoxItem item
            && int.TryParse(item.Tag?.ToString(), out var parsed) ? parsed : 300;
        _controller.SetTheaterConfig(TheaterEnabledCheck.IsChecked == true, seconds);
    }

    private void AddPet_Click(object sender, RoutedEventArgs e)
    {
        if (_controller.Settings.Pets.Count >= 3) { WpfMessageBox.Show(this, "自定义桌宠已满（3/3），请先删除不再使用的 GIF。", "已达到容量上限"); return; }
        var dialog = new WpfOpenFileDialog { Title = "选择 GIF（最多 8 MB、2048×2048）", Filter = "GIF 动图 (*.gif)|*.gif", Multiselect = false };
        if (dialog.ShowDialog(this) != true) return;
        RunUiAction(() => _controller.AddPet(dialog.FileName));
    }

    private void UseSelectedPet_Click(object sender, RoutedEventArgs e)
    {
        if (PetList.SelectedItem is PetDefinition pet) _controller.SelectPet(pet.Id);
    }

    private void UseDefaultPet_Click(object sender, RoutedEventArgs e) => _controller.SelectPet(null);

    private void DeletePet_Click(object sender, RoutedEventArgs e)
    {
        if (PetList.SelectedItem is not PetDefinition pet) return;
        if (WpfMessageBox.Show(this, $"删除桌宠“{pet.Name}”？", "删除桌宠", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        _controller.DeletePet(pet.Id);
    }

    private void ChooseLibrary_Click(object sender, RoutedEventArgs e)
    {
        if (_controller.Settings.Libraries.Count >= 3) { WpfMessageBox.Show(this, "外部图鉴已满（3/3），请先删除一个目录。", "已达到容量上限"); return; }
        if (!_controller.RequestPremiumAccess("外部 GIF 资源库", this)) return;
        using var dialog = new Forms.FolderBrowserDialog { Description = "选择桌宠图鉴的 GIF 文件夹", UseDescriptionForTitle = true };
        if (dialog.ShowDialog() != Forms.DialogResult.OK) return;
        RunUiAction(() => _controller.AddLibraryDirectory(dialog.SelectedPath));
    }

    private void LibraryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshing || LibraryList.SelectedItem is not LibraryListItem item) return;
        RunUiAction(() => _controller.SelectLibrary(item.Id));
    }

    private void DeleteLibrary_Click(object sender, RoutedEventArgs e)
    {
        if (LibraryList.SelectedItem is not LibraryListItem { Id: not null } item) return;
        if (WpfMessageBox.Show(this, $"删除资源库“{item.Name}”？", "删除资源库",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        RunUiAction(() => _controller.DeleteLibrary(item.Id));
    }

    private void RandomPetCheck_Click(object sender, RoutedEventArgs e) => ApplyRandomSettings();

    private void RandomIntervalCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_refreshing) ApplyRandomSettings();
    }

    private void ApplyRandomSettings()
    {
        if (_refreshing) return;
        var seconds = RandomIntervalCombo.SelectedItem is ComboBoxItem item
            && int.TryParse(item.Tag?.ToString(), out var parsed) ? parsed : 300;
        _controller.SetRandomPetConfig(RandomPetCheck.IsChecked == true, seconds);
    }

    private void RandomizeNow_Click(object sender, RoutedEventArgs e) => _controller.RandomizePet();

    private void ImportWords_Click(object sender, RoutedEventArgs e)
    {
        if (_controller.Settings.InteractionWordPacks.Count >= 5) { WpfMessageBox.Show(this, "词包已满（5/5），请先删除一套。", "已达到容量上限"); return; }
        if (!_controller.RequestPremiumAccess("互动词包导入", this)) return;
        var dialog = new WpfOpenFileDialog { Title = $"导入互动词包（还可添加 {5 - _controller.Settings.InteractionWordPacks.Count} 套）", Filter = "词包 (*.json;*.txt)|*.json;*.txt", Multiselect = true };
        if (dialog.ShowDialog(this) != true) return;
        RunUiAction(() => _controller.ImportInteractionWords(dialog.FileNames));
    }

    private void WordPackList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshing || WordPackList.SelectedItem is not WordPackListItem item) return;
        RunUiAction(() => _controller.SelectInteractionWordPack(item.Id));
    }

    private void DeleteWordPack_Click(object sender, RoutedEventArgs e)
    {
        if (WordPackList.SelectedItem is not WordPackListItem { Id: not null } item) return;
        if (WpfMessageBox.Show(this, $"删除互动词包“{item.Name}”？", "删除词包",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        RunUiAction(() => _controller.DeleteInteractionWordPack(item.Id));
    }

    private void OpenWordPackGuide_Click(object sender, RoutedEventArgs e)
        => new WordPackGuideWindow { Owner = this }.ShowDialog();

    private void ImportTheaterScript_Click(object sender, RoutedEventArgs e)
    {
        if (_controller.Settings.TheaterScripts.Count >= 10) { WpfMessageBox.Show(this, "剧本已满（10/10），请先删除一个剧本。", "已达到容量上限"); return; }
        if (!_controller.RequestPremiumAccess("小剧场剧本导入", this)) return;
        var dialog = new WpfOpenFileDialog
        {
            Title = $"导入小剧场剧本（还可添加 {10 - _controller.Settings.TheaterScripts.Count} 个）",
            Filter = "小剧场剧本 (*.json)|*.json",
            Multiselect = true
        };
        if (dialog.ShowDialog(this) != true) return;
        RunUiAction(() => _controller.ImportTheaterScripts(dialog.FileNames));
    }

    private void TheaterScriptList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_refreshing) DeleteTheaterScriptButton.IsEnabled = TheaterScriptList.SelectedItem is not null;
    }

    private void DeleteTheaterScript_Click(object sender, RoutedEventArgs e)
    {
        if (TheaterScriptList.SelectedItem is not TheaterScriptListItem item) return;
        if (WpfMessageBox.Show(this, $"删除小剧场剧本“{item.Name}”？", "删除剧本",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        RunUiAction(() => _controller.DeleteTheaterScript(item.Id));
    }

    private void OpenTheaterScriptGuide_Click(object sender, RoutedEventArgs e)
        => new TheaterScriptGuideWindow { Owner = this }.ShowDialog();

    private void ReminderList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshing || ReminderList.SelectedItem is not ReminderListItem item) return;
        LoadReminder(item.Reminder);
    }

    private void NewReminder_Click(object sender, RoutedEventArgs e)
    {
        if (_controller.Settings.Reminders.Count >= 20) { WpfMessageBox.Show(this, "提醒已满（20/20），请先删除一条提醒。", "已达到容量上限"); return; }
        if (!_controller.RequestPremiumAccess("提醒", this)) return;
        _editingReminderId = null;
        ReminderList.SelectedItem = null;
        ReminderFormTitle.Text = "新建提醒";
        var nextReminder = ReminderSchedule.DefaultTime(DateTime.Now);
        ReminderDatePicker.SelectedDate = nextReminder.Date;
        SetReminderTime(nextReminder.TimeOfDay);
        ReminderMessageText.Text = "休息一下吧";
        ReminderEnabledCheck.IsChecked = true;
        ReminderDailyCheck.IsChecked = false;
        ReminderExpressionPath.Text = string.Empty;
        DeleteReminderButton.IsEnabled = false;
    }

    private void LoadReminder(ReminderDefinition reminder)
    {
        _editingReminderId = reminder.Id;
        ReminderFormTitle.Text = "编辑提醒";
        ReminderDatePicker.SelectedDate = reminder.LocalTime.Date;
        SetReminderTime(reminder.LocalTime.TimeOfDay);
        ReminderMessageText.Text = reminder.Message;
        ReminderEnabledCheck.IsChecked = reminder.Enabled;
        ReminderDailyCheck.IsChecked = reminder.RepeatDaily;
        ReminderExpressionPath.Text = reminder.ExpressionPath ?? string.Empty;
        DeleteReminderButton.IsEnabled = true;
    }

    private void ChooseReminderExpression_Click(object sender, RoutedEventArgs e)
    {
        if (!_controller.RequestPremiumAccess("提醒表情", this)) return;
        var dialog = new WpfOpenFileDialog
        {
            Title = "选择提醒出现时展示的 GIF",
            Filter = "GIF 动图 (*.gif)|*.gif",
            Multiselect = false
        };
        if (dialog.ShowDialog(this) == true) ReminderExpressionPath.Text = dialog.FileName;
    }

    private void ClearReminderExpression_Click(object sender, RoutedEventArgs e)
        => ReminderExpressionPath.Text = string.Empty;

    private void SaveReminder_Click(object sender, RoutedEventArgs e)
    {
        if (!_controller.RequestPremiumAccess("提醒", this)) return;
        if (ReminderDatePicker.SelectedDate is not { } date
            || !TryGetReminderTime(out var time))
        {
            WpfMessageBox.Show(this, "请选择有效的日期和时间。", "提醒时间无效", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var reminder = new ReminderDefinition
        {
            Id = _editingReminderId ?? $"reminder-{Guid.NewGuid():N}",
            Enabled = ReminderEnabledCheck.IsChecked == true,
            Message = ReminderMessageText.Text,
            Emotion = "happy",
            ExpressionPath = string.IsNullOrWhiteSpace(ReminderExpressionPath.Text)
                ? null : ReminderExpressionPath.Text,
            RepeatDaily = ReminderDailyCheck.IsChecked == true,
            LocalTime = date.Date + time
        };
        RunUiAction(() =>
        {
            _controller.SaveReminder(reminder);
            _editingReminderId = reminder.Id;
            RefreshAll();
            LoadReminder(reminder);
            ReminderFormTitle.Text = $"已保存 · 下次 {reminder.LocalTime:M月d日 HH:mm}";
        });
    }

    private void ReminderTimeSelector_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not System.Windows.Controls.ComboBox { Items.Count: > 0 } selector) return;

        var change = e.Delta > 0 ? -1 : 1;
        var nextIndex = Math.Clamp(selector.SelectedIndex + change, 0, selector.Items.Count - 1);
        if (nextIndex == selector.SelectedIndex) return;

        selector.SelectedIndex = nextIndex;
        e.Handled = true;
    }

    private void SetReminderTime(TimeSpan time)
    {
        ReminderHourCombo.SelectedItem = time.Hours.ToString("00", CultureInfo.InvariantCulture);
        ReminderMinuteCombo.SelectedItem = time.Minutes.ToString("00", CultureInfo.InvariantCulture);
    }

    private bool TryGetReminderTime(out TimeSpan time)
    {
        var hasHour = int.TryParse(ReminderHourCombo.SelectedItem as string, NumberStyles.None,
            CultureInfo.InvariantCulture, out var hour);
        var hasMinute = int.TryParse(ReminderMinuteCombo.SelectedItem as string, NumberStyles.None,
            CultureInfo.InvariantCulture, out var minute);

        time = hasHour && hasMinute ? new TimeSpan(hour, minute, 0) : default;
        return hasHour && hasMinute;
    }

    private void DeleteReminder_Click(object sender, RoutedEventArgs e)
    {
        if (_editingReminderId is null) return;
        var reminder = _controller.Settings.Reminders.FirstOrDefault(item => item.Id == _editingReminderId);
        if (reminder is null) return;
        if (WpfMessageBox.Show(this, $"删除提醒“{reminder.Message}”？", "删除提醒", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        _controller.DeleteReminder(reminder.Id);
        NewReminder_Click(sender, e);
    }

    private async void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, MainTabs)) return;
        if (MainTabs.SelectedItem == FeedbackTab) await LoadFeedbackAsync();
        else if (MainTabs.SelectedItem == CompanionTab) await LoadCompanionAsync();
        else if (MainTabs.SelectedItem == HallTab) await LoadHallAsync();
    }

    private async Task LoadCompanionAsync()
    {
        if (_companionLoading || !_controller.HasActivatedLicense) return;
        _companionLoading = true;
        CompanionErrorText.Visibility = Visibility.Collapsed;
        RefreshAll();
        try
        {
            await _controller.RefreshCompanionAsync();
        }
        catch (Exception error)
        {
            CompanionErrorText.Text = NetworkConnectionErrors.ForUser(error, "暂时无法连接搭子服务。");
            CompanionErrorText.Visibility = Visibility.Visible;
        }
        finally
        {
            _companionLoading = false;
            RefreshAll();
        }
    }

    private async void RefreshCompanion_Click(object sender, RoutedEventArgs e) => await LoadCompanionAsync();

    private async void RefreshHall_Click(object sender, RoutedEventArgs e) => await LoadHallAsync();

    private async Task LoadHallAsync()
    {
        if (_hallLoading || !_controller.HasPremiumAccess) return;
        _hallLoading = true;
        HallErrorText.Visibility = Visibility.Collapsed;
        RefreshAll();
        try
        {
            await _controller.RefreshCompanionAsync();
            await _controller.RefreshCompanionHallAsync();
        }
        catch (Exception error)
        {
            HallErrorText.Text = NetworkConnectionErrors.ForUser(error, "暂时无法连接大厅，请点刷新重试。");
            HallErrorText.Visibility = Visibility.Visible;
        }
        finally { _hallLoading = false; RefreshAll(); }
    }

    private void RefreshHallSendButton()
    {
        var seconds = Math.Max(0, (int)Math.Ceiling((_hallSendAllowedAt - DateTimeOffset.UtcNow).TotalSeconds));
        SendHallButton.Content = _hallLoading ? "处理中…" : seconds > 0 ? $"{seconds} 秒后可再发送" : "发送这只 GIF";
        SendHallButton.IsEnabled = _controller.HasPremiumAccess && _controller.Companions.Profile?.HallEnabled == true
            && CompanionHallList.SelectedItem is HallListItem && File.Exists(_controller.CurrentPetPath()) && !_hallLoading && seconds == 0;
    }

    private async void SaveHallNickname_Click(object sender, RoutedEventArgs e)
    {
        await RunHallActionAsync(async () =>
        {
            await _controller.UpdateCompanionNameAsync(HallNicknameText.Text);
            HallResultText.Text = "昵称已保存，大厅名片已更新。";
            HallResultText.Visibility = Visibility.Visible;
        });
    }

    private async void CompanionHallEnabled_Click(object sender, RoutedEventArgs e)
    {
        if (_refreshing) return;
        var enabled = CompanionHallEnabledCheck.IsChecked == true;
        await RunHallActionAsync(async () =>
        {
            await _controller.SetCompanionHallEnabledAsync(enabled);
            if (enabled) await _controller.RefreshCompanionHallAsync();
            HallResultText.Text = enabled ? "已加入大厅。现在可以选择用户发送表情。" : "已退出大厅，其他用户不会再看到你在线。";
            HallResultText.Visibility = Visibility.Visible;
        });
    }

    private void CompanionHallList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_refreshing) RefreshAll();
    }

    private async void SendHall_Click(object sender, RoutedEventArgs e)
    {
        if (CompanionHallList.SelectedItem is not HallListItem person) return;
        await RunHallActionAsync(async () =>
        {
            await _controller.SendCurrentGifToHallAsync(person.Id, CompanionHallMessageText.Text);
            CompanionHallMessageText.Clear();
            _hallSendAllowedAt = DateTimeOffset.UtcNow.AddSeconds(30);
            HallResultText.Text = $"已发出，正在投递给 {person.DisplayName}。这不代表对方已经查看。";
            HallResultText.Visibility = Visibility.Visible;
        });
    }

    private async void SaveCompanionName_Click(object sender, RoutedEventArgs e)
        => await RunCompanionActionAsync(async () =>
        {
            await _controller.UpdateCompanionNameAsync(CompanionNameText.Text);
            SaveCompanionNameButton.Content = "已保存";
        });

    private void CopyCompanionCode_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(CompanionCodeText.Text)) return;
        System.Windows.Clipboard.SetText(CompanionCodeText.Text);
        CopyCompanionCodeButton.Content = "已复制";
    }

    private void CopyCompanionShare_Click(object sender, RoutedEventArgs e)
    {
        var code = CompanionCodeText.Text.Trim();
        if (string.IsNullOrWhiteSpace(code)) return;
        var name = string.IsNullOrWhiteSpace(CompanionNameText.Text)
            ? "我"
            : CompanionNameText.Text.Trim();
        System.Windows.Clipboard.SetText($"{name} 的桌搭子码是 {code}。打开「搭子」填进去，就能互相发 GIF 了。");
        CompanionErrorText.Visibility = Visibility.Collapsed;
        CopyCompanionShareButton.Content = "已复制";
    }

    private async void PairCompanion_Click(object sender, RoutedEventArgs e)
        => await RunCompanionActionAsync(() => _controller.PairCompanionAsync(PairCodeText.Text), clearPairCode: true, success: "已完成配对，可以把当前 GIF 发给搭子了。");

    private async void SendCompanionGif_Click(object sender, RoutedEventArgs e)
        => await RunCompanionActionAsync(() => _controller.SendCurrentGifToCompanionAsync(), success: "GIF 已发出，等待对方设备接收；这不代表对方已经查看。");

    private async void UnpairCompanion_Click(object sender, RoutedEventArgs e)
    {
        if (WpfMessageBox.Show(this, "解除搭子绑定？双方之后都不能继续投递 GIF。", "解除绑定",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        await RunCompanionActionAsync(() => _controller.UnpairCompanionAsync(), success: "已解除配对，双方可以重新绑定搭子。");
    }

    private void CompanionActivate_Click(object sender, RoutedEventArgs e)
    {
        if (_controller.ShowActivation(this, "绑定一位熟人后，可以把当前 GIF 发到对方桌角。")) _ = LoadCompanionAsync();
    }

    private async Task RunCompanionActionAsync(Func<Task> action, bool clearPairCode = false, string? success = null)
    {
        if (_companionLoading) return;
        _companionLoading = true;
        CompanionErrorText.Visibility = Visibility.Collapsed;
        CompanionResultText.Visibility = Visibility.Collapsed;
        RefreshAll();
        try
        {
            await action();
            if (clearPairCode) PairCodeText.Clear();
            if (success is not null) { CompanionResultText.Text = success; CompanionResultText.Visibility = Visibility.Visible; }
        }
        catch (Exception error)
        {
            CompanionErrorText.Text = NetworkConnectionErrors.ForUser(error, "操作未完成，请稍后重试。");
            CompanionErrorText.Visibility = Visibility.Visible;
        }
        finally
        {
            _companionLoading = false;
            RefreshAll();
        }
    }

    private async Task RunHallActionAsync(Func<Task> action)
    {
        if (_hallLoading) return;
        _hallLoading = true;
        HallErrorText.Visibility = Visibility.Collapsed;
        RefreshAll();
        try { await action(); }
        catch (Exception error)
        {
            HallErrorText.Text = NetworkConnectionErrors.ForUser(error, "大厅操作未完成，请稍后重试。");
            HallErrorText.Visibility = Visibility.Visible;
        }
        finally
        {
            _hallLoading = false;
            RefreshAll();
        }
    }

    private async void FeedbackReload_Click(object sender, RoutedEventArgs e) => await LoadFeedbackAsync();

    private async Task LoadFeedbackAsync()
    {
        if (_feedbackLoading) return;
        _feedbackLoading = true;
        FeedbackReloadButton.IsEnabled = false;
        FeedbackSubmitButton.IsEnabled = false;
        FeedbackErrorText.Visibility = Visibility.Collapsed;
        try
        {
            var response = await _controller.Feedback.GetAsync();
            FeedbackList.ItemsSource = response.Items.Select(item => new FeedbackListItem(item)).ToList();
            FeedbackEmptyState.Visibility = response.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            FeedbackQuotaText.Text = response.Quota.Remaining > 0
                ? $"当前设备有 {response.Quota.Active}/{response.Quota.Maximum} 条处理中反馈，还可提交 {response.Quota.Remaining} 条"
                : $"当前设备已有 {response.Quota.Active} 条处理中反馈，请等待后台处理后再提交";
            _feedbackLoading = false;
            SetFeedbackFormEnabled(response.Quota.Remaining > 0);
        }
        catch (Exception error)
        {
            FeedbackErrorText.Text = error.Message;
            FeedbackErrorText.Visibility = Visibility.Visible;
            FeedbackQuotaText.Text = "暂时无法获取反馈记录";
            SetFeedbackFormEnabled(true);
        }
        finally
        {
            _feedbackLoading = false;
            FeedbackReloadButton.IsEnabled = true;
        }
    }

    private async void FeedbackSubmit_Click(object sender, RoutedEventArgs e)
    {
        if (_feedbackLoading) return;
        var title = FeedbackTitleText.Text.Trim();
        var content = FeedbackContentText.Text.Trim();
        if (title.Length < 2 || content.Length < 5)
        {
            FeedbackErrorText.Text = title.Length < 2
                ? "反馈标题至少需要 2 个字符。"
                : "请补充至少 5 个字符的详细说明。";
            FeedbackErrorText.Visibility = Visibility.Visible;
            return;
        }

        var type = (FeedbackTypeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "problem";
        _feedbackLoading = true;
        SetFeedbackFormEnabled(false);
        FeedbackReloadButton.IsEnabled = false;
        FeedbackErrorText.Visibility = Visibility.Collapsed;
        try
        {
            await _controller.Feedback.SubmitAsync(type, title, content);
            FeedbackTitleText.Clear();
            FeedbackContentText.Clear();
            _feedbackLoading = false;
            await LoadFeedbackAsync();
        }
        catch (Exception error)
        {
            FeedbackErrorText.Text = error.Message;
            FeedbackErrorText.Visibility = Visibility.Visible;
            SetFeedbackFormEnabled(true);
        }
        finally
        {
            _feedbackLoading = false;
            FeedbackReloadButton.IsEnabled = true;
        }
    }

    private void SetFeedbackFormEnabled(bool enabled)
    {
        FeedbackTypeCombo.IsEnabled = enabled;
        FeedbackTitleText.IsEnabled = enabled;
        FeedbackContentText.IsEnabled = enabled;
        FeedbackSubmitButton.IsEnabled = enabled;
        FeedbackFormHint.Text = _feedbackLoading && !enabled
            ? "正在提交，请稍候…"
            : enabled ? "待处理和进行中的反馈最多同时保留 3 条。"
            : "已有 3 条反馈正在处理，后台处理完成后会自动释放名额。";
    }

    private void DailySpeechCheck_Click(object sender, RoutedEventArgs e)
    {
        if (!_refreshing) _controller.SetDailySpeech(DailySpeechCheck.IsChecked == true);
    }

    private void PauseCompanionship_Click(object sender, RoutedEventArgs e)
    {
        if (_controller.IsQuiet) _controller.ResumeCompanionship(); else _controller.PauseForOneHour();
    }

    private void StopPerformance_Click(object sender, RoutedEventArgs e) => _controller.StopCurrentPerformance();
    private void RetryTrial_Click(object sender, RoutedEventArgs e) => _controller.RetryTrialVerification();
    private void ReplayGuide_Click(object sender, RoutedEventArgs e) => _controller.ReplayGuide();

    private void FeedbackList_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { ShowFeedbackDetails(); e.Handled = true; }
    }

    private void FeedbackDetails_Click(object sender, MouseButtonEventArgs e) => ShowFeedbackDetails();

    private void ShowFeedbackDetails()
    {
        if (FeedbackList.SelectedItem is not FeedbackListItem selected) return;
        var item = selected.Item;
        var text = $"{item.Title}\n\n{selected.Metadata}\n更新时间：{item.UpdatedAt}\n\n你的说明\n{item.Content}\n\n完整回复\n{(string.IsNullOrWhiteSpace(item.AdminNote) ? "暂未回复" : item.AdminNote)}";
        var body = new System.Windows.Controls.TextBox
        {
            Text = text, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(20),
            BorderThickness = new Thickness(0), Background = System.Windows.Media.Brushes.Transparent,
            FontSize = 14, Padding = new Thickness(8)
        };
        var window = new Window
        {
            Owner = this, Title = "反馈详情 · 可选择复制全文", Width = 660, Height = 600,
            MinWidth = 440, MinHeight = 360, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = body, Background = System.Windows.Media.Brushes.White
        };
        window.ShowDialog();
    }

    private void AutoUpdateCheck_Click(object sender, RoutedEventArgs e)
    {
        if (!_refreshing) _controller.SetAutoCheckUpdates(AutoUpdateCheck.IsChecked == true);
    }

    private void Reactivate_Click(object sender, RoutedEventArgs e)
    {
        if (_controller.ShowActivation(this)) RefreshAll();
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        try { await _controller.Updates.CheckAsync(); }
        catch (Exception error) { WpfMessageBox.Show(this, NetworkConnectionErrors.ForUser(error, "暂时无法检查更新，请稍后重试。"), "检查更新失败", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private async void DownloadUpdate_Click(object sender, RoutedEventArgs e)
    {
        try { await _controller.Updates.DownloadAsync(); }
        catch (Exception error) { WpfMessageBox.Show(this, NetworkConnectionErrors.ForUser(error, "暂时无法下载更新，请稍后重试。"), "下载更新失败", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void InstallUpdate_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _controller.Updates.InstallDownloaded();
            _controller.Exit();
        }
        catch (Exception error)
        {
            WpfMessageBox.Show(this, NetworkConnectionErrors.ForUser(error, "暂时无法启动更新，请稍后重试。"), "启动更新失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void IgnoreUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_controller.Updates.State.Manifest is { } manifest) _controller.IgnoreUpdate(manifest.Version);
    }

    private void RunUiAction(Action action)
    {
        try { action(); }
        catch (Exception error) { WpfMessageBox.Show(this, NetworkConnectionErrors.ForUser(error, "操作未完成，请稍后重试。"), "操作失败", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private sealed class ReminderListItem
    {
        public ReminderDefinition Reminder { get; }
        public string Id => Reminder.Id;
        public string Display
        {
            get
            {
                var when = Reminder.RepeatDaily
                    ? $"每天 {Reminder.LocalTime:HH:mm}"
                    : Reminder.LocalTime.ToString("M月d日 HH:mm", CultureInfo.GetCultureInfo("zh-CN"));
                var state = Reminder.Enabled ? "下次" : "已关闭";
                return $"{state} {when}  {Reminder.Message}";
            }
        }
        public ReminderListItem(ReminderDefinition reminder) => Reminder = reminder;
    }

    private sealed class FeedbackListItem
    {
        public string Title { get; }
        public string Detail { get; }
        public FeedbackItem Item { get; }
        public string Metadata { get; }

        public FeedbackListItem(FeedbackItem item)
        {
            Item = item;
            Title = item.Title;
            var type = item.Type == "suggestion" ? "功能建议" : "问题反馈";
            var status = item.Status switch
            {
                "in_progress" => "进行中",
                "resolved" => "已处理",
                "closed" => "已关闭",
                _ => "待处理"
            };
            var createdAt = DateTimeOffset.TryParse(item.CreatedAt, out var parsed)
                ? parsed.LocalDateTime.ToString("MM/dd HH:mm", CultureInfo.InvariantCulture)
                : "时间未知";
            var note = string.IsNullOrWhiteSpace(item.AdminNote)
                ? string.Empty
                : $" · 回复：{Shorten(item.AdminNote, 80)}";
            Metadata = $"{type} · {status} · {createdAt}";
            Detail = Metadata + note;
        }

        private static string Shorten(string value, int length)
        {
            var clean = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            return clean.Length <= length ? clean : $"{clean[..length]}…";
        }
    }

    private sealed class HallListItem
    {
        public string Id { get; }
        public string DisplayName { get; }
        public string Initial => string.IsNullOrEmpty(DisplayName) ? "搭" : System.Globalization.StringInfo.GetNextTextElement(DisplayName);
        public string StatusText => "● 在线 · 点击选择";

        public HallListItem(CompanionHallPerson person)
        {
            Id = person.Id;
            DisplayName = person.DisplayName;
        }
    }

    private sealed record LibraryListItem(string? Id, string Name, string Detail, bool IsBuiltIn);
    private sealed record WordPackListItem(string? Id, string Name, string Detail, bool IsBuiltIn);
    private sealed record TheaterScriptListItem(string Id, string Name, string Detail);
}
