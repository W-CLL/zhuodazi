using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ZhuoDazi;
using ZhuoDazi.Models;
using ZhuoDazi.Services;

/// <summary>Real controller/window flow with isolated local data and in-memory HTTP only.</summary>
internal static class DailyFlowChecks
{
    private static int _assertions;

    public static async Task Run(AppController controller, PetWindow pet, string output, Func<string[]> requests,
        Action<DailySummaryConfig?> setSummaryConfiguration)
    {
        await Task.Delay(400);
        Require(pet.IsVisible && controller.HasPremiumAccess, "The real Start flow opens a pet with the test-only trial.");
        await CheckLocalPreviewIsolation(controller, requests);
        var baselineEntries = controller.Journal.Entries.Count;
        controller.StartMoodInteraction();
        await Settle(pet);
        Require(pet.IsInteractionVisible, "The manual mood entry opens a real pet card.");
        foreach (var label in new[] { "开心", "一般", "糟糕", "想哭", "疲惫", "烦躁", "平静", "有盼头" })
            Require(ChoiceButtons(pet).Any(button => ((PetInteractionChoice)button.Tag).Label.Contains(label)), $"Mood option {label} is present.");
        RequireChoicesFit(pet);
        Capture(pet, output, "daily-01-eight-moods");
        pet.DismissCurrentInteraction();
        Require(!pet.IsInteractionVisible, "Closing a mood card dismisses it cleanly.");
        Require(controller.Journal.Entries.Count == baselineEntries, "Closing a mood card cannot create a mood record.");

        controller.StartMoodInteraction();
        await Settle(pet);
        ClickChoice(pet, "cry");
        await Settle(pet);
        Require(controller.Journal.Entries.Count == baselineEntries + 1, "An explicit mood answer creates one local record.");
        Require(controller.Interactions.PendingEventCount == 0, "The new mood taxonomy is not mapped into legacy server events.");
        Capture(pet, output, "daily-02-mood-response");
        pet.DismissCurrentInteraction();
        pet.HideSpeech();

        controller.StartWorkdayInteraction();
        await Settle(pet);
        Require(pet.IsInteractionVisible, "The workday entry opens a real pet card.");
        RequireChoicesFit(pet);
        Capture(pet, output, "daily-03-workday");
        ClickChoice(pet, "overtime");
        await Settle(pet);
        Require(pet.IsInteractionVisible, "Overtime offers a follow-up choice.");
        RequireChoicesFit(pet);
        Capture(pet, output, "daily-04-overtime");
        ClickChoice(pet, "30");
        await Settle(pet);
        Require(controller.Settings.DailyRoutine.NextReminderAt is not null, "An explicit snooze persists a next reminder.");
        var regularTime = controller.Settings.DailyRoutine.OffWorkTime;
        Require(controller.Settings.DailyRoutine.NextReminderAt > DateTimeOffset.Now, "The snooze targets a future time.");
        pet.DismissCurrentInteraction();
        pet.HideSpeech();
        controller.StartWorkdayInteraction();
        await Settle(pet);
        ClickChoice(pet, "done");
        await Settle(pet);
        Require(controller.Settings.DailyRoutine.FinishedToday, "Clocking off stops further automatic reminders today.");
        Require(controller.Settings.DailyRoutine.OffWorkTime == regularTime, "A temporary snooze preserves the regular work schedule.");
        var summary = controller.Journal.GetAllTimeSummary();
        Require(summary.TotalInteractions == baselineEntries + 3, "Mood, overtime and clock-off each count once; selecting a snooze does not double count.");
        Require(summary.MoodRecords == 1 && summary.MoodDays == 1 && summary.QuizzesAnswered == 0,
            "Daily conversations and mood answers do not become quiz answers or extra mood days.");
        Capture(pet, output, "daily-05-clocked-off");
        pet.DismissCurrentInteraction();

        controller.ShowDailyJournal();
        var settings = Application.Current.Windows.OfType<SettingsWindow>().Single();
        settings.Title = "桌搭子 · 日常自动验收（模拟网络 / 独立数据）";
        await Settle(settings);
        var dailyTab = (TabItem)settings.FindName("DailyJournalTab");
        Require(dailyTab.IsSelected, "The journal entry selects the actual daily page.");
        Require(settings.FindName("DailyRecordPanel") is null && settings.FindName("DailyRecordFilterText") is null,
            "The daily page no longer exposes the individual-record list or its filter.");
        Require(!Children<Button>(settings).Any(button => button.Tag is string tag
            && (tag.StartsWith("edit:") || tag.StartsWith("delete:"))),
            "The daily page has no record-editing or record-deletion actions.");
        Capture(settings, output, "daily-06-journal");
        await CaptureSection(settings, "DailyCalendarGrid", output, "daily-06-calendar");
        await CaptureSection(settings, "DailyWeeklySummaryText", output, "daily-06-week-summary");
        ((ScrollViewer)dailyTab.Content).ScrollToTop();
        var periods = (ComboBox)settings.FindName("DailyPeriodCombo");
        foreach (var period in new[] { "week", "month", "all" })
        {
            var choice = periods.Items.OfType<ComboBoxItem>().FirstOrDefault(item => Equals(item.Tag, period));
            Require(choice is not null, $"The journal exposes the {period} view.");
            periods.SelectedItem = choice;
            await Settle(settings);
            Capture(settings, output, "daily-07-" + period);
        }
        await CheckRealQuizFlow(controller, pet, settings, output);
        await CheckRemoteSummaries(controller, settings, output, setSummaryConfiguration);
        await CheckFrequencyRemainsStable(controller);
        CheckCachedQuizSelection(output);
        File.WriteAllText(Path.Combine(output, "daily-flow-results.txt"),
            $"PASS: {_assertions} real WPF daily flow assertions.\n" +
            "Real AppController.Start; all HTTP intercepted by test-only in-memory handlers.\n" +
            "Production settings, startup registration, licenses and server records are untouched.\n");
    }

