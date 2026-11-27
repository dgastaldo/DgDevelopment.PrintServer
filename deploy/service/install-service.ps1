[CmdletBinding()]
param(
    [string]$InstallDir = $(if ($env:INSTALL_DIR) { $env:INSTALL_DIR } else { 'C:\ProgramData\PrintServer' }),
    [string]$ServiceName = $(if ($env:SERVICE_NAME) { $env:SERVICE_NAME } else { 'PrintServer' }),
    [System.Management.Automation.PSCredential]$Credential,
    [switch]$NoStart,
    [ValidateSet('en', 'it')]
    [string]$Language
)

$ErrorActionPreference = 'Stop'

# Messages come in English (the default) and Italian; the keys are English so the
# catalog stays readable even where a translation is missing.
# Language: -Language en|it, then PRINTSERVER_LANG, then the Windows display language, then English.
$Messages = @{
    en = @{
        RunAsAdministrator = 'Run this script from an elevated PowerShell (Run as administrator).'
        ReleaseMissing     = '{0} not found; install a release first (deploy\personal\update.ps1).'
        ConfigCreated      = 'created {0} from the example: edit it before relying on the service'
        ServiceDescription = 'Watches a folder and sends the files to a printer.'
        ServiceExists      = 'service {0} already exists, leaving its definition unchanged'
        ServiceCreated     = 'service {0} created'
        ServiceStarted     = 'service {0} started'
    }
    it = @{
        RunAsAdministrator = 'Esegui questo script da una PowerShell con privilegi elevati (Esegui come amministratore).'
        ReleaseMissing     = '{0} non trovato; installa prima una versione (deploy\personal\update.ps1).'
        ConfigCreated      = 'creato {0} dall''esempio: modificalo prima di fare affidamento sul servizio'
        ServiceDescription = 'Osserva una cartella e invia i file a una stampante.'
        ServiceExists      = 'il servizio {0} esiste già, la sua definizione resta invariata'
        ServiceCreated     = 'servizio {0} creato'
        ServiceStarted     = 'servizio {0} avviato'
    }
}

function Get-Language {
    foreach ($candidate in @($Language, $env:PRINTSERVER_LANG, (Get-UICulture).TwoLetterISOLanguageName)) {
        if ($candidate -and $candidate.Length -ge 2) {
            $code = $candidate.Substring(0, 2).ToLowerInvariant()
            if ($Messages.ContainsKey($code)) { return $code }
        }
    }
    return 'en'
}

$LanguageCode = Get-Language

function Get-Text([string]$Key, [object[]]$Arguments = @()) {
    $format = $Messages[$LanguageCode][$Key]
    if (-not $format) { $format = $Messages['en'][$Key] }
    return ($format -f $Arguments)
}

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw (Get-Text 'RunAsAdministrator')
}

$executable = Join-Path $InstallDir 'current\PrintServer.Host.exe'
if (-not (Test-Path $executable)) {
    Write-Warning (Get-Text 'ReleaseMissing' @($executable))
}

$configDir = Join-Path $env:ProgramData 'PrintServer'
$configFile = Join-Path $configDir 'appsettings.json'
if (-not (Test-Path $configFile)) {
    New-Item -ItemType Directory -Force $configDir | Out-Null
    Copy-Item (Join-Path $PSScriptRoot 'appsettings.example.json') $configFile
    Write-Output (Get-Text 'ConfigCreated' @($configFile))
}

$parameters = @{
    Name           = $ServiceName
    BinaryPathName = "`"$executable`""
    DisplayName    = 'PrintServer'
    Description    = (Get-Text 'ServiceDescription')
    StartupType    = 'Automatic'
}
if ($PSBoundParameters.ContainsKey('Credential')) { $parameters.Credential = $Credential }

if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
    Write-Output (Get-Text 'ServiceExists' @($ServiceName))
} else {
    New-Service @parameters | Out-Null
    Write-Output (Get-Text 'ServiceCreated' @($ServiceName))
}

& sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/5000/restart/30000 | Out-Null

if (-not $NoStart) {
    Start-Service -Name $ServiceName
    Write-Output (Get-Text 'ServiceStarted' @($ServiceName))
}
