using Microsoft.Win32;
using System.Globalization;
using System.Windows;
using ZhuoDazi.Interop;
using ZhuoDazi.Models;
using ZhuoDazi.Services;

namespace ZhuoDazi;

public sealed partial class AppController
{
    private DailyJournalStore? _journal;
    private string? _journalScope;
    private DateTimeOffset? _nextInteractionAt;
    private bool _currentInteractionAnswered;
    private int _unansweredInteractions;
    private DateTimeOffset? _gentleUntil;
    private bool _sessionEventsAttached;
    private bool IsSessionLocked { get; set; }
    private bool IsGentleTime => _gentleUntil > DateTimeOffset.UtcNow;

    public DailyJournalStore Journal => _journal ?? throw new InvalidOperationException("日常记录还未准备好。");
    public string DailyFrequencyPreset => Settings.DailyFrequencyPreset;

    private void EnsureCompanionAvailable()
    {
        if (IsLocalPreview) throw new InvalidOperationException("本地预览用于体验日常互动，请在正式版中使用搭子与大厅。");
    }

    private void InitializeDailyJournal()
    {
        var scope = "installation:" + _licenses.InstallationId;
        if (_journalScope == scope) return;
        var journal = new DailyJournalStore(_store.DataDirectory, scope);
        if (_journal is not null) _journal.Changed -= OnDailyJournalChanged;
        _journal = journal;
        _journalScope = scope;
        _journal.Changed += OnDailyJournalChanged;
        if (Settings.DailyRoutine.ScopeId != scope)
        {
            var routine = Settings.DailyRoutine;
            routine.ScopeId = scope;
            routine.StateDate = null;
            routine.LastWorkCheckInDate = null;
            routine.MoodPromptDate = null;
            routine.MoodPromptCount = 0;
            routine.NextMoodPromptAt = null;
            DailyRoutineSchedule.BeginDay(routine, DateTimeOffset.Now);
        }
        if (!_sessionEventsAttached)
        {
            SystemEvents.SessionSwitch += OnDailySessionSwitch;
            _sessionEventsAttached = true;
        }
    }

    private void OnDailyJournalChanged() => StateChanged?.Invoke();

