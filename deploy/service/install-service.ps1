[CmdletBinding()]
param(
    [string]$InstallDir = $(if ($env:INSTALL_DIR) { $env:INSTALL_DIR } else { 'C:\ProgramData\PrintServer' }),
    [string]$ServiceName = $(if ($env:SERVICE_NAME) { $env:SERVICE_NAME } else { 'PrintServer' }),
    [System.Management.Automation.PSCredential]$Credential,
    [switch]$NoStart
)

$ErrorActionPreference = 'Stop'

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this script from an elevated PowerShell (Run as administrator).'
}

$executable = Join-Path $InstallDir 'current\PrintServer.Host.exe'
if (-not (Test-Path $executable)) {
    Write-Warning "$executable not found; install a release first (deploy\personal\update.ps1)."
}

$configDir = Join-Path $env:ProgramData 'PrintServer'
$configFile = Join-Path $configDir 'appsettings.json'
if (-not (Test-Path $configFile)) {
    New-Item -ItemType Directory -Force $configDir | Out-Null
    Copy-Item (Join-Path $PSScriptRoot 'appsettings.example.json') $configFile
    Write-Output "created $configFile from the example: edit it before relying on the service"
}

$parameters = @{
    Name           = $ServiceName
    BinaryPathName = "`"$executable`""
    DisplayName    = 'PrintServer'
    Description    = 'Watches a folder and sends the files to a printer.'
    StartupType    = 'Automatic'
}
if ($PSBoundParameters.ContainsKey('Credential')) { $parameters.Credential = $Credential }

if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
    Write-Output "service $ServiceName already exists, leaving its definition unchanged"
} else {
    New-Service @parameters | Out-Null
    Write-Output "service $ServiceName created"
}

& sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/5000/restart/30000 | Out-Null

if (-not $NoStart) {
    Start-Service -Name $ServiceName
    Write-Output "service $ServiceName started"
}
