# Backup history and retention

## Implemented behavior

`AtomicSaveService` uses `BackupService` for indexed pre-write and recovery-pre-restore snapshots. New snapshots have collision-resistant names, flushed content and verified SHA-256/size. Index replacement uses its own unique temporary file. A named mutex coordinates cooperating application instances; a busy store fails the operation rather than silently losing index records.

The WPF backup window lists indexed snapshots for the selected save and displays date, lock, byte size, localized reason, original location and SHA-256. Its actions have localized explanations and accessibility names. Copy-hash and open-folder verify the selected file first; restoring validates the save and exact codec round-trip, creates a pre-restore snapshot and uses the normal save transaction.

## Retention boundaries

- The entire backup store has a default target of 50 snapshots and 200 * 1024 * 1024 bytes. Maintenance runs after successful open/save/restore. It is not a hard cap when safety exemptions apply.
- Locks persist in the index. Active editor snapshots also hold a Windows file handle that prevents deletion/replacement by other application instances. The latest available snapshot for each original save is always retained.
- Older records default to ineligible for automatic removal. Unindexed files, paths outside the exact store, linked paths, changed/unreadable snapshots and unknown ownership are preserved. Legacy files are not automatically imported by filename inference.
- Cleanup considers only indexed, content-verified, explicitly eligible files bearing this version's managed naming format. A preview records exact paths and metadata. Applying it re-plans under the store lease, never expands that path set, and rechecks locks, content and timestamps. A changed or busy file remains recorded for later retry; no forced unlocking occurs.
- The manual action previews the count for the entire store and defaults to No. Removal deletes backup snapshots, not game saves, and is not an app-level undo operation. Successful save/restore status is not converted into failure merely because subsequent maintenance reports an issue.
- Invalid or duplicate index entries stop related operations. A damaged index is not silently replaced with an empty one. If a verified snapshot cannot be indexed, its file is retained for recovery.

## Local verification

Cleanup now journals each snapshot before moving it to a unique, same-directory quarantine file. Only after the reduced index has been atomically persisted and checked is that quarantine deleted. The journal binds the record, its position, content hash/size, and both index hashes. A busy source that was never moved remains indexed for normal retry. A later failure leaves an explicit pending transaction instead of reporting successful cleanup.

On restart, inspection is read-only. Backups shows **Repair records** only when a transaction exists. With user confirmation (default No), an uncommitted cleanup returns the verified quarantine to its original backup filename; a committed cleanup removes only the verified quarantine. Recovery never writes a game save. New snapshots, saves, restores, locks and retention are blocked until the transaction is resolved. Conflicting files, an altered index, malformed journal, linked/outside paths or changed quarantine stop recovery and retain available evidence. Recovery is repeatable after interruption; it does not infer ownership by scanning unknown filenames.

Core fixtures cover indexed pre-write/recovery bytes and hashes, same-day deduplication, the default 52-to-50 case, separate count/byte targets, lock/unlock, active/latest protection, stale plans, damaged index and malformed paths, legacy/outside/unindexed files, tampering, Windows file sharing, and simultaneous snapshot attempts. All writes/deletes are to isolated generated fixtures.

WPF fixtures use the production backup window with an injected isolated store. They verify actual pre-write visibility, persisted lock/unlock, localized labels/tooltips/accessibility, damaged-index action blocking, and 24 combinations of two themes, two color modes, three languages and two window sizes. The screenshots mask fixture paths. The separate appearance matrix also remains applicable.

Additional Core tests exit a separate test-owned process at four boundaries: flushed journal, quarantined snapshot, committed index, and deleted quarantine. A fresh service checks and completes recovery. Tests also cover an actual Windows index-replacement sharing failure, six conflicting/damaged states, a busy quarantine, blocked mutations and repeated recovery. WPF tests exercise the production repair/cancel handlers and conflict state in all three INI-selected languages, with 12 recovery layouts across two states and two sizes. These are process-exit tests, not power-loss or storage-controller durability certification.

These checks do not click the system Clipboard/Explorer actions, approve native confirmation dialogs or replace real-save/device field acceptance. No real backup-store pruning is part of this verification.

## Remaining limits

Target-version conflict detection, a destination mutex, actual displaced-byte recovery and the new persistent save-intent/restart flow have separate write-safety checks; see write-safety.md. Pending save recovery blocks pruning, and explicitly resolving it locks the prior snapshot. Historical missing/orphan files created by older versions are not silently reconciled. The mutexes coordinate cooperating app instances, not arbitrary external writers; simultaneous hostile directory-entry replacement is outside the guarantee. Link/identity checks and revalidation reduce risk but do not constitute an adversarial filesystem sandbox. This document does not declare a production release.
