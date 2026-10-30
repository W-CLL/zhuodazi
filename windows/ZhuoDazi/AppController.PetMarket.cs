using System.Net;
using System.Windows;
using System.Windows.Threading;
using ZhuoDazi.Models;
using ZhuoDazi.Services;

namespace ZhuoDazi;

public sealed partial class AppController
{
    private PetMarketWindow? _petMarketWindow;
    private PetMarketClient? _petMarketClient;
    private PetMarketDebugConfig? _petMarketDebugConfig;
    private readonly SemaphoreSlim _petMarketInstallGate = new(1, 1);
    private readonly DispatcherTimer _petMarketGrantTimer = new() { Interval = TimeSpan.FromMinutes(1) };
    private bool _petMarketGrantTimerConfigured;
    private bool _petMarketGrantRefreshing;
    private DateTimeOffset _nextPetMarketGrantRefreshAt = DateTimeOffset.MinValue;

    public PetMarketInstallation? ActiveMarketInstallation => CurrentLibraryPath is { } path
        ? EnabledMarketInstallations.FirstOrDefault(item => item.Files.Contains(path, StringComparer.OrdinalIgnoreCase)) : null;

    internal bool HasActivatedMarketIdentity => PetMarketClient.IsActivated;
    private bool IsPetMarketDebug => _petMarketDebugConfig is not null;
    private bool IsPetMarketOnlyDebug => IsPetMarketDebug && !IsFullAppPreview;
    public bool IsFullAppPreview { get; private set; }

    private PetMarketClient PetMarketClient => _petMarketClient ??= _petMarketDebugConfig is null
        ? new PetMarketClient(_licenses, _store.DataDirectory)
        : new PetMarketClient(_petMarketDebugConfig);

    internal void ConfigurePetMarketDebug(PetMarketDebugConfig config, bool fullAppPreview = false)
    {
        if (_petMarketClient is not null) throw new InvalidOperationException("市场客户端已经初始化。");
        if (fullAppPreview && !IsLocalPreview) throw new InvalidOperationException("完整本地预览必须使用隔离模式。");
        _petMarketDebugConfig = config;
        IsFullAppPreview = fullAppPreview;
    }

    public void ShowPetMarket()
    {
        if (!IsPetMarketOnlyDebug)
        {
            ShowSettings();
            _settingsWindow?.ShowPetMarketTab();
            return;
        }
        if (_petMarketWindow is null)
        {
            _petMarketWindow = new PetMarketWindow(this);
            if (_settingsWindow?.IsVisible == true) _petMarketWindow.Owner = _settingsWindow;
            _petMarketWindow.Closed += (_, _) => _petMarketWindow = null;
        }
        _petMarketWindow.Show();
        if (_petMarketWindow.WindowState == WindowState.Minimized) _petMarketWindow.WindowState = WindowState.Normal;
        _petMarketWindow.Activate();
    }

    internal PetMarketWindow ShowPetMarketForDebug()
    {
        ShowPetMarket();
        return _petMarketWindow!;
    }

    internal Task<PetMarketCatalog> LoadPetMarketCatalogAsync(CancellationToken cancellationToken)
        => PetMarketClient.GetCatalogAsync(cancellationToken);

    internal Task<PetMarketClaimResponse> ClaimMarketProductAsync(string productId, CancellationToken cancellationToken)
        => PetMarketClient.ClaimAsync(productId, cancellationToken);

    internal Task<PetMarketOrderResponse> CreatePetMarketOrderAsync(string productId, CancellationToken cancellationToken)
        => PetMarketClient.CreateOrderAsync(productId, cancellationToken);

    internal Task<PetMarketOrderListResponse> ListPetMarketOrdersAsync(CancellationToken cancellationToken)
        => PetMarketClient.ListOrdersAsync(cancellationToken);

    internal Task<PetMarketOrder> GetPetMarketOrderAsync(string orderId, CancellationToken cancellationToken)
        => PetMarketClient.GetOrderAsync(orderId, cancellationToken);

    internal Task<PetMarketOrder> CancelPetMarketOrderAsync(string orderId, CancellationToken cancellationToken)
        => PetMarketClient.CancelOrderAsync(orderId, cancellationToken);

    internal Task<string?> DownloadPetMarketPreviewAsync(PetMarketProduct product,
        PetMarketPreviewFile? preview, CancellationToken cancellationToken)
        => PetMarketClient.DownloadPreviewAsync(product, preview, cancellationToken);

    internal void StartPetMarketDebugPreview()
    {
        RefreshLibrary(true);
        _petWindow = new PetWindow(this);
        PositionPetWindow(_petWindow);
        _petWindow.RefreshAppearance(CurrentPetPath());
        _petWindow.Show();
        RestartRandomTimer();
        StartPetMarketGrantValidation();
    }

    internal SettingsWindow StartFullAppPreview()
    {
        if (!IsFullAppPreview) throw new InvalidOperationException("完整本地预览尚未配置。");
        Start(synchronizeStartupRegistration: false);
        ShowSettings();
        return _settingsWindow!;
    }

