#Requires -RunAsAdministrator
$ErrorActionPreference = 'Stop'
$serviceName = 'TraderServer'

if (Get-Service $serviceName -ErrorAction SilentlyContinue) {
    Stop-Service $serviceName -Force -ErrorAction SilentlyContinue
    sc.exe delete $serviceName | Out-Null
    Write-Host "Service '$serviceName' removed. The database and settings were not touched."
} else {
    Write-Host "Service '$serviceName' is not installed."
}

Remove-NetFirewallRule -DisplayName 'Trader Server' -ErrorAction SilentlyContinue
