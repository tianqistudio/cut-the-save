# Save write safety and verification

## Read-only policy

Validation distinguishes structurally valid data from safe-to-write data. Errors, unresolved warnings (including duplicate keys or missing hash termination), and containers without recognized progress fields are view-only. Both WPF grids, batch actions and direct edit handlers enforce this; Core saving and backup restoration independently reject such data.

The codec uses strict UTF-8 and canonical 7-bit string lengths. It rejects truncation, unknown trailing bytes, invalid UTF-8, non-canonical or excessive lengths, files over 16 MiB and individual strings over 256 KiB. Writing validates all strings and total encoded size before emitting bytes. Unknown fields inside an otherwise supported container remain ordered and preserved; this does not imply compatibility with every game version.

## File version and commit

Opening parses precisely the snapshot bytes and checks that the destination still matches, binding path, existence, size and SHA-256 to the editor baseline. Save as explicitly confirms an existing destination; the reviewed state is carried into the transaction. Saving to the currently opened path uses its existing baseline, not a freshly recaptured version that would silently accept an external edit. Restore likewise carries the confirmation-time state.

A destination mutex coordinates app instances even when they use different backup stores. The transaction validates and serializes the draft, creates an indexed pre-write snapshot, and flushes a save intent before creating its unique temporary file. It writes/flushes and verifies the exact planned bytes, then rechecks the expected target. A read-sharing lease denies in-place writers during native replacement. The actual file displaced by File.Replace is captured beside the target on the same volume; its content is checked against the expected state to detect a replacement race.

## Failure outcomes

- Pre-commit conflict or backup failure leaves the external destination untouched and the draft unsaved.
- After a commit, a changed or unreadable current destination is retained rather than blindly overwritten or deleted. A new output whose success cannot be confirmed is also retained for inspection.
- Automatic recovery is attempted only when the current bytes still match the validated output and the actual displaced file is available. Recovery itself uses atomic replacement and preserves the file displaced during recovery. Both prior/current byte checks must pass before reporting that the prior file was restored.
- A failed or uncertain recovery reports review required and retains known recovery paths. The UI does not advance the saved baseline on failure.
- Successful save status survives cleanup failure. Busy or changed transaction artifacts are retained and reported, including through restore; pre-write indexed backups remain available.

Recovery artifacts use unique .cts-recovery / .cts-recovery-current suffixes beside the destination. Do not treat them as disposable files merely because they look temporary. They can contain the actual file present during a race. Inspect the reported paths before further overwriting.

## Restart recovery

The backup store's `save-transaction.json` binds the exact target, prior/output content hashes, unique artifact token and indexed prior backup. It is flushed before any staged output or target replacement. Normal success clears it only after owned-artifact cleanup. An uncertain commit, recovery or cleanup retains it and blocks new snapshots, writes, restores, locks and pruning in that store. Detection/opening checks it without modifying the target.

Backups → Interrupted save provides three-language, default-No decisions. **Keep current state** does not write or recreate the game save. **Restore previous** is offered only for unchanged, verified output with a verified prior snapshot and no unverified artifacts; it requires the game to be closed and separately confirms leaving an editor draft. Both choices bind the reviewed journal and current target version. The prior indexed backup is locked before resolution; restoring also creates and locks an indexed pre-restore snapshot of the current output before replacing it. The journal is archived as `save-recovery-<token>.json`, not silently forgotten.

Known, verified transaction files can be removed after explicit resolution. Busy, partial or changed leftovers are retained and reported; they are not guessed safe to delete. A malformed journal or missing/changed prior backup disables automatic resolution. An externally changed/deleted target cannot be overwritten through restart recovery; Keep current preserves that state, after which the locked backup can be used separately. Recovery itself can be retried after interruption without replaying the original save.

Archived intent records and leftover artifacts remain recovery evidence and are not subject to normal snapshot retention. The archive records the original intent, not proof of a later target state. This is not a global journal across manually chosen different backup stores, and legacy unjournaled leftovers are not discovered by scanning the disk. Store/target mutexes coordinate cooperating app instances rather than hostile namespace changes.

## Evidence and limits

Core fixtures cover malformed/oversized/non-canonical/Unicode data, read-only warnings, stale files, changes before and after commit, newly appeared/deleted destinations, native commit failures, Windows atomic rename races, busy writers, confirmed/failed rollback, cleanup sharing failures and concurrent app target locks. WPF checks cover production read-only handlers, three-language explanations, confirmation decisions, preserved drafts and updated saved baselines.

Language tests and the backup/appearance language matrices read and write isolated INI files through the same configuration methods used by startup. They preserve unrelated theme/color/save/unknown settings and require no OS input automation. User INI files and real backup stores are not test fixtures.

Restart tests terminate owned child processes at intent, staged-output, pre-replace, post-replace, verified-output and rollback boundaries for existing/new destinations. They also cover external target changes, partial output, damaged journals, altered prior backups, index-lock failure and interrupted recovery retry. Production WPF handlers are exercised in 96 layouts covering three INI-selected languages, both themes/color modes, four recovery states and two sizes, plus stale confirmation and draft protection.

These tests cover deterministic failure cases and process-exit recovery on the test host. They do not prove power-loss durability, every network/cloud filesystem behavior, hostile namespace/permission changes or a general filesystem compare-and-swap guarantee. Native replacement may already have changed the destination when an error is reported; recovery evidence is retained instead of assuming that an exception means no change. Real-device and candidate-specific save acceptance remain separate.
