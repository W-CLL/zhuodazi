using System.Text;

namespace ZhuoDazi.Services;

internal static class ConversationPresentationTiming
{
    private static readonly TimeSpan DefaultSpeechDuration = TimeSpan.FromSeconds(5);

    internal static TimeSpan DefaultSpeech => DefaultSpeechDuration;

    internal static TimeSpan TheaterLine(string? text)
        => ScaledDuration(text, baseSeconds: 5, includedRunes: 6, secondsPerRune: 0.14, maximumSeconds: 12);

    internal static TimeSpan HallVisit(string? text)
        => ScaledDuration(text, baseSeconds: 12, includedRunes: 10, secondsPerRune: 0.12, maximumSeconds: 20);

    private static TimeSpan ScaledDuration(
        string? text,
        double baseSeconds,
        int includedRunes,
        double secondsPerRune,
        double maximumSeconds)
    {
        var runeCount = string.IsNullOrWhiteSpace(text)
            ? 0
            : text.Trim().EnumerateRunes().Count();
        var seconds = baseSeconds + Math.Max(0, runeCount - includedRunes) * secondsPerRune;
        return TimeSpan.FromSeconds(Math.Min(maximumSeconds, seconds));
    }
}
