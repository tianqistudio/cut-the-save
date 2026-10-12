# Appearance implementation and verification

The default theme is Windows 11; Classic retains the original blue/gray palette, 10-pixel cards and compact button spacing. Both themes offer System, Light and Dark independently of language.

## Design reference

- Design principles: neutral surfaces, shared resources, visible keyboard focus, unclipped actions and preservation of editing drafts.
- Native control foundation: [Microsoft WPF Fluent themes](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/whats-new/net90). The application remains WPF/.NET 10; it does not claim WinUI or a pixel-exact Windows Settings replica.
- The .NET 10 `ThemeMode` API is currently annotated experimental. Its diagnostic suppression is localized to the assignment in `AppearanceService`; custom palettes, cards and Classic controls remain project-owned resources.

## Behavior

- Missing or unknown preference: Windows 11 + System.
- System mode reads Windows `AppsUseLightTheme` and reacts to user-preference notifications on the UI dispatcher. A queued notification rechecks the selected mode before updating; a later manual choice wins.
- Application theme resources cover the main editor, language and appearance pickers, README, backup viewer and game location picker. Native title bars request the corresponding dark/light appearance where DWM supports it.
- The appearance picker explains the pending choice and applies only when Apply is pressed. Cancel keeps the current selection. A failed settings write leaves an inline error and the previous appearance active.
- `Theme` and `ColorMode` are saved alongside `Language` in `%LOCALAPPDATA%/CutTheSave/CutTheSave.ini`. Updating a key preserves unrelated settings and sections; temporary-file replacement avoids truncating the previous INI on a failed write.
- Appearance changes do not call save-write or RTSS-write operations. In-memory rows stay attached during system-color changes.

## Local verification

The WPF test executable has a `--render-appearance <output-directory>` mode. It constructs unshown application-owned controls with synthetic entries; it suppresses production startup, does not drive mouse/keyboard input and does not change the Windows theme or real preference file.

The matrix produces 36 PNGs: two themes × two explicit colors × three languages × (main/default size, main/minimum size, appearance dialog). It checks header/action bounds, actual title and button text colors, automatic switching, manual override and preservation of a synthetic unsaved row. The normal WPF smoke suite also checks INI preservation, languages, tooltips and keyboard tab-stop structure.

These previews verify those local rendering cases. They do not constitute Windows 10/11 field acceptance, physical DPI testing, a real Windows settings broadcast, or a screen-reader audit. The existing field requirements remain in `FIELD_ACCEPTANCE.md`.

## Compact editing and busy state (2026-09-28)

Main-window minimum dimensions are now 720×420 DIP. Toolbar buttons wrap; the sidebar narrows and remains scrollable. At short heights introductory text and summary counts are hidden to preserve editor space. A persistent unsaved marker updates without rebuilding rows when the language changes. Save/open/detection/retention work executes outside the UI thread and restores controls on the dispatcher even on failure; closing and duplicate actions are blocked while the operation is active.

Twelve language/work-area combinations (720×420, 960×540, 1092×614 and 1320×840) check toolbar bounds, editor dimensions and sidebar access. These are local logical-size checks, not physical per-monitor DPI or screen-reader acceptance.