    internal PetMarketInstallation? FindMarketInstallation(string productId)
        => Settings.PetMarketInstallations.FirstOrDefault(item =>
            item.ProductId.Equals(productId, StringComparison.OrdinalIgnoreCase));

    internal bool IsPaidMarketOrderInstalled(PetMarketOrder order)
    {
        var installed = FindMarketInstallation(order.ProductId);
        return installed is not null
            && installed.Version.Equals(order.ProductVersion, StringComparison.Ordinal)
            && Directory.Exists(installed.Directory)
            && installed.Files.Count > 0
            && installed.Files.All(File.Exists);
    }

    internal async Task<PetMarketInstallation> EnsurePaidMarketOrderInstalledAsync(
        PetMarketOrder order, CancellationToken cancellationToken)
    {
        if (!string.Equals(order.Status, "paid", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(order.ProductId)
            || string.IsNullOrWhiteSpace(order.ProductVersion))
            throw new InvalidOperationException("只有已付款且版本完整的订单可以自动安装。");
        await _petMarketInstallGate.WaitAsync(cancellationToken);
        try
        {
            if (IsPaidMarketOrderInstalled(order)) return FindMarketInstallation(order.ProductId)!;
            var product = new PetMarketProduct
            {
                Id = order.ProductId,
                Title = string.IsNullOrWhiteSpace(order.ProductTitle) ? order.ProductId : order.ProductTitle,
                ActiveVersion = order.ProductVersion,
                EntitlementVersion = order.ProductVersion,
                OwnedVersion = order.ProductVersion,
                Owned = true,
                Downloadable = true
            };
            return await DownloadAndInstallMarketProductCoreAsync(product, cancellationToken);
        }
        finally { _petMarketInstallGate.Release(); }
    }

    internal async Task<PetMarketInstallation> DownloadAndInstallMarketProductAsync(
        PetMarketProduct product, CancellationToken cancellationToken)
    {
        await _petMarketInstallGate.WaitAsync(cancellationToken);
        try { return await DownloadAndInstallMarketProductCoreAsync(product, cancellationToken); }
        finally { _petMarketInstallGate.Release(); }
    }

    private async Task<PetMarketInstallation> DownloadAndInstallMarketProductCoreAsync(
        PetMarketProduct product, CancellationToken cancellationToken)
    {
        if (!PetMarketClient.IsActivated)
            throw new InvalidOperationException("正式激活后才可以下载和安装市场桌宠。");
        // Entitlements are locked to the version that was claimed or purchased. The catalog's
        // activeVersion may advance later, so validate against the entitlement version when the
        // server provides it and otherwise rely on the signed descriptor.
        var entitlementVersion = !string.IsNullOrWhiteSpace(product.EntitlementVersion)
            ? product.EntitlementVersion
            : product.OwnedVersion;
        var descriptor = await PetMarketClient.GetDownloadDescriptorAsync(
            product.Id, entitlementVersion, cancellationToken);
        var archive = await PetMarketClient.DownloadPackageAsync(descriptor, cancellationToken);
        try
        {
            var installed = await PetMarketInstaller.InstallAsync(
                archive,
                _store.DataDirectory,
                descriptor.ProductId,
                descriptor.Version,
                descriptor.SizeBytes,
                descriptor.Sha256,
                cancellationToken);
            var registration = new PetMarketInstallation
            {
                ProductId = installed.ProductId,
                Version = installed.Version,
                Title = string.IsNullOrWhiteSpace(installed.Title) ? product.Title : installed.Title,
                Directory = installed.Directory,
                Files = installed.Files.ToList(),
                ManifestSha256 = descriptor.ManifestSha256,
                InstalledAt = DateTimeOffset.UtcNow
            };
            var replacedActiveInstallation = IsMarketProductEnabled(registration.ProductId);
            Settings.PetMarketInstallations.RemoveAll(item =>
                item.ProductId.Equals(registration.ProductId, StringComparison.OrdinalIgnoreCase));
            Settings.PetMarketInstallations.Add(registration);
            if (replacedActiveInstallation)
            {
                Settings.EnabledMarketProductIds?.RemoveAll(id => id.Equals(registration.ProductId, StringComparison.OrdinalIgnoreCase));
                _nextPetMarketGrantRefreshAt = DateTimeOffset.MinValue;
                RefreshEnabledPetSources();
            }
            else
            {
                Save();
                StateChanged?.Invoke();
            }
            return registration;
        }
        finally
        {
            try { File.Delete(archive); } catch { }
        }
    }

    internal async Task UseMarketProductAsync(PetMarketInstallation installation, CancellationToken cancellationToken)
    {
        if (!PetMarketClient.IsActivated)
            throw new InvalidOperationException("正式激活后才可以使用市场桌宠。");
        if (!IsMarketProductEnabled(installation.ProductId)) EnsurePetSourceSlot();
        if (!Directory.Exists(installation.Directory) || installation.Files.Count == 0
            || installation.Files.Any(path => !File.Exists(path)))
            throw new InvalidOperationException("本机市场图鉴不完整，请重新下载安装。");
        var (grant, _) = await PetMarketClient.AuthorizeUseAsync(
            installation.ProductId, installation.Version, installation.ManifestSha256, cancellationToken);
        if (!ReferenceEquals(FindMarketInstallation(installation.ProductId), installation))
            throw new InvalidOperationException("桌宠安装版本已变化，请重新启用。");
        if (!IsMarketProductEnabled(installation.ProductId)) EnsurePetSourceSlot();
        installation.UseGrant = grant;
        Settings.EnabledMarketProductIds ??= [];
        if (!IsMarketProductEnabled(installation.ProductId)) Settings.EnabledMarketProductIds.Add(installation.ProductId);
        Settings.RandomPetEnabled = true;
        RefreshEnabledPetSources();
        _nextPetMarketGrantRefreshAt = DateTimeOffset.UtcNow.AddMinutes(5);
        StartPetMarketGrantValidation();
        _petWindow?.ShowReaction($"已启用 {installation.Title}，共 {EnabledPetSourceCount}/3 个桌宠来源。");
    }

    private bool IsMarketInstallationUsable(PetMarketInstallation installation)
        => installation.Files.Count > 0 && installation.Files.All(File.Exists)
            && PetMarketClient.HasValidStoredUseGrant(installation);

    private void StartPetMarketGrantValidation()
    {
        if (!_petMarketGrantTimerConfigured)
        {
            _petMarketGrantTimer.Tick += async (_, _) => await RefreshPetMarketGrantAsync();
            _petMarketGrantTimerConfigured = true;
        }
        if (EnabledMarketInstallations.Count == 0)
        {
            _petMarketGrantTimer.Stop();
            return;
        }
        if (_nextPetMarketGrantRefreshAt == DateTimeOffset.MinValue)
            _nextPetMarketGrantRefreshAt = DateTimeOffset.UtcNow.AddMinutes(5);
        _petMarketGrantTimer.Start();
    }

    private void StopPetMarketGrantValidation()
    {
        _petMarketGrantTimer.Stop();
        _petMarketGrantRefreshing = false;
    }

    private async Task RefreshPetMarketGrantAsync()
    {
        if (_disposed || IsExiting || _petMarketGrantRefreshing) return;
        var installations = EnabledMarketInstallations.ToArray();
        if (installations.Length == 0)
        {
            _petMarketGrantTimer.Stop();
            return;
        }
        foreach (var expired in installations.Where(item => !IsMarketInstallationUsable(item)))
            DeactivateMarketInstallation(expired, "部分小铺桌宠授权已过期，已停用该桌宠。");
        if (DateTimeOffset.UtcNow < _nextPetMarketGrantRefreshAt) return;
        _petMarketGrantRefreshing = true;
        try
        {
            foreach (var installation in installations.Where(IsActiveMarketInstallation))
            {
                try
                {
                    var (grant, _) = await PetMarketClient.AuthorizeUseAsync(
                        installation.ProductId, installation.Version, installation.ManifestSha256);
                    if (IsActiveMarketInstallation(installation)) installation.UseGrant = grant;
                }
                catch (PetMarketRequestException error) when (error.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    if (IsActiveMarketInstallation(installation))
                        DeactivateMarketInstallation(installation, "部分小铺桌宠授权已失效，已停用该桌宠。");
                }
            }
            _nextPetMarketGrantRefreshAt = DateTimeOffset.UtcNow.AddMinutes(5);
            Save();
        }
        catch
        {
            _nextPetMarketGrantRefreshAt = DateTimeOffset.UtcNow.AddMinutes(1);
            foreach (var installation in installations.Where(IsActiveMarketInstallation).Where(item => !IsMarketInstallationUsable(item)))
                DeactivateMarketInstallation(installation, "部分小铺桌宠授权已过期，已停用该桌宠。");
        }
        finally { _petMarketGrantRefreshing = false; }
    }

    private bool IsActiveMarketInstallation(PetMarketInstallation installation)
        => IsMarketProductEnabled(installation.ProductId)
            && FindMarketInstallation(installation.ProductId) is { } active
            && ReferenceEquals(active, installation)
            && active.Version.Equals(installation.Version, StringComparison.Ordinal);

    private void DeactivateMarketInstallation(PetMarketInstallation installation, string message)
    {
        Settings.EnabledMarketProductIds?.RemoveAll(id => id.Equals(installation.ProductId, StringComparison.OrdinalIgnoreCase));
        _nextPetMarketGrantRefreshAt = DateTimeOffset.MinValue;
        RefreshEnabledPetSources();
        _petWindow?.ShowReaction(message);
    }

    private void ClosePetMarket()
    {
        _petMarketWindow?.Close();
        _petMarketWindow = null;
        _petMarketClient?.Dispose();
        _petMarketClient = null;
    }
}
