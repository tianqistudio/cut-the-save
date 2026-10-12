# Changelog

## 1.0.2 preview — 2026-10-13

- Added a floppy-disk and cut-rope icon with a swipe accent, shared by the application, windows, installer and uninstaller.
- Included the icon assets in source snapshots and installer build inputs.
- This preview remains unsigned. Real-machine installation, upgrade and uninstall acceptance is pending; automatic updates remain disabled.

## Unreleased local optimizations — 2026-10-02

- Index level entries once per projection or batch operation, preserving exact keys, first-duplicate behavior, unknown fields and serialization order. The index is rebuilt after each call so direct list edits cannot leave stale positions.
- Correct multi-library Steam discovery to rank runnable installations by directory modification time; isolated fixtures cover executable priority, custom install directories and duplicate libraries.
- Accept equivalent casing for signed Windows runtime identifiers and request SHA-256 throughout update preparation, installation and rollback, consistent with the existing metadata and worker protocol.
- Export support bundles in the background directly into a staged ZIP; close and flush the complete archive before replacing an existing bundle. Replacement failure preserves the old file, and active-reader tests cover the file-sharing boundary.
- Local validation passed 32 Core cases, isolated Windows/WPF checks and 9 Python governance/package-validator tests. These source changes require a new candidate package and field acceptance; production update trust remains disabled.

## 1.0.1 candidate — 2026-09-28

- Added ZIP-contained complete hash verification, duplicate/missing/private/path rejection, release metadata and packaged assembly version checks. Version defaults are centralized and tag builds pass an explicit version.
- Replaced obsolete updater arguments in acceptance with the current command contract and isolated signed transaction tests, including install/rollback, process interruption, external changes, malformed ZIPs, version mismatch and cancellation. Production update trust remains unconfigured.
- RTSS repair now records durable intent before writes and binds exact before/after Profile identities. Undo preserves external changes and unverifiable interrupted/legacy state instead of guessing.
- Moved main-window detection, open/save/restore and retention work off the UI thread with busy/close guards; bounded peripheral save reads; added compact layout and persistent localized unsaved state.
- CI includes release-governance and ZIP-validator tests; save safety cases report individually. No official release or field acceptance is implied.

## Earlier unreleased development changes

- Update trust now comes only from compiled public key/key ID/channel/HTTPS hosts and two independent endpoints. External INI is an on/off preference, not a way to replace the trust root. Production provisioning remains pending and this build stays offline for updates.
- Signed-source checks now run independently with deadlines covering headers, bodies and signatures, strict bounded metadata, duplicate-field rejection and per-hop redirect validation. Asset transfers enforce exact size/hash, bounded mirror fallback and caller cancellation. Numeric version aliases cannot hide same-version identity conflicts; either signed source can withdraw a stale version.
- Added 22 synthetic dual-source failure combinations, transport/metadata/withdrawal tests and isolated INI trust-tampering checks. Update confirmation defaults to No; closing the window cancels its update operations. These local checks do not prove live publication or a production signature chain.

- Added a three-language RTSS setup guide, exact-version/binary-hash Profile API policy, native-load file leases and restricted dependency lookup. The locally checked 7.3.5.28314 pair is API-verified only, not Known Good; unknown pairs cannot perform automatic Profile operations. Global and arbitrary Profile names are rejected.
- Added reviewed-metadata download infrastructure with per-hop HTTPS allowlisting, bounded transfer/extraction, package and installer hashes, cancellation/body deadlines, retained Windows origin warnings and separately confirmed installer launch with byte revalidation. The official package endpoint/hash remains unverified after HTTP 403, so the production download catalog stays unconfigured; no installer is bundled or redistributed.

- Added durable save intents before staged output, read-only restart discovery, blocked pending-store mutations and a three-language interrupted-save window. Explicit keep-current never writes the target; verified-previous recovery binds the reviewed target version, requires a closed game, locks the prior backup and retains recovery evidence.
- Added existing/new-target and rollback process-exit tests, partial/corrupt/external-change handling, recovery retries and 96 INI-driven recovery layouts. Repaired the release-governance uniqueness fixture without weakening receipt verification; all six governance unit tests pass.

