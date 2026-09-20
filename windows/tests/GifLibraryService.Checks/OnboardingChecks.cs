using System.IO;
using ZhuoDazi.Models;
using ZhuoDazi.Services;

internal static class OnboardingChecks
{
    public static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ZhuoDazi-GuideChecks", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var store = new SettingsStore(directory);
            var fresh = store.Load();
            Require(!fresh.Onboarding.Dismissed && fresh.Onboarding.Step == 0 && !fresh.Onboarding.UpgradeNoticePending,
                "A new installation must receive the welcome card.");
            File.WriteAllText(store.SettingsPath, "{\"dailySpeechEnabled\":false,\"theaterEnabled\":true,\"onboardingHintSeen\":false}");
            var upgrade = store.Load();
            Require(upgrade.Onboarding.Dismissed && upgrade.Onboarding.UpgradeNoticePending,
                "Existing settings without guide fields must get an upgrade notice, not forced onboarding.");
            Require(!upgrade.DailySpeechEnabled && upgrade.TheaterEnabled, "Migration must preserve companionship preferences.");
            upgrade.Onboarding.UpgradeNoticePending = false;
            store.Save(upgrade);
            Require(!store.Load().Onboarding.UpgradeNoticePending, "Upgrade notice must not repeat after restart.");
            fresh.Onboarding.Advance(); // Welcome -> controls.
            fresh.Onboarding.Advance(); // Explicit next -> interaction.
            fresh.Onboarding.Dismissed = true;
            store.Save(fresh);
            var resumed = store.Load();
            Require(resumed.Onboarding.Step == 2 && resumed.Onboarding.Dismissed && resumed.Onboarding.CompletedSteps.SequenceEqual([1]),
                "Closing midway must preserve progress without claiming interaction completion.");
            resumed.Onboarding.Advance(true);
            Require(!resumed.Onboarding.CompletedSteps.Contains(2) && resumed.Onboarding.SkippedSteps.Contains(2),
                "Skipping is a separate user decision from experiencing an interaction.");
            resumed.Onboarding.Advance();
            resumed.Onboarding.Advance();
            store.Save(resumed);
            Require(store.Load().Onboarding.IsComplete, "Completed guides must remain completed after restart.");
            File.WriteAllText(store.SettingsPath, "{broken old settings");
            Require(store.Load().Onboarding.Dismissed, "Damaged existing settings must not masquerade as a new install.");
            Console.WriteLine("Onboarding migration and restart checks passed (7 scenarios).");
        }
        finally { Directory.Delete(directory, true); }
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
