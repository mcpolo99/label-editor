# LEdit label format (as reverse-engineered)

Based on 28 real label files, LEdit's own preview bitmaps, the LEdit language tables and the strings in
`LEdit.exe` and `BarCode.dll`. "Verified" means checked against LEdit output; "inferred" means consistent
with all samples but not confirmed by writing a file in LEdit.

## File

- ISO-8859-1. LEdit writes `<?xml version="1.0" encoding="ISO-8859-1"?>` + CRLF, then the **whole document
  on one line**, then CRLF. Every element value is wrapped in CDATA; empty elements are written as `<Field .../>`
  (no space before the slash). *Verified: the editor reproduces 15 LEdit-written files byte-for-byte.*
- Characters outside ISO-8859-1 cannot be stored (CDATA cannot escape them); the editor refuses to save them.

## Coordinates

- Layout units are 1/1000 of the label unit (`Miscellaneous/@unit`: 0 = mm, 1 = inch).
- Y points **up**: `Field/@top` and `@height` are negative for anything below the top edge. The editor uses
  Y down internally and negates on read and write. *Verified against the preview bitmaps.*
- `Field` numbers are 8 characters including the sign: `00084000`, `-0014000`.
- Lines: the field is the segment from (left, top) to (left+width, top+height).
- `Miscellaneous/@grid` is the grid step in layout units (1000 = 1 mm); 0 = no grid.

## Entries (`type` attribute)

| type | Element | Notes |
| --- | --- | --- |
| 2 | `TextEntry` | Font (Win32 LOGFONT), `FontOptions`, one or more `Element`s concatenated |
| 3 | `BitmapEntry` | `Path` is an absolute path on the machine |
| 4 | `LabelGraphic` | `GraphicIndex` 0..3 = part drawing "Draw 1..4", supplied at print time |
| 5 | `LineEntry` | field only |
| 6 | `EllipseEntry` | `PenWidth`, `PenStyle`, `PenColor`/`BrushColor` as "r,g,b" |
| 8 | `CornerLogoEntry` | `CornerLogoData` attributes + `EdgeThickDisplay` (%) |
| 9 | `DataRecord` | font for file printing; present in every label, not drawn |

## Text

- `Element/ElementID`: a number is an NC data field id (see the field tree); a quoted string (`"L: "`) is fixed text.
  Id 40000 is "Raw text" in the field tree.
- `PrvTxt` is the sample value shown while designing.
- `DelFirst` / `DelLast` skip characters at the start/end; `Count` limits characters (-1 = all).
  LEdit 1.32 files use `First` instead of `DelFirst`/`DelLast`.
- `FillChar`: 0 or -1 in all samples. The meaning of other values (leading zeros/blanks, trailing blanks
  in the dialog) is **inferred** from the dialog order.
- `Font/Escapement` in tenths of a degree, counter-clockwise: 0, 900, 1800, 2700.
- **`FontOptions/@FontSize` is tenths of a point.** `Font/Height` (LOGFONT) is stale and ignored by LEdit:
  entries with different `Height` but the same `FontSize` look identical in LEdit's previews.
  *Verified:* measured glyph sizes in two preview bitmaps match tenths of a point × (96 DPI / bitmap DPI).
  LEdit draws text at screen size whatever the scale of its preview bitmap, so those bitmaps are not true to
  scale for text. The editor renders the physical size.
- `FontSizeFixed="0"` scales the text to fill the field.
- Text is left-aligned and **vertically centred** in its field, even when it is taller than the field (*verified*).

## Bar codes

`FontOptions/@BarcodeSettings` = `0x10000` (bar code on) **+ symbology code in the low word**. It is not a
set of option bits.

| Code | Seen with font | Symbology | Status |
| --- | --- | --- | --- |
| 1 | C28N | Code 128 | inferred |
| 2 | A39N | Code 39 | inferred |
| 3 | C28N | Code 128 (C) | inferred; the most common value |
| 12 | Arial | QR code | *verified* against LEdit's preview |

LEdit also lists Code 39 (check digit), Code 128 C, PDF417, PDF417 (byte), DataMatrix, EAN-8, EAN-13 and
2-of-5 variants (strings in `LEdit.exe`). Their codes are unknown: such labels keep their value and show a
"not supported yet" marker. To learn a code, create one bar code of each type in LEdit and look at the
saved value.

## Field tree

`LEditTreeXML.xml` (categories) and `ListaOriginaliId.xml` (flat list): every element with an `ID` attribute
is a field. **The element name is the field's name** (`<QMin Text='(95, 9014, Quantità)' ID='45808'/>` → QMin).
`Text` refers to language table section 95, number 9014, with "Quantità" as the fallback.
`ListaOriginaliId.xml` reuses `CustomTexts` for ids 45866–45869; the editor tells them apart by their fallback
text (`CustomTexts (MatCode)`).

## Language tables

`Language\<Name>.txt`: `SS NNNN caption\\description`. Code pages: Ruski 1251, Chinese 936, Hebrew 1255,
Polski/Cesko/Slowenski/Româna 1250, others 1252 (*verified* by decoding). `LanguageMap.xml` maps OS culture codes
to language names.

## Label sheets (`*_lbl.xml`)

UTF-8 `<CutList>` with one `<Part>` per label; attributes are named after field-tree elements (IDesc, CabCode,
CabInfo, DrillInfo, Description11, …), so they fill fields by name. Aliases: `L`→FinalLength/Length,
`W`→FinalWidth/Width, `Material`→MatCode; board `Thickness` is added to every part.
