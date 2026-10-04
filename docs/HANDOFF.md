# 精简交接记录

更新：2026-10-04。当前任务以本文件和代码为依据；历史聊天只提供背景。不要重新全量加载两份聊天附件，按具体问题检索相关片段即可。

## v0.4.2 发布准备（2026-10-04）

- 用户明确授权 GitHub 上传与 VPS 部署。版本元数据统一为 0.4.2、SW v11，不更改任务/绑定/配置/加密逻辑。
- 正式全量验证 Go、PWA 15、.NET 81 通过，Windows 构建零警告/错误；生产 JS 的 demo getter 固定 false；原字体/图标/许可证随正式单文件发布目录一起打包。
- 安装包 artifacts/BetterGI.Remote.Setup.0.4.2.exe，68710148 字节，SHA-256 03b720911e4f5c1e3b3fa04da3f79ae0e652af4ab2dbb61ac4fb71b62e4f3d7d.
- 本地未安装新包；GitHub/VPS 正在发布，最终远端结果需要后续核验后记录。

## 滚动性能优化断点（2026-10-04）

- 用户反馈拖动滚动条卡顿。本轮仅修改 BufferedScrollPanel.cs、SurfacePanel.cs，移动端/后端不改，视觉不改。
- 控制测试复现：通知设置展开，滚动范围 662，8 个输入字段，180 模拟 ThumbTrack 步（60 预热+120 计时），基线平均 39.28ms、P95 77.19ms、重绘 27207 次，布局 0 次。
- 删除滚动事件 Invalidate(true)，保留 WinForms 原生暴露区刷新；SurfacePanel 使用不透明背景，自绘圆角外侧底色，消除透明背景的递归重绘。没有截图缓存，保持真实输入与光标交互。
- 首次平均 10.36ms/P95 17.91ms；复测平均 8.76ms/P95 14.38ms、重绘 5436 次。处理耗时不是显示 FPS。主窗口截图与优化前像素差均值 0；81 .NET 测试通过、Windows 构建零警告/错误，字体/输入居中/确认结果检查通过。
- 对比与说明 artifacts/scroll-performance-20261004/。确切新隔离预览 `.cache/ui-redesign-20261004/scroll-after-build/Release/net8.0-windows/BetterGI.RemoteLite.UiPreview.exe`。
- 48 后端与 31 移动文件哈希未变；未覆盖正式安装、改真实配置、启动游戏、提交/推送或部署。

## 桌面细化断点（2026-10-04，手机已获用户认可）

- 本轮仅桌面：参考 BetterGI 本地上游 97c90aa 的 MiSans-Regular/TextThemeFontFamily、WPF UI SymbolIcon 和轻量边界。没有直接复制其应用字体或后端。
- 功能图标替换为微软 Fluent Regular 官方字形子集（16 个，仅 4480 字节，MIT 许可证随包）；此前变形的 Game/Refresh 不再使用手工曲线。保留已认可的电脑+手机标识。
- 桌面正文/按钮/侧栏/字段标签使用 400 常规字重，标题字重从 600 降到 500。移动端原字体、CSS、TypeScript、资源文件均未改变；桌面边框改为低对比 0.8px，禁用态不再深色描边。
- 复核 48 冻结后端源文件和所有 web/src、web/public、web/index.html 哈希均未变；.NET 81 测试通过、Windows 构建零警告错误；输入居中和默认确认结果检查通过。
- 截图/图标图集与校验清单 `artifacts/desktop-ui-refine-20261004/`。新隔离演示可执行文件 `.cache/ui-redesign-20261004/harness-refine-build/Release/net8.0-windows/BetterGI.RemoteLite.UiPreview.exe`。
- 注意：dotnet run 在自定义 BaseOutputPath 下可能仍执行旧默认 RunCommand。以后必须先 build 再直接调用确切输出 exe，不能只凭构建成功认为运行了新 UI。
- 没有覆盖正式安装、停止正式 Agent、执行游戏、改真实 BetterGI 配置、推送或部署；旧的本地更新问题仍另行闭环。

## UI 优化接手断点（2026-10-04）

