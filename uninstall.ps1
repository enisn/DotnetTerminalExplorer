# Dotnet Terminal Explorer (dte) - Windows Standalone Uninstaller
# Usage:
#   irm https://raw.githubusercontent.com/enisn/DotnetTerminalExplorer/main/uninstall.ps1 | iex

[CmdletBinding()]
param(
    [string] $InstallDir = "$HOME\.dte"
)

$ErrorActionPreference = "Stop"

Write-Host "==> Uninstalling Dotnet Terminal Explorer (dte)..." -ForegroundColor Yellow

$removed = $false

if (Test-Path $InstallDir) {
    Write-Host "    Removing directory: $InstallDir"
    Remove-Item -Path $InstallDir -Recurse -Force
    $removed = $true
}

# Clean PATH if needed
$binDir = Join-Path $InstallDir "bin"
$userPath = [Environment]::GetEnvironmentVariable("Path", "User")
if ($userPath -and ($userPath -like "*$binDir*")) {
    $pathParts = $userPath -split ';' | Where-Object { $_ -ne "" -and $_ -ne $binDir }
    $newUserPath = $pathParts -join ';'
    [Environment]::SetEnvironmentVariable("Path", $newUserPath, "User")
    Write-Host "    Removed '$binDir' from User PATH." -ForegroundColor Gray
}

Write-Host ""
Write-Host "=================================================================" -ForegroundColor Green
if ($removed) {
    Write-Host " 🗑️  Dotnet Terminal Explorer (dte) successfully uninstalled." -ForegroundColor Green
} else {
    Write-Host " ℹ️  No installation of 'dte' found in '$InstallDir'." -ForegroundColor Yellow
}
Write-Host "=================================================================" -ForegroundColor Green
Write-Host ""
