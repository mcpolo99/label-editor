<#
.SYNOPSIS
  Prepares a release: sets the version, regenerates CHANGELOG.md from the commits and tags it.
.DESCRIPTION
  Run on develop with a clean working tree. Afterwards push the branch and the tag:
    git push origin develop --follow-tags
  The tag starts the GitHub "Release" workflow, which builds the installer and the portable zip and publishes
  them as a GitHub release with that version's changelog as notes.
.EXAMPLE
  ./build/release.ps1 -Version 0.3.0
#>
param(
    [Parameter(Mandatory)][string]$Version
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$cliff = "git-cliff@2.10.0"

if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version must look like 1.2.3" }
if (git -C $root status --porcelain) { throw "Commit or stash your changes first." }
if (git -C $root tag --list "v$Version") { throw "Tag v$Version already exists." }

$props = Join-Path $root "Directory.Build.props"
$text = Get-Content $props -Raw
Set-Content $props ([regex]::Replace($text, '<Version>[^<]*</Version>', "<Version>$Version</Version>")) -NoNewline

Push-Location $root
try {
    npx --yes $cliff --tag "v$Version" -o CHANGELOG.md
    if ($LASTEXITCODE -ne 0) { throw "git-cliff failed." }

    git add Directory.Build.props CHANGELOG.md
    git commit -m "chore: release $Version"
    git tag -a "v$Version" -m "Label Editor $Version"
}
finally {
    Pop-Location
}

Write-Host "Tagged v$Version. Push with: git push origin develop --follow-tags" -ForegroundColor Green
