<#
.SYNOPSIS
  Builds a release: tests, self-contained publish, portable zip and the Inno Setup installer.
.DESCRIPTION
  The product version lives in Directory.Build.props (<Version>). Pass -Version to bump it first.
  Outputs (artifacts/):
    publish/win-x64/                         the app (LabelEditor.exe + Defaults\)
    LabelEditor-<version>-portable-win-x64.zip  unzip anywhere; already in portable mode
    installer/LabelEditor-Setup-<version>.exe   PC install or portable extraction, with in-place updates
.EXAMPLE
  ./build/package.ps1                  # package the current version
  ./build/package.ps1 -Version 0.2.0   # bump the version, then package
#>
param(
    [string]$Version,
    [string]$Runtime = "win-x64",
    [switch]$SkipTests,
    [switch]$SkipInstaller,
    [string]$Iscc
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$props = Join-Path $root "Directory.Build.props"
$artifacts = Join-Path $root "artifacts"

# ---------------------------------------------------------------- version
if ($Version) {
    if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version must look like 1.2.3" }
    $text = Get-Content $props -Raw
    $text = [regex]::Replace($text, '<Version>[^<]*</Version>', "<Version>$Version</Version>")
    Set-Content $props $text -NoNewline
    Write-Host "Version set to $Version in Directory.Build.props (commit it)." -ForegroundColor Yellow
}
$Version = ([xml](Get-Content $props)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$revision = (git -C $root rev-parse --short HEAD 2>$null)
if (-not $revision) { $revision = "local" }
if (git -C $root status --porcelain 2>$null) { $revision = "$revision-dirty" }
Write-Host "Packaging Label Editor $Version ($revision)" -ForegroundColor Cyan

# ---------------------------------------------------------------- tests
if (-not $SkipTests) {
    dotnet test (Join-Path $root "LabelEditor.sln") -c Release
    if ($LASTEXITCODE -ne 0) { throw "Tests failed; not packaging." }
}

# ---------------------------------------------------------------- publish
$publish = Join-Path $artifacts "publish/$Runtime"
if (Test-Path $publish) { Remove-Item -Recurse -Force $publish }
dotnet publish (Join-Path $root "src/LabelEditor.App/LabelEditor.App.csproj") `
    -c Release -r $Runtime --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=embedded `
    -p:GenerateDocumentationFile=false -p:Version=$Version -p:SourceRevisionId=$revision `
    -o $publish
if ($LASTEXITCODE -ne 0) { throw "Publish failed." }

# ---------------------------------------------------------------- portable zip
$zip = Join-Path $artifacts "LabelEditor-$Version-portable-$Runtime.zip"
$staging = Join-Path $artifacts "staging/LabelEditor"
if (Test-Path (Split-Path $staging)) { Remove-Item -Recurse -Force (Split-Path $staging) }
New-Item -ItemType Directory -Force $staging | Out-Null
Copy-Item -Recurse (Join-Path $publish "*") $staging
Set-Content (Join-Path $staging "portable") "Label Editor portable mode: settings and labels are stored in the UserData folder next to the program. Delete this file to use the Windows user profile instead."
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path $staging -DestinationPath $zip
Remove-Item -Recurse -Force (Split-Path $staging)
Write-Host "Portable zip: $zip"

# ---------------------------------------------------------------- installer
if ($SkipInstaller) { return }

function Find-Iscc {
    if ($Iscc) { return $Iscc }
    if ($env:ISCC -and (Test-Path $env:ISCC)) { return $env:ISCC }
    $keys = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*",
            "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*",
            "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*"
    $found = Get-ItemProperty $keys -ErrorAction SilentlyContinue |
        Where-Object { $_.DisplayName -like "Inno Setup*" -and $_.InstallLocation } |
        Sort-Object { [version]($_.DisplayVersion -replace '[^\d.].*$', '') } -Descending |
        ForEach-Object { Join-Path $_.InstallLocation "ISCC.exe" } |
        Where-Object { Test-Path $_ } | Select-Object -First 1
    if ($found) { return $found }
    foreach ($candidate in "${env:ProgramFiles(x86)}\Inno Setup 7\ISCC.exe", "$env:ProgramFiles\Inno Setup 7\ISCC.exe",
                           "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe") {
        if (Test-Path $candidate) { return $candidate }
    }
    throw "Inno Setup (ISCC.exe) not found. Install Inno Setup, set `$env:ISCC, or pass -Iscc <path>."
}

$compiler = Find-Iscc
Write-Host "Inno Setup: $compiler"
$installerOut = Join-Path $artifacts "installer"
& $compiler /Qp "/DAppVersion=$Version" "/DSourceDir=$publish" "/DOutputDir=$installerOut" (Join-Path $root "installer/LabelEditor.iss")
if ($LASTEXITCODE -ne 0) { throw "Installer build failed." }
Write-Host "Installer: $(Join-Path $installerOut "LabelEditor-Setup-$Version.exe")" -ForegroundColor Green
