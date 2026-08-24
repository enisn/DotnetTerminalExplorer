[CmdletBinding()]
param(
    [string] $Configuration = "Release",
    [string] $Version
)

$ErrorActionPreference = "Stop"

$repositoryRoot = $PSScriptRoot
$projectPath = Join-Path $repositoryRoot "src/DotnetTerminalExplorer/DotnetTerminalExplorer.csproj"
$packageDirectory = Join-Path $repositoryRoot "nupkg"
$packScript = Join-Path $repositoryRoot "pack.ps1"

$packArgs = @{
    Configuration = $Configuration
}

if (-not [string]::IsNullOrWhiteSpace($Version)) {
    $packArgs["Version"] = $Version
}

& $packScript @packArgs

$versionArgs = @($projectPath, "-getProperty:Version")
if (-not [string]::IsNullOrWhiteSpace($Version)) {
    $normalizedVersion = $Version.TrimStart("v", "V")
    $versionArgs += "-p:Version=$normalizedVersion"
}

$version = (dotnet msbuild @versionArgs).Trim()
$installedTools = dotnet tool list --global
$isInstalled = $installedTools -match "^dotnetterminalexplorer\s"

if ($isInstalled) {
    dotnet tool uninstall --global DotnetTerminalExplorer

    if ($LASTEXITCODE -ne 0) {
        throw "Unable to uninstall the existing DotnetTerminalExplorer tool."
    }
}

dotnet tool install --global DotnetTerminalExplorer `
    --add-source $packageDirectory `
    --version $version `
    --ignore-failed-sources

if ($LASTEXITCODE -ne 0) {
    throw "Unable to install DotnetTerminalExplorer $version from '$packageDirectory'."
}
