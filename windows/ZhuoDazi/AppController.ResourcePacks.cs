using ZhuoDazi.Models;
using ZhuoDazi.Services;

namespace ZhuoDazi;

public sealed partial class AppController
{
    private ResourcePackClient? _resourcePackClient;
    private readonly SemaphoreSlim _resourcePackInstallLock = new(1, 1);
    private ResourcePackClient ResourcePacks
    {
        get
        {
            if (IsLocalPreview && _petMarketDebugConfig is null)
                throw new InvalidOperationException("本地预览尚未配置资源后台。");
            return _resourcePackClient ??= new ResourcePackClient(
                _petMarketDebugConfig?.Origin ?? new Uri(DeskPetApi.BaseUrl), _store.DataDirectory,
                _petMarketDebugConfig is not null);
        }
    }

    internal Task<List<ShopResourcePack>> LoadResourcePacksAsync(CancellationToken token)
        => ResourcePacks.GetCatalogAsync(token);

    internal bool IsResourcePackInstalled(ShopResourcePack pack) => pack.FileCount > 0 && (pack.IsWords
        ? Settings.InteractionWordPacks.Count(item => item.SourceResourcePackId == pack.Id
            && string.Equals(item.SourceResourcePackSha256, pack.Sha256, StringComparison.OrdinalIgnoreCase))
        : Settings.TheaterScripts.Count(item => item.SourceResourcePackId == pack.Id
            && string.Equals(item.SourceResourcePackSha256, pack.Sha256, StringComparison.OrdinalIgnoreCase))) == pack.FileCount;

    internal bool IsResourcePackEnabled(ShopResourcePack pack) => IsResourcePackInstalled(pack) && (pack.IsWords
        ? Settings.InteractionWordPacks.Where(item => item.SourceResourcePackId == pack.Id)
            .All(item => item.Enabled || item.Id == Settings.ActiveInteractionWordPackId)
        : Settings.TheaterScripts.Where(item => item.SourceResourcePackId == pack.Id).All(item => item.Enabled));

    internal async Task DownloadAndInstallResourcePackAsync(ShopResourcePack pack, CancellationToken token)
    {
        await _resourcePackInstallLock.WaitAsync(token);
        try
        {
            if (IsResourcePackInstalled(pack)) return;
            var archive = await ResourcePacks.DownloadAsync(pack, token);
            ParsedResourcePack parsed;
            try { parsed = await Task.Run(() => ResourcePackInstaller.Parse(archive, pack, token), token); }
            finally { try { File.Delete(archive); } catch { } }
            token.ThrowIfCancellationRequested();
            var retainedWords = Settings.InteractionWordPacks.Where(item => item.SourceResourcePackId != pack.Id).ToList();
            var retainedScripts = Settings.TheaterScripts.Where(item => item.SourceResourcePackId != pack.Id).ToList();
            var previousWords = Settings.InteractionWordPacks;
            var previousScripts = Settings.TheaterScripts;
            var previousActive = Settings.ActiveInteractionWordPackId;
            Settings.InteractionWordPacks = retainedWords.Concat(parsed.Words).ToList();
            Settings.TheaterScripts = retainedScripts.Concat(parsed.Scripts).ToList();
            if (!Settings.InteractionWordPacks.Any(item => item.Id == previousActive)) Settings.ActiveInteractionWordPackId = null;
            try { Save(); }
            catch
            {
                Settings.InteractionWordPacks = previousWords;
                Settings.TheaterScripts = previousScripts;
                Settings.ActiveInteractionWordPackId = previousActive;
                throw;
            }
            StateChanged?.Invoke();
        }
        finally { _resourcePackInstallLock.Release(); }
    }

    internal bool SetResourcePackEnabled(ShopResourcePack pack, bool enabled)
    {
        if (!IsResourcePackInstalled(pack)) throw new InvalidOperationException("请先下载并安装这个资源包。");
        return pack.IsWords
            ? SetWordsEnabled(Settings.InteractionWordPacks.Where(item => item.SourceResourcePackId == pack.Id).ToList(), enabled)
            : SetScriptsEnabled(Settings.TheaterScripts.Where(item => item.SourceResourcePackId == pack.Id).ToList(), enabled);
    }
}
