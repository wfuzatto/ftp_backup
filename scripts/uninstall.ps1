param(
    [switch]$PurgeData,
    [string]$InstallDir = "$env:ProgramFiles\FtpBackup"
)

$ErrorActionPreference = "Stop"

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Execute este script em PowerShell como Administrador."
}

$ServiceName = "FtpBackupService"

Get-Process "FtpBackup.Tray" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($service) {
    if ($service.Status -ne "Stopped") {
        Stop-Service -Name $ServiceName -Force
    }
    sc.exe delete $ServiceName | Out-Null
}

$runKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
Remove-ItemProperty -Path $runKey -Name "FtpBackupTray" -ErrorAction SilentlyContinue

if (Test-Path $InstallDir) {
    Remove-Item $InstallDir -Recurse -Force
}

if ($PurgeData) {
    $dataDir = Join-Path $env:ProgramData "FtpBackup"
    if (Test-Path $dataDir) {
        Remove-Item $dataDir -Recurse -Force
    }
}

Write-Host "FTP Backup removido." -ForegroundColor Green
if (-not $PurgeData) {
    Write-Host "Configurações e logs preservados em $env:ProgramData\FtpBackup"
}
