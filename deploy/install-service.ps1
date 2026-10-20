#Requires -RunAsAdministrator
param(
    [string]$ServerPath = (Join-Path $PSScriptRoot 'Server'),
    [switch]$AllowNetwork
)

$ErrorActionPreference = 'Stop'
$serviceName = 'TraderServer'
$exe = Join-Path $ServerPath 'Trader.Web.exe'

if (-not (Test-Path $exe)) {
    throw "Trader.Web.exe not found in $ServerPath"
}

if (Get-Service $serviceName -ErrorAction SilentlyContinue) {
    Stop-Service $serviceName -Force -ErrorAction SilentlyContinue
    sc.exe delete $serviceName | Out-Null
    Start-Sleep -Seconds 2
}

New-Service -Name $serviceName -BinaryPathName "`"$exe`"" -DisplayName 'Trader Server' `
    -Description 'Trader market information server' -StartupType Automatic | Out-Null
sc.exe failure $serviceName reset= 86400 actions= restart/5000/restart/5000/restart/5000 | Out-Null

if ($AllowNetwork) {
    Remove-NetFirewallRule -DisplayName 'Trader Server' -ErrorAction SilentlyContinue
    New-NetFirewallRule -DisplayName 'Trader Server' -Direction Inbound -Protocol TCP -LocalPort 5256, 5257 -Action Allow -Profile Private | Out-Null
    Write-Host 'Firewall rule created for private networks (ports 5256 and 5257).'
}

Start-Service $serviceName
Write-Host "Service '$serviceName' installed and started. It now starts with Windows."
