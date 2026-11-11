# Changelog

All notable changes to Label Editor. Generated from the commit history by [git-cliff](https://git-cliff.org).

## [0.2.0] - 2026-11-11

### Features

- Add canvas context menu and arrange toolbar
- Select entries hidden behind others
- Pick overlapping entries with a plain click
- Offer the overlap list on shift and ctrl click
- Compose text entries from several fields and fixed text
- Add lock position and rotation-aware multi-entry resizing
- Add a dark theme with a dimmed label paper
- Support the edge indicator with four edge sides and orient

### Fixes

- Request administrator rights automatically when setup starts
- Stop the field dialog from opening and entries sticking to the cursor when clicking around
- Show the edge fields in their own group and name the all-fields group correctly

### Changes

- Add fixed text through raw text in the field tree
- Read ledit data only from the ledit installation and drop the bundled biesse files

### Maintenance

- Add changelog generation and github release workflows

## [0.1.1] - 2026-11-11

### Features

- Add label layout domain model
- Add editor session and application use cases
- Add ledit file format and storage adapters
- Merge consecutive property edits into one undo step
- Fill labels with preview data from label sheets
- Add label scene rendering with code 128, code 39 and qr encoders
- Add presentation view models
- Add avalonia desktop app with wysiwyg label canvas
- Change which field a text element prints
- Add le application icon
- Edit any opened label and confirm the first replace
- Choose element fields from a field tree dialog
- Lock entry size per entry
- Keep label version history beside the label
- Add inno setup installer with pc and portable modes

### Fixes

- Decode barcode settings as a symbology code
- Open labels at 200 percent zoom
- Leave fields blank when the preview record has no value
- Install on this pc into program files for all users

### Documentation

- Add architecture, ledit format findings, roadmap and publish script

### Tests

- Boot the main window headlessly with the real composition root

### Maintenance

- Add repository scaffolding and build settings
- Package default ledit data and label fixtures
- Bump version to 0.1.1


