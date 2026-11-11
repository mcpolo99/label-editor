<#
.SYNOPSIS
  Publishes a tagged version as a GitHub release, from this PC: builds the installer and portable zip, writes the
  release notes from the changelog and uploads everything with the GitHub CLI.
.DESCRIPTION
  Releases are made by hand only; nothing on GitHub builds or publishes automatically. Run build/release.ps1 first
  (version, CHANGELOG.md, tag), push develop with the tag, then this script.
.EXAMPLE
  ./build/release.ps1 -Version 0.3.0
  git push origin develop --follow-tags
  ./build/publish-release.ps1               # publishes the version in Directory.Build.props
  ./build/publish-release.ps1 -Draft        # as a draft, to check it on GitHub before publishing
#>
param(
    [switch]$Draft
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$cliff = "git-cliff@2.10.0"

Push-Location $root
try {
    $version = ([xml](Get-Content Directory.Build.props)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
    $tag = "v$version"

    if (git status --porcelain) { throw "The working tree has uncommitted changes; the build must match the tag." }
    if ((git rev-parse HEAD) -ne (git rev-parse "$tag^{commit}" 2>$null)) { throw "HEAD is not at $tag. Run build/release.ps1 -Version $version first, or check out the tag." }
    git ls-remote --exit-code --tags origin $tag *> $null
    if ($LASTEXITCODE -ne 0) { throw "Tag $tag is not on GitHub yet. Push it: git push origin develop --follow-tags" }
    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { throw "The GitHub CLI (gh) is needed: https://cli.github.com" }

    & (Join-Path $PSScriptRoot "package.ps1")
    if ($LASTEXITCODE) { throw "Packaging failed." }

    $notes = Join-Path $root "artifacts/release-notes.md"
    npx --yes $cliff --latest --strip header -o $notes
    if ($LASTEXITCODE -ne 0) { throw "git-cliff failed." }

    $assets = @(
        "artifacts/installer/LabelEditor-Setup-$version.exe",
        "artifacts/LabelEditor-$version-portable-win-x64.zip"
    )
    $arguments = @("release", "create", $tag, "--title", "Label Editor $version", "--notes-file", $notes) + $assets
    if ($Draft) { $arguments += "--draft" }
    gh @arguments
    if ($LASTEXITCODE -ne 0) { throw "gh release create failed." }
    Write-Host "Published Label Editor $version." -ForegroundColor Green
}
finally {
    Pop-Location
}