- Backup pruning now uses a flushed per-snapshot journal and same-store quarantine before committing the reduced index. Restart inspection is read-only; a three-language Repair records action restores an uncommitted backup or finalizes committed expiry after confirmation, without modifying game saves. Unverifiable/conflicting state blocks further backup mutations and is retained for review.
- Added four actual child-process exit boundaries, Windows index-write failure, conflicting journal/file/index cases, interrupted-recovery retry, and 12 INI-selected recovery layouts. Game-save replacement journaling and physical/power-loss acceptance remain separate unfinished work.

- Language selection can be verified through the production INI startup reader/writer with isolated files, without computer control. Added zhcn/zhtw/usen aliases and direct-edit/restart guidance; automated backup/appearance language matrices now use isolated INI settings.

- Unsafe parsed saves are now genuinely read-only in both grids, batch handlers, saving and restore validation. Strict UTF-8, canonical bounded strings, a 16-MiB file limit and pre-serialization checks prevent silent encoding normalization or unchecked allocation.
- Opening now parses the exact snapshot bytes and binds an optimistic destination version. Save/restore confirmation retains that version, rejects detected external changes, and preserves the in-memory draft.
- Save transactions coordinate by destination as well as backup store, preserve the actual atomically displaced file, and distinguish confirmed rollback, retained current output and recovery needing review. Busy/changed cleanup artifacts are reported without changing successful save status.
- Added isolated codec, destination-change, native commit race, readback/rollback/cleanup fault, and three-language read-only/overwrite UI regressions. These do not replace power-loss or real-device acceptance.

- Connected atomic pre-write and pre-restore snapshots to indexed backup history. Added verified size/hash metadata, lock/unlock, copy-hash and open-folder actions with three-language guidance and theme-aware layout.
- Added a 50-snapshot/200-MiB global retention target with protection for locked, active, newest-per-save, legacy and unrecognized backups. Cleanup rechecks the exact reviewed candidates; damaged indexes, changed files and busy snapshots are retained instead of silently reset or force-removed.
- Added unique flushed snapshot/temp files and a cross-instance backup-store mutex. Successful save status is preserved if later backup maintenance fails. Synthetic safety and backup-window matrix checks are separate from real-save/device acceptance.

- Fixed lost edits when mixing raw entries, level edits, and batch operations. Both views now share accepted in-memory values; score changes recalculate the existing score hash, reordered columns keep their field identity, and unchanged string fields are not reinterpreted during save.
- Added three-language unsaved-change confirmation for close, detection, opening another save, backup restoration and update installation. Invalid cell input must be corrected or explicitly cancelled; successful saves establish the new change baseline.
- Save and restore actions now block while either the game launcher or renderer is running, or when the process state cannot be checked. No game process is force-closed.

- Added Windows 11 as the default visual theme, using the built-in WPF Fluent base. Classic preserves the original palette and compact spacing. Both support Follow system, Light, and Dark modes with a localized Appearance picker.
- Theme and color preferences persist in the shared CutTheSave.ini without erasing language, remembered save paths, or unknown keys; writes replace the settings file atomically. A queued Windows theme event cannot override a later manual selection.
- Fixed accent/disabled label contrast, dark title colors, minimum-width header clipping, and the multi-installation picker's bottom actions. Isolated UI tests now suppress production startup and generate synthetic offscreen theme previews.
- Candidate builds use isolated build outputs so packaging can proceed while a prior development executable remains open.

