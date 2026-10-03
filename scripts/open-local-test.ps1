param([ValidateRange(1024,65535)][int]$Port = 18080)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$buildRoot = Join-Path $projectRoot 'agent/src/BetterGI.RemoteLite.Agent/bin/Release/net8.0-windows'
$agentExecutable = Join-Path $buildRoot 'BetterGI.RemoteLite.Agent.exe'
$relayExecutable = Join-Path $projectRoot 'artifacts/relay-windows-x64.exe'
$testProfile = Join-Path $projectRoot '.cache/interactive-test'
$baseUrl = "http://127.0.0.1:$Port"
if (!(Test-Path -LiteralPath $agentExecutable) -or !(Test-Path -LiteralPath $relayExecutable)) {
    throw 'Missing local build output. Build the agent and relay first.'
}
Add-Type -Path (Join-Path $buildRoot 'BetterGI.RemoteLite.Core.dll')
Add-Type -Path (Join-Path $buildRoot 'BetterGI.RemoteLite.Agent.dll')
Add-Type -AssemblyName System.Security.Cryptography.ProtectedData
$originalFile = Join-Path $env:LOCALAPPDATA 'BetterGI Remote Lite/settings.json'
$original = if(Test-Path -LiteralPath $originalFile) {
    [System.Text.Json.JsonSerializer]::Deserialize([IO.File]::ReadAllText($originalFile), [BetterGI.RemoteLite.Agent.Storage.AgentSettings], [BetterGI.RemoteLite.Protocol.RemoteJson]::Options)
} else { [BetterGI.RemoteLite.Agent.Storage.AgentSettings]::new() }
$env:BGRL_DATA_DIRECTORY = $testProfile
$env:BGRL_DEFAULT_RELAY_BASE_URL = $baseUrl
$store = [BetterGI.RemoteLite.Agent.Storage.AgentSettingsStore]::new()
$settings = $store.Current
if(-not $settings.IsConfigured) {
    $settings.BetterGiExecutablePath = $original.BetterGiExecutablePath
    $settings.SourceConfigName = $original.SourceConfigName
    $settings.RemoteConfigName = $original.RemoteConfigName
    $settings.CancelHotkey = $original.CancelHotkey
}
if(-not (Test-Path -LiteralPath $settings.BetterGiExecutablePath)) { throw 'BetterGI installation was not found in the existing desktop settings.' }
$settings.RelayBaseUrl = $baseUrl
$settings.LastUpdateCheckAt = [DateTimeOffset]::UtcNow
$settings.LastBetterGiUpdateCheckAt = [DateTimeOffset]::UtcNow
$entropy = [Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes('BetterGI Remote Lite v1 settings'))
if([string]::IsNullOrEmpty($settings.ProtectedPairingSecret)) {
    $secret = [Security.Cryptography.RandomNumberGenerator]::GetBytes(32)
    $plain = [Text.Encoding]::UTF8.GetBytes([Convert]::ToBase64String($secret))
    $settings.ProtectedPairingSecret = [Convert]::ToBase64String([Security.Cryptography.ProtectedData]::Protect($plain,$entropy,[Security.Cryptography.DataProtectionScope]::CurrentUser))
} else {
    $plain = [Security.Cryptography.ProtectedData]::Unprotect([Convert]::FromBase64String($settings.ProtectedPairingSecret),$entropy,[Security.Cryptography.DataProtectionScope]::CurrentUser)
    $secret = [Convert]::FromBase64String([Text.Encoding]::UTF8.GetString($plain))
}
$store.Save($settings)
if(-not (Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue)) {
    $env:PORT = "$Port"
    $env:LISTEN_ADDRESS = "127.0.0.1:$Port"
    $env:WEB_ROOT = Join-Path $projectRoot 'web/dist'
    $env:ALLOWED_ORIGIN = $baseUrl
    $relayProcess = Start-Process -FilePath $relayExecutable -WindowStyle Hidden -PassThru
    for($attempt=0;$attempt -lt 30;$attempt++) {
        try { Invoke-RestMethod "$baseUrl/healthz" -TimeoutSec 1 | Out-Null; break } catch { Start-Sleep -Milliseconds 200 }
    }
}
Invoke-RestMethod "$baseUrl/healthz" -TimeoutSec 3 | Out-Null
$arguments = "--local-test-profile `"$testProfile`" --local-test-relay $baseUrl"
$agentProcess = Start-Process -FilePath $agentExecutable -ArgumentList $arguments -Verb RunAs -PassThru
$encoded = [Convert]::ToBase64String($secret).TrimEnd('=').Replace('+','-').Replace('/','_')
$pairingUrl = "$baseUrl/#pair=$encoded"
$browser = 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'
if(Test-Path -LiteralPath $browser) {
    Start-Process -FilePath $browser -ArgumentList @('--new-window',$pairingUrl)
} else { Start-Process $pairingUrl }
[Array]::Clear($secret,0,$secret.Length)
[Array]::Clear($plain,0,$plain.Length)
[pscustomobject]@{TestUrl=$baseUrl;AgentProcess=$agentProcess.Id;Profile=$testProfile;ProductionSettingsUnchanged=$true} | Format-List
