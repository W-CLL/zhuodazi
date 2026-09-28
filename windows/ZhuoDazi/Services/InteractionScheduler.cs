using System.Security.Cryptography;

namespace ZhuoDazi.Services;

internal static class InteractionScheduler
{
    public static (int MinimumMinutes, int MaximumMinutes) Bounds(string mode) => mode switch
    {
        "quiet" => (60, 120),
        "lively" => (10, 30),
        _ => (30, 60)
    };

    public static TimeSpan NextDelay(string mode)
    {
        var bounds = Bounds(mode);
        return TimeSpan.FromMinutes(RandomNumberGenerator.GetInt32(
            bounds.MinimumMinutes,
            bounds.MaximumMinutes + 1));
    }

    public static TimeSpan NextMoodCooldown()
        => TimeSpan.FromMinutes(RandomNumberGenerator.GetInt32(180, 361));

    public static (int MinimumMinutes, int MaximumMinutes) DailyBounds(string preset) => preset switch
    {
        "eager" => (5, 15),
        "frequent" or "legacy-lively" => (10, 30),
        "legacy-quiet" => (60, 120),
        "legacy-standard" => (30, 60),
        _ => (15, 40)
    };

    public static TimeSpan NextDailyDelay(string preset)
    {
        var (minimum, maximum) = DailyBounds(preset);
        return TimeSpan.FromMinutes(RandomNumberGenerator.GetInt32(minimum, maximum + 1));
    }
}
