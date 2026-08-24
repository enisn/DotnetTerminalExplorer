[CmdletBinding()]
param(
    [string] $Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$repositoryRoot = $PSScriptRoot
$projectPath = Join-Path $repositoryRoot "src/DotnetTerminalExplorer/DotnetTerminalExplorer.csproj"
$packageDirectory = Join-Path $repositoryRoot "nupkg"

dotnet pack $projectPath `
    --configuration $Configuration `
    --include-symbols `
    --include-source `
    --output $packageDirectory

if ($LASTEXITCODE -ne 0) {
    throw "dotnet pack failed with exit code $LASTEXITCODE."
}
