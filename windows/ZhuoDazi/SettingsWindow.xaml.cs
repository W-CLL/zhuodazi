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
    private string? _editingReminderId;

    public SettingsWindow(AppController controller)
    {
        _controller = controller;
        _refreshing = true;
        InitializeComponent();
        MainTabs.SelectionChanged += MainTabs_SelectionChanged;
        _controller.StateChanged += Controller_StateChanged;
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
            : $"免费版正在使用内置资源库 · {_controller.LibraryCount} 个 GIF · 激活可导入外部目录";
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
            : "激活后可使用随机互动、在线内容和互动词包";
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
            : "激活后可创建提醒，并为提醒指定 GIF 表情";
        ReminderEmptyState.Visibility = settings.Reminders.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        AutoUpdateCheck.IsChecked = settings.AutoCheckUpdates;
        CurrentVersionText.Text = $"当前版本 v{UpdateService.CurrentVersion}";
        LicenseStatusText.Text = _controller.LicenseSummary;
        RenderUpdateState(_controller.Updates.State);
        _refreshing = false;
    }

    private void RenderUpdateState(UpdateState state)
    {
        UpdateStatusText.Text = state.Message;
        UpdateNotesText.Visibility = state.Manifest is null ? Visibility.Collapsed : Visibility.Visible;
        UpdateNotesText.Text = state.Manifest?.Notes ?? string.Empty;
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
        if (!ReferenceEquals(e.OriginalSource, MainTabs) || MainTabs.SelectedItem != FeedbackTab) return;
        await LoadFeedbackAsync();
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
        public string Display => $"{(Reminder.Enabled ? "●" : "○")} {Reminder.LocalTime:MM/dd HH:mm}  {Reminder.Message}";
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

    private sealed record LibraryListItem(string? Id, string Name, string Detail, bool IsBuiltIn);
    private sealed record WordPackListItem(string? Id, string Name, string Detail, bool IsBuiltIn);
    private sealed record TheaterScriptListItem(string Id, string Name, string Detail);
}
