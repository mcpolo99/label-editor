# Architecture

## Layers and dependency direction

```
LabelEditor.App  (Avalonia views, canvas, composition root)
   │        │            │
   ▼        ▼            ▼
Presentation   Infrastructure   Rendering
(view models)  (LEdit formats,  (layout → scene,
   │            files, settings)  bar code encoders)
   │                │              │
   └───────► Application ◄─────────┘
             (use cases, session, ports)
                    │
                    ▼
                 Domain
         (immutable model, rules)
```

| Project | Responsibility | May reference |
| --- | --- | --- |
| `LabelEditor.Domain` | Label model (`LabelLayout`, entries, geometry, fonts, field catalog) and pure rules (alignment, hit testing, text composition). No I/O. | nothing |
| `LabelEditor.Application` | Use cases: `EditorSession` (undo/redo, selection, interactive edits), `DocumentService`, editing commands, preview resolution. Defines the **ports** (`ILabelLayoutFormat`, `ILabelDocumentStore`, `IStorageLocations`, `IFieldCatalogSource`, `ILanguageSource`, `IPreviewDataSource`, …). | Domain |
| `LabelEditor.Infrastructure` | Adapters for the ports: LEdit XML reader/writer, field tree and language readers, label-sheet reader, file storage, settings, portable paths. | Application |
| `LabelEditor.Rendering` | Builds a UI-agnostic `LabelScene` (text runs, encoded bar codes, shapes) from a layout and an optional data record. Reusable for future image export or printing. | Application |
| `LabelEditor.Presentation` | View models (CommunityToolkit.Mvvm). No UI framework reference, so they are testable. | Application |
| `LabelEditor.App` | Avalonia views, `LabelCanvas`, `ScenePainter`, dialogs and the composition root (`App.ConfigureServices`). | everything |

`tests/LabelEditor.Architecture.Tests` enforces the rules (for example, the domain references nothing,
and the application layer knows no UI framework).

## Key design decisions

- **Immutable domain + snapshot undo.** Entries are records; every edit returns a new `LabelLayout`.
  Undo keeps old layouts (cheap: unchanged entries are shared). Every change goes through
  `EditorSession.Execute` or `BeginInteractive`, so history and change notifications live in one place.
- **Interactive edits** (dragging, resizing) are previewed live and recorded as one undo step on commit;
  cancelling restores the original layout exactly.
- **Round-trip fidelity first.** The model keeps every value LEdit writes, including optional and legacy
  attributes (`First` vs `DelFirst/DelLast`, missing `TextColor`). Unknown entries are carried
  verbatim. A custom emitter reproduces LEdit's exact serialization, so saved files diff cleanly
  against LEdit's.
- **Editor-only settings and version history live in a sidecar file.** Things LEdit has no place for (per-entry *lock size* and *lock position*,
  and the gzip-compressed LEdit XML of every saved version)
  are stored in `<label>.xml.labeleditor.json` next to the label; the LEdit XML stays byte-for-byte what LEdit writes.
  LEdit entries have no ids, so records match by position plus a kind/size fingerprint; if a label was changed
  elsewhere and an entry no longer matches, the setting is dropped (never applied to the wrong entry).
  A separate editor project format with an export step was considered and deferred: two copies of every label can
  drift apart, and the domain model is format-independent, so it remains an easy step if editor-only data grows.
- **Fields are identified by element name** (`QMin`, `CabCode`), never by translated captions. Label
  sheets bind to fields by the same names.
- **Scene, then paint.** `LabelSceneBuilder` (no UI types) decides what is drawn; `ScenePainter` (Avalonia)
  only draws it. Rendering is covered by headless PNG tests (`tests/LabelEditor.App.Tests`).

## Async policy

- All file and data I/O is asynchronous, end to end (`XDocument.LoadAsync`, async streams).
- Infrastructure uses `ConfigureAwait(false)`. Application code that touches an `EditorSession` after an
  `await` (for example `DocumentService.SaveAsAsync`) deliberately resumes on the caller's context,
  because the session raises UI notifications.
- Start-up shows the window immediately; settings, field tree, language and the label library load
  behind it.
- `EditorSession` is single-threaded (UI thread). Saving snapshots the immutable layout, so editing can
  continue while a save runs, and the document correctly stays dirty if it changed meanwhile.

## Where to put things

| You want to… | Put it in |
| --- | --- |
| Support a new LEdit entry type | Domain record + `LeditLabelReader/Writer` + a scene item + property view model + template |
| Read another data file for previews (e.g. bNest project output) | A new `IPreviewDataSource` in Infrastructure, registered in DI |
| Add a bar code symbology | `Rendering/Barcodes` + `BarcodeType` once its LEdit code is known |
| Export PNG/BMP previews or print | A new consumer of `LabelScene` |
