namespace ZhuoDazi.Models;

/// <summary>Only explicit user decisions advance the guide. Missing fields on an existing
/// installation are migrated by SettingsStore, never treated as a new installation.</summary>
public sealed class OnboardingProgress
{
    public int Version { get; set; } = 1;
    public int Step { get; set; }
    public bool Dismissed { get; set; }
    public bool UpgradeNoticePending { get; set; }
    public List<int> CompletedSteps { get; set; } = [];
    public List<int> SkippedSteps { get; set; } = [];
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsComplete => Step >= 5;

    public static OnboardingProgress ForExistingInstallation() => new()
    {
        Dismissed = true, UpgradeNoticePending = true
    };

    public void Advance(bool skipped = false)
    {
        if (Step is >= 1 and <= 4)
        {
            var destination = skipped ? SkippedSteps : CompletedSteps;
            if (!destination.Contains(Step)) destination.Add(Step);
        }
        Step = Math.Min(5, Step + 1);
    }

    public void Normalize()
    {
        Version = 1;
        Step = Math.Clamp(Step, 0, 5);
        CompletedSteps = (CompletedSteps ?? []).Where(x => x is >= 1 and <= 4).Distinct().ToList();
        SkippedSteps = (SkippedSteps ?? []).Where(x => x is >= 1 and <= 4 && !CompletedSteps.Contains(x)).Distinct().ToList();
    }
}
