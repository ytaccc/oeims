[CmdletBinding()]
param(
    [ValidateRange(1, 65535)]
    [int]$Port = 5173,

    [string]$PublicUrl,

    [switch]$NoBrowser
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$composeFile = Join-Path $repoRoot "docker-compose.yml"
$envFile = Join-Path $repoRoot ".env"
$localUrl = "http://localhost:$Port"

function New-RandomSecret {
    $bytes = New-Object byte[] 32
    $generator = [System.Security.Cryptography.RandomNumberGenerator]::Create()

    try {
        $generator.GetBytes($bytes)
        return [Convert]::ToBase64String($bytes)
    }
    finally {
        $generator.Dispose()
    }
}

function Get-LanAddress {
    if (-not (Get-Command Get-NetRoute -ErrorAction SilentlyContinue)) {
        return $null
    }

    $route = Get-NetRoute `
        -AddressFamily IPv4 `
        -DestinationPrefix "0.0.0.0/0" `
        -ErrorAction SilentlyContinue |
        Sort-Object RouteMetric |
        Select-Object -First 1

    if (-not $route) {
        return $null
    }

    return Get-NetIPAddress `
        -AddressFamily IPv4 `
        -InterfaceIndex $route.InterfaceIndex `
        -AddressState Preferred `
        -ErrorAction SilentlyContinue |
        Where-Object { $_.IPAddress -notlike "169.254.*" } |
        Select-Object -ExpandProperty IPAddress -First 1
}

if (-not (Test-Path $composeFile)) {
    throw "Could not find docker-compose.yml at $composeFile."
}

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    throw "Docker Desktop is required and was not found in PATH."
}

& docker info *> $null
if ($LASTEXITCODE -ne 0) {
    throw "Docker is installed but is not running. Start Docker Desktop and try again."
}

if (-not (Test-Path $envFile)) {
    Set-Content `
        -LiteralPath $envFile `
        -Value "JWT_SECRET=$(New-RandomSecret)" `
        -Encoding Ascii
}
elseif (-not (Select-String -LiteralPath $envFile -Pattern '^JWT_SECRET=.+$' -Quiet)) {
    Add-Content `
        -LiteralPath $envFile `
        -Value "JWT_SECRET=$(New-RandomSecret)" `
        -Encoding Ascii
}

if ([string]::IsNullOrWhiteSpace($PublicUrl)) {
    $PublicUrl = $localUrl
}

$publicUri = $null
if (-not [Uri]::TryCreate($PublicUrl, [UriKind]::Absolute, [ref]$publicUri)) {
    throw "PublicUrl must be an absolute HTTP or HTTPS URL."
}

if ($publicUri.Scheme -notin @("http", "https")) {
    throw "PublicUrl must use HTTP or HTTPS."
}

$PublicUrl = $publicUri.GetLeftPart([UriPartial]::Authority).TrimEnd("/")

$previousPort = $env:OEIMS_PORT
$previousPublicUrl = $env:FRONTEND_BASE_URL
$env:OEIMS_PORT = $Port.ToString()
$env:FRONTEND_BASE_URL = $PublicUrl

Push-Location $repoRoot
try {
    & docker compose --env-file $envFile -f $composeFile up --build -d

    if ($LASTEXITCODE -ne 0) {
        throw "Docker Compose could not start OEIMS."
    }
}
finally {
    Pop-Location

    if ($null -eq $previousPort) {
        Remove-Item Env:\OEIMS_PORT -ErrorAction SilentlyContinue
    }
    else {
        $env:OEIMS_PORT = $previousPort
    }

    if ($null -eq $previousPublicUrl) {
        Remove-Item Env:\FRONTEND_BASE_URL -ErrorAction SilentlyContinue
    }
    else {
        $env:FRONTEND_BASE_URL = $previousPublicUrl
    }
}

$deadline = (Get-Date).AddMinutes(2)
$ready = $false

while ((Get-Date) -lt $deadline) {
    try {
        $response = Invoke-WebRequest `
            -Uri "$localUrl/api/auth/login" `
            -UseBasicParsing `
            -TimeoutSec 3

        if ($response.StatusCode -lt 500) {
            $ready = $true
            break
        }
    }
    catch {
        if ($_.Exception.Response -and [int]$_.Exception.Response.StatusCode -lt 500) {
            $ready = $true
            break
        }
    }

    Start-Sleep -Seconds 2
}

if (-not $ready) {
    throw "OEIMS containers started, but the API was not ready after two minutes. Run 'docker compose logs' from the repository root."
}

$professor = @{
    email = "professor@isel.pt"
    password = "profpass123"
    role = "PROFESSOR"
} | ConvertTo-Json

try {
    Invoke-RestMethod `
        -Uri "$localUrl/api/auth/register" `
        -Method Post `
        -ContentType "application/json" `
        -Body $professor | Out-Null
}
catch {
    if (-not $_.Exception.Response -or [int]$_.Exception.Response.StatusCode -ne 409) {
        throw
    }

    $login = @{
        email = "professor@isel.pt"
        password = "profpass123"
    } | ConvertTo-Json

    Invoke-RestMethod `
        -Uri "$localUrl/api/auth/login" `
        -Method Post `
        -ContentType "application/json" `
        -Body $login | Out-Null
}

$lanAddress = Get-LanAddress

Write-Host ""
Write-Host "OEIMS is ready." -ForegroundColor Green
Write-Host "Professor console: $localUrl"
Write-Host "Public URL:       $PublicUrl"

if ($lanAddress -and $PublicUrl -eq $localUrl) {
    Write-Host "Remote students require restarting with -PublicUrl http://${lanAddress}:$Port"
}

if (-not $NoBrowser) {
    Start-Process $localUrl
}
