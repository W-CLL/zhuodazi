# 桌搭子

桌搭子是一款原生桌面宠物应用。Windows 与 macOS 使用各自的平台原生技术实现，并在同一仓库中独立构建。

## 目录

```text
zhuodazi/
├─ windows/                 # C# / WPF Windows 正式版
├─ macos/                   # Swift / AppKit macOS 预览版
└─ .github/workflows/       # 两个平台的自动构建
```

## 自动构建

- `Build Windows`：在 Windows runner 上构建 x64 单文件程序、Inno Setup 安装包和便携 ZIP。
- `Build macOS`：分别在 Apple Silicon 与 Intel runner 上构建原生 AppKit 应用，执行 ad-hoc 签名并输出 ZIP。
- 推送到 `main`、创建 Pull Request 或手动触发工作流时会自动运行。

构建完成后，可在对应 GitHub Actions 运行记录的 Artifacts 区域下载产物。macOS 当前产物没有 Developer ID 签名和 Apple 公证，首次打开需要在“系统设置 > 隐私与安全性”中手动允许。

## 平台状态

Windows 版为功能完整的 2.4.1。macOS 版当前提供透明桌宠窗口、GIF 播放、拖拽投掷、惯性、边缘反弹、跟随鼠标和随机移动开关，后续继续移植设置、激活、更新与小剧场功能。

具体开发和打包方式见 [Windows 说明](windows/README.md) 与 [macOS 说明](macos/README.md)。
