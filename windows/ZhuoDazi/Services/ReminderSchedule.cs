namespace ZhuoDazi.Services;

internal static class ReminderSchedule
{
    public static DateTime DefaultTime(DateTime now) => now.AddMinutes(10);

    public static DateTime ValidateAndAdvance(DateTime selected, bool enabled, bool repeatDaily, DateTime now)
    {
        if (!enabled || selected > now) return selected;
        if (!repeatDaily) throw new InvalidOperationException("一次性提醒的时间已经过去，请选择未来的时间。");
        while (selected <= now) selected = selected.AddDays(1);
        return selected;
    }
}
