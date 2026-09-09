param(
    [ValidateRange(1024, 65535)]
    [int]$Port = 18080
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$relay = Join-Path $root "artifacts/relay-windows-x64.exe"
$webRoot = Join-Path $root "web/dist"
$logDir = Join-Path $root "artifacts/local-test"

if (-not (Test-Path $relay)) {
    throw "Windows relay binary was not found. Run scripts/publish.ps1 first."
}

if (-not (Test-Path (Join-Path $webRoot "index.html"))) {
    throw "PWA build output was not found. Run npm ci and npm run build in web first."
}

$cloudflaredCommand = Get-Command cloudflared -ErrorAction SilentlyContinue
$cloudflared = if ($cloudflaredCommand) {
    $cloudflaredCommand.Source
} else {
    $candidate = Join-Path $env:LOCALAPPDATA "Microsoft/WinGet/Links/cloudflared.exe"
    if (Test-Path $candidate) {
        $candidate
    } else {
        Get-ChildItem (Join-Path $env:LOCALAPPDATA "Microsoft/WinGet/Packages") `
            -Recurse -File -Filter "cloudflared.exe" -ErrorAction SilentlyContinue |
            Select-Object -First 1 -ExpandProperty FullName
    }
}

if (-not $cloudflared) {
    throw "cloudflared was not found. Run: winget install --id Cloudflare.cloudflared -e"
}

New-Item -ItemType Directory -Force $logDir | Out-Null
$stdout = Join-Path $logDir "relay.stdout.log"
$stderr = Join-Path $logDir "relay.stderr.log"

$previousPort = $env:PORT
$previousWebRoot = $env:WEB_ROOT
$previousAllowedOrigin = $env:ALLOWED_ORIGIN
$relayProcess = $null

try {
    $env:PORT = $Port.ToString()
    $env:WEB_ROOT = $webRoot
    $env:ALLOWED_ORIGIN = ""

    $relayProcess = Start-Process -FilePath $relay `
        -WindowStyle Hidden `
        -RedirectStandardOutput $stdout `
        -RedirectStandardError $stderr `
        -PassThru

    $healthy = $false
    for ($attempt = 0; $attempt -lt 20; $attempt++) {
        try {
            $response = Invoke-WebRequest "http://127.0.0.1:$Port/healthz" -UseBasicParsing -TimeoutSec 2
            if ($response.StatusCode -eq 200) {
                $healthy = $true
                break
            }
        } catch {
            Start-Sleep -Milliseconds 250
        }
    }

    if (-not $healthy) {
        throw "The local relay failed to start. Check $stderr."
    }

    Write-Host ""
    Write-Host "Local relay started: http://127.0.0.1:$Port" -ForegroundColor Green
    Write-Host "Cloudflare will print an https://*.trycloudflare.com URL below." -ForegroundColor Cyan
    Write-Host "Save that URL in the Windows agent, then create a new pairing QR code." -ForegroundColor Cyan
    Write-Host "Press Ctrl+C when testing is complete; the local relay will stop too." -ForegroundColor Yellow
    Write-Host ""

    & $cloudflared tunnel --url "http://127.0.0.1:$Port" --no-autoupdate
} finally {
    if ($relayProcess -and -not $relayProcess.HasExited) {
        Stop-Process -Id $relayProcess.Id -Force
        $relayProcess.WaitForExit()
    }
    $env:PORT = $previousPort
    $env:WEB_ROOT = $previousWebRoot
    $env:ALLOWED_ORIGIN = $previousAllowedOrigin
}
