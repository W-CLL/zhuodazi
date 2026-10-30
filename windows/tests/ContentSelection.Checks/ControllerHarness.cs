using ZhuoDazi.Models;
namespace ZhuoDazi;

// Exercise the production selection methods without a window, network or user settings.
public sealed partial class AppController
{
    public AppSettings Settings { get; } = new();
    public bool HasPremiumAccess { get; set; } = true;
    internal bool FailSave { get; set; }
    private IReadOnlyList<string> _libraryFiles = [];
    private string? _activeLibraryPetPath;
    private readonly TestStore _store = new();
    private bool RequestPremiumAccess(string _) => HasPremiumAccess;
    private bool IsMarketInstallationUsable(PetMarketInstallation item) => item.UseGrant is not null;
    private void StopPetMarketGrantValidation() { }
    private void StartPetMarketGrantValidation() { }
    private void RefreshLibrary(bool _) { _activeLibraryPetPath = null; }
    private void RestartRandomTimer() { }
    private void SaveAndRefresh() { if (FailSave) throw new IOException("simulated storage failure"); }
    internal bool EnableWordGroup(params InteractionWordPackDefinition[] items) => SetWordsEnabled(items, true);
    internal bool EnableScriptGroup(params TheaterScriptDefinition[] items) => SetScriptsEnabled(items, true);
    internal bool CheckPetFile(string path) => CanUsePetFile(path);
    private sealed class TestStore { public string PetMarketDirectory => Path.Combine(Path.GetTempPath(), "selection-market-checks"); }
}
