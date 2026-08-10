# 桌搭子

桌搭子是一款原生桌面宠物应用。Windows 与 macOS 使用各自的平台原生技术实现，并在同一仓库中独立构建。

## 目录

```text
zhuodazi/
├─ windows/                 # C# / WPF Windows 正式版
├─ macos/                   # Swift / AppKit macOS 版
└─ .github/workflows/       # 构建与自动 Release
```

## 自动构建

- `Build Windows`：在 Windows runner 上构建 x64 自包含程序、Inno Setup 安装包和便携 ZIP，并直接上传到对应的 Windows Draft Release。
- `Build macOS`：分别在 Apple Silicon 与 Intel runner 上构建原生 AppKit 应用，执行 ad-hoc 签名并输出 ZIP。
- 创建 Pull Request 或手动触发工作流时会自动运行构建。
- 合并到 `main` 后，`Build and Release` 会在三个构建全部成功后分别创建或更新 Windows 与 macOS 的 Draft Release，例如 `v3.0.0` 和 `macos-v3.0.0`。

推送到 `main` 或手动运行工作流后，Windows 和 macOS 产物会直接上传到各自的 Draft Release，不占用 GitHub Actions Artifact 存储额度。Pull Request 只执行构建与测试，不上传安装包。验证通过后，在 GitHub Release 页面手动点击发布即可；已发布的同版本 Release 不会被后续构建覆盖。macOS 当前产物没有 Developer ID 签名和 Apple 公证，首次打开需要在“系统设置 > 隐私与安全性”中手动允许。

## 平台状态

Windows 与 macOS 当前版本均为 3.0.0。首次使用可完整体验 5 分钟；体验结束后保留 3 个自定义桌宠、1 个内置 GIF 资源库和基础陪伴，激活后解锁随机互动、互动词包、小剧场、指定表情提醒和外部 GIF 资源库导入。原有激活记录继续有效。两个平台都提供账号隔离的随机心情问候、冷笑话、数学题、趣味知识、脑筋急转弯、生活小贴士和关怀内容互动，并支持 Ed25519 签名内容缓存、离线包、互动设置同步和行为事件批量上传。macOS 更新按 Apple Silicon（arm64）和 Intel（x86_64）分别提供安装包；Windows 提供 Windows x64 安装包。两个系统的随机换宠都会使用系统随机源，每轮不重复，并避免轮次交界处连续出现同一张 GIF。

具体开发和打包方式见 [Windows 说明](windows/README.md) 与 [macOS 说明](macos/README.md)。

## 扩展内容

可直接导入 Windows 版的互动词包和小剧场剧本位于 [`content-packs/`](content-packs/README.md)，包含 4 套共 576 句互动台词和 10 套完整五轮小剧场。

## 版本规则

Windows 与 macOS 各自使用 `MAJOR.MINOR.PATCH` 三段式版本号。普通修复递增最后一位，例如 `2.4.3` 修复后发布为 `2.4.4`；新增兼容功能时递增中间位；不兼容的大版本升级递增第一位。CI 分别校验两个平台各自的版本与安装包元数据，并分别创建 Release。
