# Windows / macOS 3.5.7 发布记录

发布日期：2026-11-09。桌宠小铺下载方式可在后台切换，当前生产环境已启用 OSS。

## 下载方式

后台入口为“桌宠小铺 → 下载设置”，可选择“OSS 加速下载”或“服务器下载”。保存后对新的下载授权立即生效，设置持久化到数据库。正在进行的下载继续原方式。

Windows 和 macOS 需先更新到 3.5.7；之后切换这两种方式无需再次发布客户端。3.5.6 及更早客户端继续使用服务器下载。

下载前检查激活及领取/购买权益。OSS 地址位于签名授权内，最长有效期 10 分钟。客户端仅接受指定 HTTPS 下载域名和对应商品、版本、摘要的对象路径，不向 OSS 发送设备凭据。文件大小、SHA-256、包清单与使用授权校验继续执行。

已同步全部 12 个桌宠资源包版本，共 530,967,723 字节。OSS 配置及密钥保留在服务器。同步时使用的临时上传授权文件已删除。

## 发布与验证

- 客户端构建提交：`7fb15986892ffe58acda35dd89fdd7326323201c`。
- 后端实现提交：`3eee25e0597476c3aa8a2bb0de101272f6ccedf7`。
- 候选构建：https://github.com/W-CLL/zhuodazi/actions/runs/42579438421 。Windows 和 macOS 两架构构建及测试通过，macOS 每架构 84 项测试通过。
- 后端 CI：https://github.com/W-CLL/deskpet/actions/runs/42583191386 ，通过。
- 发布检查：https://github.com/W-CLL/zhuodazi/actions/runs/42583219075 ，通过并复用已验证的正式资产。
- 官网 CI：https://github.com/W-CLL/zhuodazi/actions/runs/42583219014 ，通过。
- 后台浏览器检查通过：OSS 切换到服务器、再切回 OSS，状态正确保存。
- 生产服务方法验证两种模式保存成功；服务重启后当前 OSS 设置仍保留。10 个部署文件经换行格式归一化后与仓库源码一致。
- 12 个 OSS 对象的文件大小及摘要元数据验证通过；最小和最大资源包通过正式下载域名完整下载，SHA-256 匹配。最大包 163,964,156 字节，服务器验证下载约 12.6 秒；该时间仅代表本次验证环境。
- 外网完整下载三份正式安装包，大小、SHA-256 与 CI 产物一致；自动更新清单 Ed25519 签名全部通过。
- 官网下载页、下载确认弹窗、作者分享、支付宝激活入口与月薪喵内容通过浏览器检查，无脚本错误。
- 14 项客户端边缘接口检查通过；下载设置接口拒绝未登录访问。

| 文件 | 字节数 | SHA-256 |
| --- | ---: | --- |
| ZhuoDazi-Desktop-Pet-3.5.7.exe | 54,174,131 | 658321d08b99db7ba75d0f67e175229836773307f35d4eab5d8f607da1dfdb86 |
| ZhuoDazi-Desktop-Pet-3.5.7-portable.zip | 77,017,772 | 19505673513fd100ba5ff85eed96f4142b86b00f36987fd4358dd1baa9af9cd2 |
| ZhuoDazi-macOS-3.5.7-arm64.zip | 3,572,348 | b901b9c62c6561ed6528cde21feb61462cf2337ba7b228561c35e645f83c07f5 |
| ZhuoDazi-macOS-3.5.7-x86_64.zip | 3,648,259 | 06ad0fe8da4a58cd5774d2b6b0db0c3c16e2d083ba342fae4e9c38251d728fbb |

## 地址与备份

- 官网下载：https://desktoppet.online/download/
- 更新介绍：https://desktoppet.online/posts/desktop-3-5-7-update/
- Windows Release：https://github.com/W-CLL/zhuodazi/releases/tag/v3.5.7
- macOS Release：https://github.com/W-CLL/zhuodazi/releases/tag/macos-v3.5.7
- 后端与数据库备份：`/www/deskpet-backups/oss-market-20261109T073651Z`。
- 版本元数据备份：`/www/deskpet-backups/release-20261109T080121Z-2458796-3.5.7`。
- 官网旧站：`/www/wwwroot/desktoppet.online.previous-7fb15986892f-20261109080233`。
- 本地正式包：`D:\code_file\pro_python\pet\outputs\desktop-3.5.7`。
- 验证回执与部署脚本：`D:\code_file\pro_python\pet\.release-staging\desktop-3.5.7`。

本次仅更新桌面端及下载服务，未进行新的真实付款。Android 保持 1.5.4。
