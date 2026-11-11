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
  grid snapping and **snap-to-entries guides** (edges and centres line up with other entries and the label;
  hold **Alt** to move freely), **rulers** in mm or inch (View menu), arrow-key nudging (**Shift** = 0.1 mm), Shift-constrained
  drawing, z-order, cut/copy/paste/duplicate between labels, and LEdit's align / same size / space
  commands (the first selected entry is the reference). Dragging a handle with several entries selected resizes them
  all. Widths and heights follow each entry's text direction, so a 90° text gets longer along its own reading direction.
- **Dark theme** (View ▸ Theme: System / Light / Dark). In dark mode the label paper is shown soft grey instead of
  bright white; turn that off with View ▸ Theme ▸ Dim label paper. Remembered between runs.
- **Lock size** and **lock position** per entry (property panel, Arrange menu, toolbar, right-click): a size-locked
  entry moves without changing width/height; a position-locked one keeps its X / Y (no dragging, nudging or
  aligning) and only resizes from its right and bottom. Stored in a `.labeleditor.json` sidecar beside the label,
  so the LEdit file is unaffected.
- **Version history** instead of `_v1`/`_v2` copies: every Save that changes a label records a version (time, user, full
  LEdit XML, compressed) in the sidecar. The first save over an existing label keeps the original as version 1.
  *File → History…* restores a version (undoable) or opens it as a copy.
- **Text that does not fit**: a fixed-size text wider than its field at print size is outlined in orange on the
  canvas (fields without a sample value are not judged). *File ▸ Warn when text does not fit* also checks before
  saving, with the sample values and every loaded preview record.
- **Filler characters** (LEdit's "Insert filler characters"): leading zeros, leading or trailing blanks pad a value
  up to the element's character count.
- **File ▸ Open recent** lists the last ten labels; missing files are removed from the list.
- **Undo/redo** for every change. Typing in a property box is one step.
- **Fields by element name**: the field tree comes from `LEditTreeXML.xml` and `ListaOriginaliId.xml`.
  Fields are shown and placeholdered by their element name (`[QMin]`, `[CabCode]`); the translated
  caption is only a tooltip. Drag a field onto the label or double-click it.
- **Preview with data**: *Load data…* takes a label sheet (`*_lbl.xml`) and fills every field from the
  chosen part (PageUp/PageDown to step through the parts).
- **19 LEdit languages**, decoded with their original code pages; picked from the OS language.
- **Safe saving**: every label opens editable. The first Save over an opened file asks *Replace existing?*
  (Replace / Save As / Cancel). The installed LEdit folder is protected and only offers
  Save As; set `AllowWritingMachineLabels` in settings to allow replacing labels in the LEdit folder.
- **Installer with two modes**: install on this PC (Windows entry, uninstaller) or portable extraction to any folder
  or USB stick (no Windows entry). Running a newer setup updates either in place and keeps settings and labels.

## Data sources

| What | Where |
| --- | --- |
| Installed LEdit (read-only): fields, languages, machine labels | `C:\BIESSE\BiesseWorks\Utils\LEdit` (change with **File ▸ Choose LEdit folder…**, stored as `LeditInstallationPath`) |
| Your labels | `Documents\LabelEditor\Labels`, or `UserData\Labels` in portable mode |
| Settings | `%APPDATA%\LabelEditor\settings.json`, or `UserData\settings.json` in portable mode |

The editor ships **none of Biesse's LEdit files**; it reads them from the installed LEdit. Without one it still opens
and edits labels, with an empty field tree, until you choose an LEdit folder.

**Portable mode:** put an empty file named `portable` next to `LabelEditor.exe`.

## Build, test, run

```powershell
dotnet build
dotnet test   # tests that need Biesse's own LEdit files run when LEdit is found, else they are skipped
dotnet run --project src/LabelEditor.App -- "path\to\label.xml"
```

Release (installer + portable zip, see [Releasing](docs/releasing.md)):

```powershell
./build/package.ps1            # -> artifacts/installer/LabelEditor-Setup-<version>.exe
```

## Documentation

- [Architecture](docs/architecture.md): layers, dependency rules, async policy, where things go.
- [LEdit file format](docs/ledit-format.md): the format as reverse-engineered, with evidence.
- [Releasing and installing](docs/releasing.md): installer, portable mode, updates.
- [Roadmap](docs/roadmap.md): known gaps and next steps.