- 用户明确要求全浅色、参考 BetterGI 0.65.0 面板、移除应用原生标题框；拒绝墨绿色、默认中文字体、常驻解释段落、含义不明的波形标识和不齐的控件。方向合同 `DESIGN.md`，不要恢复被拒绝的版本。
- 已落盘：StudioForm 客户区标题栏/拖动/缩放/最小化/最大化/关闭，AppDialog、绑定和更新窗统一；Remote UI Sans（Noto Sans SC 改名 UI 子集）400/600 随包私有加载，网页同字体+Manrope；输入框使用 PreferredHeight 居中；解释移到按需帮助；标识改为电脑+手机；手机滑动导航与路由动效遵守减少动态效果。
- 48 份冻结后端/加密/存储/协议源文件哈希未变；33 个既有前端业务方法原样保留。AgentApplicationContext/Program 仅将 MessageBox 替换为 AppDialog，未改更新、任务和绑定分支。UI 版本仍是开发候选，不是新发布版本。
- 最终回归 .NET 81、PWA 15、Go 通过，Windows 构建 0 警告/错误；原生字体加载、输入居中、YesNo 默认结果验证通过；320/390/768/1440px 无横向溢出/JS 异常，字体加载、草稿、小数、键盘排序、演示启动/停止和 reduced-motion 检查通过。
- 截图 `artifacts/ui-preview-20261004/`；隔离桌面演示 `.cache/ui-redesign-20261004/harness/UiPreview.csproj`（不加载 AgentRuntime，真实保存已禁用）；网页演示本地端口 4186 的 `?demo`，仅 DEV 模式。不要用旧 Agent 测试输出替代这个隔离演示。
- npm ci 的 EPERM 来自本轮自己运行的 Vite/esbuild 占用；已确认并停止自建预览后恢复依赖。运行预览时不要再次 npm ci；可直接 npm run verify。
- 已有 HyperFrames 空项目 `.cache/ui-redesign-20261004/video`，尚未编写/渲染介绍视频，不能声称已交付 MP4。先接受/调整当前 UI，避免为被拒绝的视觉反复制作视频。
- 未覆盖正式安装、未改真实 BetterGI 配置、未执行游戏、未提交/推送/再次部署。正式客户端的 0.3.5 更新修复待正常退出旧进程后另行闭环。

## 当前待处理：本机旧版更新失败（2026-10-04）

- 用户截图报错为文件占用异常；已在临时目录复现旧版行为：校验流未关闭时 `File.Move` 失败，关闭后成功。v0.4.1 已在作用域结束后关闭下载和校验流，修复代码无需重复改写。
- 通过管理员只读进程核验确认两个运行中的 Agent 都是 0.3.5：正式安装路径 `G:\bgi_remote\BetterGI Remote\BetterGI.RemoteLite.Agent.exe`；旧测试路径 `G:\Projects\bettergi-remote-lite\agent\src\BetterGI.RemoteLite.Agent\bin\Release\net8.0-windows\BetterGI.RemoteLite.Agent.exe`。源码版本/隔离构建更新不能等同这些旧进程已升级。
- 本次重新读取公网更新清单为 0.4.1，并通过 v0.4.1 的真实下载器完成 66409287 字节下载、SHA-256 校验、移动与独占打开；从当前版本 0.4.1 检查该清单返回无更新。SHA-256 与下方发布记录一致。验证下载在 `.cache/update-repair-20261004/Downloads/v0.4.1/`。
- 已尝试在原安装目录安装校验过的 0.4.1 补丁，明确禁用强制关闭和自动重启。Inno RestartManager 无法正常关闭旧 Agent，安装以退出码 5 中止并回滚；安装日志 `.cache/update-repair-20261004/install-0.4.1.log`。未因此宣称本机修复完成。
- 原设置文件哈希在回滚后未变化；没有 BetterGI/原神进程，没有启动游戏、修改真实 BetterGI 配置或进行新的发布/部署。未强杀任何客户端。
- 桌面自动化能读取错误框树，但截图没有显示目标窗口且动作未生效，已停止尝试，不盲目点击坐标。已请用户正常退出正式和旧测试客户端，等待用户确认后再安装并核验版本、绑定和检查更新。
- 后续不要重新启动旧测试输出。若要继续本地交互测试，需先在退出后重建该输出，或明确使用已验证的 v0.4.1 隔离构建路径，并保留原测试 profile。

## 当前断点：2026-10-04 新会话接手核验

