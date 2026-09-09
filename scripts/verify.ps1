$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
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

Push-Location (Join-Path $root "relay")
try {
    go test ./...
    if ($LASTEXITCODE -ne 0) { throw "Go relay tests failed with exit code $LASTEXITCODE." }
} finally {
    Pop-Location
}

Push-Location (Join-Path $root "web")
try {
    npm ci
    if ($LASTEXITCODE -ne 0) { throw "npm ci failed with exit code $LASTEXITCODE." }
    npm run verify
    if ($LASTEXITCODE -ne 0) { throw "PWA verification failed with exit code $LASTEXITCODE." }
} finally {
    Pop-Location
}

Push-Location (Join-Path $root "agent")
try {
    dotnet test BetterGI.RemoteLite.sln --configuration Release
    if ($LASTEXITCODE -ne 0) { throw ".NET tests failed with exit code $LASTEXITCODE." }
} finally {
    Pop-Location
}
