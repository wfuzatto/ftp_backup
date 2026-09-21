param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$Dist = Join-Path $Root "dist"

if (Test-Path $Dist) {
    Remove-Item $Dist -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $Dist | Out-Null

dotnet restore (Join-Path $Root "ftp_backup.sln")
dotnet publish (Join-Path $Root "src\FtpBackup.Service\FtpBackup.Service.csproj") -c $Configuration -r $Runtime --self-contained true -p:PublishSingleFile=true -o (Join-Path $Dist "service")
dotnet publish (Join-Path $Root "src\FtpBackup.Tray\FtpBackup.Tray.csproj") -c $Configuration -r $Runtime --self-contained true -p:PublishSingleFile=true -o (Join-Path $Dist "tray")

Write-Host "Build concluído em $Dist" -ForegroundColor Green
