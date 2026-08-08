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
}
