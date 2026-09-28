using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ZhuoDazi.Models;
using ZhuoDazi.Services;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using CheckBox = System.Windows.Controls.CheckBox;

namespace ZhuoDazi;

public partial class SettingsWindow
{
    private DateOnly _dailyAnchor = DateOnly.FromDateTime(DateTime.Today);
    private DateOnly _dailyCalendarMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private int _dailyWeekOffset;
    private int _dailyMonthOffset;
    private DateTime _dailySummaryMinute;
    private DailyJournalStore? _dailySeenJournal;
    private string? _dailyRoutineSnapshot;

    public void ShowDailyJournalTab()
    {
        MainTabs.SelectedItem = DailyJournalTab;
        RefreshDailyJournal();
        Show();
        Activate();
    }

    private string DailyPeriod => (DailyPeriodCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "month";
    private static DateOnly DailyToday => DateOnly.FromDateTime(DateTime.Today);
    private static DateOnly DailyMonday(DateOnly day) => day.AddDays(-(((int)day.DayOfWeek + 6) % 7));
    private static DateOnly DailyMonthStart(DateOnly day) => new(day.Year, day.Month, 1);

    private (DateOnly From, DateOnly To) DailyRange()
    {
        var today = DailyToday;
        if (DailyPeriod == "all")
        {
            var first = _controller.Journal.Entries.Select(entry => entry.LocalDate).DefaultIfEmpty(today).Min();
            return (first > today ? today : first, today);
        }
        var from = DailyPeriod == "week" ? DailyMonday(_dailyAnchor) : DailyMonthStart(_dailyAnchor);
        var end = DailyPeriod == "week" ? from.AddDays(6) : from.AddMonths(1).AddDays(-1);
        return (from, end > today ? today : end);
    }

    private void RefreshDailyJournal()
    {
        if (DailyJournalTab is null || DailyStatsPanel is null) return;
        var journal = _controller.Journal;
        if (!ReferenceEquals(_dailySeenJournal, journal))
        {
            _dailySeenJournal = journal;
            _dailyRoutineSnapshot = null;
        }
        if (_dailyAnchor > DailyToday) _dailyAnchor = DailyToday;
        var (from, to) = DailyRange();
        var summary = journal.GetSummary(from, to);
        DailyRangeText.Text = $"{from:yyyy.MM.dd} — {to:yyyy.MM.dd}" + (to == DailyToday ? " · 截至今天" : string.Empty);
        DailyPreviousPeriodButton.IsEnabled = DailyPeriod != "all";
        DailyNextPeriodButton.IsEnabled = DailyPeriod != "all" && (DailyPeriod == "week"
            ? DailyMonday(_dailyAnchor) < DailyMonday(DailyToday)
            : DailyMonthStart(_dailyAnchor) < DailyMonthStart(DailyToday));

        DailyStatsPanel.Children.Clear();
        AddDailyMetric("总互动", summary.TotalInteractions, "次");
        AddDailyMetric("答题总数", summary.QuizzesAnswered, "次");
        AddDailyMetric("答对", summary.QuizzesCorrect, "次");
        AddDailyMetric("心情记录", summary.MoodRecords, "次");
        AddDailyMetric("记录心情", summary.MoodDays, "天");
        AddDailyMetric("一起互动", summary.InteractionDays, "天");
        DailyMoodCountsPanel.Children.Clear();
        foreach (var mood in DailyMoodCatalog.Options)
            AddDailyMoodCount(mood.Code, mood.Label, summary.MoodCounts.GetValueOrDefault(mood.Code));
        foreach (var extra in summary.MoodCounts.Where(pair => pair.Value > 0 && !DailyMoodCatalog.Options.Any(mood => mood.Code == pair.Key)))
            AddDailyMoodCount(extra.Key, DailyMoodCatalog.GetLabel(extra.Key), extra.Value);

        var firstMonth = DailyMonthStart(from);
        var lastMonth = DailyMonthStart(to);
        if (_dailyCalendarMonth < firstMonth) _dailyCalendarMonth = firstMonth;
        if (_dailyCalendarMonth > lastMonth) _dailyCalendarMonth = lastMonth;
        RenderDailyCalendar(from, to);
        RenderDailySummaries();
        RefreshDailyRoutine();
    }

    private void AddDailyMetric(string label, int value, string unit)
    {
        var content = new StackPanel();
        content.Children.Add(new TextBlock { Text = label, FontSize = 12, Foreground = (Brush)FindResource("MutedBrush") });
        content.Children.Add(new TextBlock { Text = $"{value} {unit}", FontSize = 24, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 5, 0, 0) });
        DailyStatsPanel.Children.Add(new Border
        {
            Width = 140, Margin = new Thickness(0, 0, 10, 10), Padding = new Thickness(12),
            Background = (Brush)FindResource("SoftBrush"), CornerRadius = new CornerRadius(6), Child = content
        });
    }

