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
- 合并到 `main` 后，`Build and Release` 会在三个构建全部成功后分别创建或更新 Windows 与 macOS 的 Draft Release，例如 `v2.5.8` 和 `macos-v2.2.6`。

推送到 `main` 或手动运行工作流后，Windows 和 macOS 产物会直接上传到各自的 Draft Release，不占用 GitHub Actions Artifact 存储额度。Pull Request 只执行构建与测试，不上传安装包。验证通过后，在 GitHub Release 页面手动点击发布即可；已发布的同版本 Release 不会被后续构建覆盖。macOS 当前产物没有 Developer ID 签名和 Apple 公证，首次打开需要在“系统设置 > 隐私与安全性”中手动允许。

## 平台状态

Windows 版当前为 2.5.8，macOS 版当前为 2.2.6。两个平台都提供账号隔离的随机心情问候、冷笑话、数学题、趣味知识、脑筋急转弯、生活小贴士和关怀内容互动，并支持 Ed25519 签名内容缓存、离线包、互动设置同步和行为事件批量上传。互动题目与答案会完整换行显示，超长正文可以滚动查看，拖动桌宠时当前互动保持可见。macOS 小剧场包含入场、跳跃、换位和摇摆动作；两端也都支持外观行为、GIF 桌宠管理、资源库、互动词包、提醒、设备绑定和在线更新。Windows 与 macOS 首次使用均可免费试用 5 分钟，之后使用激活码完成绑定；联系作者入口按需展开微信二维码，不会在页面打开时直接展示。macOS 更新会按 Apple Silicon（arm64）和 Intel（x86_64）分别获取对应安装包；Windows 只获取 Windows x64 安装包。两个系统的随机换宠都会使用系统随机源，每轮不重复，并避免轮次交界处连续出现同一张 GIF。

具体开发和打包方式见 [Windows 说明](windows/README.md) 与 [macOS 说明](macos/README.md)。

## 扩展内容

可直接导入 Windows 版的互动词包和小剧场剧本位于 [`content-packs/`](content-packs/README.md)，包含 4 套共 576 句互动台词和 10 套完整五轮小剧场。

## 版本规则

Windows 与 macOS 各自使用 `MAJOR.MINOR.PATCH` 三段式版本号。普通修复递增最后一位，例如 `2.4.3` 修复后发布为 `2.4.4`；新增兼容功能时递增中间位；不兼容的大版本升级递增第一位。CI 分别校验两个平台各自的版本与安装包元数据，并分别创建 Release。
