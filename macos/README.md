# 桌搭子 macOS 版

这是使用 Swift 与 AppKit 实现的原生 macOS 预览版，最低支持 macOS 13。

## 当前功能

- 透明、无边框、跨桌面的 GIF 桌宠窗口
- 拖拽投掷、惯性和屏幕可见区域边缘反弹
- 跟随鼠标和随机移动，可从菜单栏独立开关
- 自动随机换宠和立即换一只；使用系统随机源，轮内不会重复 GIF
- 菜单栏显示/隐藏和退出

随机换宠会扫描 App 包内 `Resources/Pets/yuexinmiao` 目录中的全部 GIF。打包脚本会复用 Windows 版的 62 张内置“月薪喵”资源，避免两张素材只能固定交替。默认每 5 分钟自动切换一次；每轮会以随机顺序使用完所有 GIF，再重新生成下一轮顺序，并避免两轮交界处连续显示同一张。

## 本机开发

需要安装 Xcode Command Line Tools：

```bash
cd macos
swift run ZhuoDaziMac
```

运行核心逻辑测试：

```bash
swift test
```

生成测试包：

```bash
bash macos/scripts/build-app.sh
```

输出位于 `macos/dist/`。脚本使用 ad-hoc 签名，不需要 Apple Developer 账号，但首次打开需要在 macOS“隐私与安全性”设置中手动允许。

## GitHub Actions

`Build macOS` 工作流分别使用 Apple Silicon 与 Intel runner 编译，并上传：

从 `2.4.3` 开始，App、ZIP 和 GitHub Actions Artifact 都包含三段式版本号，例如：

- `ZhuoDazi-macOS-2.4.3-arm64.zip`
- `ZhuoDazi-macOS-2.4.3-x86_64.zip`

以后配置 Developer ID 和公证凭据时，可在现有打包脚本后追加正式签名、公证和 DMG 步骤。
