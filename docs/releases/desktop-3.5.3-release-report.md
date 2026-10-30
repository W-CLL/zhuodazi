# 桌搭子 3.5.3 发布记录

发布日期：2026-10-30（北京时间 / 新加坡时间）。Windows、macOS 和配套后端均已上线。

## 版本与下载

| 平台 | 正式版本 | 产物 |
| --- | --- | --- |
| Windows x64 | 3.5.3 | EXE 安装器、便携 ZIP |
| macOS Apple Silicon | 3.5.3 | arm64 App ZIP |
| macOS Intel | 3.5.3 | x86_64 App ZIP |
| 管理后台 / API | 1.1.0 | 桌宠、内容包、订单、卸载与搭子状态接口 |
| Android | 1.5.4 / 28 | 保留原版本及原下载记录 |

- 官网下载：https://desktoppet.online/download/
- Windows：https://in.desktoppet.online/downloads/latest/windows/x64
- Mac Apple Silicon：https://in.desktoppet.online/downloads/latest/macos/arm64
- Mac Intel：https://in.desktoppet.online/downloads/latest/macos/x86_64
- 管理后台：https://in.desktoppet.online/admin
- GitHub Windows：https://github.com/W-CLL/zhuodazi/releases/tag/v3.5.3
- GitHub macOS：https://github.com/W-CLL/zhuodazi/releases/tag/macos-v3.5.3

官网从公开下载 API 读取版本；已确认三个桌面下载入口与 API 对应 3.5.3，不需要重新部署未改动的官网页面。客户端自动更新已指向 3.5.3。

## 本轮功能

| 功能 | 发布行为 |
| --- | --- |
| 桌宠小铺 | 独立菜单；图鉴与预览/付款左右 5:5；每个资源包仅预览指定的部分内容；小窝隐藏目录路径 |
| 资源管理 | 后台上传桌宠，配置免费/收费、价格、默认款、标记与上下架；正式激活后才能领取、购买、下载并使用小铺桌宠；体验期可浏览 |
| 付款与订单 | 下单生成五位备注码（三位数字、两位英文，不区分大小写），15 分钟有效；支持好友转账消息匹配；用户订单弹窗保留付款与安装记录 |
| 后台订单 | 显示购买者设备号和桌搭子昵称；支持搜索及人工确认，需要核对说明和确认操作，保留审计；重复确认不重复发放权益 |
| 安装与启用 | 付款确认后自动下载、安装，用户自己启用；免费小剧场和互动词库同样自动安装、手动启用 |
| 内容选择 | 互动词库可批量导入、多套同时使用；互动词库最多启用 10 套、小剧场最多 10 个；本地目录与小铺桌宠合计最多启用 3 个来源 |
| 尺寸与对话 | 桌宠大小 60–300；私人搭子发送间隔 1 秒、普通大厅 15 秒；大厅文字显示更久，小剧场按顺序对白并延长阅读时间 |
| 离线投递 | 发送前检查私人搭子状态；离线或暂时无法确认状态时，询问是否继续留言/发送；默认取消，取消不发送；在线直接发送 |
| 卸载反馈 | Windows 安装器卸载入口、macOS“卸载与反馈”入口提供可跳过的理由反馈；记录受控卸载完成及重新安装状态 |
| macOS 对齐 | 小铺、订单、免费内容、内容多选、尺寸、搭子离线提示、卸载助手均纳入双架构正式包 |

正式商品由用户自行上传、定价、标记和上下架。上线核验时桌宠商品数为 0；没有导入本地演示商品、订单、支付记录或资源包，也没有清理生产已有资源。

## 正式支付监听

独立目录：`D:\code_file\pro_python\zhuodazi-payments`。原 wxlocal 项目保留。正式链路为本机微信消息读取 → HTTPS 付款接口 → 后台核对订单并授权 → 客户端自动安装。

当前正式监听在后台运行。双击目录中的 `payment-status.bat` 可持续查看连接状态，关闭查看窗口不会停止后台；电脑重启或监听停止后，双击 `start-production-payments.bat` 启动正式监听。没有新增开机启动项。

- 正式目标：`https://in.desktoppet.online`，启用独立 `config/production.json` 与 `state/production/`。
- 收款方式：好友转账；昵称“小王”，微信号 `wcl_lcw627`。
- 实际来源读取、历史基线、HTTPS 心跳均已通过；上线核验时待上报 0、需处理 0，后台 `enabled / listenerOnline / available` 均为 true。
- 监听请求使用产品标识 `ZhuoDazi-PaymentMonitor/3.5.3`；线上入口拦截 Python 默认标识的问题已修复，无须修改入口规则。
- 现有本地监听已正常停止，其配置和队列保留；正式队列单独建立，没有重放本地测试付款或历史消息。
- 后台启用收款通过既有设置服务校验并写入审计；配置密钥不进入仓库、文档或终端输出。

