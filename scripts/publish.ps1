$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $root "artifacts"
$cache = Join-Path $root ".cache"
New-Item -ItemType Directory -Force $cache | Out-Null
$env:DOTNET_CLI_HOME = Join-Path $cache "dotnet-home"
$env:NUGET_PACKAGES = Join-Path $cache "nuget"
$env:npm_config_cache = Join-Path $cache "npm"
$env:GOCACHE = Join-Path $cache "go-build"
$env:GOMODCACHE = Join-Path $cache "go-mod"
$env:GOPROXY = "https://goproxy.cn,direct"
$env:TEMP = Join-Path $cache "temp"
$env:TMP = $env:TEMP
New-Item -ItemType Directory -Force $env:DOTNET_CLI_HOME, $env:TEMP | Out-Null

dotnet publish (Join-Path $root "agent/src/BetterGI.RemoteLite.Agent/BetterGI.RemoteLite.Agent.csproj") `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:EnableCompressionInSingleFile=true `
    --output (Join-Path $artifacts "agent-win-x64")
if ($LASTEXITCODE -ne 0) { throw ".NET publish failed with exit code $LASTEXITCODE." }

Push-Location (Join-Path $root "web")
try {
    npm ci
    if ($LASTEXITCODE -ne 0) { throw "npm ci failed with exit code $LASTEXITCODE." }
    npm run build
    if ($LASTEXITCODE -ne 0) { throw "PWA build failed with exit code $LASTEXITCODE." }
} finally {
    Pop-Location
}

Push-Location (Join-Path $root "relay")
try {
    $env:CGO_ENABLED = "0"
    $env:GOOS = "linux"
    $env:GOARCH = "amd64"
    go build -trimpath -ldflags "-s -w" -o (Join-Path $artifacts "relay-linux-amd64") ./cmd/relay
    if ($LASTEXITCODE -ne 0) { throw "Linux relay build failed with exit code $LASTEXITCODE." }

    $env:GOOS = "windows"
    go build -trimpath -ldflags "-s -w" -o (Join-Path $artifacts "relay-windows-x64.exe") ./cmd/relay
    if ($LASTEXITCODE -ne 0) { throw "Windows relay build failed with exit code $LASTEXITCODE." }
} finally {
    Pop-Location
}