- 实际开发仓库为 `G:\Projects\bettergi-remote-lite`，不是聊天默认目录 `C:\Users\16383\Documents\New project 3` 下的同名目录；后者属于尚无提交的另一个父仓库。
- 接手时工作区干净，分支 `main`，HEAD `1b2aab9`；本地 `origin/main` 跟踪引用一致，未重新 fetch 或核验远端状态。发布代码提交 `6901d73`，本地标签 `v0.4.1` 指向该提交。
- 代码版本：Windows Agent 与 PWA 均为 `0.4.1`，Service Worker 缓存为 `v10`。GitHub/VPS 已发布的结论来自下方 2026-10-03 的核验记录；本次未重新访问生产服务，不把历史健康检查当作当前在线保证。
- 本次重新执行 `scripts/verify.ps1`：Go 测试通过；PWA 12 项测试、类型检查和生产构建通过；.NET 81 项测试通过；Windows Release 构建 0 警告、0 错误。日志 `.cache/takeover-verify-20261004.log`，隔离构建输出 `.cache/takeover-build-20261004`；缓存、依赖和输出均在 G 盘项目内。
- 本机正式安装仍是 `0.3.5`：卸载注册表和 `G:\bgi_remote\BetterGI Remote\BetterGI.RemoteLite.Agent.exe` 的版本信息均确认。发现两个 Agent 进程，但未能读取其路径，未确认来源或版本，也未退出它们。
- `artifacts/BetterGI.Remote.Setup.0.4.1.exe` 存在，大小 66409287 字节，本次 SHA-256 与发布记录一致：`ca6ef8096e4b0b9ee1573f0d91da16a93330a1341bdea4f0ed7679aa3530d621`。
- 当前待闭环：本机升级至补丁版后验证“检查更新”、现有绑定及手机重连；v0.4.x 的实际游戏启动/停止/报告链路尚不能宣称已完整实机验收。不得把自动测试通过等同真实任务完成。
- 本次仅接手、核验和更新交接文档，没有覆盖安装、启动游戏任务、修改真实 BetterGI 配置、提交/推送或再次部署 VPS。历史发布授权不作为本会话执行上述操作的新授权；按用户后续明确要求继续。
- 保留轻量无账号、一机一绑、端到端加密、电脑配置为事实来源的产品方向；旧 Android/Bridge/深度修改 BetterGI 路线已废弃，不恢复。每日自动检查任务已删除，不恢复。

### 后续开发定位

- 托盘、设置、绑定与更新：`agent/src/BetterGI.RemoteLite.Agent/AgentApplicationContext.cs`、`UI/`、`Runtime/UpdateService.cs`。
- RPC 与生命周期：`Runtime/AgentRuntime.cs`、`Runtime/BetterGiController.cs`；核心配置目录、修订检查在 `BetterGI.RemoteLite.Core/BetterGi/`。
- 日志、友好错误、物品口径与通知：`BetterGI.RemoteLite.Core/Reports/`、`Notifications/`。奖励识别、拾取记录、木材估算必须保持独立，不能合并成背包增量。
- 手机页面与通信：`web/src/main.ts`、`client.ts`、`types.ts`；分页读取在 `config-transfer.ts` 与 `report-transfer.ts`；协议契约见 `protocol/README.md`。
- 中转与发布：`relay/internal/relay/`、`scripts/verify.ps1`、打包/部署脚本及 `deploy/`；界面编辑前按现有 `impeccable` 流程处理。

## 已完成并发布

- 基线：v0.3.5，main 提交 598ea87（README 展示截图）；工作开始时工作区干净。
- Windows .NET 8 托盘 Agent + TypeScript/Lit PWA + Go Relay。
- 手机扫码绑定、180 天可续期绑定、端到端加密、配置冲突检查、远程一条龙、停止快捷键、日志报告、飞书/QQ 邮箱、更新安装与卸载已实现。
- 已实机验证 BetterGI 0.64.0；更高版本仅做结构检查，不能宣称已实机验证。
- 已发布 GitHub Denght123/bettergi_remote；VPS 入口 https://bgiremote.163831.xyz；记录中的生产发布版本为 v0.4.1（见下方补丁发布记录）。
- 每日自动检查任务已按用户要求删除，不要恢复。
- VPS 连接说明位于用户桌面 BetterGI-Remote-VPS连接说明.md；不要复制私钥。

## 2026-10-03 功能升级要求（历史）

1. BetterGI 已启动时通过官方入口直接发起任务；未启动时自动打开并发起任务。
2. 移动端配置扩展到电脑 BetterGI 对应功能，超出原有一条龙范围。
3. 实时任务/步骤状态；失败提示明确指出任务、步骤与处理建议，避免原始错误代码/堆栈。
4. 报告统计任务实际获得的材料和物品，不能把拾取尝试或背包存量当作获得数量。
5. 保持双端协议一致、交互流畅，回归既有绑定/启动/停止/配置/报告链路。

## 执行边界

- 用户已于 2026-10-03 授权验证逻辑后推送 GitHub 并部署 VPS；本轮不自动覆盖用户安装或实际启动原神任务。
- 保持现有视觉和加密协议，新增字段尽量向后兼容。
- 运行中的全局配置应用方式必须依据 BetterGI 实际行为；不可把仅写磁盘误称实时生效。
- 不更改用户真实 BetterGI 配置；测试使用临时目录和仿真数据。

## v0.4.0 进度与发布记录