    private void OnDailySessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (_disposed || IsExiting) return;
            IsSessionLocked = e.Reason == SessionSwitchReason.SessionLock
                || e.Reason != SessionSwitchReason.SessionUnlock && IsSessionLocked;
            if (IsSessionLocked) _interactionTimer.Stop();
            else RestartInteractionTimer(true);
        });
    }

    public void ShowDailyJournal()
    {
        ShowSettings();
        _settingsWindow?.ShowDailyJournalTab();
    }

    public void SetDailyInteractionSettings(bool enabled, string preset)
    {
        if (!RequestPremiumAccess("日常互动")) return;
        if (preset is not ("eager" or "frequent" or "relaxed" or "legacy-quiet" or "legacy-standard" or "legacy-lively"))
            throw new ArgumentException("请选择有效的互动频率。");
        Settings.DailyFrequencyPreset = preset;
        Settings.RandomInteractionsEnabled = enabled;
        Settings.RemoteDefaultsApplied = true;
        // The preview's extended ranges are local. Keep the current server's three-mode contract valid.
        Settings.InteractionMode = preset switch
        {
            "eager" or "frequent" or "legacy-lively" => "lively",
            "legacy-quiet" => "quiet",
            _ => "standard"
        };
        if (!IsLocalPreview) Interactions.MarkProfileDirty(Settings.InteractionMode, enabled);
        RestartInteractionTimer(true);
        SaveAndRefresh();
        ScheduleInteractionSync();
    }

    public void SetDailyRoutine(bool enabled, string offWorkTime, IEnumerable<int> workDays)
    {
        if (!TimeOnly.TryParseExact(offWorkTime.Trim(), "HH:mm", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var time))
            throw new ArgumentException("下班时间请填写为 17:00 或 18:30 这样的格式。");
        var days = workDays.Distinct().Order().ToList();
        if (days.Any(day => day is < 0 or > 6) || enabled && days.Count == 0)
            throw new ArgumentException("请至少选择一个工作日。");
        var routine = Settings.DailyRoutine;
        routine.Enabled = enabled;
        routine.OffWorkTime = time.ToString("HH:mm", CultureInfo.InvariantCulture);
        routine.WorkDays = days;
        // Editing the regular schedule must not undo an already completed day.
        routine.NextReminderAt = null;
        SaveAndRefresh();
    }

    public void StartMoodInteraction()
    {
        if (!BeginManualDailyInteraction()) return;
        RunDailyPresentation(() => PresentDailyMood(_petWindow!));
    }

    public void StartWorkdayInteraction()
    {
        if (!BeginManualDailyInteraction()) return;
        RunDailyPresentation(() => PresentOffWorkInteraction(_petWindow!, DateTimeOffset.Now, true));
    }

    public void StartQuizInteraction()
    {
        if (!PrepareManualScene() || !RequestPremiumAccess("互动答题")) return;
        _ = PresentRandomInteractionAsync(true, quizOnly: true);
    }

    private bool BeginManualDailyInteraction()
    {
        if (!PrepareManualScene() || !RequestPremiumAccess("日常互动")) return false;
        if (_interactionActive || _theaterActive || _visitorQueue.IsShowing || _petWindow is null)
        {
            ShowActionMessage("先结束眼前这一轮，再来聊聊吧。");
            return false;
        }
        if (Settings.ClickThrough) SetClickThrough(false);
        _petWindow.Show();
        _interactionTimer.Stop();
        _interactionActive = true;
        _interactionWasManual = true;
        _currentInteractionAnswered = false;
        return true;
    }

    private bool IsDailyMoodDue()
    {
        var routine = Settings.DailyRoutine;
        var now = DateTimeOffset.Now;
        var today = DateOnly.FromDateTime(now.DateTime);
        if (routine.MoodPromptDate == today && routine.MoodPromptCount >= 2) return false;
        return routine.NextMoodPromptAt is null || now >= routine.NextMoodPromptAt;
    }

    private void PresentDailyMood(PetWindow pet)
    {
        var now = DateTimeOffset.Now;
        var routine = Settings.DailyRoutine;
        if (!_interactionWasManual)
        {
            var today = DateOnly.FromDateTime(now.DateTime);
            if (routine.MoodPromptDate != today) { routine.MoodPromptDate = today; routine.MoodPromptCount = 0; }
            routine.MoodPromptCount++;
        }
        routine.NextMoodPromptAt = now.AddHours(3);
        Save();
        var workContext = routine.Enabled && routine.WorkDays.Contains((int)now.DayOfWeek)
            && now.Hour >= 9 && now <= DailyRoutineSchedule.ScheduledTime(routine, now)
            && !(routine.StateDate == DateOnly.FromDateTime(now.DateTime) && routine.FinishedToday);
        ShowDailyCard(pet, "聊聊现在的心情", workContext ? "今天工作到现在，心情怎么样？" : "现在的心情怎么样？",
            DailyMoodCatalog.Options.Select(mood => new PetInteractionChoice(mood.Label, mood.Code)).ToArray(),
            choice =>
            {
                if (choice is null) { FinishInteraction(); return; }
                if (!MarkDailyAnswer(() => Journal.RecordMood(choice.Value))) { FinishInteraction(); return; }
                if (choice.Value is "bad" or "cry" or "tired" or "annoyed")
                {
                    _gentleUntil = DateTimeOffset.UtcNow.AddMinutes(30);
                    ShowDailyCard(pet, "我在这里", DailyMoodCatalog.GetReply(choice.Value),
                        [new("安静陪我", "quiet"), new("谢谢你", "done")],
                        response =>
                        {
                            FinishInteraction();
                            if (response?.Value == "quiet") PauseForOneHour();
                        });
                }
                else
                {
                    pet.ShowReaction(DailyMoodCatalog.GetReply(choice.Value));
                    FinishInteraction();
                }
            });
    }

    private bool TryShowWorkCheckIn(PetWindow pet)
    {
        var now = DateTimeOffset.Now;
        var today = DateOnly.FromDateTime(now.DateTime);
        var routine = Settings.DailyRoutine;
        if (!routine.Enabled || !routine.WorkDays.Contains((int)now.DayOfWeek)
            || routine.LastWorkCheckInDate == today || now.Hour < 10
            || now > DailyRoutineSchedule.ScheduledTime(routine, now).AddMinutes(-15)
            || routine.StateDate == today && routine.FinishedToday) return false;
        routine.LastWorkCheckInDate = today;
        Save();
        ShowDailyCard(pet, "工作间隙", "今天工作进展怎么样？",
            [new("挺顺利", "smooth"), new("忙但还行", "busy"), new("有点卡住", "stuck"), new("先不聊", "skip")],
            choice =>
            {
                if (choice is not null && choice.Value != "skip"
                    && MarkDailyAnswer(() => Journal.RecordDaily("work", choice.Value)))
                    pet.ShowReaction(choice.Value switch
                    {
                        "smooth" => "顺顺利利的，给今天加一颗小星星。",
                        "busy" => "那你慢慢忙，我在旁边陪着。",
                        _ => "先喘口气，再从一小步开始。"
                    });
                FinishInteraction();
            });
        return true;
    }

    private void CheckDailyRoutine()
    {
        if (_disposed || IsExiting || !HasPremiumAccess || IsQuiet || IsSessionLocked || NativeMethods.IsUserIdle
            || IsGuideActive || GuideBusy || _interactionActive || _theaterActive || _visitorQueue.IsShowing
            || Settings.ClickThrough || _petWindow is not { IsVisible: true } pet) return;
        var now = DateTimeOffset.Now;
        if (!DailyRoutineSchedule.IsDue(Settings.DailyRoutine, now)) return;
        _interactionTimer.Stop();
        _interactionActive = true;
        _interactionWasManual = false;
        _currentInteractionAnswered = false;
        RunDailyPresentation(() => PresentOffWorkInteraction(pet, now, false));
    }

    private void PresentOffWorkInteraction(PetWindow pet, DateTimeOffset now, bool manual)
    {
        var routine = Settings.DailyRoutine;
        var wasSnoozed = routine.NextReminderAt.HasValue;
        DailyRoutineSchedule.MarkShown(routine, now);
        Save();
        var choices = new List<PetInteractionChoice> { new("下班啦", "done"), new("还得加会班", "overtime") };
        if (now.Hour < 18 && routine.PromptCount < 2) choices.Add(new("今天六点下班", "six"));
        choices.Add(new("今天休息", "rest"));
        ShowDailyCard(pet, "收工时刻", manual ? "今天可以收工了吗？"
                : wasSnoozed ? "到了你选的时间，现在可以收工了吗？" : $"{now:HH:mm} 啦，今天能准点收工吗？",
            choices, choice =>
            {
                if (choice is null) { FinishInteraction(); return; }
                if (DateTimeOffset.Now.Date != now.Date)
                {
                    pet.ShowReaction("已经是新的一天啦，点我重新聊聊今天吧。");
                    FinishInteraction();
                    return;
                }
                if (choice.Value is "done" or "rest")
                {
                    routine.FinishedToday = true;
                    routine.NextReminderAt = null;
                    Save();
                    if (MarkDailyAnswer(() => Journal.RecordDaily("offwork", choice.Value)))
                        pet.ShowReaction(choice.Value == "done" ? "收工啦！今天辛苦了，接下来是属于你的时间。" : "那今天好好休息，不催你上班啦。");
                    FinishInteraction();
                }
                else if (choice.Value == "six")
                {
                    var target = new DateTimeOffset(now.Date.AddHours(18), now.Offset);
                    if (target <= DateTimeOffset.Now) { pet.ShowReaction("已经过六点啦，需要的话可以再选一次稍后提醒。"); FinishInteraction(); return; }
                    DailyRoutineSchedule.Snooze(routine, now, target);
                    Save();
                    if (MarkDailyAnswer(() => Journal.RecordDaily("offwork", "six")))
                        pet.ShowReaction("记住啦，今天 18:00 再来找你。平时的下班时间保持不变。");
                    FinishInteraction();
                }
                else
                {
                    if (!MarkDailyAnswer(() => Journal.RecordDaily("offwork", "overtime"))) { FinishInteraction(); return; }
                    if (routine.PromptCount >= 2)
                    {
                        pet.ShowReaction("辛苦啦，今天不再催你。忙完了可以主动告诉我下班啦。");
                        FinishInteraction();
                        return;
                    }
                    ShowDailyCard(pet, "陪你加会班", "那我多久后再来看看？",
                        [new("30 分钟后", "30"), new("1 小时后", "60"), new("今天不用提醒", "quiet")],
                        response =>
                        {
                            if (response is not null && int.TryParse(response.Value, out var minutes))
                            {
                                var selectedAt = DateTimeOffset.Now;
                                var target = selectedAt.AddMinutes(minutes);
                                if (target.Date == now.Date && selectedAt.Date == now.Date)
                                {
                                    DailyRoutineSchedule.Snooze(routine, selectedAt, target);
                                    Save();
                                    pet.ShowReaction($"好的，今天 {target:HH:mm} 再轻轻提醒一次。");
                                }
                                else pet.ShowReaction("今天已经很晚了，我不再催你。需要时点点我就好。");
                            }
                            else pet.ShowReaction("好，今天不再催你。需要时点点我就好。");
                            FinishInteraction();
                        });
                }
            });
    }

    private bool MarkDailyAnswer(Action save)
    {
        _currentInteractionAnswered = true;
        try { save(); return true; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _petWindow?.ShowReaction("这次记录暂时没保存成功，请检查本机可用空间后重试。");
            System.Diagnostics.Trace.TraceError(error.ToString());
            return false;
        }
    }

    private void RunDailyPresentation(Action show)
    {
        try { show(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            FinishInteraction();
            _petWindow?.ShowReaction("暂时无法保存日常设置，请检查本机可用空间后再试。");
            System.Diagnostics.Trace.TraceError(error.ToString());
        }
    }

    private void ShowDailyCard(PetWindow pet, string title, string message,
        IReadOnlyList<PetInteractionChoice> choices, Action<PetInteractionChoice?> callback)
        => pet.ShowInteraction(title, message, choices, choice => RunDailyPresentation(() => callback(choice)));
}
