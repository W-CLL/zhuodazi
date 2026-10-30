using ZhuoDazi.Models;

namespace ZhuoDazi;

public sealed partial class AppController
{
    private string? CurrentLibraryPath => !Settings.RandomPetEnabled
        && Settings.SelectedLibraryPetPath is { } selected
        && _libraryFiles.Contains(selected, StringComparer.OrdinalIgnoreCase) && File.Exists(selected)
            ? selected : _activeLibraryPetPath;

    private IReadOnlyList<LibraryDefinition> EnabledLibraries => HasPremiumAccess
        ? Settings.Libraries.Where(item => Settings.EnabledLibraryIds?.Contains(item.Id) == true).ToList() : [];
    private IReadOnlyList<PetMarketInstallation> EnabledMarketInstallations
        => Settings.PetMarketInstallations.Where(item => IsMarketProductEnabled(item.ProductId)).ToList();
    public int EnabledPetSourceCount => (Settings.EnabledLibraryIds?.Count ?? 0)
        + (Settings.EnabledMarketProductIds?.Count ?? 0);
    internal bool IsMarketProductEnabled(string id)
        => Settings.EnabledMarketProductIds?.Contains(id, StringComparer.OrdinalIgnoreCase) == true;

    private bool CanUsePetFile(string path)
    {
        var installation = Settings.PetMarketInstallations.FirstOrDefault(item =>
            IsWithinLibrary(path, item.Directory) || item.Files.Contains(path, StringComparer.OrdinalIgnoreCase));
        if (installation is not null)
            return IsMarketProductEnabled(installation.ProductId) && IsMarketInstallationUsable(installation)
                && installation.Files.Contains(path, StringComparer.OrdinalIgnoreCase);
        return !IsWithinLibrary(path, _store.PetMarketDirectory);
    }

    private static bool IsWithinLibrary(string path, string directory)
    {
        try { return Path.GetFullPath(path).StartsWith(Path.GetFullPath(directory)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    private void ClearEnabledPetSources()
    {
        Settings.EnabledLibraryIds = [];
        Settings.EnabledMarketProductIds = [];
        Settings.ActiveLibraryId = null;
        Settings.ActiveMarketProductId = null;
        StopPetMarketGrantValidation();
    }

    private void EnsurePetSourceSlot()
    {
        if (EnabledPetSourceCount >= AppSettings.MaxEnabledPetSources)
            throw new InvalidOperationException("最多同时启用 3 个桌宠来源（本地目录和小铺桌宠合计），请先停用一个。");
    }

    private void RefreshEnabledPetSources()
    {
        Settings.ActiveLibraryId = Settings.EnabledLibraryIds?.FirstOrDefault();
        Settings.ActiveMarketProductId = Settings.EnabledMarketProductIds?.FirstOrDefault();
        Settings.ActivePetId = null;
        Settings.SelectedLibraryPetPath = null;
        RefreshLibrary(true);
        if (!Settings.RandomPetEnabled) Settings.SelectedLibraryPetPath = _activeLibraryPetPath;
        RestartRandomTimer();
        SaveAndRefresh();
        StartPetMarketGrantValidation();
    }

    public void SetLibraryEnabled(string? id, bool enabled)
    {
        if (id is null)
        {
            if (!enabled) return;
            ClearEnabledPetSources();
        }
        else
        {
            if (!Settings.Libraries.Any(item => item.Id == id))
                throw new InvalidOperationException("所选资源库不存在。");
            if (enabled && !RequestPremiumAccess("外部 GIF 资源库")) return;
            Settings.EnabledLibraryIds ??= [];
            if (enabled && !Settings.EnabledLibraryIds.Contains(id))
            {
                EnsurePetSourceSlot();
                Settings.EnabledLibraryIds.Add(id);
            }
            if (!enabled) Settings.EnabledLibraryIds.Remove(id);
        }
        RefreshEnabledPetSources();
    }

    internal void DisableMarketProduct(string productId)
    {
        Settings.EnabledMarketProductIds?.RemoveAll(id => id.Equals(productId, StringComparison.OrdinalIgnoreCase));
        RefreshEnabledPetSources();
    }

    public void SetInteractionWordPackEnabled(string? id, bool enabled)
    {
        var selected = id is null ? Settings.InteractionWordPacks.ToList()
            : Settings.InteractionWordPacks.Where(item => item.Id == id).ToList();
        if (id is not null && selected.Count == 0) throw new InvalidOperationException("所选互动词库不存在。");
        SetWordsEnabled(selected, id is not null && enabled);
    }

    private bool SetWordsEnabled(IReadOnlyCollection<InteractionWordPackDefinition> selected, bool enabled)
    {
        if (enabled && !RequestPremiumAccess("互动词库")) return false;
        var previousActive = Settings.ActiveInteractionWordPackId;
        var previous = Settings.InteractionWordPacks.Select(item => (Item: item, item.Enabled)).ToList();
        var activeCount = Settings.InteractionWordPacks.Count(item => !selected.Contains(item)
            && (item.Enabled || item.Id == previousActive));
        if (enabled && activeCount + selected.Count > AppSettings.MaxEnabledContent)
            throw new InvalidOperationException("互动词库最多同时启用 10 套，请先停用一些词库。");
        foreach (var item in Settings.InteractionWordPacks)
            item.Enabled = selected.Contains(item) ? enabled : item.Enabled || item.Id == previousActive;
        Settings.ActiveInteractionWordPackId = null;
        try { SaveAndRefresh(); }
        catch
        {
            foreach (var entry in previous) entry.Item.Enabled = entry.Enabled;
            Settings.ActiveInteractionWordPackId = previousActive;
            throw;
        }
        return true;
    }

    public void SetTheaterScriptEnabled(string id, bool enabled)
    {
        var script = Settings.TheaterScripts.FirstOrDefault(item => item.Id == id)
            ?? throw new InvalidOperationException("所选小剧场不存在。");
        SetScriptsEnabled([script], enabled);
    }

    private bool SetScriptsEnabled(IReadOnlyCollection<TheaterScriptDefinition> selected, bool enabled)
    {
        if (enabled && !RequestPremiumAccess("小剧场")) return false;
        if (enabled && Settings.TheaterScripts.Count(item => item.Enabled && !selected.Contains(item))
            + selected.Count > AppSettings.MaxEnabledContent)
            throw new InvalidOperationException("小剧场最多同时启用 10 个，请先停用一些剧本。");
        var previous = selected.Select(item => (Item: item, item.Enabled)).ToList();
        foreach (var item in selected) item.Enabled = enabled;
        try { SaveAndRefresh(); }
        catch { foreach (var entry in previous) entry.Item.Enabled = entry.Enabled; throw; }
        return true;
    }
}
