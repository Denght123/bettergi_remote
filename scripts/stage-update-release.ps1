param(
    [Parameter(Mandatory = $true)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [Parameter(Mandatory = $true)][string]$InstallerPath,
    [Parameter(Mandatory = $true)][string]$OutputDirectory
)

$ErrorActionPreference = "Stop"
$installer = Get-Item -LiteralPath $InstallerPath
$expectedName = "BetterGI.Remote.Setup.$Version.exe"
if ($installer.Name -ne $expectedName) {
    throw "Expected installer $expectedName, got $($installer.Name)."
}

$output = New-Item -ItemType Directory -Force -Path $OutputDirectory
$digest = (Get-FileHash -LiteralPath $installer.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
Copy-Item -LiteralPath $installer.FullName -Destination (Join-Path $output.FullName $expectedName) -Force
$manifest = [ordered]@{
    version = $Version
    tag = "v$Version"
    releaseUrl = "https://github.com/Denght123/bettergi_remote/releases/tag/v$Version"
    downloadUrl = "https://bgiremote.163831.xyz/downloads/$expectedName"
    fileName = $expectedName
    sha256 = $digest
}
$manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output.FullName "latest.json") -Encoding utf8NoBOM
Write-Output "UPDATE_DIRECTORY=$($output.FullName)"
Write-Output "UPDATE_SHA256=$digest"
