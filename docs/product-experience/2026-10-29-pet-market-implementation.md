# Windows 桌宠小铺：独立菜单、免费默认款与激活门槛

日期：2026-10-29。范围：本地开发和验证，不部署、不推送、不更新线上。

> 本文是首轮小铺实现记录，文中的 18766 为保留的旧隔离环境。当前完整预览使用 18767，已追加自动安装、订单弹窗、免费内容和后台购买者信息；以 [本轮调整汇总与 macOS 范围](2026-10-29-windows-review-and-macos-scope.md) 为准。

## 本轮确认

- 后台逐个桌宠设置免费或收费；普通付费款 2.80 元，复杂或热门款 5.80 元。
- 后台可指定一个免费、已上架的默认桌宠；默认款不自动覆盖用户当前桌宠。
- 只有正式激活的账号可以下单、免费领取、下载和使用小铺桌宠。试用、未激活、无效或已撤销授权不能绕过接口获得完整包。
- “免费”只表示无需支付，不取消激活要求。未激活用户可浏览商品和后台允许的有限预览。
- 此规则作用于新增小铺资源，不改写现有软件内置桌宠的试用体验。
- 好友转账沿用已验证规则：结构化转账消息、短码、金额、收款账号及有效时间匹配后直接确认订单并发放权益，不等待微信确认收款；同一交易或同一订单不重复发放。
- 免费领取不创建付款短码，不展示收款码，不伪造转账记录。

## 本轮实现清单

1. 独立受保护的小铺商品、版本和账号权益；收费/免费/默认款后台管理。
2. 复用现有正式激活身份，服务端严格区分正式激活和试用，客户端同时提示并拦截。
3. 每个包只公开后台选择的部分素材；完整 ZIP 仅在激活与权益均通过时下载。
4. Windows 浏览、预览、免费领取或付费下单、下载、校验、安装为图鉴并切换使用。
5. 本地隔离数据验收免费与付费流程、激活拒绝、重复事件、版本校验及安全解压。

## 名称与软件入口

面向用户和后台管理员的名称统一为“桌宠小铺”。Windows 完整设置窗口侧栏提供独立的“桌宠小铺”菜单，与其他设置页面并列；从该菜单进入小铺页面，浏览、领取或购买桌宠。内置图鉴继续用于选择和管理本地桌宠。

内部 `pet-market` 路由、API、清单文件名和数据字段沿用原标识，已有订单、权益及资源包不因界面改名迁移或失效。

## 资源包约定

ZIP 根目录包含 `pet-market.json`，内容为 `schemaVersion: 1`、`productId`、`version`、`title` 和 `files` 数组；每项含相对 `path`、`sizeBytes`、`sha256`。首版资源仅接受 GIF。服务器保存完整包的大小和 SHA-256，客户端安装前核验，资源逐项检查。

拒绝路径穿越、绝对路径、符号链接、大小写重复路径、未列入清单的额外文件、超限解压以及不合法的 GIF。新版本安装失败不覆盖原可用版本，不运行包内代码。

限制为最多 80 个 GIF，每个不超过 8 MB，宽高不超过 2048，完整 ZIP 与解压总量均不超过 64 MB，清单不超过 256 KB。打包工具 `scripts/build_pet_market_pack.py` 接受 `--files` 多文件或 `--source-dir` 目录，生成清单和逐文件 SHA-256，不覆盖已有输出。

目录使用 `activeVersion` 表示当前上架版本，`entitlementVersion` / `ownedVersion` 表示账号获得的固定版本。下载描述和使用授权均由服务端签名，绑定账号、许可证、设备、商品和版本；客户端验证签名后使用。五位备注码仅用于匹配订单，不充当下载密码或激活凭据。

## 本地隔离环境

完整小铺使用 `127.0.0.1:18766`，状态位于 `zhuodazi/.local-review/pet-market-20261029/`。旧 0.01 元联调仍使用 `18765` 与 `local-market-transfer/`，两套订单、凭据和桥接状态互不复用。

