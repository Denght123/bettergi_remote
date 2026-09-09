# 不使用 Docker 的测试方法

## 当前开发阶段

源码、Windows 安装包、PWA、中转协议、可续期绑定、任务同步、任务控制、报告、飞书与 QQ 邮箱通知均已实现并通过自动测试。发布前仍建议使用真实 BetterGI 0.64.x 做一轮短任务灰度验证。

建议使用 BetterGI 的可抛弃副本进行第一次测试，不要直接修改正在日常使用的安装目录。游戏自动化可能违反游戏或服务规则，测试者自行承担相关风险。

## 方案一：本机中转加临时 HTTPS 隧道

该方式不需要 Docker、VPS、公网 IP、路由器端口映射或 Cloudflare 账号。`cloudflared` 会建立一个仅在本次运行期间有效的随机 HTTPS 地址。

### 准备

1. 安装 Cloudflare Tunnel 客户端：

   ```powershell
   winget install --id Cloudflare.cloudflared -e
   ```

2. 在项目根目录生成测试产物：

   ```powershell
   ./scripts/publish.ps1
   ```

3. 启动本机测试环境：

   ```powershell
   ./scripts/start-local-test.ps1
   ```

4. 终端会显示一个类似 `https://random-name.trycloudflare.com` 的地址。保持该窗口运行。

### 配置电脑小助手

1. 安装 `artifacts/BetterGI.RemoteLite.Setup.0.1.0.exe`。
2. 选择官方 BetterGI 0.64.x 的 `BetterGI.exe`。
3. 选择一份现有一条龙 JSON 作为复制来源。
4. 将终端显示的 `https://*.trycloudflare.com` 填入中转地址。
5. 填写 BetterGI 中已经注册的全局取消快捷键，例如 `Ctrl+Shift+F12`。
6. 如需测试通知，填写飞书机器人 Webhook 和可选签名密钥；或填写 QQ 邮箱地址、SMTP 授权码和可选收件地址。
7. 保存设置。小助手会创建独立的 `远程每日` 配置，不修改作为来源的配置。
8. 打开绑定二维码，用手机扫码，并在电脑弹窗中确认手机设备。

临时隧道每次启动都可能更换域名。域名更换后，手机浏览器会把它视为新的 PWA 来源，需要在小助手中更新中转地址并重新扫码绑定。

## 推荐测试顺序

### 1. 只读连接

- 手机应显示电脑在线、Windows 未锁屏、BetterGI 版本和进程状态。
- 查看配置和最近报告，不启动任务。
- 锁定 Windows 后确认手机显示不可启动，解锁后恢复。
- 手动打开 BetterGI，确认手机不能保存配置或开始新任务。

### 2. 配置隔离

- 手机只调整一个无风险参数并保存。
- 检查 `User/OneDragon/远程每日.json` 已变化。
- 检查作为复制来源的原配置没有变化。
- 在手机读取配置后从电脑端修改远程配置，再从手机保存，确认出现修订冲突而不是覆盖。

### 3. 最小任务运行

- 首次只启用一个任务，例如领取邮件。
- 确认 BetterGI 已关闭、Windows 已解锁、原神账号状态适合测试。
- 从手机确认启动。
- 检查实际命令等价于 `BetterGI.exe startOneDragon "远程每日"`。
- 等待完成，确认 BetterGI 和原神按照远程配置关闭。

### 4. 停止和报告

- 再次启动测试任务，在执行过程中点击停止。
- 确认电脑只收到预设取消快捷键，没有强杀进程。
- 日志确认取消时报告应为已停止；无法确认时应显示结果未知。
- 检查手机报告、奖励识别和飞书消息是否一致。
- 在电脑源一条龙配置中临时增加一个任务，点击手机“立即同步”，确认新增任务出现在远程列表末尾。
- 从托盘执行“检查更新”，确认最新版提示；更新测试不得在任务运行中进行。

### 5. 断线恢复

- 任务运行时关闭手机网络，电脑任务应继续。
- 恢复网络后手机应重新显示进度或最终报告。
- 停止本地测试脚本后，手机不应能够提交离线命令。

## 日志与报告位置

- 小助手设置和本地报告：`%LOCALAPPDATA%\BetterGI Remote Lite`
- 本机中转日志：`artifacts/local-test`
- PWA 构建结果：`web/dist`

## 方案二：VPS 原生进程部署

如果需要连续测试多天，可以绕过 Docker，直接使用 `artifacts/relay-linux-amd64`：

1. 将中转二进制和 `web/dist` 上传到 VPS 的 `/opt/bettergi-remote-lite`。
2. 创建低权限用户 `bettergi-remote-lite`。
3. 将 `deploy/native/bettergi-remote-lite.service` 复制到 `/etc/systemd/system/`。
4. 将 `deploy/native/Caddyfile` 中的域名替换为真实域名，并将文件复制到 `/etc/caddy/Caddyfile`。
5. 将 service 文件中的 `ALLOWED_ORIGIN` 同样替换为真实 HTTPS 域名。
6. 启动服务：

   ```bash
   sudo systemctl daemon-reload
   sudo systemctl enable --now bettergi-remote-lite
   sudo systemctl reload caddy
   curl https://你的域名/healthz
   ```

中转服务没有数据库和离线队列，重启只会断开当前连接，电脑和手机随后会自动重连。
