# Windows / macOS 3.5.6 发布记录

发布日期：2026-11-09。用户授权将支付宝、联系作者、作者分享和月薪喵默认素材一起发布，Windows 与 macOS 同步到 3.5.6。

- 官网下载：https://desktoppet.online/download/
- 作者分享：https://desktoppet.online/author-shares/
- 更新介绍：https://desktoppet.online/posts/desktop-3-5-6-update/
- Windows Release：https://github.com/W-CLL/zhuodazi/releases/tag/v3.5.6
- macOS Release：https://github.com/W-CLL/zhuodazi/releases/tag/macos-v3.5.6

## 发布内容

永久激活与桌宠购买统一使用支付宝电脑网页支付。后台保存付款与权益交付状态，保留历史订单；旧微信收款 API 拒绝新订单、收款心跳和通知。

“版本更新”新增“联系作者”，显示后台维护的联系二维码、微信号与复制按钮。微信仅用于联系作者。

新增“作者分享”菜单，Windows、macOS 和官网使用同一公开接口。后台支持分类、网址、标题、介绍、排序和删除，保存后发布；限制 HTTP(S) 外链并拒绝含账号密码的 URL。管理写入要求登录与 CSRF，版本冲突会拒绝覆盖。

内置默认桌宠替换为用户指定目录 `D:\old_cpu\桌宠合集\默认桌宠` 的 20 份月薪喵动画。两端读取随包分发的素材清单，覆盖升级遗留文件不参与默认轮换；保留用户自定义、已购资源和随机切换偏好。

## 构建与校验

实际安装包源提交：`7b25a95163cf1181e36234d78a916b67fe7f5a61`。后续发布记录提交不修改平台代码或素材。

完整候选构建：https://github.com/W-CLL/zhuodazi/actions/runs/42574102601 。Windows 和 macOS 两架构均成功。

| 文件 | 字节数 | SHA-256 |
| --- | ---: | --- |
| ZhuoDazi-Desktop-Pet-3.5.6.exe | 54,174,460 | cacbc1b18a8a7c17a546e29c59985acb4d70112a8ca3d6b67eefa74bb8dd4f91 |
| ZhuoDazi-macOS-3.5.6-arm64.zip | 3,570,548 | 937761db941cb3316486c6b4b74ab201b8ee147c62c89b77dcd6c6b1129bd2c5 |
| ZhuoDazi-macOS-3.5.6-x86_64.zip | 3,645,809 | 6805e53d5d68a82a074b0e0919afa85ea7b87ea98417a7c51f93d1e10bc0e2b7 |

- Windows 编译、功能检查、默认素材覆盖升级与导入资源保留检查通过；CI 安装包和便携包离线启动通过。
- macOS 两个原生 runner 各 83 项测试通过；包架构、ad-hoc 签名、20 份素材与归档校验通过。
- 发布脚本 82 项测试通过，官网 15 项测试和生成检查通过：35 个 HTML、34 条 sitemap URL、8 篇文章。
- 作者分享已通过浏览器分类切换、空状态、外链、安全 URL、窄屏布局和后台编辑保存检查；Windows 新控件完成程序化渲染复核。
- 外网完整下载 Windows、Apple Silicon 和 Intel 三个安装包，文件大小与 SHA-256 均匹配 CI 包；三份自动更新清单 Ed25519 签名验证通过。
- GitHub 安装包资产大小与服务端摘要匹配上述安装包。

macOS 仍使用 ad-hoc 签名，未进行 Developer ID 签名或公证。真实支付宝付款沿用此前用户完成的两笔验证；本次正式环境创建 ¥6.88 订单，验证官方签名、查询、通知拒绝无效签名与订单持久化后关闭，未收取新测试款。

## 部署与备份

正式后端支付入口已改为服务器端口 3100，不再经过本机服务或 SSH 隧道。正式商户密钥位于服务器仓库外的私有目录，激活价格为 688 分；隔离测试数据库、设备和 1 分配置未导入生产。

- 后台代码与完整生产数据备份：`/www/deskpet-backups/desktop-alipay-20261109T063934Z-3.5.6`。
- 版本元数据备份：`/www/deskpet-backups/release-20261109T065058Z-2455133-3.5.6`。
- 官网旧站：`/www/wwwroot/desktoppet.online.previous-7b25a95163cf-20261109065218`。
- 本地正式包及 SHA-256 清单：`D:\code_file\pro_python\pet\outputs\desktop-3.5.6`。
- 构建截图、接口回执、发布脚本：`D:\code_file\pro_python\pet\.release-staging\desktop-3.5.6`。

Android 仍为 1.5.4，发布前后比对保留其版本记录、下载指针及现有站点设置。
