# 桌搭子客户端工作区

桌搭子包含 Windows、macOS、Android 三个客户端，以及官网、内容包和发布辅助脚本。授权、更新、大厅、互动内容与管理后台位于旁边独立的 `deskpet` 仓库。

## 目录

| 目录 | 职责 |
|---|---|
| `windows/` | .NET 8 / WPF 桌面客户端、内置图鉴及 Windows 构建 |
| `macos/` | Swift / AppKit 客户端、核心逻辑与 Swift 测试 |
| `android/` | Android 原生悬浮服务、授权/网络/存储与打包宿主 |
| `mobile_ui/` | Android 使用的 Flutter 主界面、桥接与 widget tests |
| `content-packs/` | 可分发的互动词包、小剧场剧本和导入说明 |
| `website/` | 官网与文章 |
| `marketing/` | 面向用户的功能说明与素材 |
| `scripts/` | 发布准备等本地/CI 辅助脚本及其测试 |
| `docs/releases/` | 按版本维护的用户更新说明，发布流程读取这些文件 |
| `docs/product-experience/` | 产品规则、实施范围、验证结果与手动验收说明 |

## 产品规则与本次改动

联合需求：[卸载反馈、词库多选与在线桌宠市场](docs/product-experience/2026-10-28-uninstall-wordpacks-pet-market-requirements.md)。付款使用固定三个数字加两个字母的备注码（大小写无关）、15 分钟订单超时；短码有 676000 种组合，历史不复用，耗尽报错。收款码保留人工核对；好友转账按最新规则，结构化转账与订单短码、金额、收款账号等严格匹配后自动确认订单并开放下载，不等待微信领取。漏填备注时管理员可人工核对并确认收款。Windows / macOS 已补齐小铺、免费资源、内容多选、离线投递确认及可选卸载反馈；开发与验收记录见 [本次实施范围](docs/product-experience/2026-10-29-windows-review-and-macos-scope.md)。

小铺本地实现与验证见 [Windows 桌宠小铺：独立菜单、免费默认款与激活门槛](docs/product-experience/2026-10-29-pet-market-implementation.md)。Windows 完整设置侧栏提供独立“桌宠小铺”菜单，后台名称同步统一。后台按商品设置免费或 2.80 / 5.80 元收费、一个免费默认款和每包有限预览；正式激活后才能领取、购买、下载与使用。已拥有权益绑定获得时的版本，资源更新或价格调整不会改写旧订单。旧 18766 环境的五个 GIF 默认款保留为早期本地验证记录；新增 8 款、235 个真实素材的独立场景及后台文件夹上传、定价、标记、上架流程见 [桌宠小铺真实素材与上架流程](docs/product-experience/2026-10-29-pet-shop-showcase.md)。使用 `--app-preview-config` 和现有隔离 `client.json` 可查看完整 Windows 软件，预览阻止生产网络访问；原 `--pet-market-config` 保留为小铺单窗口调试入口。

本机运行与实际付款步骤见 [Windows 本地 0.01 元付款联调](docs/product-experience/2026-10-28-local-payment-test.md)。只连接 `127.0.0.1`，使用隔离数据和现有 wxlocal 密钥；好友转账仅在 `personal_transfer / incoming / observed / verified:false` 严格匹配后自动确认并发权益，收款码仍人工核对。原订单 `116PF` 已自动确认，下载接口 HTTP 200，Windows 用户主动下载成功且 SHA-256 与服务端一致；有效 ZIP 只有 1 个测试文件，不包含正式桌宠，也不静默下载或安装。

完整小铺已补齐 [付款页、后台收款设置与监听整合](docs/product-experience/2026-10-29-pet-shop-payment-completion.md)：下单后展示收款人、微信号或二维码、金额、五位短码及倒计时；订单冻结收款目标，监听不可用时暂停新订单。`windows/scripts/start-pet-shop-preview.ps1` 启动本地 `18767` 后台、独立监听和完整 Windows 预览，`stop-pet-shop-preview.ps1` 停止对应后台和监听。新场景与旧付款实验隔离，实际付款验收记录分别说明。

后续小铺已增加 [订单记录、付款后自动安装与免费内容标签页](docs/product-experience/2026-10-29-shop-orders-and-free-content.md)：已付款订单持续可查，确认付款后自动安装但不启用；小剧场和互动词库可从后台上传上架，在客户端免费下载并安装，再由用户手动启用。当前本地准备了 4 套词库、10 个剧本；桌宠目录加宽，历史订单放入独立弹窗。

内容互动与答题已改为每次触发时联网获取一条，取消题目预取和缓存回退；详见 [实时取题行为与验证](docs/product-experience/2026-10-27-realtime-content.md)。需要发布客户端新版后生效。

当前桌面发布目标按用户指定为 Windows/macOS **v3.5.3**，包含桌宠小铺、付款与订单恢复、免费小剧场和互动词库、内容多选、可选卸载反馈及搭子离线投递确认。Android 保持 **v1.5.4**（versionCode 28），本轮不发布 Android。已有设置和日常数据保留。详见 [版本递增规则](docs/releases/versioning.md)、[桌面更新说明](docs/releases/desktop-3.5.3.md) 与 [Android 更新说明](docs/releases/android-1.5.4.md)。版本文件不代表已经正式发布。

参见 [2026-09-18 实施基线](docs/product-experience/2026-09-18-plan.md)、[实施对照与手动验收](docs/product-experience/2026-09-18-acceptance.md) 与 [当前功能说明](marketing/桌搭子功能点说明.md)。

有效体验期和正式激活均可进入公开大厅；一对一搭子仍需正式激活。大厅需主动加入，不因新设备启动自动公开。关闭设置窗口不会退出常驻桌宠。

3.3.0 的逐项能力、平台差异与本地默认值见 [当前功能与默认设置](docs/product-experience/3.3.0-features-and-defaults.md)。

## 本地验证

- Windows：安装 .NET 8 SDK 后运行 `dotnet build windows/ZhuoDazi/ZhuoDazi.csproj -c Release`。
- macOS：在 Mac 安装 Xcode Command Line Tools 后，在 `macos/` 运行 `swift test` 与 `swift build`。
- Android 界面：在 `mobile_ui/` 运行 `flutter analyze`、`flutter test`；宿主构建参见 `android/README.md`。
- 发布准备逻辑：运行 `python -m unittest discover -s scripts/tests -v`，测试不会创建远程 Release。
- 服务端：在独立 `deskpet/` 中运行 `npm test` 和 `npm run check`。

发布前需要检查版本号、对应更新说明及目标架构。版本说明缺失时发布准备应失败，不自动填入模糊占位文案。产品代码、构建输出和本地工具分别存放；开发快照及本轮临时验证产物位于上级工作区 `.local-review/`。
