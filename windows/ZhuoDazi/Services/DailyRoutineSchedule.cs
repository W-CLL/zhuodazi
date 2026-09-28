using System.Globalization;
using ZhuoDazi.Models;

namespace ZhuoDazi.Services;

public static class DailyRoutineSchedule
{
    public static void BeginDay(DailyRoutineSettings routine, DateTimeOffset now)
    {
        var day = DateOnly.FromDateTime(now.DateTime);
        if (routine.StateDate == day) return;
        routine.StateDate = day;
        routine.FinishedToday = false;
        routine.NextReminderAt = null;
        routine.PromptCount = 0;
    }

    public static DateTimeOffset ScheduledTime(DailyRoutineSettings routine, DateTimeOffset now)
    {
        var time = TimeOnly.ParseExact(routine.OffWorkTime, "HH:mm", CultureInfo.InvariantCulture);
        return new DateTimeOffset(now.Date + time.ToTimeSpan(), now.Offset);
    }

    public static bool IsDue(DailyRoutineSettings routine, DateTimeOffset now)
    {
        BeginDay(routine, now);
        if (routine.NextReminderAt is null && (!routine.Enabled || !routine.WorkDays.Contains((int)now.DayOfWeek))
            || routine.FinishedToday || routine.PromptCount >= 2) return false;
        var target = routine.NextReminderAt ?? ScheduledTime(routine, now);
        // Returning late must not replay a reminder from hours ago.
        return now >= target && now - target <= TimeSpan.FromMinutes(30);
    }

    public static void MarkShown(DailyRoutineSettings routine, DateTimeOffset now)
    {
        BeginDay(routine, now);
        routine.PromptCount++;
        routine.NextReminderAt = null;
        // Closing a card also ends automatic reminders today, unless the user explicitly snoozes.
        routine.FinishedToday = true;
    }

    public static void Snooze(DailyRoutineSettings routine, DateTimeOffset now, DateTimeOffset target)
    {
        BeginDay(routine, now);
        if (target <= now || target.Date != now.Date)
            throw new ArgumentException("请选择今天稍晚的时间。");
        routine.NextReminderAt = target;
        routine.FinishedToday = false;
    }

    public static DateOnly WeekStart(DateOnly day)
        => day.AddDays(-(((int)day.DayOfWeek + 6) % 7));
}
