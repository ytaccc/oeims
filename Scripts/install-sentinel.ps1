[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$ServerUrl,

    [ValidateRange(1, 65535)]
    [int]$LoopbackPort = 17653,

    [ValidateSet("win-x64", "win-arm64")]
    [string]$Runtime = "win-x64",

    [string]$TaskName = "OEIMS Sentinel Agent",
    [string]$InstallRoot = (Join-Path $env:ProgramFiles "OEIMS\Sentinel"),
    [string]$DataRoot = (Join-Path $env:ProgramData "OEIMS\Sentinel")
)

$ErrorActionPreference = "Stop"
$ServiceName = "oeims"

function Assert-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)

    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw "Sentinel installation requires an Administrator PowerShell window."
    }
}

function Stop-InstalledAgent {
    param([string]$ExecutablePath)

    $processName = [IO.Path]::GetFileNameWithoutExtension($ExecutablePath)

    Get-Process -Name $processName -ErrorAction SilentlyContinue |
        Where-Object {
            try {
                $_.Path -eq $ExecutablePath
            }
            catch {
                $false
            }
        } |
        Stop-Process -Force
}

if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    throw "Sentinel can only be installed on Windows."
}

Assert-Administrator

$serverUri = $null
if (-not [Uri]::TryCreate($ServerUrl, [UriKind]::Absolute, [ref]$serverUri)) {
    throw "ServerUrl must be an absolute HTTP or HTTPS URL."
}

if ($serverUri.Scheme -notin @("http", "https")) {
    throw "ServerUrl must use HTTP or HTTPS."
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw ".NET 10 SDK is required to publish Sentinel and was not found in PATH."
}

$sdkVersions = & dotnet --list-sdks
if ($LASTEXITCODE -ne 0 -or -not ($sdkVersions -match '^10\.')) {
    throw ".NET 10 SDK is required to publish Sentinel."
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$serviceProject = Join-Path $repoRoot "Sentinel\Service\Service.csproj"
$agentProject = Join-Path $repoRoot "Sentinel\Agent\Agent.csproj"

if (-not (Test-Path $serviceProject) -or -not (Test-Path $agentProject)) {
    throw "Sentinel projects were not found. Run this script from the cloned OEIMS repository."
}

$serviceInstallDirectory = Join-Path $InstallRoot "Service"
$agentInstallDirectory = Join-Path $InstallRoot "Agent"
$serviceExecutable = Join-Path $serviceInstallDirectory "Sentinel.exe"
$agentExecutable = Join-Path $agentInstallDirectory "Agent.exe"
$configPath = Join-Path $DataRoot "appsettings.Production.json"
$stagingRoot = Join-Path ([IO.Path]::GetTempPath()) "oeims-sentinel-$([Guid]::NewGuid())"
$serviceStagingDirectory = Join-Path $stagingRoot "Service"
$agentStagingDirectory = Join-Path $stagingRoot "Agent"

$origin = $serverUri.GetLeftPart([UriPartial]::Authority).TrimEnd("/")
$realtimeScheme = if ($serverUri.Scheme -eq "https") { "wss" } else { "ws" }
$realtimeBaseUrl = "${realtimeScheme}://$($serverUri.Authority)/api"

try {
    New-Item -ItemType Directory -Path $serviceStagingDirectory -Force | Out-Null
    New-Item -ItemType Directory -Path $agentStagingDirectory -Force | Out-Null

    Write-Host "Publishing Sentinel Service..."
    & dotnet publish $serviceProject `
        --configuration Release `
        --runtime $Runtime `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        --output $serviceStagingDirectory

    if ($LASTEXITCODE -ne 0) {
        throw "Sentinel Service publish failed."
    }

    Write-Host "Publishing Sentinel Agent..."
    & dotnet publish $agentProject `
        --configuration Release `
        --runtime $Runtime `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        --output $agentStagingDirectory

    if ($LASTEXITCODE -ne 0) {
        throw "Sentinel Agent publish failed."
    }

    $existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue

    if ($existingService) {
        if ($existingService.Status -ne "Stopped") {
            Stop-Service -Name $ServiceName -Force
            $existingService.WaitForStatus("Stopped", [TimeSpan]::FromSeconds(20))
        }

        & sc.exe delete $ServiceName | Out-Null

        $deadline = (Get-Date).AddSeconds(20)
        while ((Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) -and (Get-Date) -lt $deadline) {
            Start-Sleep -Milliseconds 250
        }

        if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
            throw "The existing Sentinel Service could not be removed."
        }
    }

    Stop-InstalledAgent -ExecutablePath $agentExecutable

    New-Item -ItemType Directory -Path $serviceInstallDirectory -Force | Out-Null
    New-Item -ItemType Directory -Path $agentInstallDirectory -Force | Out-Null
    New-Item -ItemType Directory -Path $DataRoot -Force | Out-Null

    Remove-Item (Join-Path $serviceInstallDirectory "*") -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item (Join-Path $agentInstallDirectory "*") -Recurse -Force -ErrorAction SilentlyContinue

    Copy-Item (Join-Path $serviceStagingDirectory "*") $serviceInstallDirectory -Recurse -Force
    Copy-Item (Join-Path $agentStagingDirectory "*") $agentInstallDirectory -Recurse -Force

    $configuration = [ordered]@{
        Logging = [ordered]@{
            LogLevel = [ordered]@{
                Default = "Information"
                "Microsoft.Hosting.Lifetime" = "Information"
            }
        }
        Server = [ordered]@{
            Enabled = $true
            ApiBaseUrl = "$origin/api"
            RealtimeBaseUrl = $realtimeBaseUrl
        }
        Loopback = [ordered]@{
            Port = $LoopbackPort
            ClientOrigin = $origin
        }
        Application = [ordered]@{
            AllowMultipleInstances = $false
        }
    }

    $configuration |
        ConvertTo-Json -Depth 5 |
        Set-Content -LiteralPath $configPath -Encoding UTF8

    New-Service `
        -Name $ServiceName `
        -BinaryPathName ('"{0}"' -f $serviceExecutable) `
        -DisplayName "Online Exam Integrity Monitoring Service" `
        -StartupType Automatic | Out-Null

    & sc.exe description $ServiceName "This service monitors the computer during online exams and reports activity to the teacher." | Out-Null

    $currentUser = [Security.Principal.WindowsIdentity]::GetCurrent().Name
    $action = New-ScheduledTaskAction `
        -Execute $agentExecutable `
        -WorkingDirectory $agentInstallDirectory
    $trigger = New-ScheduledTaskTrigger -AtLogOn -User $currentUser
    $principal = New-ScheduledTaskPrincipal `
        -UserId $currentUser `
        -LogonType Interactive `
        -RunLevel Limited
    $settings = New-ScheduledTaskSettingsSet `
        -AllowStartIfOnBatteries `
        -DontStopIfGoingOnBatteries `
        -StartWhenAvailable

    Register-ScheduledTask `
        -TaskName $TaskName `
        -Action $action `
        -Trigger $trigger `
        -Principal $principal `
        -Settings $settings `
        -Force | Out-Null

    Start-Service -Name $ServiceName
    Start-ScheduledTask -TaskName $TaskName

    Write-Host ""
    Write-Host "Sentinel installed and started." -ForegroundColor Green
    Write-Host "Service: $serviceExecutable"
    Write-Host "Agent:   $agentExecutable"
    Write-Host "Config:  $configPath"
}
finally {
    Remove-Item $stagingRoot -Recurse -Force -ErrorAction SilentlyContinue
}
