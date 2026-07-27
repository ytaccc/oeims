[CmdletBinding()]
param(
    [ValidateRange(1, 65535)]
    [int]$Port = 5173,

    [ValidateRange(1, 65535)]
    [int]$LoopbackPort = 17653,

    [ValidateSet("win-x64", "win-arm64")]
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$localUrl = "http://localhost:$Port"

& (Join-Path $PSScriptRoot "start-oeims.ps1") -Port $Port -NoBrowser
& (Join-Path $PSScriptRoot "install-sentinel.ps1") `
    -ServerUrl $localUrl `
    -LoopbackPort $LoopbackPort `
    -Runtime $Runtime

Start-Process $localUrl

Write-Host ""
Write-Host "OEIMS is running. The Sentinel Agent was not started so demonstration links can be copied." -ForegroundColor Green
Write-Host "Start it when ready with: Start-ScheduledTask -TaskName 'OEIMS Sentinel Agent'"
