$ErrorActionPreference = "SilentlyContinue"
Unregister-ScheduledTask -TaskName "BetterGI Remote" -Confirm:$false
Unregister-ScheduledTask -TaskName "BetterGI Remote Lite" -Confirm:$false
