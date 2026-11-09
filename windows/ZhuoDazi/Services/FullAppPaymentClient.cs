using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ZhuoDazi.Models;

namespace ZhuoDazi.Services;

internal sealed class FullAppPaymentClient : IDisposable
{
    private readonly LicenseService _licenses;
    private readonly AlipayPreviewClient _client;
    private readonly string _pendingPath;
    private readonly Action<Uri> _openCheckout;
    private readonly Dictionary<string, AlipayPreviewOrderResponse> _pending = new();
    private readonly Dictionary<string, DateTimeOffset> _nextRefresh = new();
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("ZhuoDazi.LocalPaymentOrders.v1");

    internal FullAppPaymentClient(LicenseService licenses, HttpMessageHandler? transport = null, Action<Uri>? openCheckout = null, Uri? origin = null)
    {
        _licenses = licenses;
        _client = new AlipayPreviewClient(origin ?? new Uri("https://in.desktoppet.online/"), transport);
        _openCheckout = openCheckout ?? (target => Process.Start(new ProcessStartInfo(target.AbsoluteUri) { UseShellExecute = true }));
        _pendingPath = Path.Combine(licenses.DataDirectory, "payment-orders.dat");
        try
        {
            if (File.Exists(_pendingPath) && new FileInfo(_pendingPath).Length <= 2 * 1024 * 1024)
            {
                var raw = ProtectedData.Unprotect(File.ReadAllBytes(_pendingPath), Entropy, DataProtectionScope.CurrentUser);
                var saved = JsonSerializer.Deserialize<AlipayPreviewOrderResponse[]>(raw, AlipayPreviewConfig.JsonOptions) ?? [];
                foreach (var item in saved.Take(100))
                    if (Guid.TryParse(item.Order.Id, out _) && item.CheckoutUrl is not null)
                    {
                        AlipayPreviewClient.ValidateCheckoutUrl(item.CheckoutUrl, item.Order.Id);
                        _pending[item.Order.Id] = item;
                    }
            }
        }
        catch { /* A damaged saved link cannot grant payment or prevent reopening an order. */ }
    }

    private AlipayPreviewIdentity Identity => new()
    {
        InstallationId = _licenses.InstallationId,
        Credential = _licenses.InstallEventIdentity.Credential,
        LicenseId = _licenses.IsActivated ? _licenses.LicenseId : null
    };

    internal async Task<AlipayPreviewCatalog> CatalogAsync(CancellationToken token)
    {
        if (!_licenses.IsActivated) await _licenses.CheckTrialAsync(token);
        return await _client.CatalogAsync(Identity, token);
    }

    internal async Task RefreshActivationOrdersAsync(CancellationToken token)
    {
        foreach (var saved in _pending.Values.Where(item => item.Order.Kind == "activation").ToArray())
            await UpdateOrderAsync(saved, token);
        Save();
    }

    private async Task<AlipayPreviewOrderResponse> UpdateOrderAsync(AlipayPreviewOrderResponse saved, CancellationToken token)
    {
        var response = await _client.OrderAsync(Identity, saved.Order.Id, token);
        if ((response.Order.Status == "pending" || response.Order.Status == "paid" && response.Order.FulfillmentStatus != "fulfilled")
            && (!_nextRefresh.TryGetValue(saved.Order.Id, out var next) || DateTimeOffset.UtcNow >= next))
        {
            _nextRefresh[saved.Order.Id] = DateTimeOffset.UtcNow.AddSeconds(30);
            try { response = await _client.RefreshOrderAsync(Identity, saved.Order.Id, token); }
            catch (OperationCanceledException) { throw; }
            catch { /* Keep the authenticated order result when Alipay query is temporarily unavailable. */ }
        }
        response = response with { CheckoutUrl = saved.CheckoutUrl };
        _pending[response.Order.Id] = response;
        return response;
    }

    internal async Task<AlipayPreviewOrderResponse> OpenAsync(bool activation, string? productId, CancellationToken token)
    {
        var response = await _client.CreateOrderAsync(Identity, activation, productId, token);
        _pending[response.Order.Id] = response;
        Save();
        Open(response);
        return response;
    }

    internal void OpenOrder(string id)
    {
        if (!_pending.TryGetValue(id, out var response)) throw new InvalidOperationException("请重新点击商品的购买按钮打开订单。");
        Open(response);
    }

    private void Open(AlipayPreviewOrderResponse response)
    {
        var target = AlipayPreviewClient.ValidateCheckoutUrl(response.CheckoutUrl ?? string.Empty, response.Order.Id);
        _openCheckout(target);
    }

    internal async Task<IReadOnlyList<PetMarketOrder>> PendingPetOrdersAsync(CancellationToken token)
    {
        var results = new List<PetMarketOrder>();
        foreach (var saved in _pending.Values.Where(item => item.Order.Kind == "pet").ToArray())
        {
            var response = await UpdateOrderAsync(saved, token);
            var order = response.Order;
            // Fulfilled purchases come from the original signed market/entitlement APIs.
            if (order.Status == "paid" && order.FulfillmentStatus == "fulfilled") continue;
            results.Add(new PetMarketOrder
            {
                Id = order.Id, ProductId = order.ProductId ?? string.Empty, ProductTitle = order.Title,
                ProductVersion = order.ProductVersion ?? string.Empty, AmountFen = order.AmountFen, Currency = "CNY",
                CreatedAt = order.CreatedAt, ExpiresAt = order.ExpiresAt,
                Status = order.Status == "pending" || order.Status == "paid" ? "pending_payment" : "expired",
                Payment = new PetMarketPaymentInfo { Method = "alipay_page", Enabled = true, Available = true,
                    ListenerOnline = true, Instructions = "支付宝支付" }
            });
        }
        Save();
        return results;
    }

    private void Save()
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(_pending.Values.TakeLast(100).ToArray(), AlipayPreviewConfig.JsonOptions);
        var protectedBytes = ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser);
        var temporary = _pendingPath + ".tmp";
        File.WriteAllBytes(temporary, protectedBytes);
        File.Move(temporary, _pendingPath, true);
    }

    public void Dispose() => _client.Dispose();
}
