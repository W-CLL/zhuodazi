# 内容互动实时取题（2026-10-27）

## 行为约定

Windows、macOS、Android 的内容互动和手动答题在触发时请求现有 `POST /api/content/batch`，每次固定 `limit: 1`。普通内容允许笑话、数学、趣味知识、脑筋急转弯、贴士和关心；答题入口仅允许数学、趣味知识和脑筋急转弯。

客户端不再提前拉取题池，也不从旧缓存或上次返回的题目中取题。升级时丢弃旧题目缓存，保留已展示内容 ID、待上传事件、互动偏好和日常记录。题目仍经过现有 Ed25519 签名及 SHA-256 校验。后台接口兼容此次变更，无需修改服务端。

服务端按请求中的类型顺序选题，因此客户端随机排列完整允许类型，并把上次展示的类型放到最后。这样既保留类别变化，也不会因为随机选中的单一类别没有题而遗漏其他类别。

只有题目实际显示后才记录 `content_shown`；取到但被取消、隐藏或账号变化打断的题目不计为已展示。请求返回时重新检查当前互动是否有效，迟到结果不会重新弹出。后台设置同步和事件上传继续保留，不再负责取题。

手动取题失败提示联网重试，空库提示暂时没有新内容；自动取题失败安静跳过，不因网络失败计入连续未回答次数。有旧缓存时也不会离线出题。心情、上下班和本地日常记录保持原有独立流程。

后台修改会影响后续取题请求；已经打开的题目保持本轮内容，答题过程中不替换答案。

## 验证入口

- Windows：`dotnet build windows/ZhuoDazi/ZhuoDazi.csproj -c Release`。
- Windows 迁移与既有回归：`dotnet run --project windows/tests/GifLibraryService.Checks/GifLibraryService.Checks.csproj -c Release`。
- Windows 真实窗口与签名模拟 HTTP：构建 `windows/tests/ZhuoDazi.UiPreview/ZhuoDazi.UiPreview.csproj`，使用 `--daily-smoke <独立输出目录>` 运行。所有测试网络由内存处理器拦截，不使用生产数据。
- Android：在 `android/` 执行 `gradle :app:testDebugUnitTest :app:lintDebug --console=plain`。
- macOS：在 Mac 的 `macos/` 执行 `swift test` 和 `swift build`。Windows 上的 Swift 语法解析不能替代这些检查。

覆盖范围包括单次请求数量、题型过滤、旧缓存迁移、去重历史、空库、签名错误、断网、取消后的迟到结果，以及答题记录。

## 本次验证结果

| 检查 | 结果 |
| --- | --- |
| Windows 主程序及 UiPreview Release 构建 | 通过，0 警告、0 错误 |
| Windows GifLibraryService.Checks | 通过，含旧题池迁移及既有回归 |
| Windows 真实 WPF 日常流程 | 111 条断言通过，其中 23 条覆盖实时内容服务；实际题目和答案卡片截图复核通过 |
| Android 单元及 Robolectric 测试 | 全量 121 项通过，其中生命周期测试 52 项 |
| Android lintDebug | 通过，0 错误；现有警告保留 |
| macOS 修改的 4 个 Swift 文件 | Tree-sitter Swift 语法解析通过；当前 Windows 环境未运行 Swift 编译、AppKit 或 XCTest |
| 差异格式检查 | `git diff --check` 通过 |

Windows 和 Android 均验证后台配置同步阻塞时仍可单独取题。Windows 验证产物保存在上级工作区 `.local-review/20261027-realtime-windows-checks-final/`。此次没有使用正式账号向线上取题，也未进行 Android 真机验证。

## 发布边界

此修改针对客户端源码。需要重新构建并发布各端新版后，已安装用户才能获得实时取题行为；旧版仍按旧缓存逻辑运行。此记录本身不代表已经发布或替换正在运行的客户端。
