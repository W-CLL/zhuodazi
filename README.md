# 桌搭子

桌搭子是一款原生桌面宠物应用。Windows 与 macOS 使用各自的平台原生技术实现，并在同一仓库中独立构建。

## 目录

```text
zhuodazi/
├─ windows/                 # C# / WPF Windows 正式版
├─ macos/                   # Swift / AppKit macOS 预览版
└─ .github/workflows/       # 构建与自动 Release
```

## 自动构建

- `Build Windows`：在 Windows runner 上构建 x64 单文件程序、Inno Setup 安装包和便携 ZIP，安装版与便携版分别上传，避免同一 Artifact 重复包含两份程序。
- `Build macOS`：分别在 Apple Silicon 与 Intel runner 上构建原生 AppKit 应用，执行 ad-hoc 签名并输出 ZIP。
- 创建 Pull Request 或手动触发工作流时会自动运行构建。
- 合并到 `main` 后，`Build and Release` 会在三个构建全部成功后自动创建或更新对应版本的 Draft Release，例如 `v2.4.2`。

构建完成后，可在对应 GitHub Actions 运行记录的 Artifacts 区域下载产物；合并到 `main` 后，产物还会自动附加到 Draft Release。验证通过后，在 GitHub Release 页面手动点击发布即可。已发布的同版本 Release 不会被后续构建覆盖。macOS 当前产物没有 Developer ID 签名和 Apple 公证，首次打开需要在“系统设置 > 隐私与安全性”中手动允许。

## 平台状态

Windows 版为功能完整的 2.4.2。macOS 版当前提供透明桌宠窗口、GIF 播放、拖拽投掷、惯性、边缘反弹、跟随鼠标、随机移动，以及基于随机袋的自动换宠和手动换宠；后续继续移植设置、激活、更新与小剧场功能。Windows 与 macOS 的随机换宠都会使用系统随机源，每轮不重复，并避免轮次交界处连续出现同一张 GIF。

具体开发和打包方式见 [Windows 说明](windows/README.md) 与 [macOS 说明](macos/README.md)。

## 扩展内容

可直接导入 Windows 版的互动词包和小剧场剧本位于 [`content-packs/`](content-packs/README.md)，包含 4 套共 576 句互动台词和 10 套完整五轮小剧场。

## 版本规则

Windows 与 macOS 必须使用相同的 `MAJOR.MINOR.PATCH` 三段式版本号。普通修复递增最后一位，例如 `2.4.1` 修复后发布为 `2.4.2`；新增兼容功能时递增中间位；不兼容的大版本升级递增第一位。CI 会在构建前校验两个平台的版本完全一致。
