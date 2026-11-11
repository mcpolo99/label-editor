# Releasing and installing

## Publish a release

Releases are made **by hand only**: nothing on GitHub builds, tests or publishes automatically (there are no
GitHub Actions workflows).

```powershell
./build/release.ps1 -Version 0.3.0        # on develop: version, CHANGELOG.md, commit "chore: release 0.3.0", tag v0.3.0
git push origin develop --follow-tags
./build/publish-release.ps1               # tests, installer + portable zip, GitHub release (add -Draft to review first)
```

`publish-release.ps1` refuses to run unless the working tree is clean, `HEAD` is at the version's tag and the tag is
on GitHub, so the published files always match the tagged source. It needs the GitHub CLI (`gh`). The release notes
are that version's section of the changelog.

**The changelog is generated** from the commit messages by [git-cliff](https://git-cliff.org) (`cliff.toml`):
`feature:` commits are listed under *Features*, `fix:` under *Fixes*, `refactor:` under *Changes*, and so on, so
writing good commit messages is all it takes. Preview the next release's notes with
`npx git-cliff@2.10.0 --unreleased`.

Tests run locally (`dotnet test`, and as part of `package.ps1`). Tests that need Biesse's own LEdit files run when
LEdit is installed, or point `LABELEDITOR_LEDIT_DATA` at an LEdit folder; set it to `none` to run as on a PC without
LEdit (those tests are then skipped, since Biesse's files are not in the repository).

## Build locally

```powershell
./build/package.ps1                  # package the version in Directory.Build.props
./build/package.ps1 -Version 0.2.0   # bump the version first (commit the change)
```

The script runs all tests, publishes a self-contained build, and writes:

| Output | Use |
| --- | --- |
| `artifacts/installer/LabelEditor-Setup-<version>.exe` | The installer, for both PC and portable use (below) |
| `artifacts/LabelEditor-<version>-portable-win-x64.zip` | Unzip anywhere; already in portable mode |
| `artifacts/publish/win-x64/` | The raw build |

Inno Setup is found automatically through the registry (Inno Setup 7 or 6). Override with `$env:ISCC` or
`-Iscc <path>`. The version is stamped into the exe together with the git commit (`0.2.0+8bb57b1`), and shown in
the title bar and under *Help → About*.

## Install

The first page of the setup asks for the installation type.

| | Install on this PC | Portable |
| --- | --- | --- |
| Where | `C:\Program Files\Label Editor`, for all users | Any folder: USB stick, network share, fixed folder |
| Administrator rights | Setup asks for them automatically (UAC) when it starts | Same setup, so also asked; for PCs without admin rights use the portable **zip** instead (no setup needed) |
| Windows entry ("Installed apps") + uninstaller | Yes | **No** (nothing in the registry) |
| Start menu / desktop shortcut | Yes / optional | No |
| Settings and labels | `%APPDATA%\LabelEditor`, `Documents\LabelEditor\Labels` | `UserData\` next to the program |

## Update

Run a newer setup and choose the same location (a PC install is found automatically). Setup shows
"update 0.1.0 → 0.2.0", replaces the program files, and **keeps settings, labels and `UserData`**.
Re-running the same version repairs; an older version asks before downgrading.

## Silent / scripted use

```powershell
LabelEditor-Setup-0.2.0.exe /VERYSILENT /SUPPRESSMSGBOXES                               # PC install or update
LabelEditor-Setup-0.2.0.exe /VERYSILENT /SUPPRESSMSGBOXES /PORTABLE=1 /DIR="E:\LabelEditor" # portable to a USB stick
"C:\Program Files\Label Editor\unins000.exe" /VERYSILENT                                 # uninstall a PC install (elevated)
```

Silent updates do not ask about downgrades. Silent runs from a normal (non-admin) prompt still show the UAC prompt;
run them from an elevated prompt for fully unattended installs.