此电脑需要保持开机、不睡眠，收款微信保持登录，监听持续运行。心跳超过 60 秒后会过期，新付费下单会显示暂不可收款。收款识别按已确认的产品规则处理好友转账发起消息，不要求手动点击收款。

正式配置由 `/www/deskpet-data/ecosystem.payments.config.cjs` 叠加到原 PM2 配置；原 `ecosystem.config.cjs` 内容保留。后续重载使用该 wrapper，避免丢失两个收款桥环境变量。PM2 状态已保存。

## 验证与边界

- 桌面源提交：`20aa4b341f0a16487d1ddc3381ee8e17edf47a4a`。
- 桌面 CI：https://github.com/W-CLL/zhuodazi/actions/runs/40989277153，全流程成功。
- Windows：旧功能及新功能检查通过；Inno 安装器编译、实际安装、安装后与便携目录的隔离启动检查通过。
- macOS：两个原生 runner 各 50 项 XCTest、15 条心跳断言通过；Release 编译、主程序与卸载助手架构/权限/签名、ZIP 解包复核通过。
- 四份最终产物大小及 SHA256 与 GitHub 资产完全一致；Windows 版本资源和 x64 可执行文件、两种 Mac Mach-O 架构以及 20 份默认素材校验通过。
- 后端首发源提交：`3f4bc2261295f4845c84465377eda2f22ee27d89`；CI https://github.com/W-CLL/deskpet/actions/runs/40989488253 成功；224 项业务回归、99 个 JavaScript 文件语法检查通过。
- 后台登录补修提交：`cc509e708be317c4a5602d6105d916c3f6225412`；CI https://github.com/W-CLL/deskpet/actions/runs/40993391203 成功；227 项回归、99 个 JavaScript 文件语法检查及重复数据库迁移检查通过。生产运行依赖保持一致，无须重启服务。
- 生产三个桌面目标的 Ed25519 更新清单签名与 OSS 下载已核验；外网下载检查通过；Android 原发布记录和指针保持不变。
- 原线上 OSS 下载、公众号授权扩展已并入发布代码并做保留回归。

上线后补修了管理后台登录按钮无响应：旧页面生成器覆盖了完整设置页，导致脚本访问缺失的 `activationQrFile` 时中断，登录事件未绑定。现已恢复原有完整页面及 270 个原有控件，保留新增小铺、订单、卸载与资源表单；相关资产已更新缓存版本。管理员密码与上线前备份一致。线上真实浏览器已验证页面启动无异常、登录事件触发及错误反馈渲染，验证请求由浏览器隔离拦截，没有使用或重置真实密码。

`assemble-admin.js` 已改为只读校验正式 HTML，确认 276 个 ID 和 23 个路由，阻止旧模板再次覆盖页面；新增回归实际运行全部页面工厂与登录脚本，缺失设置控件的反例能够复现原故障。独立临时数据库和一次性账号完成真实登录、退出及 23 个导航入口、12 个编辑/上传/订单抽屉检查，浏览器异常和失败接口均为 0，未写入生产业务数据。最终静态文件同步后再次验证线上登录事件正常。

macOS 延续 ad-hoc 签名，没有 Developer ID 签名或 Apple 公证；云端原生构建验证不能代替实体 Mac 的完整交互验收。本轮没有代用户发送真实转账，也没有执行用户机器上的真实卸载。Windows 已安装的旧版程序未被直接替换，可通过正式下载或应用内更新升级。

## 备份与回退

- 后端完整代码及数据备份：`/www/deskpet-backups/backend-1.1.0-20261030T021111Z`。
- 本次发布元数据备份：`/www/deskpet-backups/release-20261030T021628Z-2199054-3.5.3`。
- 后台登录修复同步前的页面、校验脚本及依赖元数据备份：`/www/deskpet-backups/admin-login-cc509e7-20261030T024517Z`。
- 本地包、哈希清单、验证记录和发布脚本：`D:\code_file\pro_python\pet\.release-staging\desktop-3.5.3`。

搭子库新增 `companion/8` 迁移。代码回退需要使用已验证的兼容版 `companion-store.js`，不能直接启动不认识此迁移的旧代码。回退验证覆盖新代码写入 → 旧业务兼容运行并写入 → 再由新代码打开，数据保留。上线后不使用旧数据库快照覆盖用户新写入。
