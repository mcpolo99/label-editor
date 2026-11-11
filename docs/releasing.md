# Releasing and installing

## Build a release

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
| Administrator rights | Yes: setup starts normally and asks (UAC) when you choose this mode | **No** |
| Windows entry ("Installed apps") + uninstaller | Yes | **No** (nothing in the registry) |
| Start menu / desktop shortcut | Yes / optional | No |
| Settings and labels | `%APPDATA%\LabelEditor`, `Documents\LabelEditor\Labels` | `UserData\` next to the program |

## Update

Run a newer setup and choose the same location (a PC install is found automatically). Setup shows
"update 0.1.0 → 0.2.0", replaces the program files, and **keeps settings, labels and `UserData`**.
Re-running the same version repairs; an older version asks before downgrading.

## Silent / scripted use

```powershell
LabelEditor-Setup-0.2.0.exe /VERYSILENT /SUPPRESSMSGBOXES /ALLUSERS                     # PC install or update (elevated prompt)
LabelEditor-Setup-0.2.0.exe /VERYSILENT /SUPPRESSMSGBOXES /PORTABLE=1 /DIR="E:\LabelEditor" # portable to a USB stick
"C:\Program Files\Label Editor\unins000.exe" /VERYSILENT                                 # uninstall a PC install (elevated)
```

Silent updates do not ask about downgrades. A silent PC install started without `/ALLUSERS` from a normal prompt
raises a UAC prompt and continues elevated.
