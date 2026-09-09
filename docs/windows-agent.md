# Windows agent

The Windows agent is a .NET 8 WinForms tray application. The installer registers it as a highest-privilege scheduled task at user logon so BetterGI can start without a second unattended UAC prompt.

## First setup (0.2.0 and later)

1. The agent searches common fixed-drive locations for an official BetterGI 0.64.x installation.
2. If automatic discovery fails, select `BetterGI.exe` once.
3. Select an existing one-dragon JSON file as the source.
4. Click `完成设置并连接手机`.
5. Scan the QR code and confirm the browser device locally.

The production relay URL and default cancel hotkey are built in. Feishu and custom relay settings remain available under advanced settings for maintainers and development testing.

The agent creates `User/OneDragon/远程每日.json`, fixes its completion action to `关闭游戏和软件`, and retains the original source unchanged. The installer registers an interactive scheduled task at sign-in and retries the agent after an unexpected exit.

Settings and reports live under `%LOCALAPPDATA%\BetterGI Remote Lite`. The pairing secret and Feishu credentials are protected with Windows DPAPI for the current user.

## Runtime rules

- Configuration writes and task start are rejected while BetterGI is running.
- Task start is rejected while Windows is locked or the executable version is outside 0.64.x.
- The phone cannot submit a path, executable, command line, or key combination.
- Stop sends only the preconfigured cancel hotkey and waits for a BetterGI cancellation log marker.
- A run report is retained locally even if Feishu delivery fails.
