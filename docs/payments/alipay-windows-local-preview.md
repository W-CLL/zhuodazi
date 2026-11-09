# Windows 支付宝电脑网站支付独立本地预览

此入口只运行独立的激活页与桌宠小铺窗口，不初始化正式授权、设置、统计、试用检查或正式单实例互斥锁。现有启动方式不变。

支付产品为支付宝“电脑网站支付”。客户端只向隔离后端创建本地订单，然后打开我们自己的商品确认页。用户在网页核对金额并点击支付宝付款，网页才向后端请求 `alipay.trade.page.pay` 的付款地址并跳转支付宝官方收银台。支付宝官方页面提供付款操作；当前流程无需接入“当面付”预下单或在我们的网站生成二维码。

启动：

```powershell
pwsh -File windows/scripts/start-alipay-preview.ps1 -ConfigPath C:\LocalReview\alipay\client.json
```

已构建时可加 `-NoBuild`。也可直接运行构建产物：

```powershell
dotnet windows/ZhuoDazi/bin/x64/Release/net8.0-windows/ZhuoDazi.dll --alipay-preview-config C:\LocalReview\alipay\client.json
```

本机 JSON 配置格式如下，身份必须由隔离的本地后台准备，不使用正式凭据：

```json
{
  "baseUrl": "http://127.0.0.1:18788/",
  "dataDirectory": "C:\\LocalReview\\alipay-client",
  "activationIdentity": {
    "installationId": "activation_preview_device_01",
    "credential": "activation_preview_credential_replace_with_local_seed_01"
  },
  "shopIdentity": {
    "installationId": "shop_preview_device_0000001",
    "credential": "shop_preview_credential_replace_with_local_seed_00000001",
    "licenseId": "00000000-0000-4000-8000-000000000001"
  }
}
```

安装标识为 20–80 位字母、数字、下划线或连字符；凭据为 40–80 位同类字符。两组身份的安装标识和凭据分别不同。激活身份不提供 `licenseId`，以 `Trial <installationId>.<credential>` 请求；小铺以 `Bearer <licenseId>.<credential>` 请求。所有请求附带测试安装标识。

API 只连接 `http://127.0.0.1:18788/api/payments/alipay/`，拒绝其他 host、端口、重定向、代理和 Host 覆盖。收银台链接只由浏览器打开，且必须匹配 `https://in.desktoppet.online/checkout/alipay/<订单UUID>#token=<访问令牌>`，订单 UUID 必须与返回订单一致。

桌面创建订单合同仍为 `{ order, checkoutUrl }`。客户端不调用付款接口，也不使用或保存 `paymentUrl`；缺少有效 `checkoutUrl` 时不会用支付宝链接代替它。网页付款接口返回的 `paymentUrl` 由网页验证为支付宝官方网关地址后打开。支付宝同步返回页只提示用户等待确认，不凭返回参数将订单或权益标记为成功。

窗口分开保存激活订单和小铺订单，只有订单编号与收银台恢复链接写入 `dataDirectory/alipay-preview/`。返回窗口、点击刷新或四秒轮询会向本地后台读取实际状态；不提供模拟已付款按钮。后台交付的激活码只在窗口显示及按用户操作复制，不写入正式授权或预览恢复文件。

激活订单只有在后端返回 `status=paid` 且 `fulfillmentStatus=fulfilled` 时展示激活码。`deliveryMode=device_activated` 显示本地测试身份已在服务端激活；`deliveryMode=code_only` 显示本次购买已交付激活码，供复制保存后用于其他设备。交付成功后当前激活购买按钮停用，避免在目录缓存尚未刷新时重复发起购买。

缺少支付宝配置时仍可创建服务端允许的待支付订单、查看网页，窗口会显示后台返回的支付配置状态；不会将待支付状态改成已支付。浏览器本地映射和后端启动由独立的本地演示脚本负责。

商户需在对应 APPID 开通并完成“电脑网站支付”签约及能力绑定。应用审核通过、配置密钥或成功打开支付宝页面均不代表已付款；付款成功以服务端核验的支付宝异步通知或官方交易查询为准。

验证：

```powershell
dotnet run --project windows/tests/AlipayPreview.Checks/AlipayPreview.Checks.csproj -c Release
dotnet build windows/ZhuoDazi/ZhuoDazi.csproj -c Release -p:Platform=x64
```
