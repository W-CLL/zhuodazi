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
            MainTabs.SelectedIndex = 0;
            RefreshAll();
        };
        Closing += OnClosing;
        ReminderDatePicker.SelectedDate = DateTime.Today;
        ReminderHourCombo.ItemsSource = Enumerable.Range(0, 24)
            .Select(hour => hour.ToString("00", CultureInfo.InvariantCulture)).ToList();
        ReminderMinuteCombo.ItemsSource = Enumerable.Range(0, 60)
            .Select(minute => minute.ToString("00", CultureInfo.InvariantCulture)).ToList();
        SetReminderTime(DateTime.Now.AddMinutes(10).TimeOfDay);
        RefreshAll();
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

    private void RefreshAll()
    {
        _refreshing = true;
        var settings = _controller.Settings;
        var premium = _controller.HasPremiumAccess;
        SizeSlider.Value = settings.Size;
        SizeValue.Text = $"{settings.Size} px";
        OpacitySlider.Value = settings.Opacity;
        OpacityValue.Text = $"{settings.Opacity}%";
        CurrentPetPreview.FilePath = _controller.CurrentPetPath();
        CurrentPetNameText.Text = _controller.LibraryName;
        LicenseBannerText.Text = _controller.LicenseSummary;
        var announcement = _controller.RemoteConfig.Announcement;
        AnnouncementBanner.Visibility = string.IsNullOrWhiteSpace(announcement) ? Visibility.Collapsed : Visibility.Visible;
        AnnouncementText.Text = announcement;
        FishModeRow.Visibility = _controller.RemoteConfig.FishMode ? Visibility.Visible : Visibility.Collapsed;
        CompanionHallPanel.Visibility = _controller.RemoteConfig.CompanionHall ? Visibility.Visible : Visibility.Collapsed;
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
        InteractionModeCombo.IsEnabled = settings.RandomInteractionsEnabled;
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
            new(null, "月薪喵", "内置资源库", true)
        };
        libraryItems.AddRange(settings.Libraries.Select(item => new LibraryListItem(
            item.Id, item.Name, item.Path, false)));
        LibraryList.ItemsSource = libraryItems;
        LibraryList.SelectedItem = premium
            ? libraryItems.First(item => item.Id == settings.ActiveLibraryId)
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
            new(null, "内置提示语", "桌搭子默认互动内容", true)
        };
        wordPackItems.AddRange(settings.InteractionWordPacks.Select(item => new WordPackListItem(
            item.Id, item.Name, $"{item.WordCount} 条互动台词", false)));
        WordPackList.ItemsSource = wordPackItems;
        WordPackList.SelectedItem = premium
            ? wordPackItems.First(item => item.Id == settings.ActiveInteractionWordPackId)
            : wordPackItems[0];
        WordPackSummary.Text = $"已上传 {settings.InteractionWordPacks.Count}/5 个词包 · 当前 {(_controller.ActiveInteractionWordPack?.Name ?? "内置提示语")}";
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
            ? settings.Reminders.Count == 0 ? "暂无提醒" : $"共 {settings.Reminders.Count} 个提醒"
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

        var hallEnabled = companionEnabled && companionProfile?.HallEnabled == true;
        CompanionHallEnabledCheck.IsChecked = companionProfile?.HallEnabled == true;
        CompanionHallEnabledCheck.IsEnabled = companionEnabled && !_hallLoading;
        RefreshHallButton.IsEnabled = companionEnabled && !_hallLoading;
        CompanionHallStatusText.Text = hallEnabled
            ? $"大厅已开启 · 当前 {_controller.Companions.HallPeople.Count} 人在线"
            : "关闭大厅后，你不会出现在陌生人列表里。";
        var selectedHallId = (CompanionHallList.SelectedItem as HallListItem)?.Id;
        var hallItems = _controller.Companions.HallPeople
            .Select(person => new HallListItem(person))
            .ToList();
        CompanionHallList.ItemsSource = hallItems;
        CompanionHallList.SelectedItem = hallItems.FirstOrDefault(item => item.Id == selectedHallId);
        CompanionHallList.IsEnabled = hallEnabled && !_hallLoading;
        CompanionHallEmptyText.Visibility = hallItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        CompanionHallMessageText.IsEnabled = hallEnabled;
        SendHallButton.IsEnabled = hallEnabled
            && CompanionHallList.SelectedItem is HallListItem
            && File.Exists(_controller.CurrentPetPath())
            && !_hallLoading;

        AutoUpdateCheck.IsChecked = settings.AutoCheckUpdates;
        AutoUpdateCheck.Visibility = _controller.RemoteConfig.AutoUpdates ? Visibility.Visible : Visibility.Collapsed;
        CurrentVersionText.Text = $"当前版本 v{UpdateService.CurrentVersion}";
        LicenseStatusText.Text = _controller.LicenseSummary;
        RenderUpdateState(_controller.Updates.State);
        _refreshing = false;
    }

    private void RenderUpdateState(UpdateState state)
    {
        UpdateStatusText.Text = state.Message;
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
        SetInteractionContentLoading(true, "正在同步线上内容…");
        try
        {
            var added = await _controller.SyncInteractionContentAsync();
            RefreshAll();
            InteractionContentStatusText.Text = $"{_controller.InteractionStatus} · 本次新增 {added} 条";
        }
        catch (Exception error)
        {
            InteractionContentStatusText.Text = _controller.InteractionStatus;
            WpfMessageBox.Show(this, NetworkConnectionErrors.ForUser(error, "暂时无法同步互动内容，请稍后重试。"), "同步互动内容失败", MessageBoxButton.OK, MessageBoxImage.Warning);
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
        var dialog = new WpfOpenFileDialog { Title = "选择桌宠 GIF", Filter = "GIF 动图 (*.gif)|*.gif", Multiselect = false };
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
        if (!_controller.RequestPremiumAccess("外部 GIF 资源库", this)) return;
        using var dialog = new Forms.FolderBrowserDialog { Description = "选择包含 GIF 的资源库目录", UseDescriptionForTitle = true };
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
        if (!_controller.RequestPremiumAccess("互动词包导入", this)) return;
        var dialog = new WpfOpenFileDialog { Title = "导入互动词包", Filter = "词包 (*.json;*.txt)|*.json;*.txt", Multiselect = true };
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
        if (!_controller.RequestPremiumAccess("小剧场剧本导入", this)) return;
        var dialog = new WpfOpenFileDialog
        {
            Title = "导入小剧场剧本",
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
        if (!_controller.RequestPremiumAccess("提醒", this)) return;
        _editingReminderId = null;
        ReminderList.SelectedItem = null;
        ReminderFormTitle.Text = "新建提醒";
        ReminderDatePicker.SelectedDate = DateTime.Today;
        SetReminderTime(DateTime.Now.AddMinutes(10).TimeOfDay);
        ReminderMessageText.Text = "休息一下吧";
        ReminderEnabledCheck.IsChecked = true;
        ReminderDailyCheck.IsChecked = false;
        ReminderEmotionCombo.SelectedIndex = 0;
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
        foreach (var item in ReminderEmotionCombo.Items.OfType<ComboBoxItem>())
            if (item.Tag?.ToString() == reminder.Emotion) item.IsSelected = true;
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
            Emotion = (ReminderEmotionCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "happy",
            ExpressionPath = string.IsNullOrWhiteSpace(ReminderExpressionPath.Text)
                ? null : ReminderExpressionPath.Text,
            RepeatDaily = ReminderDailyCheck.IsChecked == true,
            LocalTime = date.Date + time
        };
        RunUiAction(() => _controller.SaveReminder(reminder));
        _editingReminderId = reminder.Id;
        RefreshAll();
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
            await LoadHallAsync();
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
        if (_hallLoading || !_controller.HasActivatedLicense) return;
        _hallLoading = true;
        CompanionErrorText.Visibility = Visibility.Collapsed;
        RefreshAll();
        try { await _controller.RefreshCompanionHallAsync(); }
        catch (Exception error)
        {
            CompanionErrorText.Text = NetworkConnectionErrors.ForUser(error, "暂时无法连接桌宠大厅。");
            CompanionErrorText.Visibility = Visibility.Visible;
        }
        finally
        {
            _hallLoading = false;
            RefreshAll();
        }
    }

    private async void CompanionHallEnabled_Click(object sender, RoutedEventArgs e)
    {
        if (_refreshing) return;
        var enabled = CompanionHallEnabledCheck.IsChecked == true;
        await RunHallActionAsync(() => _controller.SetCompanionHallEnabledAsync(enabled));
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
        });
    }

    private async void SaveCompanionName_Click(object sender, RoutedEventArgs e)
        => await RunCompanionActionAsync(() => _controller.UpdateCompanionNameAsync(CompanionNameText.Text));

    private void CopyCompanionCode_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(CompanionCodeText.Text)) System.Windows.Clipboard.SetText(CompanionCodeText.Text);
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
    }

    private async void PairCompanion_Click(object sender, RoutedEventArgs e)
        => await RunCompanionActionAsync(() => _controller.PairCompanionAsync(PairCodeText.Text));

    private async void SendCompanionGif_Click(object sender, RoutedEventArgs e)
        => await RunCompanionActionAsync(() => _controller.SendCurrentGifToCompanionAsync());

    private async void UnpairCompanion_Click(object sender, RoutedEventArgs e)
    {
        if (WpfMessageBox.Show(this, "解除搭子绑定？双方之后都不能继续投递 GIF。", "解除绑定",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        await RunCompanionActionAsync(() => _controller.UnpairCompanionAsync());
    }

    private void CompanionActivate_Click(object sender, RoutedEventArgs e)
    {
        if (_controller.ShowActivation(this, "绑定一位熟人后，可以把当前 GIF 发到对方桌角。")) _ = LoadCompanionAsync();
    }

    private async Task RunCompanionActionAsync(Func<Task> action)
    {
        if (_companionLoading) return;
        _companionLoading = true;
        CompanionErrorText.Visibility = Visibility.Collapsed;
        RefreshAll();
        try
        {
            await action();
            PairCodeText.Clear();
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
        CompanionErrorText.Visibility = Visibility.Collapsed;
        RefreshAll();
        try { await action(); }
        catch (Exception error)
        {
            CompanionErrorText.Text = NetworkConnectionErrors.ForUser(error, "大厅操作未完成，请稍后重试。");
            CompanionErrorText.Visibility = Visibility.Visible;
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
        FeedbackFormHint.Text = enabled
            ? "待处理和进行中的反馈最多同时保留 3 条。"
            : "已有 3 条反馈正在处理，后台处理完成后会自动释放名额。";
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

        public FeedbackListItem(FeedbackItem item)
        {
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
            Detail = $"{type} · {status} · {createdAt}{note}";
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
        public string StatusText => "在线，可以收到你的表情";

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
