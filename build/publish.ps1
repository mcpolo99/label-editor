<#
.SYNOPSIS
  Publishes a portable, self-contained Label Editor build.
.DESCRIPTION
  Produces artifacts/publish/<runtime>/LabelEditor.exe plus Defaults\LEdit. Copy the folder anywhere;
  add an empty "portable" file next to the exe to keep settings and labels inside the folder.
#>
param(
    [string]$Runtime = "win-x64",
    [string]$Configuration = "Release",
    [switch]$Portable
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$output = Join-Path $root "artifacts/publish/$Runtime"

dotnet test (Join-Path $root "LabelEditor.sln") -c $Configuration
if ($LASTEXITCODE -ne 0) { throw "Tests failed; not publishing." }

dotnet publish (Join-Path $root "src/LabelEditor.App/LabelEditor.App.csproj") `
    -c $Configuration -r $Runtime --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=embedded `
    -o $output
if ($LASTEXITCODE -ne 0) { throw "Publish failed." }

if ($Portable) {
    New-Item -ItemType File -Force (Join-Path $output "portable") | Out-Null
}

Write-Host "Published to $output"
