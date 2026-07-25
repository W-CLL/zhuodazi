using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
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
    private string? _editingReminderId;

    public SettingsWindow(AppController controller)
    {
        _controller = controller;
        _refreshing = true;
        InitializeComponent();
        _controller.StateChanged += Controller_StateChanged;
        _controller.Updates.StateChanged += Updates_StateChanged;
        Loaded += (_, _) =>
        {
            MainTabs.SelectedIndex = 0;
            RefreshAll();
        };
        Closing += OnClosing;
        ReminderDatePicker.SelectedDate = DateTime.Today;
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
        LibraryList.SelectedItem = libraryItems.First(item => item.Id == settings.ActiveLibraryId);
        LibrarySummary.Text = $"已绑定 {settings.Libraries.Count}/3 个目录 · 当前 {_controller.LibraryName} · {_controller.LibraryCount} 个 GIF";
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
        WordPackList.SelectedItem = wordPackItems.First(item => item.Id == settings.ActiveInteractionWordPackId);
        WordPackSummary.Text = $"已上传 {settings.InteractionWordPacks.Count}/5 个词包 · 当前 {(_controller.ActiveInteractionWordPack?.Name ?? "内置提示语")}";
        DeleteWordPackButton.IsEnabled = settings.ActiveInteractionWordPackId is not null;

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
        ReminderCountText.Text = settings.Reminders.Count == 0 ? "暂无提醒" : $"共 {settings.Reminders.Count} 个提醒";

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
            WpfMessageBox.Show(this, error.Message, "设置开机启动失败", MessageBoxButton.OK, MessageBoxImage.Warning);
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
        var dialog = new WpfOpenFileDialog { Title = "导入互动词包", Filter = "词包 (*.json;*.txt)|*.json;*.txt", Multiselect = false };
        if (dialog.ShowDialog(this) != true) return;
        RunUiAction(() => _controller.ImportInteractionWords(dialog.FileName));
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
        var dialog = new WpfOpenFileDialog
        {
            Title = "导入小剧场剧本",
            Filter = "小剧场剧本 (*.json)|*.json",
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true) return;
        RunUiAction(() => _controller.ImportTheaterScript(dialog.FileName));
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
        _editingReminderId = null;
        ReminderList.SelectedItem = null;
        ReminderFormTitle.Text = "新建提醒";
        ReminderDatePicker.SelectedDate = DateTime.Today;
        ReminderTimeText.Text = DateTime.Now.AddMinutes(10).ToString("HH:mm");
        ReminderMessageText.Text = "休息一下吧";
        ReminderEnabledCheck.IsChecked = true;
        ReminderDailyCheck.IsChecked = false;
        ReminderEmotionCombo.SelectedIndex = 0;
        DeleteReminderButton.IsEnabled = false;
    }

    private void LoadReminder(ReminderDefinition reminder)
    {
        _editingReminderId = reminder.Id;
        ReminderFormTitle.Text = "编辑提醒";
        ReminderDatePicker.SelectedDate = reminder.LocalTime.Date;
        ReminderTimeText.Text = reminder.LocalTime.ToString("HH:mm");
        ReminderMessageText.Text = reminder.Message;
        ReminderEnabledCheck.IsChecked = reminder.Enabled;
        ReminderDailyCheck.IsChecked = reminder.RepeatDaily;
        foreach (var item in ReminderEmotionCombo.Items.OfType<ComboBoxItem>())
            if (item.Tag?.ToString() == reminder.Emotion) item.IsSelected = true;
        DeleteReminderButton.IsEnabled = true;
    }

    private void SaveReminder_Click(object sender, RoutedEventArgs e)
    {
        if (ReminderDatePicker.SelectedDate is not { } date
            || !TimeSpan.TryParseExact(ReminderTimeText.Text.Trim(), ["h\\:mm", "hh\\:mm"], CultureInfo.InvariantCulture, out var time))
        {
            WpfMessageBox.Show(this, "请输入有效的日期和时间，例如 09:30。", "提醒时间无效", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var reminder = new ReminderDefinition
        {
            Id = _editingReminderId ?? $"reminder-{Guid.NewGuid():N}",
            Enabled = ReminderEnabledCheck.IsChecked == true,
            Message = ReminderMessageText.Text,
            Emotion = (ReminderEmotionCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "happy",
            RepeatDaily = ReminderDailyCheck.IsChecked == true,
            LocalTime = date.Date + time
        };
        RunUiAction(() => _controller.SaveReminder(reminder));
        _editingReminderId = reminder.Id;
        RefreshAll();
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
        catch (Exception error) { WpfMessageBox.Show(this, error.Message, "检查更新失败", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private async void DownloadUpdate_Click(object sender, RoutedEventArgs e)
    {
        try { await _controller.Updates.DownloadAsync(); }
        catch (Exception error) { WpfMessageBox.Show(this, error.Message, "下载更新失败", MessageBoxButton.OK, MessageBoxImage.Warning); }
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
            WpfMessageBox.Show(this, error.Message, "启动更新失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void IgnoreUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_controller.Updates.State.Manifest is { } manifest) _controller.IgnoreUpdate(manifest.Version);
    }

    private void RunUiAction(Action action)
    {
        try { action(); }
        catch (Exception error) { WpfMessageBox.Show(this, error.Message, "操作失败", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private sealed class ReminderListItem
    {
        public ReminderDefinition Reminder { get; }
        public string Id => Reminder.Id;
        public string Display => $"{(Reminder.Enabled ? "●" : "○")} {Reminder.LocalTime:MM/dd HH:mm}  {Reminder.Message}";
        public ReminderListItem(ReminderDefinition reminder) => Reminder = reminder;
    }

    private sealed record LibraryListItem(string? Id, string Name, string Detail, bool IsBuiltIn);
    private sealed record WordPackListItem(string? Id, string Name, string Detail, bool IsBuiltIn);
    private sealed record TheaterScriptListItem(string Id, string Name, string Detail);
}
