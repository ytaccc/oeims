[CmdletBinding()]
param(
    [string]$ServiceName = "oeims",
    [string]$TaskName = "OEIMS Sentinel Agent",
    [string]$InstallRoot = (Join-Path $env:ProgramFiles "OEIMS\Sentinel"),
    [string]$DataRoot = (Join-Path $env:ProgramData "OEIMS\Sentinel"),

    [switch]$RemoveData
)

$ErrorActionPreference = "Stop"

function Assert-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)

    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw "Sentinel removal requires an Administrator PowerShell window."
    }
}

if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    throw "Sentinel can only be removed on Windows."
}

Assert-Administrator

$agentExecutable = Join-Path $InstallRoot "Agent\Agent.exe"
$processName = [IO.Path]::GetFileNameWithoutExtension($agentExecutable)

$task = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
if ($task) {
    Stop-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
}

Get-Process -Name $processName -ErrorAction SilentlyContinue |
    Where-Object {
        try {
            $_.Path -eq $agentExecutable
        }
        catch {
            $false
        }
    } |
    Stop-Process -Force

$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($service) {
    if ($service.Status -ne "Stopped") {
        Stop-Service -Name $ServiceName -Force
        $service.WaitForStatus("Stopped", [TimeSpan]::FromSeconds(20))
    }

    & sc.exe delete $ServiceName | Out-Null
}

Remove-Item $InstallRoot -Recurse -Force -ErrorAction SilentlyContinue

if ($RemoveData) {
    Remove-Item $DataRoot -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host "Sentinel removed." -ForegroundColor Green

if (-not $RemoveData -and (Test-Path $DataRoot)) {
    Write-Host "Machine configuration and authorization data were kept at $DataRoot."
}
