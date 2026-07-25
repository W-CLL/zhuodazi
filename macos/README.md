# 桌搭子 macOS 版

这是使用 Swift 与 AppKit 实现的原生 macOS 预览版，最低支持 macOS 13。

## 当前功能

- 透明、无边框、跨桌面的 GIF 桌宠窗口
- 拖拽投掷、惯性和屏幕可见区域边缘反弹
- 跟随鼠标和随机移动，可从菜单栏独立开关
- 菜单栏显示/隐藏和退出

## 本机开发

需要安装 Xcode Command Line Tools：

```bash
cd macos
swift run ZhuoDaziMac
```

生成测试包：

```bash
bash macos/scripts/build-app.sh
```

输出位于 `macos/dist/`。脚本使用 ad-hoc 签名，不需要 Apple Developer 账号，但首次打开需要在 macOS“隐私与安全性”设置中手动允许。

## GitHub Actions

`Build macOS` 工作流分别使用 Apple Silicon 与 Intel runner 编译，并上传：

- `ZhuoDazi-macOS-arm64`
- `ZhuoDazi-macOS-x86_64`

以后配置 Developer ID 和公证凭据时，可在现有打包脚本后追加正式签名、公证和 DMG 步骤。
