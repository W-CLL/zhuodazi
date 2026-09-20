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

当前发布版本为 Windows/macOS **v3.3.0**、Android **v1.4.0**（versionCode 22）。新增可跳过、可继续的四步首次体验；本地互动与短剧可离线试看，已有用户升级只看到一次可关闭提示。Android 互动结束后自动恢复操作，小菜单避开桌宠，双宠剧场固定在屏幕上方、轮流接话并留足阅读时间；桌面菜单采用“桌宠小窝”“灵感口袋”等统一名称。详见 [实施基线](docs/product-experience/2026-09-20-plan.md)、[版本递增规则](docs/releases/versioning.md)、[桌面更新说明](docs/releases/desktop-3.3.0.md) 与 [Android 更新说明](docs/releases/android-1.4.0.md)。

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
