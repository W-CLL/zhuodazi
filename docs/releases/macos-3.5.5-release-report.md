# macOS 3.5.5 发布记录

发布日期：2026-11-06（北京时间）。用户完成候选包测试并确认后，将相同安装包同步到 GitHub、正式下载、客户端自动更新和官网。

## 正式入口

- 官网下载：https://desktoppet.online/download/
- Apple Silicon：https://in.desktoppet.online/downloads/latest/macos/arm64
- Intel：https://in.desktoppet.online/downloads/latest/macos/x86_64
- GitHub：https://github.com/W-CLL/zhuodazi/releases/tag/macos-v3.5.5
- 官网更新说明：https://desktoppet.online/posts/macos-3-5-5-update/
- 桌宠玩法：https://desktoppet.online/gameplay/

macOS 两个架构的公开下载版本与自动更新版本均为 3.5.5。Windows 为 3.5.4，Android 两架构为 1.5.4；发布操作仅新增 macOS 版本，其他平台的记录、指针和站点设置均已比对保留。

## 安装包与源码

| 架构 | 文件 | 字节数 | SHA-256 |
| --- | --- | ---: | --- |
| arm64 | ZhuoDazi-macOS-3.5.5-arm64.zip | 5,146,067 | f1c2d2af8e027d67bc0f7204f4facda05c0e351c9f0eae0b87d507d21a55d726 |
| x86_64 | ZhuoDazi-macOS-3.5.5-x86_64.zip | 5,218,787 | 833d02814b23efdde1b38bcefe9a1e75de81b2aedec41d842e138a9a89533c37 |

- 用户验收包源提交及 GitHub 标签目标：`1e0e9c58b28d1da0537fa55f4e21689ca8959283`。
- 双架构原生测试与候选构建：https://github.com/W-CLL/zhuodazi/actions/runs/42173523574。
- 官网与主分支同步提交：`dbba0b4daae05b268b7a18de2655dda67c7e63a0`，与候选提交相比，macOS、共享默认素材和图标源码没有变化。
- 主分支发布检查：https://github.com/W-CLL/zhuodazi/actions/runs/42176317859。
- 官网检查：https://github.com/W-CLL/zhuodazi/actions/runs/42176317773。

正式发布直接使用已验收的两份候选 ZIP。GitHub 资产大小和摘要、正式 OSS 下载大小和摘要均与上表一致。

## 本轮变化

- 设置改为左侧菜单与右侧内容，主要菜单名称和顺序对齐 Windows。
- 桌宠大厅直接嵌入设置，菜单栏入口定位到同一页面；切页、关闭或最小化停止刷新，返回后恢复。
- 小铺左右等宽，预览与详情滚动，购买、免费领取安装、安装和启用操作固定在详情底部。
- 下载改为后台分块接收，素材包使用单独的超时策略；整包校验和安装在后台执行，保留来源、签名、大小、哈希和 ZIP 验证。

## 已完成验证

- 两个原生 runner 各 81 项测试，0 失败；Release、主程序和卸载助手的架构与 ad-hoc 签名、20 份默认素材、ZIP 解包检查通过。
- 780×580、1040×730、1400×900 小铺布局及 1100×760 设置大厅原生截图已复核；购买、免费安装、安装状态、付款状态和资源启用操作完整可见。
- 生产更新清单使用客户端公钥验证 Ed25519 签名通过，版本、架构、大小、SHA-256 和更新说明一致。
- 两架构的公开 latest 地址与更新清单下载地址均完成全量 GET，共四次下载，返回 HTTP 200，文件 SHA-256 与验收包一致。
- 官网构建检查通过：15 项测试、33 个 HTML、32 条 sitemap URL、7 篇文章和 1,290 处内部引用。官网新增更新说明，并将大厅文案同步为在设置中打开；既有玩法十图和下载确认弹窗保留。
- 真实 Chrome 首页和下载页均显示 macOS 3.5.5，Apple Silicon / Intel 大小为 4.9 / 5.0 MB；下载确认提示正常，取消后留在原页。八个页面与文本入口均 HTTP 200，十张玩法图片的 SHA-256 与原图一致。
- 发布后服务健康检查、公开下载接口及三个桌面目标的外部下载检查通过。

应用继续使用 ad-hoc 签名。实体 Mac 候选包验收由用户完成，本轮没有新增支付、领取商品、发送来访或执行卸载等业务操作。

## 备份与追溯

- 正式版本元数据备份：`/www/deskpet-backups/release-20261106T064106Z-2381156-3.5.5`。
- 官网部署：`dbba0b4daae0-20261106064441`。
- 官网旧站：`/www/wwwroot/desktoppet.online.previous-dbba0b4daae0-20261106064441`。
- 本地安装包与校验回执：`D:\code_file\pro_python\pet\outputs\macos-3.5.5\tested`。
- 发布脚本、manifest 和外部验证记录：`D:\code_file\pro_python\pet\.release-staging\macos-3.5.5`。

发布失败回退仅撤销本轮 macOS 3.5.5 的版本记录和仍指向它的指针，不覆盖用户数据库、站点设置或其他版本记录。
