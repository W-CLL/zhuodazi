# 桌搭子 Android 版

Android 版使用原生 Java 实现，通过系统“显示在其他应用上层”权限提供悬浮桌宠。它直接打包仓库中的月薪喵 GIF 和互动词包，不复制素材文件。

## 已实现

- 透明动态 GIF 悬浮桌宠和常驻通知控制
- 拖动、投掷惯性、屏幕边缘反弹、随机走动
- 单击互动气泡、双击打开设置
- 54 个内置桌宠、随机/手动换宠、自定义 GIF 导入
- 大小、透明度、镜像、性格、互动频率和换宠间隔
- 仓库内置互动词包
- 开机后恢复桌宠

Android 不支持 Windows 托盘、全局鼠标追逐和 WPF 窗口模型。PC 端授权、搭子联机、提醒、小剧场与在线内容接口尚未接入 Android 版。

## 构建

### GitHub Actions

推送 Android 相关文件到 `main`、创建 Pull Request，或在 GitHub 的 **Actions > Build Android > Run workflow** 中手动运行。构建通过后，在该次运行页面底部的 **Artifacts** 下载 `ZhuoDazi-Android-1.0.0-debug`，解压后即可得到 APK。

工作流会执行 Android Lint 和 APK 编译，定义位于 `.github/workflows/build-android.yml`。

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
