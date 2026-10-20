param(
    [Parameter(Mandatory = $true)][string]$Hostname,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'Server\certs'),
    [Parameter(Mandatory = $true)][string]$PfxPassword
)

$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
$cert = Join-Path $OutputDirectory "$Hostname.crt"
$key = Join-Path $OutputDirectory "$Hostname.key"
$pfx = Join-Path $OutputDirectory "$Hostname.pfx"

tailscale cert --cert-file $cert --key-file $key $Hostname
openssl pkcs12 -export -out $pfx -inkey $key -in $cert -passout "pass:$PfxPassword"

Write-Host "Certificate created: $pfx"
Write-Host 'Add this to Server\appsettings.Production.json (see REMOTE-ACCESS.md):'
Write-Host ('  "Kestrel": { "Endpoints": { "Https": { "Url": "https://0.0.0.0:5257", "Certificate": { "Path": "' + ($pfx -replace '\', '\') + '", "Password": "<password>" } } } }')
