# Dotnet Terminal Explorer (dte) - Windows Standalone Installer
# Usage:
#   irm https://raw.githubusercontent.com/enisn/DotnetTerminalExplorer/main/install.ps1 | iex

[CmdletBinding()]
param(
    [string] $Version = "latest",
    [string] $InstallDir = "$HOME\.dte\bin",
    [string] $GitHubToken = $env:GITHUB_TOKEN,
    [string] $LocalArchive = ""
)

$ErrorActionPreference = "Stop"
$Repo = "enisn/DotnetTerminalExplorer"

function Show-ErrorAndFallback {
    param([string] $Message)
    Write-Host ""
    Write-Host "=================================================================" -ForegroundColor Yellow
    Write-Host " ⚠️  $Message" -ForegroundColor Yellow
    Write-Host "=================================================================" -ForegroundColor Yellow
    Write-Host ""
    Write-Host " Standalone native binaries are not available for your architecture,"
    Write-Host " but you can still run Dotnet Terminal Explorer using the .NET tool:"
    Write-Host ""
    Write-Host "   dotnet tool install --global DotnetTerminalExplorer" -ForegroundColor Cyan
    Write-Host ""
    Write-Host " For more information, visit: https://github.com/$Repo"
    Write-Host "=================================================================" -ForegroundColor Yellow
    Write-Host ""
    exit 1
}

# 1. Detect CPU Architecture
$arch = $env:PROCESSOR_ARCHITECTURE
$rid = switch ($arch) {
    "AMD64" { "win-x64" }
    "ARM64" { "win-arm64" }
    Default {
        # Check through .NET RuntimeInformation if available
        try {
            $osArch = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
            if ($osArch -eq "X64") { "win-x64" }
            elseif ($osArch -eq "Arm64") { "win-arm64" }
            else { Show-ErrorAndFallback "Unsupported CPU architecture: '$arch'" }
        }
        catch {
            Show-ErrorAndFallback "Unsupported CPU architecture: '$arch'"
        }
    }
}

Write-Host "==> Installing Dotnet Terminal Explorer (dte)..." -ForegroundColor Green
Write-Host "    Platform detected: $rid"
Write-Host "    Target directory:  $InstallDir"

# 2. Ensure Install Directory Exists
if (-not (Test-Path $InstallDir)) {
    New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
}

# 3. Resolve & Download Archive
$tempDir = Join-Path ([System.IO.Path]::GetTempPath()) ([System.Guid]::NewGuid().ToString())
New-Item -ItemType Directory -Path $tempDir -Force | Out-Null

try {
    $archivePath = Join-Path $tempDir "dte.zip"

    if ($LocalArchive -and (Test-Path $LocalArchive)) {
        Write-Host "    Using local archive: $LocalArchive"
        $archivePath = $LocalArchive
    }
    else {
        $headers = @{ "User-Agent" = "dte-installer" }
        if ($GitHubToken) {
            $headers["Authorization"] = "Bearer $GitHubToken"
        }

        if ($Version -eq "latest") {
            try {
                $apiUrl = "https://api.github.com/repos/$Repo/releases/latest"
                $release = Invoke-RestMethod -Uri $apiUrl -Headers $headers -UseBasicParsing -ErrorAction Stop
                $resolvedTag = $release.tag_name
                if ($resolvedTag) {
                    $Version = $resolvedTag
                }
            }
            catch {
                # Fall back to direct download if API rate limited / unreachable
            }
        }

        $downloadUrl = if ($Version -eq "latest") {
            "https://github.com/$Repo/releases/latest/download/dte-latest-$rid.zip"
        } else {
            "https://github.com/$Repo/releases/download/$Version/dte-$Version-$rid.zip"
        }

        Write-Host "    Downloading release asset for $rid..."
        try {
            Invoke-WebRequest -Uri $downloadUrl -OutFile $archivePath -Headers $headers -UseBasicParsing -ErrorAction Stop
        }
        catch {
            Show-ErrorAndFallback "Failed to download release asset from $downloadUrl"
        }
    }

    # 4. Extract and Install
    Write-Host "    Extracting binary..."
    $extractDir = Join-Path $tempDir "extracted"
    Expand-Archive -Path $archivePath -DestinationPath $extractDir -Force

    # Find dte.exe or DotnetTerminalExplorer.exe
    $exePath = Join-Path $extractDir "dte.exe"
    if (-not (Test-Path $exePath)) {
        $exePath = Join-Path $extractDir "DotnetTerminalExplorer.exe"
    }

    if (Test-Path $exePath) {
        Copy-Item -Path $exePath -Destination (Join-Path $InstallDir "dte.exe") -Force
    }
    else {
        throw "Executable binary not found in archive."
    }

    # Copy any companion dlls / assets
    Get-ChildItem -Path $extractDir -Filter "*.dll" | ForEach-Object {
        Copy-Item -Path $_.FullName -Destination $InstallDir -Force
    }

    Write-Host ""
    Write-Host "=================================================================" -ForegroundColor Green
    Write-Host " 🎉 Dotnet Terminal Explorer successfully installed to:" -ForegroundColor Green
    Write-Host "    $InstallDir\dte.exe" -ForegroundColor Cyan
    Write-Host "=================================================================" -ForegroundColor Green

    # 5. Check and update User PATH
    $userPath = [Environment]::GetEnvironmentVariable("Path", "User")
    $pathParts = $userPath -split ';' | Where-Object { $_ -ne "" }

    if ($pathParts -notcontains $InstallDir) {
        Write-Host ""
        Write-Host " Adding '$InstallDir' to your User PATH..." -ForegroundColor Yellow
        $newUserPath = if ($userPath) { "$userPath;$InstallDir" } else { $InstallDir }
        [Environment]::SetEnvironmentVariable("Path", $newUserPath, "User")
        $env:Path = "$env:Path;$InstallDir"
        Write-Host " PATH updated! (You may need to restart existing terminal windows)" -ForegroundColor Gray
    }

    Write-Host ""
    Write-Host " Run 'dte' to start exploring your directories!" -ForegroundColor Green
    Write-Host ""
}
finally {
    if (Test-Path $tempDir) {
        Remove-Item -Path $tempDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}
