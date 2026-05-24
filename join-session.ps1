<#
.SYNOPSIS
    Registers (or reuses) a test student, joins a session by code, and writes
    the resulting token + participantId into Daemon/appsettings.Development.json
    so the daemon can be started immediately.

.PARAMETER Code
    Session code shown on the professor console (e.g. A3F9). Mandatory.

.PARAMETER Email
    Student email to register/login with. Defaults to test.student@oeims.local.

.PARAMETER Password
    Student password. Defaults to Test1234!

.PARAMETER BaseUrl
    Server base URL. Defaults to http://localhost:8080.

.EXAMPLE
    .\join-session.ps1 -Code A3F9
    .\join-session.ps1 -Code A3F9 -Email alice@isel.pt -Password hunter2
#>
param(
    [Parameter(Mandatory)]
    [string] $Code,

    [string] $Email    = "test.student@oeims.local",
    [string] $Password = "Test1234!",
    [string] $BaseUrl  = "http://localhost:8080"
)

$ErrorActionPreference = "Stop"

$jsonHeader = @{ "Content-Type" = "application/json" }

Write-Host ""
Write-Host "OEIMS - student session setup" -ForegroundColor Cyan
Write-Host "Code: $Code | Student: $Email"
Write-Host ""

# 1. Register
Write-Host "[1/3] Registering student..." -NoNewline
try {
    $body = [ordered]@{ email = $Email; password = $Password; role = "STUDENT" } | ConvertTo-Json
    $null = Invoke-RestMethod -Uri "$BaseUrl/auth/register" -Method Post -Body $body -Headers $jsonHeader
    Write-Host " done." -ForegroundColor Green
} catch {
    Write-Host " already registered." -ForegroundColor DarkGray
}

# 2. Login
Write-Host "[2/3] Logging in..." -NoNewline
$body  = [ordered]@{ email = $Email; password = $Password } | ConvertTo-Json
$auth  = Invoke-RestMethod -Uri "$BaseUrl/auth/login" -Method Post -Body $body -Headers $jsonHeader
$token = $auth.token
Write-Host " ok." -ForegroundColor Green

# 3. Join session
Write-Host "[3/3] Joining session '$Code'..." -NoNewline
$authHeaders = @{ "Content-Type" = "application/json"; "Authorization" = "Bearer $token" }
$body        = [ordered]@{ code = $Code } | ConvertTo-Json
$join        = Invoke-RestMethod -Uri "$BaseUrl/sessions/join" -Method Post -Body $body -Headers $authHeaders
Write-Host " joined." -ForegroundColor Green
Write-Host "   Exam:     $($join.examTitle)" -ForegroundColor DarkGray
Write-Host "   Duration: $($join.durationMins) min" -ForegroundColor DarkGray

# 4. Patch Daemon/appsettings.Development.json
$settingsPath = Join-Path $PSScriptRoot "Daemon\appsettings.Development.json"
$settings = Get-Content $settingsPath -Raw | ConvertFrom-Json

$settings.Server.Enabled       = $true
$settings.Server.BaseUrl       = $BaseUrl
$settings.Server.Token         = $token
$settings.Server.ParticipantId = $join.participantId

$updated = $settings | ConvertTo-Json -Depth 10
[System.IO.File]::WriteAllText($settingsPath, $updated)

Write-Host ""
Write-Host "appsettings.Development.json patched." -ForegroundColor Cyan
Write-Host ""
Write-Host "  Token         : $token"
Write-Host "  ParticipantId : $($join.participantId)"
Write-Host "  SessionId     : $($join.sessionId)"
Write-Host ""
Write-Host "Start the daemon:" -ForegroundColor Yellow
Write-Host "  cd Daemon; dotnet run" -ForegroundColor Yellow
Write-Host ""
