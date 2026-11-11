# Roadmap

## Next

- **Preview from bNest project output** (`OUT\*.xml` nesting result + `.bSolid` archives): a second
  `IPreviewDataSource`. The format is much richer than label sheets (sheets, part geometry, custom info),
  and part graphics (`LabelGraphic`) could be drawn from it.
- **Remaining bar code types**: learn the LEdit codes for PDF417, DataMatrix, EAN-8/13 and 2-of-5 (one
  sample label each), then add encoders.
- **Export preview images** (PNG/BMP) from `LabelScene`. LEdit writes a `.bmp` next to each label;
  BiesseWorks may use it.
- **Printing** through the same scene.

## Improvements

- Rulers and snap-to-other-entries guides on the canvas.
- Field `FillChar` semantics: confirm values 1..3 with a file saved by LEdit.
- Move drag-and-drop to Avalonia's `DataTransfer` API (the `IDataObject` API is obsolete since 11.3; it is
  still used in `LabelCanvas` and `MainWindow` with a local suppression).
- Optional "save in LEdit's preview size" check that warns when text overflows its field.
- A recent-files menu (recent files are already stored in settings).
