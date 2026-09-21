param(
    [string]$InstallDir = "$env:ProgramFiles\FtpBackup"
)

$ErrorActionPreference = "Stop"

function Assert-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw "Execute este script em PowerShell como Administrador."
    }
}

Assert-Administrator

$Root = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot "build.ps1")

$ServiceName = "FtpBackupService"
$DataDir = Join-Path $env:ProgramData "FtpBackup"
$ServiceDir = Join-Path $InstallDir "Service"
$TrayDir = Join-Path $InstallDir "Tray"

Get-Process "FtpBackup.Tray" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    if ($existing.Status -ne "Stopped") {
        Stop-Service -Name $ServiceName -Force
    }
    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 2
}

New-Item -ItemType Directory -Force -Path $ServiceDir | Out-Null
New-Item -ItemType Directory -Force -Path $TrayDir | Out-Null
New-Item -ItemType Directory -Force -Path $DataDir | Out-Null

Remove-Item (Join-Path $ServiceDir "*") -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $TrayDir "*") -Recurse -Force -ErrorAction SilentlyContinue

Copy-Item (Join-Path $Root "dist\service\*") $ServiceDir -Recurse -Force
Copy-Item (Join-Path $Root "dist\tray\*") $TrayDir -Recurse -Force

$usersSid = New-Object System.Security.Principal.SecurityIdentifier("S-1-5-32-545")
$usersAccount = $usersSid.Translate([System.Security.Principal.NTAccount])
$acl = Get-Acl $DataDir
$rule = New-Object System.Security.AccessControl.FileSystemAccessRule($usersAccount, "Modify", "ContainerInherit,ObjectInherit", "None", "Allow")
$acl.SetAccessRule($rule)
Set-Acl -Path $DataDir -AclObject $acl

$serviceExe = Join-Path $ServiceDir "FtpBackup.Service.exe"
$trayExe = Join-Path $TrayDir "FtpBackup.Tray.exe"

New-Service -Name $ServiceName -BinaryPathName ('"' + $serviceExe + '"') -DisplayName "FTP Backup Service" -Description "Compacta pastas em ZIP e envia backups agendados para FTP/FTPS." -StartupType Automatic | Out-Null
sc.exe config $ServiceName start= delayed-auto | Out-Null
sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/15000/restart/60000 | Out-Null
sc.exe failureflag $ServiceName 1 | Out-Null

Start-Service -Name $ServiceName

$runKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
New-Item -Path $runKey -Force | Out-Null
Set-ItemProperty -Path $runKey -Name "FtpBackupTray" -Value ('"' + $trayExe + '" --minimized')

Start-Process -FilePath $trayExe

Write-Host ""
Write-Host "FTP Backup instalado com sucesso." -ForegroundColor Green
Write-Host "Serviço: $ServiceName"
Write-Host "Aplicação: $trayExe"
Write-Host "Dados: $DataDir"
