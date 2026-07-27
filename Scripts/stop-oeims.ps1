[CmdletBinding()]
param(
    [switch]$RemoveData
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$composeFile = Join-Path $repoRoot "docker-compose.yml"
$envFile = Join-Path $repoRoot ".env"

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    throw "Docker was not found in PATH."
}

$arguments = @("compose")

if (Test-Path $envFile) {
    $arguments += @("--env-file", $envFile)
}

$arguments += @("-f", $composeFile, "down")

if ($RemoveData) {
    $arguments += "--volumes"
}

Push-Location $repoRoot
try {
    & docker @arguments

    if ($LASTEXITCODE -ne 0) {
        throw "Docker Compose could not stop OEIMS."
    }
}
finally {
    Pop-Location
}

Write-Host "OEIMS stopped." -ForegroundColor Green

if ($RemoveData) {
    Write-Host "The local database volume was removed."
}
