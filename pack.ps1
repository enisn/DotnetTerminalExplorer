[CmdletBinding()]
param(
    [string] $Configuration = "Release",
    [string] $Version
)

$ErrorActionPreference = "Stop"

$repositoryRoot = $PSScriptRoot
$projectPath = Join-Path $repositoryRoot "src/DotnetTerminalExplorer/DotnetTerminalExplorer.csproj"
$packageDirectory = Join-Path $repositoryRoot "nupkg"

$packArgs = @(
    "pack",
    $projectPath,
    "--configuration", $Configuration,
    "--include-symbols",
    "--include-source",
    "--output", $packageDirectory
)

if (-not [string]::IsNullOrWhiteSpace($Version)) {
    $normalizedVersion = $Version.TrimStart("v", "V")
    $packArgs += "-p:Version=$normalizedVersion"
}

dotnet @packArgs

if ($LASTEXITCODE -ne 0) {
    throw "dotnet pack failed with exit code $LASTEXITCODE."
}
