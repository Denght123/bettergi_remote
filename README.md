# BetterGI Remote

[![CI](https://github.com/Denght123/bettergi_remote/actions/workflows/ci.yml/badge.svg)](https://github.com/Denght123/bettergi_remote/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/Denght123/bettergi_remote)](https://github.com/Denght123/bettergi_remote/releases/latest)
[![License](https://img.shields.io/github/license/Denght123/bettergi_remote)](LICENSE)

BetterGI Remote 是一个轻量、无账号的 BetterGI 手机远程控制工具。电脑安装一次并扫码绑定后，日常只需从手机打开控制页面，即可调整远程专用的一条龙配置、启动或停止任务并查看执行报告。

本项目不修改 BetterGI，也不会把 BetterGI 配置、任务、日志、飞书地址或解密密钥保存到中转服务器。

```text
手机 PWA 网页 ← HTTPS/WSS + 端到端加密 → 轻量中转服务
                                              ↕
                                      Windows 托盘小助手
                                              ↕
                                  BetterGI 配置文件与官方命令行
```

## 下载

当前版本：**v0.2.1**

- [下载 Windows 安装包](https://github.com/Denght123/bettergi_remote/releases/latest/download/BetterGI.Remote.Setup.0.2.1.exe)
- SHA-256：`EEB581731C86DFE9361F9620FFB01217C405F8E508E752377CCB31E34CD6EFC7`
- 手机控制入口：[https://bgiremote.163831.xyz](https://bgiremote.163831.xyz)

安装包目前没有购买商业代码签名证书，因此 Windows SmartScreen 可能显示“Windows 已保护你的电脑”。请确认文件来自本仓库 Release，并核对 SHA-256；然后点击“更多信息”→“仍要运行”。

## 使用条件

- Windows 10/11 x64。
- 官方 BetterGI 0.64.x。
- 电脑已开机、Windows 已登录且未锁屏。
- 开始任务和保存配置前，BetterGI 必须处于关闭状态。
- 手机使用现代 Android/iPhone 浏览器，微信内置浏览器也可用于首次扫码。

## 三步开始使用

1. 在电脑上下载安装 BetterGI Remote，完成安装后打开首次设置向导。
2. 确认自动识别的 BetterGI 目录；未识别时手动选择 `BetterGI.exe`。小助手会建立独立的“远程每日”配置，不修改你平时使用的一条龙配置。
3. 用手机扫描二维码，并在电脑弹窗中确认绑定。绑定完成后建议把控制页面添加到手机主屏幕，或发送到微信“文件传输助手”/收藏，之后无需再次扫码。

一台电脑同时只绑定一个手机浏览器。只要没有清除该浏览器的网站数据、重装电脑端或主动重新绑定，密钥会持久保存，长期使用通常不需要重复扫码。重新绑定会立即使旧手机失效。

Windows 小助手会在当前用户登录后自动启动并驻留系统托盘。它负责安全地读写 BetterGI 配置、执行官方启动命令、监视日志和发送报告；网页本身受浏览器安全限制，无法直接操作电脑上的 BetterGI。

## 手机端功能

- 查看电脑在线、Windows 锁定、BetterGI、原神和当前任务状态。
- 修改任务开关、拖动排序、保存配置。
- 启动当前“远程每日”一条龙任务。
- 通过预先设定的 BetterGI 取消快捷键停止远程任务。
- 查看任务进度、最近执行结果、识别到的奖励和错误信息。
- 任务完成后由电脑直接发送飞书机器人通知（可选）。

目前支持 BetterGI 0.64.x 的八项官方一条龙任务：

- 领取邮件
- 合成树脂
- 自动秘境
- 自动首领讨伐
- 自动幽境危战
- 自动地脉花
- 领取每日奖励
- 领取尘歌壶奖励

已有自定义调度器配置组支持开关和排序，但手机不能新增、删除或修改组内脚本。

## 安全设计

- 无项目账号、无用户数据库，一台电脑绑定一个手机浏览器。
- 使用 256 位随机绑定密钥，并通过 HKDF 派生双向独立的 AES-256-GCM 密钥。
- 配置、命令、状态和报告均端到端加密；服务器只转发密文和在线连接元数据。
- 中转服务不保存业务数据、不提供离线命令队列，手机离线时不能提交新命令。
- 电脑端密钥由 Windows DPAPI 保护，手机密钥保存在浏览器 IndexedDB 中。
- 不开放任意文件访问、程序路径、Shell、桌面控制、通用按键、进程强杀或远程开机。
- BetterGI 已打开、Windows 锁屏、配置冲突或版本不兼容时拒绝危险操作。

## 源码结构

- `agent`：.NET 8 Windows 托盘小助手和测试。
- `web`：TypeScript + Vite PWA 手机控制页面。
- `relay`：Go 编写的无数据库 WebSocket 中转服务。
- `deploy`：原生服务、反向代理、Docker Compose 和 Inno Setup 打包配置。
- `protocol`：通信协议说明及加密互操作测试向量。
- `docs`：本地测试、Windows 客户端和服务器运维文档。

## 自行构建与测试

需要安装 .NET 8 SDK、Go 1.24+ 和 Node.js 22+。在仓库根目录运行：

```powershell
./scripts/verify.ps1
```

也可以分别执行：

```powershell
dotnet test ./agent/BetterGI.RemoteLite.sln --configuration Release

Set-Location ./web
npm ci
npm run verify

Set-Location ../relay
go test ./...
```

无需 Docker 的本地联调步骤参见 [`docs/local-testing.md`](docs/local-testing.md)，Windows 安装和绑定说明参见 [`docs/windows-agent.md`](docs/windows-agent.md)，VPS 原生部署及运维参见 [`docs/operations.md`](docs/operations.md)。

## 已验证项目

- .NET 自动测试 11 项通过。
- PWA 自动测试 6 项通过。
- Go 中转测试全部通过。
- BetterGI 0.64.0 实机完成“领取邮件”、启动原神和正常退出流程。
- 线上模拟 20 对设备、40 条 WebSocket 同时连接通过。
- PWA 依赖安全审计无已知漏洞。

## 限制与声明

- 首版仅支持 Windows 10/11 x64、BetterGI 0.64.x、中文日志和现代手机浏览器。
- 不支持远程开机、锁屏执行、自动定时启动、多手机绑定、多电脑聚合、完整背包 OCR 或 BetterGI 自动升级。
- 这是社区制作的非官方工具，与 BetterGI 项目及《原神》官方无隶属或授权关系。
- 使用自动化工具可能违反游戏或服务条款，并可能带来账号风险。请自行了解规则、谨慎使用并承担相应后果。

## License

[MIT](LICENSE)
