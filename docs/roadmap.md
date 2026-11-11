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

- Edge indicator `Orient`: the values are known (0°, +90°, -90°, 180°), but not what LEdit rotates; once a label
  saved by LEdit with each setting is available, draw the part rotated accordingly.
- Filler characters: confirm with a label printed by LEdit that the fill pads up to the character count.