开发服务入口为旁边 `deskpet/scripts/local-pet-market-demo.js`：

```powershell
node scripts/local-pet-market-demo.js `
  --state-directory 'D:\code_file\pro_python\pet\zhuodazi\.local-review\pet-market-20261029' `
  --port 18766 `
  --seed-pack 'D:\code_file\pro_python\pet\zhuodazi\.local-review\pet-market-20261029\fixtures\starter-free-1.0.0.zip' `
  --bridge-config 'D:\code_file\pro_python\pet\zhuodazi\.local-review\local-market-transfer\bridge.json' `
  --collection-config 'D:\code_file\pro_python\pet\zhuodazi\.local-review\local-market-transfer\server.json'
```

在 `deskpet` 目录运行。服务仅绑定字面量 IPv4 回环地址；自动创建独立开发数据库、签名密钥、后台凭据及开发激活身份，身份不属于线上用户。后台地址是 `http://127.0.0.1:18766/admin#pet-market`，导航显示“桌宠小铺”；密码只保存在本地 `demo-secrets.json`，不放入文档或日志。`client.json` 提供 Windows 本地预览所需隔离配置，不使用普通软件数据目录；配置文件中的身份凭据也不应复制到文档、截图或日志中。

可选的两份旧配置仅用于生成新 `bridge.json`，不改写原文件，也不启动真实监听。新桥接使用 `eventPath: /api/pet-market/payment-events`；必须搭配新的 `bridge.sqlite3`，已有 outbox 绑定原地址/路由后不能改投另一服务。启动参数详见 `scripts/local_market_payments.py --help`。不重新使用旧付款记录测试新商品，也不要求再次真实付款。

Windows 完整软件本地预览（在 `zhuodazi` 目录运行，先启动开发服务）：

```powershell
& 'D:\code_file\pro_python\pet\.local-review\tooling\dotnet\dotnet.exe' build `
  windows/ZhuoDazi/ZhuoDazi.csproj -c Release -p:Platform=x64
& 'D:\code_file\pro_python\pet\.local-review\tooling\dotnet\dotnet.exe' `
  windows/ZhuoDazi/bin/x64/Release/net8.0-windows/ZhuoDazi.dll `
  --app-preview-config 'D:\code_file\pro_python\pet\zhuodazi\.local-review\pet-market-20261029\client.json'
