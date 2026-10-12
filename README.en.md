# Cut the Save

[简体中文](README.md) | **English overview**

Cut the Save checks and edits Steam *Cut the Rope* saves and assists with high-refresh-rate speed issues. The interface supports Simplified Chinese, Traditional Chinese and English. Windows 11 is the default theme; Classic and system/light/dark preferences remain available.

This is a development candidate, not a confirmed release. Local tests and package verification do not replace real-save, RTSS or Windows 10/11 field acceptance. Production update trust and official RTSS download metadata are not provisioned.

Open a save, inspect its validation status, edit the in-memory draft and use **Save as**. Unrecognized or warning-bearing saves remain read-only. Save and restore require the game to be closed. Backups are verified before recovery; unresolved or external changes stop automatic writes.

RTSS operations target only `CutTheRopeApp.exe`. The repair records its intent before writing, then binds the verified resulting Profile. Undo refuses changed Profiles, changed backups and unverifiable legacy/interrupted records. Preserve their files for review.

The main toolbar wraps in smaller work areas. A persistent unsaved marker survives language changes. Detection, opening, saving and retention work run away from the UI thread, with duplicate actions and closing blocked while a transaction is active.

## Build

Two [save examples](samples/README.md) include original/edited copies and SHA-256 checksums for inspecting parsing and edit differences.

On Windows x64 with .NET 10 SDK and PowerShell 7:

```powershell
pwsh -File scripts/Publish-CutTheSave.ps1 -RunUpdaterAcceptance
```

Version defaults come from `Directory.Build.props`. `-ReleaseVersion` overrides the three-component version. Package checks read the actual ZIP, require every file hash and compare packaged assembly metadata with the release version. Updater tests use isolated signed fixtures; they do not enable production trust or exercise a production update publication.

This is an overview, not the complete user manual. See the [three-language guide](docs/guide.md), [RTSS safety](docs/rtss-safety.md) and [field acceptance](FIELD_ACCEPTANCE.md).