    private void AddDailyMoodCount(string code, string label, int count)
        => DailyMoodCountsPanel.Children.Add(new Border
        {
            Background = DailyMoodBrush(code), Padding = new Thickness(10, 7, 10, 7),
            Margin = new Thickness(0, 0, 8, 8), CornerRadius = new CornerRadius(5),
            Child = new TextBlock { Text = $"{label} {count} 次", FontSize = 12 }
        });

    private static Brush DailyMoodBrush(string? code)
        => new SolidColorBrush((Color)ColorConverter.ConvertFromString(code switch
        {
            "happy" => "#FFF0C9", "hopeful" => "#F6E4D3", "calm" => "#DCEFE9", "okay" => "#EDF1EE",
            "tired" => "#E9E4F3", "annoyed" => "#F7E2DD", "cry" => "#DDE8F4", "bad" or "low" => "#E7E6EF",
            _ => "#F7F9F8"
        }));

    private void RenderDailyCalendar(DateOnly from, DateOnly to)
    {
        DailyCalendarTitle.Text = _dailyCalendarMonth.ToString("yyyy 年 M 月", CultureInfo.InvariantCulture);
        DailyCalendarPreviousButton.IsEnabled = _dailyCalendarMonth > DailyMonthStart(from);
        DailyCalendarNextButton.IsEnabled = _dailyCalendarMonth < DailyMonthStart(to);
        DailyCalendarGrid.Children.Clear();
        var first = DailyMonday(_dailyCalendarMonth);
        var byDay = _controller.Journal.Entries.Where(entry => entry.LocalDate >= first && entry.LocalDate <= first.AddDays(41))
            .GroupBy(entry => entry.LocalDate).ToDictionary(group => group.Key, group => group.OrderBy(entry => entry.OccurredAt).ToArray());
        for (var index = 0; index < 42; index++)
        {
            var day = first.AddDays(index);
            var entries = byDay.GetValueOrDefault(day) ?? [];
            var mood = entries.LastOrDefault(entry => entry.Kind == "mood");
            var inRange = day >= from && day <= to && day.Month == _dailyCalendarMonth.Month;
            var content = new StackPanel { HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch };
            content.Children.Add(new TextBlock { Text = day.Day.ToString(CultureInfo.InvariantCulture), TextAlignment = TextAlignment.Center, FontWeight = day == DailyToday ? FontWeights.Bold : FontWeights.Normal });
            content.Children.Add(new TextBlock
            {
                Text = mood is not null ? DailyMoodCatalog.GetLabel(mood.Mood) : entries.Length > 0 ? "·" : " ",
                TextAlignment = TextAlignment.Center, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 3, 0, 0)
            });
            var cell = new Border
            {
                Child = content, Padding = new Thickness(1, 5, 1, 5), Margin = new Thickness(2), MinHeight = 54,
                Background = DailyMoodBrush(mood?.Mood), Opacity = inRange ? 1 : 0.45,
                BorderBrush = (Brush)FindResource("LineBrush"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5),
                ToolTip = $"{day:yyyy.MM.dd} · {entries.Length} 次互动" + (mood is null ? string.Empty : $" · {DailyMoodCatalog.GetLabel(mood.Mood)}")
            };
            DailyCalendarGrid.Children.Add(cell);
        }
    }

    private void RefreshDailySummaryClock()
    {
        if (!IsVisible || DailyJournalTab?.IsSelected != true) return;
        var now = DateTime.Now;
        var minute = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0);
        if (_dailySummaryMinute == minute) return;
        _dailySummaryMinute = minute;
        RenderDailySummaries();
    }

    private void RenderDailySummaries()
    {
        var config = _controller.RemoteConfig.DailySummaries;
        var now = DateTimeOffset.Now;
        var weekly = DailySummaryService.Build(config, _controller.Journal.Entries, "weekly", _dailyWeekOffset, now);
        var monthly = DailySummaryService.Build(config, _controller.Journal.Entries, "monthly", _dailyMonthOffset, now);
        RenderDailySummary(weekly, true, DailyWeeklyRangeText, DailyWeeklyTitleText, DailyWeeklySummaryText, DailyWeeklyScheduleText);
        RenderDailySummary(monthly, false, DailyMonthlyRangeText, DailyMonthlyTitleText, DailyMonthlySummaryText, DailyMonthlyScheduleText);
        DailyNextWeekButton.IsEnabled = _dailyWeekOffset > 0;
        DailyNextMonthSummaryButton.IsEnabled = _dailyMonthOffset > 0;
    }

    private static void RenderDailySummary(DailySummaryResult result, bool weekly, TextBlock range, TextBlock title, TextBlock body, TextBlock schedule)
    {
        range.Text = result.PeriodStart is { } from && result.PeriodEnd is { } to
            ? weekly ? $"{from:yyyy.MM.dd HH:mm} — {to:yyyy.MM.dd HH:mm} · 已结束"
                : $"{from:yyyy.MM.dd} — {to.AddDays(-1):yyyy.MM.dd} · 已结束"
            : string.Empty;
        title.Text = result.Title;
        title.Visibility = result.Available ? Visibility.Visible : Visibility.Collapsed;
        body.Text = result.Available ? result.Body : result.Status;
        schedule.Text = result.NextAvailableAt is { } next
            ? $"下一期：{next.ToString("yyyy.MM.dd dddd HH:mm", CultureInfo.GetCultureInfo("zh-CN"))} 后可查看"
            : string.Empty;
    }

    private void RefreshDailyRoutine()
    {
        var routine = _controller.Settings.DailyRoutine;
        var snapshot = $"{routine.Enabled}|{routine.OffWorkTime}|{string.Join(",", routine.WorkDays)}";
        // StateChanged can fire while typing: do not discard an unsaved form unless persisted settings changed.
        if (_dailyRoutineSnapshot == snapshot) return;
        _dailyRoutineSnapshot = snapshot;
        DailyRoutineEnabledCheck.IsChecked = routine.Enabled;
        DailyOffWorkTimeText.Text = routine.OffWorkTime;
        foreach (var day in DailyWorkDaysPanel.Children.OfType<CheckBox>())
            day.IsChecked = int.TryParse(day.Tag?.ToString(), out var number) && routine.WorkDays.Contains(number);
    }

    private void DailySaveRoutine_Click(object sender, RoutedEventArgs e)
    {
        if (!TimeOnly.TryParseExact(DailyOffWorkTimeText.Text.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        { DailyRoutineStatusText.Text = "请填写 24 小时制时间，例如 17:00 或 18:30。"; return; }
        var days = DailyWorkDaysPanel.Children.OfType<CheckBox>().Where(day => day.IsChecked == true)
            .Select(day => int.Parse(day.Tag.ToString()!, CultureInfo.InvariantCulture)).ToArray();
        if (DailyRoutineEnabledCheck.IsChecked == true && days.Length == 0)
        { DailyRoutineStatusText.Text = "开启提醒时，请至少选择一个工作日。"; return; }
        try
        {
            _controller.SetDailyRoutine(DailyRoutineEnabledCheck.IsChecked == true, time.ToString("HH:mm", CultureInfo.InvariantCulture), days);
            DailyRoutineStatusText.Text = "工作节奏已保存；今天临时晚一点，可以在桌宠问起时再调整。";
        }
        catch (Exception exception) { DailyRoutineStatusText.Text = "暂时没能保存工作节奏，请重试。"; System.Diagnostics.Debug.WriteLine(exception); }
    }

    private void DailyPeriod_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshing || DailyStatsPanel is null) return;
        _dailyAnchor = DailyToday;
        ResetDailyPeriod();
    }

    private void ResetDailyPeriod()
    {
        _dailyCalendarMonth = DailyMonthStart(_dailyAnchor);
        RefreshDailyJournal();
    }

    private void MoveDailyPeriod(int direction)
    {
        if (DailyPeriod == "all") return;
        var candidate = DailyPeriod == "week" ? _dailyAnchor.AddDays(direction * 7) : DailyMonthStart(_dailyAnchor).AddMonths(direction);
        if (candidate > DailyToday) candidate = DailyToday;
        _dailyAnchor = candidate;
        ResetDailyPeriod();
    }

    private void DailyMood_Click(object sender, RoutedEventArgs e) => _controller.StartMoodInteraction();
    private void DailyWorkday_Click(object sender, RoutedEventArgs e) => _controller.StartWorkdayInteraction();
    private void DailyQuiz_Click(object sender, RoutedEventArgs e) => _controller.StartQuizInteraction();
    private void DailySeeWeek_Click(object sender, RoutedEventArgs e) => DailyWeeklySummaryText.BringIntoView();
    private void DailyPreviousPeriod_Click(object sender, RoutedEventArgs e) => MoveDailyPeriod(-1);
    private void DailyNextPeriod_Click(object sender, RoutedEventArgs e) => MoveDailyPeriod(1);
    private void DailyToday_Click(object sender, RoutedEventArgs e) { _dailyAnchor = DailyToday; ResetDailyPeriod(); }
    private void DailyCalendarPrevious_Click(object sender, RoutedEventArgs e) { _dailyCalendarMonth = _dailyCalendarMonth.AddMonths(-1); RefreshDailyJournal(); }
    private void DailyCalendarNext_Click(object sender, RoutedEventArgs e) { _dailyCalendarMonth = _dailyCalendarMonth.AddMonths(1); RefreshDailyJournal(); }
    private void DailyPreviousWeek_Click(object sender, RoutedEventArgs e) { _dailyWeekOffset++; RenderDailySummaries(); }
    private void DailyNextWeek_Click(object sender, RoutedEventArgs e) { _dailyWeekOffset = Math.Max(0, _dailyWeekOffset - 1); RenderDailySummaries(); }
    private void DailyCurrentWeek_Click(object sender, RoutedEventArgs e) { _dailyWeekOffset = 0; RenderDailySummaries(); }
    private void DailyPreviousMonthSummary_Click(object sender, RoutedEventArgs e) { _dailyMonthOffset++; RenderDailySummaries(); }
    private void DailyNextMonthSummary_Click(object sender, RoutedEventArgs e) { _dailyMonthOffset = Math.Max(0, _dailyMonthOffset - 1); RenderDailySummaries(); }
    private void DailyCurrentMonthSummary_Click(object sender, RoutedEventArgs e) { _dailyMonthOffset = 0; RenderDailySummaries(); }
    private async void DailyRefresh_Click(object sender, RoutedEventArgs e)
    {
        RefreshDailyJournal();
        await _controller.RefreshRemoteConfigAsync();
        RefreshDailyJournal();
    }
}