    private static async Task CheckRealQuizFlow(AppController controller, PetWindow pet, SettingsWindow settings, string output)
    {
        // Only the smoke test's isolated in-memory cache receives this fixture. Network signature verification remains unchanged.
        var state = (InteractionCacheDocument)typeof(InteractionService)
            .GetField("_state", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(controller.Interactions)!;
        state.Items.Add(new InteractionContentItem
        {
            Id = "math.daily-smoke-only", Type = "math", Revision = 1, Prompt = "【自动验收模拟题】2 + 2 等于多少？",
            Answer = "4", Choices = ["3", "4", "5"], Explanation = "这道题只用于本地自动验收，不会加入线上题库。"
        });
        var before = controller.Journal.GetAllTimeSummary();
        ((Button)settings.FindName("DailyQuizButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Settle(pet);
        Require(pet.IsInteractionVisible && ((TextBlock)pet.FindName("InteractionTitle")).Text == "来道数学题",
            "The real daily quiz button opens a question rather than a mood or greeting.");
        Require(controller.Journal.GetAllTimeSummary().QuizzesAnswered == before.QuizzesAnswered, "Merely displaying a quiz does not count as answering.");
        Capture(pet, output, "daily-10-quiz");
        var answer = ChoiceButtons(pet).Single(button => ((PetInteractionChoice)button.Tag).Value == "1");
        answer.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Settle(pet);
        Require(((TextBlock)pet.FindName("InteractionTitle")).Text == "答对了", "A correct choice opens the actual answer feedback.");
        var answered = controller.Journal.GetAllTimeSummary();
        Require(answered.QuizzesAnswered == before.QuizzesAnswered + 1 && answered.QuizzesCorrect == before.QuizzesCorrect + 1,
            "The real answer callback records exactly one answered and correct quiz.");
        Require(answered.MoodRecords == before.MoodRecords && answered.TotalInteractions == before.TotalInteractions + 1,
            "Answering a quiz counts as one interaction and does not change mood records.");
        answer.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(controller.Journal.GetAllTimeSummary().QuizzesAnswered == answered.QuizzesAnswered, "A stale answer button cannot count the quiz twice.");
        Capture(pet, output, "daily-11-answer");
        ClickChoice(pet, "done");
        await Settle(pet);
        Require(!pet.IsInteractionVisible && controller.Journal.GetAllTimeSummary().QuizzesAnswered == answered.QuizzesAnswered,
            "Closing the answer finishes the interaction without a second quiz record.");
    }

    private static async Task CheckLocalPreviewIsolation(AppController controller, Func<string[]> requests)
    {
        Require(controller.IsLocalPreview, "Daily smoke uses the same local-preview isolation as the delivered preview.");
        Require(!requests().Any(request => request.Contains("/api/companion") || request.Contains("/api/analytics")),
            "Local-preview startup does not poll companions, acknowledge visits or post analytics.");
        var before = requests().Length;
        var poll = typeof(AppController).GetMethod("PollCompanionAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        await (Task)poll.Invoke(controller, null)!;
        Require(requests().Length == before, "Explicitly invoking the polling callback in a local preview cannot request deliveries.");
        var rejected = false;
        try { await controller.SetCompanionHallEnabledAsync(true); }
        catch (InvalidOperationException) { rejected = true; }
        Require(rejected && requests().Length == before, "Enabling the companion hall is rejected before any request in a local preview.");
        rejected = false;
        try { await controller.PlayTrialVisitAsync("friend"); }
        catch (InvalidOperationException) { rejected = true; }
        Require(rejected && requests().Length == before, "Trial visitor playback is rejected before consuming a server-side visit.");
    }

    private static void CheckCachedQuizSelection(string output)
    {
        // This local fixture checks selection only; it is never installed into the running pet's content cache.
        var store = new SettingsStore(Path.Combine(output, "selection-check"));
        new InteractionCacheStore(store.InteractionsPath).Save(new InteractionCacheDocument
        {
            LastPromptType = "math",
            Items =
            [
                new() { Id = "math.local-check", Type = "math", Revision = 1, Prompt = "2 + 2 等于多少？", Answer = "4", Choices = ["3", "4"] },
                new() { Id = "joke.local-check", Type = "joke", Revision = 1, Prompt = "仅供选择逻辑检查的笑话", Answer = "测试回答" }
            ]
        });
        using var license = new LicenseService(store.DataDirectory);
        using var service = new InteractionService(store, license);
        Require(service.TakeNextContent(["math", "trivia", "riddle"])?.Type == "math", "Quiz selection remains a quiz even when the previous type was the same.");
        Require(service.TakeNextContent(["math", "trivia", "riddle"]) is null && service.CachedContentCount == 1,
            "An empty quiz subset cannot fall back to or consume a cached joke.");
        Require(service.TakeNextContent()?.Type == "joke", "Ordinary random interaction can still select non-quiz content.");
    }

    private static async Task CheckFrequencyRemainsStable(AppController controller)
    {
        controller.SetDailyInteractionSettings(true, "eager");
        var type = typeof(AppController);
        var timer = (DispatcherTimer)type.GetField("_interactionTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(controller)!;
        Require(timer.Interval >= TimeSpan.FromMinutes(5) && timer.Interval <= TimeSpan.FromMinutes(15), "The eager preset schedules a 5–15 minute interval.");
        var deadlineField = type.GetField("_nextInteractionAt", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var deadline = deadlineField.GetValue(controller);
        var apply = type.GetMethod("ApplyInteractionProfile", BindingFlags.Instance | BindingFlags.NonPublic)!;
        apply.Invoke(controller, [new InteractionProfile { Mode = "quiet", PromptsEnabled = true }]);
        await Task.Delay(100);
        Require(controller.Settings.DailyFrequencyPreset == "eager", "A legacy server mode cannot overwrite the local frequency preset.");
        Require(Equals(deadlineField.GetValue(controller), deadline), "A background profile refresh preserves the already planned daily deadline.");
        await controller.RefreshRemoteConfigAsync();
        Require(Equals(deadlineField.GetValue(controller), deadline), "Refreshing backend summary settings preserves the planned random-interaction deadline.");
    }

    private static async Task CheckRemoteSummaries(AppController controller, SettingsWindow settings, string output,
        Action<DailySummaryConfig?> setSummaryConfiguration)
    {
        TextBlock Text(string name) => (TextBlock)settings.FindName(name);
        string Body(bool weekly) => Text(weekly ? "DailyWeeklySummaryText" : "DailyMonthlySummaryText").Text;
        string Title(bool weekly) => Text(weekly ? "DailyWeeklyTitleText" : "DailyMonthlyTitleText").Text;
        Button Next(bool weekly) => (Button)settings.FindName(weekly ? "DailyNextWeekButton" : "DailyNextMonthSummaryButton");
        void SummaryButton(bool weekly, string label)
        {
            FrameworkElement element = Text(weekly ? "DailyWeeklySummaryText" : "DailyMonthlySummaryText");
            while (element is not Border && element.Parent is FrameworkElement parent) element = parent;
            Children<Button>(element).Single(button => Equals(button.Content, label))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }

        Require(settings.FindName("InteractionContentStatusText") is null && settings.FindName("SyncInteractionContentButton") is null
            && settings.FindName("DownloadInteractionPackButton") is null, "The fresh-content counter, fetch button and offline-package button are removed from the real settings window.");
        Require(Body(true).Contains("尚未在后台配置") && Body(false).Contains("尚未在后台配置")
            && Title(true) == string.Empty && Title(false) == string.Empty, "No backend configuration displays a clear empty state, without built-in weekly or monthly prose.");

        var now = DateTimeOffset.Now;
        var config = new DailySummaryConfig
        {
            Enabled = true,
            // Tomorrow's cutoff keeps every answer already made in this test in the unfinished week.
            Weekly = new() { Enabled = true, Weekday = ((int)now.DayOfWeek + 1) % 7, Time = "18:00" },
            Monthly = new() { Enabled = true, Time = "09:00" },
            Templates =
            [
                new() { Id = "smoke-weekly-a", Kind = "weekly", Enabled = true, Title = "模拟后台周报 A", Body = "模拟后台第一版 A：{totalInteractions} 次互动，{moodRecords} 次心情，{moodDays} 天；{moodSummary}。" },
                new() { Id = "smoke-weekly-b", Kind = "weekly", Enabled = true, Title = "模拟后台周报 B", Body = "模拟后台第一版 B：{periodStart}—{periodEnd}，共 {totalInteractions} 次互动；开心 {happyCount} 次。" },
                new() { Id = "smoke-monthly-a", Kind = "monthly", Enabled = true, Title = "模拟后台月报 A", Body = "模拟后台月报 A：{periodStart}—{periodEnd}，共 {totalInteractions} 次互动，{moodSummary}。" },
                new() { Id = "smoke-monthly-b", Kind = "monthly", Enabled = true, Title = "模拟后台月报 B", Body = "模拟后台月报 B：答题 {quizzesAnswered} 次，答对 {quizzesCorrect} 次；{moodSummary}。" }
            ]
        };
        var fixtureIds = new List<string>();
        var originalCount = controller.Journal.Entries.Count;
        try
        {
            setSummaryConfiguration(config);
            await controller.RefreshRemoteConfigAsync();
            await Settle(settings);
            Require(controller.RemoteConfig.DailySummaries?.Templates.Count == 4, "Summary configuration flows from the mocked HTTP endpoint through the real remote-config service.");
            Require(Body(true).Contains("没有日常记录") && Title(true) == string.Empty
                && Body(false).Contains("没有日常记录") && Title(false) == string.Empty,
                "Today's answers do not produce an early summary for the unfinished week or natural month.");
            Require(Text("DailyWeeklyScheduleText").Text.Contains("下一期") && Text("DailyMonthlyScheduleText").Text.Contains("下一期")
                && Text("DailyWeeklyRangeText").Text.Contains("已结束") && Text("DailyMonthlyRangeText").Text.Contains("已结束"),
                "Both cards show their completed range and next release time while waiting.");
            Require(!Next(true).IsEnabled && !Next(false).IsEnabled, "The latest available week and month cannot navigate into future summaries.");
            await CaptureSection(settings, "DailyWeeklySummaryText", output, "daily-12-summary-waiting");

            foreach (var kind in new[] { "weekly", "monthly" })
            {
                foreach (var offset in new[] { 0, 1 })
                {
                    var period = DailySummaryService.Build(config, [], kind, offset, now);
                    var entry = controller.Journal.RecordMood(offset == 0 ? "happy" : "low", period.PeriodStart!.Value.AddDays(1),
                        note: "自动验收临时历史记录；测试结束后删除。");
                    fixtureIds.Add(entry.Id);
                }
            }
            await Settle(settings);
            foreach (var weekly in new[] { true, false })
            {
                var kind = weekly ? "weekly" : "monthly";
                var expected = DailySummaryService.Build(config, controller.Journal.Entries, kind, 0, now);
                Require(expected.Available && Title(weekly) == expected.Title && Body(weekly) == expected.Body && !Body(weekly).Contains('{'),
                    $"The real {kind} card renders backend template text and actual completed-period counts.");
                await CaptureSection(settings, weekly ? "DailyWeeklySummaryText" : "DailyMonthlySummaryText", output,
                    weekly ? "daily-13-week-summary-from-backend" : "daily-14-month-summary-from-backend");
                var latestTitle = Title(weekly);
                var latestBody = Body(weekly);
                SummaryButton(weekly, weekly ? "上一周" : "上一月");
                await Settle(settings);
                var older = DailySummaryService.Build(config, controller.Journal.Entries, kind, 1, now);
                Require(Next(weekly).IsEnabled && Title(weekly) == older.Title && Body(weekly) == older.Body
                    && Title(weekly) != latestTitle, $"The real {kind} previous button displays an older completed period with a different template.");
                Next(weekly).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Settle(settings);
                Require(!Next(weekly).IsEnabled && Title(weekly) == latestTitle && Body(weekly) == latestBody,
                    $"Navigating back to the latest {kind} report preserves its template and text.");
            }

            var unchangedWeekly = Body(true);
            var unchangedMonthly = Body(false);
            await controller.RefreshRemoteConfigAsync();
            await Settle(settings);
            Require(Body(true) == unchangedWeekly && Body(false) == unchangedMonthly, "Refreshing unchanged backend settings does not randomly change either summary.");

            var edited = config with
            {
                Templates = config.Templates.Select(template => template with
                {
                    Title = "后台修改后 · " + template.Title,
                    Body = "后台修改后：{totalInteractions} 次互动；{moodSummary}。"
                }).ToArray()
            };
            setSummaryConfiguration(edited);
            // Exercise the actual user-facing refresh action, not only a service method.
            ((Button)settings.FindName("DailyRefreshButton"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (var attempt = 0; attempt < 20 && !Title(true).StartsWith("后台修改后"); attempt++) await Settle(settings);
            Require(Title(true).StartsWith("后台修改后") && Title(false).StartsWith("后台修改后")
                && Body(true).StartsWith("后台修改后") && Body(false).StartsWith("后台修改后"),
                "Editing backend templates and clicking the real refresh button updates both cards without restarting or rebuilding the client.");
            await CaptureSection(settings, "DailyMonthlySummaryText", output, "daily-15-summary-remote-edit");

            setSummaryConfiguration(edited with { Monthly = edited.Monthly with { Enabled = false } });
            await controller.RefreshRemoteConfigAsync();
            await Settle(settings);
            Require(Body(false).Contains("关闭") && Title(false) == string.Empty && Title(true).StartsWith("后台修改后"),
                "Disabling monthly summaries in backend clears that report without disabling weekly summaries.");
        }
        finally
        {
            foreach (var id in fixtureIds) controller.Journal.Delete(id);
            setSummaryConfiguration(null);
            await controller.RefreshRemoteConfigAsync();
            await Settle(settings);
        }
        Require(controller.Journal.Entries.Count == originalCount, "Temporary historical records are removed without changing the smoke test's actual user records.");
        Require(Title(true) == string.Empty && Title(false) == string.Empty && Body(true).Contains("尚未在后台配置"),
            "Removing backend summary settings clears previously rendered template text.");
    }

    private static IEnumerable<Button> ChoiceButtons(PetWindow pet)
        => ((WrapPanel)pet.FindName("InteractionChoicePanel")).Children.OfType<Button>();

    private static IEnumerable<T> Children<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) yield return match;
            foreach (var descendant in Children<T>(child)) yield return descendant;
        }
    }

    private static void ClickChoice(PetWindow pet, string value)
    {
        var button = ChoiceButtons(pet).Single(item => ((PetInteractionChoice)item.Tag).Value == value);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    private static void RequireChoicesFit(PetWindow pet)
    {
        var card = (Border)pet.FindName("InteractionCard");
        foreach (var button in ChoiceButtons(pet))
        {
            var bounds = button.TransformToAncestor(card).TransformBounds(new Rect(button.RenderSize));
            Require(bounds.Left >= 0 && bounds.Top >= 0 && bounds.Right <= card.ActualWidth + 1 && bounds.Bottom <= card.ActualHeight + 1,
                $"Choice {button.Content} fits inside the actual card.");
        }
    }

    private static async Task Settle(Window window)
    {
        window.UpdateLayout();
        await Task.Delay(220);
        window.UpdateLayout();
    }

    private static async Task CaptureSection(SettingsWindow window, string elementName, string output, string name)
    {
        var element = (FrameworkElement)window.FindName(elementName);
        while (element is not Border && element.Parent is FrameworkElement parent) element = parent;
        element.BringIntoView();
        await Settle(window);
        Capture(window, output, name);
    }

    private static void Capture(Window window, string output, string name)
    {
        window.UpdateLayout();
        var image = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        image.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(Path.Combine(output, name + ".png"));
        encoder.Save(stream);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _assertions++;
    }
}