```

若使用自己安装的 .NET 8 SDK，将上述可执行文件替换为 `dotnet`。`--app-preview-config` 用于展示完整桌搭子：打开桌宠与完整设置窗口，可从侧栏单独进入“桌宠小铺”，检查各页面的布局和本地交互。预览复用上述配置，但将数据写入 `config.dataDirectory/app-preview` 子目录（本次为 `client/app-preview`），不迁移或覆盖旧 `client/settings.json` 及其已安装资源，并与普通桌搭子使用不同实例锁。生产网络访问被拦截，不启动生产同步、试用验证、遥测或开机注册。需要线上服务的页面在本地预览中不可完成线上操作。

原 `--pet-market-config` 参数保留为仅打开小铺调试窗口的入口。查看完整软件时应使用 `--app-preview-config`；两种入口都不代表正式发布，也不应通过普通 `--settings` 启动来替代隔离预览。

停止开发服务时，在本次状态目录创建 `market-stop.signal`；它只停止 18766 这份开发服务，不删除订单和资源，不影响旧 18765 测试服务。

示例 `starter-free` 含仓库已有的五个 GIF，只开放两个预览。它用于验证真实 GIF 的领取、签名下载和安装，并非已经准备好的正式 25+ 素材付费包。

本轮还添加了 `paid-local-test`，复用同五个 GIF，名称和简介明确标为“本地模拟测试，请勿真实支付”。开发订单 `441NA` 由带 `synthetic-` 前缀的本地模拟转账事件确认，界面由“付款订单”自动变为“已拥有 / 下载并安装”；相同事件重发被识别为重复。这不是微信真实转账，也不计入此前 `116PF` 的真实付款记录。

## 此前单窗口验收记录

以下记录对应改名及完整软件预览入口之前的本地验证。新增独立菜单与 `--app-preview-config` 的检查另列于下方“完整软件预览验收”，不将旧记录当作新增入口已通过的证明。

- 后端 JavaScript 静态检查：86 个文件通过；全套 Node 测试 141/141 通过，含小铺 API、后台表单及种子恢复测试。
- Python 脚本全套测试：52/52 通过，覆盖桥接路由绑定、重复支付事件和资源打包。新桥接配置检查通过，检查过程没有打开微信源数据库。
- Windows 安装器：18 项断言通过，覆盖包和文件哈希、路径/清单拒绝、安装失败保留旧版，以及校验和解压使用同一只读句柄。
- Windows 小铺客户端：12 项断言通过，含获得时版本锁定、下载文件句柄释放、签名篡改、身份错配、过期授权，以及生产目录/子目录隔离拒绝。
- 本地真实 GIF 接口：默认款共 5 个素材，公开选择的 2 个；允许预览返回 `200 image/gif`，第 3 个未开放素材返回 `404`，匿名下载完整包返回 `401`。
- Windows Release/x64 编译通过，0 警告、0 错误。实际窗口完成默认款免费领取、下载、校验、安装、启用；桌面显示了新 GIF，持久化记录包含有效使用授权。
- 付费模拟权益已在实际窗口完成下载安装。两份安装各有 5 个 GIF，安装后逐文件大小及 SHA-256 全部一致；安装付费包时当前免费桌宠仍保持启用，未自动替换。
- 实机发现并修复付款状态轮询后 busy 标志未释放导致安装按钮不响应；同时修复切回默认宠物遗留小铺缓存、恢复启动漏启授权定时器、旧商品刷新响应误停新商品等边界。小铺使用授权每 5 分钟尝试续期，过期或收到拒绝后停用并退回本地桌宠。
- 原 `--pet-market-config` 单窗口调试关闭通用设置/正式激活入口，不启动生产同步、试用验证、遥测或开机注册。普通已安装软件与旧付款测试数据保持独立。新增 `--app-preview-config` 则提供完整设置界面，并继续隔离生产网络。

## 完整软件预览验收

2026-10-29 实测结果：

- Windows Release/x64 主构建通过，0 警告、0 错误。`AppPreview.Checks` 通过隔离存储、精确 loopback 白名单、Host 防护、真实 loopback 请求及禁用重定向检查；小铺客户端 12 项断言、安装器 18 项断言均通过。
- 完整预览进程以 PID `54024` 启动，窗口标题为“桌搭子 3.4.4 · Windows 本地预览”，左侧显示 11 项菜单，包含独立“桌宠小铺”入口。该 PID 为本次验收时的记录，重启后会变化。
- 在实际界面点击“桌宠小铺”，两个本地商品正常加载。免费默认包预览从 `1/2` 切换到 `2/2`，到达末张后“下一张”按钮禁用。
- 从小铺切换到“陪伴日常”后再返回，仍保留 `2/2` 的预览选择及对应图片。嵌入页面复用 `PetMarketView`，原独立窗口包装仍保留，供 `--pet-market-config` 调试使用。
- 验收结束时窗口保留在“桌宠小铺”，供用户直接查看。本次仅验证完整预览、菜单切换和图片浏览，没有再次支付、创建新订单或安装新包；此前测试数据保持原样并继续隔离，没有上线或发布。

2026-10-28 的独立 0.01 元调试目录及订单 `116PF` 保留，不用旧付款冒充新的正式商品购买。本轮不部署、不发布，不改动微信程序或 wxlocal 项目。
