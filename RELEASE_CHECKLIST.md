# Release checklist

Checked items record implementation checks and historical local verification. They do not certify the current source commit as a release. Repeat package verification and candidate-specific device/save acceptance for every new candidate; maintainer-local indexes and receipts are excluded from this source repository.

## Release governance gates

- [x] Assign one stable release ID and register the exact source set, ZIP, adjacent SHA-256 manifest, and validator in `artifact-index.json`.
- [x] Record an immutable candidate-specific receipt; missing source provenance or hashes remain explicitly `pending` or `unresolved`.
- [x] Run `python tools/release_context.py audit`; no path escapes the workspace, no indexed hash drifts, `CURRENT.md` is within budget, and cleanup remains a validated dry-run allowlist.
- [x] Run the full package validator for this exact ZIP/manifest pair and retain its receipt. A matching ZIP hash in the adjacent manifest is necessary but not the complete package-validation gate.
- [ ] Complete or explicitly waive, with release-owner rationale, every candidate-specific item in `FIELD_ACCEPTANCE.md`.
- [ ] Select exactly one `current_release.release_id` only after candidate, package-verification, distribution/update, and field gates are closed. Never select by file-name date or modification time.
- [ ] Run `python tools/release_context.py build-current`, then `python tools/release_context.py audit`, and retain the final governance receipt before external publication.

The publish command below creates a candidate only. It does not update the current pointer, satisfy field acceptance, sign a manifest, or publish externally.

## Local build gates

- [x] Isolated write-safety fixtures cover strict codec bounds, warnings/unknown read-only containers, stale destination versions, pre/post-commit changes, actual native rename races, rollback outcomes, busy writers, cleanup failure and target-level coordination. UI checks exercise read-only production handlers, three-language outcome explanations and overwrite/saved-baseline decisions. See `docs/write-safety.md`.

- [x] Isolated backup tests cover indexed pre-write/pre-restore bytes, default 50-file and custom byte limits, locks, active/newest protection, stale plans, legacy/outside/unindexed retention, damaged indexes, tampered/busy files and concurrent index operations.
- [x] Synthetic backup-window checks cover visible pre-write history, persisted lock/unlock, metadata/action explanations and 24 theme/language/window-size renders. Clipboard, Explorer, modal confirmations and real save restore remain separate physical checks. See `docs/backup-safety.md`.

- [x] Synthetic editor regressions cover mixed raw/level editing, reordered columns, batch preservation, score-hash refresh, unchanged strings, invalid cell rejection, unsaved-leave decisions, and save/restore game-running guards.

- [x] Windows 11 / Classic light and dark rendering, three languages, minimum-width header bounds, accent/disabled labels, INI preservation and automatic/manual color selection are covered by local WPF checks. See `docs/appearance.md`; OS broadcast and physical DPI acceptance remain field checks.

- [x] `dotnet restore --locked-mode` succeeds.
- [x] Core tests pass, including malformed files, round-trip, hash, batch scope, and backup restore.
- [x] Windows infrastructure tests pass without modifying the real RTSS Profile.
- [x] Support bundle contains sanitized diagnostics and excludes save contents by default.
- [x] In-app README loads the shared `docs/guide.md` resource.
- [x] CI workflow restores locked dependencies, builds, and runs all local smoke tests.
- [x] `scripts/Publish-CutTheSave.ps1` produces a repeatable local ZIP and adjacent SHA-256 file.
- [x] An earlier build parsed and validated one real local save, byte-round-tripped it, wrote it back with a pre-write backup, and revalidated it without changing bytes. This does not replace new-candidate acceptance.
- [x] In-app backup viewer lists history for the selected save and backs up the current file before restore.
- [x] `dotnet publish` produces the intended `win-x64` output.
- [x] Published executable starts on the current Windows x64 machine.
- [x] Steam AppID `223280` launches the installed game from an additional library and the process remains responsive; visual speed remains a separate field gate.
- [x] First-start language selection and the `zh-CN`, `zh-TW`, and `en-US` choices are covered by the WPF UI smoke test.
- [x] Main window / README / tooltips, key screen-reader names, enabled keyboard tab-stop structure, and 1.25x/1.5x layout measurement are covered by the WPF UI smoke test; the current Windows x64 release window was also opened and inspected through Windows UI Automation. Other devices and display-scaling combinations still require field acceptance.
- [x] Refresh-rate policy and app gating keep `60 Hz` and unknown refresh rates from enabling the RTSS limiter.
- [x] RTSS apply/undo uses a narrow one-time elevation helper; automated tests cover helper-command allowlisting, persisted rollback state, failed-state-save rollback, exact restore, and silent-delete detection.
- [ ] High-refresh-rate RTSS changes are backed up, read back as `60/1`, and undo restores the previous state.

## Distribution gates

- [x] Publish the application ZIP, not the RTSS installer.
- [x] Record SHA-256 for every local release asset.
- [x] Add release notes and known issues.
- [ ] Verify website and GitHub copies byte-for-byte if both are used.
- [x] Distributable archive name and text scans contain no private paths, account information, or unapproved user save files. The two reviewed example pairs under `samples/` are intentional distribution content.
- [x] Signed update manifests are verified before parsing or downloading any executable; detached signatures support the documented Base64 form and raw-byte compatibility form.

## Update module gates

- [x] Manifest schema includes channel, version, runtime asset, mirrors, size, and SHA-256.
- [x] Ed25519 detached signature verification is covered by a golden test.
- [x] Same-version mirror hash conflict is rejected.
- [x] Asset download requires HTTPS, a fixed host allowlist, exact size, and SHA-256.
- [x] External INI cannot replace compiled trust; independent source failures, body deadlines, metadata limits, per-hop redirects, version aliases and signed withdrawals have isolated tests.
- [ ] Configure the project-owned release public key and publish signed manifests.
- [ ] Connect two real manifest URLs and a user-confirmed updater after the signing key is provisioned.
- [x] Build the standalone updater and verify its argument validation path.
- [x] Package the updater beside the main application and test a full staged replacement on a disposable copy (`scripts/Test-CutTheSaveUpdater.ps1`).

The repeatable local publish command with the updater acceptance is:

```powershell
pwsh -File scripts/Publish-CutTheSave.ps1 -RunUpdaterAcceptance
```
