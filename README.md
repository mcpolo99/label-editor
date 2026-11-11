# Label Editor

A .NET 9 desktop editor for Biesse **LEdit** label layouts (`MixedData\Label\*.xml`), ported from the
earlier HTML prototype and rebuilt with a layered architecture. It reads and writes the LEdit format
byte-for-byte and shows the label as it prints: fonts, sizes, rotation, Code 128 / Code 39 bar codes
and QR codes. It can fill the label with real part data from generated label sheets.

## Features

- **Open, edit and save** LEdit label layouts. Files written by LEdit round-trip byte-for-byte.
- **WYSIWYG canvas**: text with real fonts, point sizes, bold/italic/underline/strike-out and the four
  LEdit directions; encoded Code 128, Code 39 and QR codes; ellipses, lines, bitmaps, part graphics
  and corner logos.
- **Layout editing**: select, marquee, multi-select, move, resize (box handles and line end points),
  grid snapping (hold **Alt** to move freely), arrow-key nudging (**Shift** = 0.1 mm), Shift-constrained
  drawing, z-order, cut/copy/paste/duplicate between labels, and LEdit's align / same size / space
  commands (the first selected entry is the reference).
- **Lock size** per entry (property panel or Arrange menu): move freely without changing width/height. Stored in a
  `.labeleditor.json` sidecar beside the label, so the LEdit file is unaffected.
- **Version history** instead of `_v1`/`_v2` copies: every Save that changes a label records a version (time, user, full
  LEdit XML, compressed) in the sidecar. The first save over an existing label keeps the original as version 1.
  *File → History…* restores a version (undoable) or opens it as a copy.
- **Undo/redo** for every change. Typing in a property box is one step.
- **Fields by element name**: the field tree comes from `LEditTreeXML.xml` and `ListaOriginaliId.xml`.
  Fields are shown and placeholdered by their element name (`[QMin]`, `[CabCode]`); the translated
  caption is only a tooltip. Drag a field onto the label or double-click it.
- **Preview with data**: *Load data…* takes a label sheet (`*_lbl.xml`) and fills every field from the
  chosen part (PageUp/PageDown to step through the parts).
- **19 LEdit languages**, decoded with their original code pages; picked from the OS language.
- **Safe saving**: every label opens editable. The first Save over an opened file asks *Replace existing?*
  (Replace / Save As / Cancel). Packaged templates and the installed LEdit folder are protected and only offer
  Save As; set `AllowWritingMachineLabels` in settings to allow replacing labels in the LEdit folder.
- **Portable**: self-contained single executable, no installer.

## Data sources

| What | Where |
| --- | --- |
| Installed LEdit (preferred, read-only) | `C:\BIESSE\BiesseWorks\Utils\LEdit` (override: `LeditInstallationPath` in settings) |
| Packaged defaults (fallback) | `Defaults\LEdit` next to the executable (copied from `defaults/` in this repo) |
| Your labels | `Documents\LabelEditor\Labels`, or `UserData\Labels` in portable mode |
| Settings | `%APPDATA%\LabelEditor\settings.json`, or `UserData\settings.json` in portable mode |

**Portable mode:** put an empty file named `portable` next to `LabelEditor.exe`.

## Build, test, run

```powershell
dotnet build
dotnet test
dotnet run --project src/LabelEditor.App -- "path\to\label.xml"
```

Portable release (single file plus `Defaults\`):

```powershell
./build/publish.ps1            # -> artifacts/publish/win-x64
```

## Documentation

- [Architecture](docs/architecture.md): layers, dependency rules, async policy, where things go.
- [LEdit file format](docs/ledit-format.md): the format as reverse-engineered, with evidence.
- [Roadmap](docs/roadmap.md): known gaps and next steps.
