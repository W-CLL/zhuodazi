# Windows / macOS 3.5.8 发布记录

发布日期：2026-11-11（北京时间）。用户确认卸载修复有效，并授权发布内存优化版本。Windows、macOS Apple Silicon 和 Intel 已同步为 3.5.8。

## 面向用户的更新说明

1. 优化内存占用问题，减少了内存占用，更加轻量。
2. 解决了一些其他已知问题。

上述两条说明同步用于 GitHub Release、公开下载信息及签名自动更新清单。

## 发布与验证

- 正式源码与发布标签：`92e2249fc3c247a3dc8a6be2fa7f3419274141a7`。已推送远程 main；没有改动用户其他本地工作树。
- Windows 正式构建：https://github.com/W-CLL/zhuodazi/actions/runs/42978858549 ，成功。
- macOS 构建：https://github.com/W-CLL/zhuodazi/actions/runs/42977640006 ，其中 arm64、x86_64 两个原生作业成功，各 87 项测试通过。该次运行的 Windows 作业曾失败，修复后由上述 Windows 运行完成；不能将整个初次运行写为成功。
- macOS 包源提交为 `e7560d761a701b2d62f95a8a7a6f738451371048`；与正式标签相比，macOS、共享素材和默认桌宠目录完全一致，后续变更仅涉及 Windows 退出进程检查。
- Windows 使用 Inno Setup 6.7.1。卸载生命周期 34 项、进程退出 21 项、真实安装器 7 场景和旧卸载器覆盖升级检查通过。覆盖升级后卸载器为 3.5.8，真实客户端自动退出，主程序删除，完成请求收到确认。
- 修复强制终止的异步退出窗口：Windows 可能先无法查询进程路径，再完成退出；现在通过已经核验路径的同一进程句柄等待退出，避免过早误报失败。不会仅按同名进程终止其他安装。
- GIF 21 文件/1,518 帧像素对照、窗口隐藏/最小化/恢复、列表刷新、日常 UI 和实时服务检查通过；安装版与便携版离线启动检查通过。
- macOS 原生包架构、应用及卸载助手 ad-hoc 签名、默认素材和归档检查通过。未做用户 Mac 上的交互验收，也没有新增 Developer ID 签名或公证。
- 三个正式安装包从外网完整下载，字节数与 SHA-256 均匹配 GitHub 构建产物；三份自动更新清单 Ed25519 签名及更新说明验证通过。
- 官网首页、下载页、作者分享和玩法页 HTTP 检查通过。下载版本使用现有动态接口，此次无需部署官网静态文件。
- Android 保持 1.5.4；导入前后核对保留 Android 发布记录、下载指针和现有站点设置。

| 文件 | 字节数 | SHA-256 |
| --- | ---: | --- |
| ZhuoDazi-Desktop-Pet-3.5.8.exe | 54,183,184 | cd7394a9b97e6ac2975d7524a9aec86485e1e90449a7d980b1074edfcc21d54f |
| ZhuoDazi-Desktop-Pet-3.5.8-portable.zip | 77,027,874 | 9cfa8b1038e666a63cd581d9b0ebc8eb9848afe01ad165da6e0002e6525dca7f |
| ZhuoDazi-macOS-3.5.8-arm64.zip | 3,582,636 | 52185c33e4b0ec3e9e8e97de42d96e30a040b8e61a0573a2876b911ef9718b7c |
| ZhuoDazi-macOS-3.5.8-x86_64.zip | 3,661,399 | 500ed012b79fca049747b49f3e1287984db5d612e2c35868764820eb95959dd7 |

## 地址与备份

- 官网下载：https://desktoppet.online/download/
- Windows Release：https://github.com/W-CLL/zhuodazi/releases/tag/v3.5.8
- macOS Release：https://github.com/W-CLL/zhuodazi/releases/tag/macos-v3.5.8
- 版本元数据备份：`/www/deskpet-backups/release-20261111T073521Z-2510934-3.5.8`。
- 本地正式包、更新说明和 SHA-256：`D:\code_file\pro_python\pet\outputs\desktop-3.5.8`。
- 构建日志、包清单、发布脚本和外网回执：`D:\code_file\pro_python\pet\.release-staging\desktop-3.5.8`。

内存对照的方法和收益范围见 [内存优化补充验收](desktop-3.5.8-memory-followup.md)，卸载历史修复见 [卸载验收](desktop-3.5.8-verification.md)。已安装同版本本地候选的设备可用正式安装包覆盖安装；正式 3.5.7 设备可检查更新。
