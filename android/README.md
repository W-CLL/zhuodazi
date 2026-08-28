# 桌搭子 Android 版

Android 版使用原生 Java 实现，通过系统“显示在其他应用上层”权限提供悬浮桌宠。它直接打包仓库中的月薪喵 GIF 和互动词包，不复制素材文件。

## 已实现

- 首页、桌宠、互动、搭子、我的五栏移动端界面
- 透明动态 GIF 悬浮桌宠、拖动、投掷惯性、边缘反弹和随机走动；窗口按屏幕收紧，旋转后会重新贴边
- 单击快捷菜单：互动、换一只、发给搭子、女友/好友/搭子来访、触摸穿透、隐藏桌宠；点菜单按钮时不会被拖动抢走，小屏会先放大窗口再显示
- 隐藏或穿透后通过常驻通知恢复，后台服务与搭子接收保持运行
- 10 个内置桌宠、随机/手动换宠、最多 3 个自定义 GIF
- 图鉴目录：体验或正式激活后可用系统文件选择器绑定最多 3 个 GIF 文件夹，递归扫描最多 500 张并作为轮换池
- 大小、透明度、镜像、性格、互动频率、词包和换宠间隔
- Android Keystore 加密的独立设备身份、7 天体验与正式激活
- 搭子昵称、配对码、配对/解除配对、当前 GIF 互发与来访展示
- 开机后恢复桌宠
- 激活页引导：微信二维码、复制微信号、官网和闲鱼跳转
- 小剧场：内置三场对白、自动/立即上演、导入最多 10 个 JSON 剧本
- 提醒：最多 20 条，支持每天重复；到点后桌宠说话并弹出通知
- 检查更新：在「我的」对照桌面端同一份签名清单；可下载 APK 并调用系统安装页覆盖安装

搭子联机只对正式激活设备开放；体验期开放高级互动、词包、小剧场、提醒、外部图鉴目录，以及点一下模仿女友、好友、搭子来访（表情走线上随机库）。安卓必须先开悬浮窗，桌宠才会出现；授权后回到应用会自动启动桌宠。体验期首页主按钮是女友来访，不把发给搭子放第一位。体验结束后基础桌宠、拖动、走动和最多 3 个自定义 GIF 继续免费。一组购买码最多填两台设备：电脑激活后，手机再填同一组码进入同一个账号，搭子码不用换。本地桌宠、GIF、词包和提醒仍按设备，不与 PC/macOS 同步。绑定目录走系统「打开文件夹」授权，卸载应用后权限会失效，需要重新选择。

Android 不支持 Windows 托盘、全局鼠标追逐和 WPF 窗口模型。提醒按约 20 秒轮询触发，不申请精确闹钟权限。在线更新走 `GET /api/update/latest?platform=android&architecture=` 当前 ABI；公开渠道无需激活即可检查，非公开版本需要体验或正式激活。安装前系统会要求允许“安装未知应用”。调试包带 `.debug` 后缀，不能覆盖正式签名包。

## 构建

### GitHub Actions

推送 Android 相关文件、创建 Pull Request，或在 GitHub 的 **Actions > Build Android > Run workflow** 中手动运行。非 Pull Request 构建通过后，会使用仓库 Actions Secrets 中的稳定发布密钥签名，并把 `ZhuoDazi-Android-1.3.6.apk` 上传到 `android-v1.3.6` Draft Release。Pull Request 只构建临时调试包，不接触发布密钥。

工作流会执行 Android Lint、APK 编译和 v2 签名验证。仓库当前的 Actions Artifact 存储额度已满，因此和 Windows/macOS 构建一样直接使用 GitHub Release 保存安装包。

v1.0 测试包由 GitHub 临时调试证书签名，不能覆盖升级到稳定签名版。安装 v1.1.0 前需先卸载 v1.0；从 v1.1.0 起后续版本可以直接覆盖升级。当前试用包是 v1.3.6（versionCode 10）。

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

首次启动后按首页提示打开悬浮窗，回到应用后启动桌宠。点桌宠打开菜单，或从首页把当前形象发给搭子。
