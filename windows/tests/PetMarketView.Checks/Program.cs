using System.Text.Json;
using ZhuoDazi.Models;

var product = JsonSerializer.Deserialize<PetMarketProduct>("""
{
  "id": "real-pet",
  "title": "真实角色",
  "description": "轻松陪伴",
  "chargingMode": "paid",
  "priceFen": 580,
  "currency": "CNY",
  "isFeatured": true,
  "isDefault": false,
  "assetCount": 28,
  "tags": ["精选", "治愈", "日常", "可爱", "不会显示", "十二字以上的标签需要被安全截断"]
}
""") ?? throw new InvalidOperationException("Product JSON did not deserialize.");
var card = new PetMarketCardViewModel(product);

Require(card.IsFeatured, "Featured products must expose their badge state.");
Require(card.PriceLabel == "¥5.80", "Paid cards must keep the actual price visible.");
Require(card.OwnershipLabel == "可购买", "Unowned paid products must remain purchasable.");
Require(card.AssetLabel == "28 个动作资源", "Cards must show the actual asset count.");
Require(card.Tags.Count == 4 && card.Tags.All(tag => tag.Length <= 12),
    "Cards must display at most four bounded tags.");
Require(card.Matches("治愈", "all") && card.Matches(string.Empty, "paid") && !card.Matches(string.Empty, "free"),
    "Search and price filters must match the card data.");

product.Owned = true;
var owned = new PetMarketCardViewModel(product);
Require(owned.PriceLabel == "¥5.80" && owned.OwnershipLabel == "已拥有" && owned.Matches(string.Empty, "owned"),
    "Owned cards must show ownership separately without hiding price.");

var pngDataUrl = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=";
var qrPayment = JsonSerializer.Deserialize<PetMarketPaymentInfo>($$"""
{
  "enabled": true,
  "available": true,
  "method": "collection_qr",
  "qrCodeDataUrl": "{{pngDataUrl}}",
  "listenerOnline": true
}
""") ?? throw new InvalidOperationException("Payment JSON did not deserialize.");
Require(qrPayment.CanCreateOrder && qrPayment.TryGetQrCodeBytes(out var qrBytes) && qrBytes.Length > 8,
    "A bounded PNG data URL must be accepted for QR collection.");

qrPayment.QrCodeDataUrl = "https://example.invalid/payment.png";
Require(!qrPayment.TryGetQrCodeBytes(out _), "Remote QR URLs must never be accepted.");
qrPayment.QrCodeDataUrl = "data:image/svg+xml;base64,PHN2Zz48L3N2Zz4=";
Require(!qrPayment.TryGetQrCodeBytes(out _), "Non-PNG/JPEG data URLs must never be accepted.");
qrPayment.QrCodeDataUrl = "data:image/png;base64,iVBORw0KGgo=";
Require(!qrPayment.TryGetQrCodeBytes(out _), "Truncated PNG data must never be accepted.");
qrPayment.QrCodeDataUrl = "data:image/jpeg;base64,/9j/AA==";
Require(!qrPayment.TryGetQrCodeBytes(out _), "JPEG data without an end marker must never be accepted.");

var transfer = new PetMarketPaymentInfo
{
    Enabled = true, Available = true, ListenerOnline = true, Method = "personal_transfer",
    RecipientName = "公开昵称", RecipientAccount = "public_account"
};
Require(transfer.CanCreateOrder, "A complete personal-transfer destination must allow order creation.");
transfer.ListenerOnline = false;
Require(!transfer.CanCreateOrder, "An offline listener must block new order creation.");

Console.WriteLine("PetMarketView checks passed (14 assertions).");

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
