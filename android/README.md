# 桌搭子 Android 版

Android 版使用原生 Java 实现，通过系统“显示在其他应用上层”权限提供悬浮桌宠。它直接打包仓库中的月薪喵 GIF 和互动词包，不复制素材文件。

## 已实现

- 首页、桌宠、互动、搭子、我的五栏移动端界面
- 透明动态 GIF 悬浮桌宠、拖动、投掷惯性、边缘反弹和随机走动
- 单击快捷菜单：互动、发送给搭子、换宠、触摸穿透、隐藏桌宠
- 隐藏或穿透后通过常驻通知恢复，后台服务与搭子接收保持运行
- 54 个内置桌宠、随机/手动换宠、最多 3 个自定义 GIF
- 大小、透明度、镜像、性格、互动频率、词包和换宠间隔
- Android Keystore 加密的独立设备身份、5 分钟体验与正式激活
- 搭子昵称、配对码、配对/解除配对、当前 GIF 互发与来访展示
- 开机后恢复桌宠

搭子联机只对正式激活设备开放；体验期开放高级互动与词包，体验结束后基础桌宠、拖动、走动和最多 3 个自定义 GIF 继续免费。Android 设备独立计为一个激活设备，暂不与 PC/macOS 同步本地数据。

Android 不支持 Windows 托盘、全局鼠标追逐和 WPF 窗口模型。小剧场未包含在 Android 版中。提醒需要确定是否申请 Android 精确闹钟特殊权限后再接入。

## 构建

### GitHub Actions

推送 Android 相关文件、创建 Pull Request，或在 GitHub 的 **Actions > Build Android > Run workflow** 中手动运行。非 Pull Request 构建通过后，会使用仓库 Actions Secrets 中的稳定发布密钥签名，并把 `ZhuoDazi-Android-1.1.0.apk` 上传到 `android-v1.1.0` Draft Release。Pull Request 只构建临时调试包，不接触发布密钥。

工作流会执行 Android Lint、APK 编译和 v2 签名验证。仓库当前的 Actions Artifact 存储额度已满，因此和 Windows/macOS 构建一样直接使用 GitHub Release 保存安装包。

v1.0 测试包由 GitHub 临时调试证书签名，不能覆盖升级到稳定签名版。安装 v1.1.0 前需先卸载 v1.0；从 v1.1.0 起后续版本可以直接覆盖升级。

### 本地构建

要求 JDK 17 或更高版本，以及包含 Android SDK Platform 36 和 Build Tools 36 的 Android SDK。

```powershell
cd .\android
gradle :app:assembleDebug
```

调试 APK 输出到 `android\app\build\outputs\apk\debug\app-debug.apk`。安装到已连接且开启 USB 调试的设备：

```powershell
adb install -r .\app\build\outputs\apk\debug\app-debug.apk
```

首次启动后点击“授予悬浮窗权限”，在系统设置中允许“显示在其他应用上层”，再启动桌宠。