- Fixed live language switching so validation, Steam/save detection, RTSS status, Profile recovery guidance, and path accessibility text all refresh in `zh-CN`, `zh-TW`, or `en-US`; the language-dialog title now follows the active UI language while its recommendation continues to reflect the actual Windows UI culture.
- Fixed real RTSS Profile writes on standard UAC sessions: the main app stays unelevated and launches a narrowly scoped one-time administrator helper only for apply/undo, with machine-level rollback state, `60/1` read-back, silent-delete detection, and automatic RTSS startup.
- Added a localized, guided “Launch through Steam” action for AppID `223280`; it is enabled only after `CutTheRopeApp.exe` is confirmed and does not modify save or RTSS state.
- Fixed the multiple-Steam-library picker so cancelling it no longer silently falls back to the first installation.
- Added first-start language selection with Windows language recommendation.
- Added a non-blocking startup scan for Steam game/save candidates and RTSS status; multiple Steam locations remain user-selectable instead of being silently chosen.
- Added a tag-triggered GitHub Actions release workflow that packages and verifies the ZIP and SHA-256 artifact using the same local publish script.
- Added `zh-CN`, `zh-TW`, and `en-US` UI resources.
- Saved language preference to `%LOCALAPPDATA%\CutTheSave\CutTheSave.ini`.
- Added a quiet bilingual in-app README and button tooltips.
- Included `CutTheSave.update.ini.example` in the distributable package so the README's optional signed-update configuration link is usable offline.
- Added a sanitized support bundle that excludes `ctr_save.bin` by default.
- Added manual `CutTheRopeApp.exe` selection when Steam metadata does not confirm the executable.
- Added a localized backup viewer with filtered history and restore-before-backup behavior.
- Hardened backup restore to validate the selected backup before writing and validate the restored file after replacement.
- Remembered a successfully opened manual save path as the next detection override.
- Added WPF accessibility names, keyboard-cancel semantics, and a repeatable publish script.
- Fixed Steam discovery to use the app manifest `installdir` instead of assuming a fixed folder name.
- Set a Windows Chinese UI font for the WPF controls so CJK text remains legible in the main window and README.
- Added primary-display refresh-rate detection; `60 Hz` skips the RTSS limiter.
- Added a tested refresh-rate policy that keeps the RTSS Apply action disabled when the refresh rate is unknown or at/below `60 Hz`.
- Added open-save snapshots, backup index metadata, and restore-to-opened-version.
- Added a 275-level view with Box, level, unlock, stars, score, and changed state.
- Preserved raw key/value editing for advanced inspection.
- Kept RTSS changes scoped to `CutTheRopeApp.exe`, with backup and read-back verification.
- Added Ed25519 detached-signature verification, dual-source version selection, same-version hash conflict rejection, HTTPS host allowlisting, and asset size/SHA-256 verification in Core.
- Added an optional asynchronous startup update check with a 24-hour success throttle; it stays offline when no signed update configuration is present and never blocks save editing.
- Hardened update configuration readiness checks to require a 32-byte Ed25519 key, two HTTPS sources, and hosts covered by the allowlist before any request is attempted.
- Update decisions now exclude versions explicitly listed as blocked by a signed manifest.
- Added a multiple-Steam-library picker so automatic detection does not silently choose among several installations.
- Added an optional signed-update entry point in the main window. Its original INI-based trust configuration has been replaced by compiled trust; the current INI only enables or disables checks.
- Added Base64 detached-signature support and safe rejection of malformed signed manifests.
- Added repeatable release-archive privacy, required-file, and SHA-256 preflight verification.
- Added a standalone Ed25519 manifest-signing tool for release environments; production private keys remain external to the repository and application package.
- Added a repeatable disposable-directory updater replacement acceptance script and wired it into the publish command with `-RunUpdaterAcceptance`.
- An earlier build passed a real-save write check: the detected save parsed without warnings, serialized byte-identically, was backed up before write, and was revalidated after write with matching SHA-256 hashes. New candidates require their own field acceptance.
- Completed a current-machine desktop acceptance pass for the published window, language recommendation dialog, guide text, tooltips, and RTSS `60 Hz` gating state.
- Added a bilingual field-acceptance guide for three real save samples, real RTSS write/read-back/undo, and Windows 10/11 scaling checks.

## Known limitations

- The update workflow is implemented but intentionally inactive in this development package because no project-owned public key or real manifest endpoints have been provisioned.
- RTSS installation download is not automated; the application does not redistribute the RTSS installer.
- Multiple Steam installations are detected and presented in a chooser before the save is opened.
- Cross-device, high-refresh-monitor, and additional Windows scaling acceptance still require field testing.
