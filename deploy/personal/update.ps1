[CmdletBinding()]
param(
    [switch]$Rollback,
    [string]$Repo = $(if ($env:REPO) { $env:REPO } else { 'dgastaldo/DgDevelopment.PrintServer' }),
    [string]$InstallDir = $(if ($env:INSTALL_DIR) { $env:INSTALL_DIR } else { 'C:\ProgramData\PrintServer' }),
    [string]$ServiceName = $(if ($env:SERVICE_NAME) { $env:SERVICE_NAME } else { 'PrintServer' }),
    [string]$HealthUrl = $env:HEALTH_URL,
    [int]$HealthTimeout = $(if ($env:HEALTH_TIMEOUT) { [int]$env:HEALTH_TIMEOUT } else { 30 }),
    [string]$TagPrefix = 'personal/v'
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$Current = Join-Path $InstallDir 'current'
$Previous = Join-Path $InstallDir 'previous'
$VersionFile = Join-Path $InstallDir '.version'
$PreviousVersionFile = Join-Path $InstallDir '.version.previous'
$FailedFile = Join-Path $InstallDir '.failed-tag'
$LockFile = Join-Path $InstallDir '.update.lock'

function Write-Log($Message) {
    $timestamp = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    Write-Output "$timestamp $Message"
}

function Restart-PrintService {
    $service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($null -eq $service) {
        Write-Log "service $ServiceName not found, restart it manually"
        return
    }
    if ($service.Status -ne 'Stopped') { Stop-Service -Name $ServiceName -Force }
    Start-Service -Name $ServiceName
}

function Test-Healthy {
    if ([string]::IsNullOrEmpty($HealthUrl)) { return $true }
    for ($i = 0; $i -lt $HealthTimeout; $i++) {
        try {
            Invoke-WebRequest -Uri $HealthUrl -UseBasicParsing | Out-Null
            return $true
        } catch {
            Start-Sleep -Seconds 1
        }
    }
    return $false
}

function Invoke-Rollback {
    if (-not (Test-Path $Previous)) {
        Write-Log 'no previous version to roll back to'
        exit 1
    }
    $failed = Join-Path $InstallDir 'failed'
    if (Test-Path $failed) { Remove-Item $failed -Recurse -Force }
    $service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($null -ne $service -and $service.Status -ne 'Stopped') { Stop-Service -Name $ServiceName -Force }
    Move-Item $Current $failed
    Move-Item $Previous $Current
    if (Test-Path $PreviousVersionFile) { Move-Item $PreviousVersionFile $VersionFile -Force }
    Remove-Item $failed -Recurse -Force
    Restart-PrintService
    Write-Log ("rolled back to {0}" -f (Get-Content $VersionFile -ErrorAction SilentlyContinue))
}

New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
try {
    $lock = [System.IO.File]::Open($LockFile, 'OpenOrCreate', 'ReadWrite', 'None')
} catch {
    Write-Log 'another update is running'
    exit 0
}

$work = $null
try {
    if ($Rollback) {
        Invoke-Rollback
        exit 0
    }

    $releases = Invoke-RestMethod -Uri "https://api.github.com/repos/$Repo/releases" -Headers @{ 'User-Agent' = 'printserver-updater' }
    $latest = $releases |
        Where-Object { $_.prerelease -and $_.tag_name.StartsWith($TagPrefix) } |
        Sort-Object { [datetime]$_.published_at } |
        Select-Object -Last 1

    if ($null -eq $latest) {
        Write-Log "no $TagPrefix* release found"
        exit 0
    }

    $tag = $latest.tag_name
    $installed = if (Test-Path $VersionFile) { (Get-Content $VersionFile -Raw).Trim() } else { '' }
    if ($tag -eq $installed) {
        Write-Log "already up to date ($tag)"
        exit 0
    }
    $failedTag = if (Test-Path $FailedFile) { (Get-Content $FailedFile -Raw).Trim() } else { '' }
    if ($tag -eq $failedTag) {
        Write-Log "$tag failed its health check earlier, skipping until a newer release"
        exit 0
    }

    $archiveName = 'printserver-win-x64.zip'
    $archive = $latest.assets | Where-Object { $_.name -eq $archiveName } | Select-Object -First 1
    $sums = $latest.assets | Where-Object { $_.name -eq 'SHA256SUMS' } | Select-Object -First 1
    if ($null -eq $archive -or $null -eq $sums) {
        Write-Log "release $tag has no $archiveName or SHA256SUMS"
        exit 1
    }

    $work = Join-Path ([IO.Path]::GetTempPath()) ([Guid]::NewGuid().ToString())
    New-Item -ItemType Directory -Path $work | Out-Null
    Write-Log "downloading $tag (win-x64)"
    Invoke-WebRequest -Uri $archive.browser_download_url -OutFile (Join-Path $work $archiveName) -UseBasicParsing
    Invoke-WebRequest -Uri $sums.browser_download_url -OutFile (Join-Path $work 'SHA256SUMS') -UseBasicParsing

    $line = Get-Content (Join-Path $work 'SHA256SUMS') | Where-Object { $_ -match "\s$([regex]::Escape($archiveName))$" } | Select-Object -First 1
    $expected = if ($line) { ($line -split '\s+')[0] } else { '' }
    $actual = (Get-FileHash -Algorithm SHA256 -Path (Join-Path $work $archiveName)).Hash
    if ([string]::IsNullOrEmpty($expected) -or $expected.ToLower() -ne $actual.ToLower()) {
        Write-Log "checksum mismatch for $archiveName, aborting"
        exit 1
    }

    $extract = Join-Path $work 'extract'
    Expand-Archive -Path (Join-Path $work $archiveName) -DestinationPath $extract
    $newDir = Join-Path $extract 'printserver-win-x64'
    if (-not (Test-Path (Join-Path $newDir 'PrintServer.Host.exe'))) {
        Write-Log 'archive does not contain PrintServer.Host.exe'
        exit 1
    }

    $service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($null -ne $service -and $service.Status -ne 'Stopped') { Stop-Service -Name $ServiceName -Force }

    if (Test-Path $Previous) { Remove-Item $Previous -Recurse -Force }
    if (Test-Path $Current) { Move-Item $Current $Previous }
    if (Test-Path $VersionFile) { Copy-Item $VersionFile $PreviousVersionFile -Force }
    Move-Item $newDir $Current
    Set-Content -Path $VersionFile -Value $tag -NoNewline

    Restart-PrintService
    if (Test-Healthy) {
        Write-Log "updated to $tag"
    } else {
        Write-Log "health check failed after updating to $tag, rolling back"
        Set-Content -Path $FailedFile -Value $tag -NoNewline
        Invoke-Rollback
        exit 1
    }
} finally {
    if ($work -and (Test-Path $work)) { Remove-Item $work -Recurse -Force }
    $lock.Close()
}