- 实现代码已落盘：生命周期操作锁/启动确认；全局和调度/脚本参数目录；实时步骤与可读错误；物品来源区分；配置与报告分页；前端草稿、分类搜索及控件状态。
- 当前真实配置只读同步成功，超过四千字段。保存/运行模拟仅使用临时测试目录。
- 本轮说明：feature-upgrade-2026-10-03.md，包含范围、上游限制、验收和未实机执行的部分。
- 本地预览服务：127.0.0.1:4175，可使用 ?demo&view=config&scope=global、?demo&scenario=running、?demo&view=reports；演示数据已明确标注。
- v0.4.0 正式包、GitHub Release/标签及 VPS 部署完成；本机安装保持 v0.3.5，没有覆盖安装，需通过检查更新升级才能完整使用新功能。
- 最终完整验证：Go 测试通过；PWA 12 测试、类型检查与构建通过；.NET 66 测试通过，Windows Release 构建 0 警告、0 错误。
- 当前真实配置只读验证：4346 字段（全局 185、一条龙 67、调度/脚本 4094），68 页全部传输，最大页 22605 字节，初次读取约 77ms（本次电脑环境）。
- 浏览器已验证分类、搜索、小数输入、未保存提示、Alt+方向键排序、320/390/1280px 横向布局及桌面最后字段无遮挡；截图 .impeccable/review/*20261003.png，内容是明确标注的演示数据。
- 独立界面复核发现桌面内容底部预留被断点覆盖，已修复并在最大滚动位置确认；复核的三项修正（滚动空间、对比度、键盘排序）均判定 resolved，限定范围结论 SHIP。通知/物品来源区分继续沿用同一报告契约。
- 补齐官方取消日志四种格式、停止等待状态和重复停止处理；新网页通过能力字段提示旧电脑端升级。新版 SW 缓存版本 v9 用于触发网页刷新。
- 正式包：artifacts/BetterGI.Remote.Setup.0.4.0.exe；SHA-256 ab1a83db75551150ef8f64cdf6c363388c13eb057fef84c008b41689b6f2c1d0。
- 本次用户称已手动停止；检查时没有 BetterGI 进程，但本次测试没有新增执行报告，因此未将其宣称为完整实机验证。
- 发布代码提交 c10c0f62d0393486870ce32daafa7331fdf92601（功能提交 5417d84）；v0.4.0 标签指向发布代码提交。
- GitHub 该发布代码的两次 CI 运行均 success。正式资产大小 66405579 字节，GitHub 资产摘要与 VPS 公网下载 SHA-256 一致。
- 线上验证：systemd active/enabled，Nginx 配置通过，healthz 正常，PWA/更新清单 0.4.0，SW v9，Relay 仍仅监听 127.0.0.1:8080，0.3.5 旧包下载仍返回 200。
- VPS 备份 /opt/bettergi-remote-lite/backups/20261003T145519Z-0.4.0。x-ui 部署前后均 inactive，未修改其服务或端口配置。

## v0.4.1 更新故障修复

- 用户截图的 '<' JSON 错误在本地测试入口复现：18080/updates/latest.json 返回 200 text/html 首页。正式线上 0.4.0 清单正常，但正式客户端同样缺少异常 JSON 回退，且下载移动文件时校验流未关闭。
- 修复：独立正式更新地址，结构/摘要/内容类型校验与备用源，共用 BetterGI 版本检查防御；流关闭后移动文件；HTTP 失败/HTML/错误下载不覆盖可信包；更新并发保护和任务忙碌保护；缺失更新配置时服务端返回 404。
- 全量验证：.NET 81、PWA 12、Go 全部通过，Windows 构建零警告/零错误。实际网络检查确认本地控制地址与正式更新地址分离。
- 补丁包 artifacts/BetterGI.Remote.Setup.0.4.1.exe，66409287 字节，SHA-256 ca6ef8096e4b0b9ee1573f0d91da16a93330a1341bdea4f0ed7679aa3530d621。
- 本地自动安装/启动命令被自动审批拒绝，未执行。本机正式安装仍为 0.3.5，需手动使用已校验补丁覆盖安装；不要宣称已更新本机。
- v0.4.1 GitHub Release、版本标签与 VPS 补丁部署完成。发布代码提交 6901d7334ad5bea00d70e776adf4ff946e2cba39，GitHub 两次 CI 均 success。
- 公网清单为 0.4.1、下载 SHA-256 与 GitHub 摘要一致；healthz 正常，Nginx 配置通过，SW v10，保留更新路径缺失返回 404，Relay 仅监听 127.0.0.1:8080。
- VPS 备份 /opt/bettergi-remote-lite/backups/20261003T163845Z-0.4.1。本地自动覆盖安装与启动命令均未获准执行；本机安装依旧为 0.3.5，需要用户手动运行补丁安装器，并退出旧的本地测试客户端。
