# macOS 小铺本地预览与构建

本轮源码支持桌宠小铺、订单记录、部分预览、自动安装与手动启用，以及免费词库/剧本。当前开发主机是 Windows；以下 macOS 原生编译、测试和界面检查需在有 Mac 时执行，不能把静态代码检查当作已经通过原生验收。

在 macOS 13+ 安装 Xcode Command Line Tools 后执行：

```bash
bash macos/scripts/check-shop-build.sh
```

脚本运行 Core 测试与 Release 编译，不启动应用、不上传资源、不发布。原 `macos/scripts/build-app.sh` 仍用于生成本机 `.app` 和 ZIP；其签名是本机 ad-hoc 签名，正式发布另行安排。

如要对独立本地后台调试小铺，准备本机绝对路径 JSON：

```json
{
  "baseUrl": "http://127.0.0.1:18767/",
  "dataDirectory": "/Users/你的用户名/LocalReview/ZhuoDaziShop",
  "accountId": "独立后台的测试账号ID",
  "licenseId": "独立后台的测试授权UUID",
  "installationId": "该测试设备的安装标识",
  "credential": "该测试设备的凭据",
  "signingPublicKeySpki": "独立后台的Ed25519 SPKI公钥Base64"
}
```

启动编译产物时仅为该进程设置 `ZHUODAZI_SHOP_PREVIEW_CONFIG` 为此文件的绝对路径。该模式在正常应用初始化前进入独立小铺窗口；不读取正式 Keychain 授权，不启动正式试用、统计、更新、大厅或设备上报，不写正式设置。网络仅允许配置的 `127.0.0.1` HTTP origin，安装和预览状态保存在独立目录的 `mac-shop-preview` 子目录。无效配置直接启动失败，不回退线上。

独立预览的启用操作只保存测试选择，不会展示完整桌面宠物；正式客户端中的小铺启用通过统一桌宠运行池执行。测试设备凭据只保存在本机配置中，不要提交仓库或贴进日志。Windows 的旧本地路径不能直接作为 Mac 配置；Mac 上需要可访问的独立后台与配套测试身份。正式数据与样例不由脚本迁移。

需要原生验收的关键场景：两栏5:5和横向滚动；个人转账/收款码显示与到期收起；待付款3秒、其它30秒订单同步；付款后自动安装但不启用；切换/隐藏设置不停止订单同步；免费资源整组安装后手动启用；激活门槛；同账号重装与失效授权回退；恶意 ZIP、错误签名/哈希和断网时不安装。
