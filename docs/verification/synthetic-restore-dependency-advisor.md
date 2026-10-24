# Synthetic data-only restore FK dependency advisor — issue #1355

**Review-only diagnostic. No database access, commands, schema change or release authorization.**

The archived isolated PostgreSQL 18.6 run retained two separate failures:
(1) ordinary logical pg_restore changes raw source/target schema-catalog
expression bytes (14 CHECK constraints and one partial unique index); and
(2) exact original-EF-DDL-first data-only replay failed on
`FK_announcements_users_AuthorUserId`, with announcements loaded before users.

This prospective advisor addresses **only** FK table-data ordering.
It cannot repair, waive or normalize the **strict schema comparison failure**.

## Input and output

Command on an **independently controlled disposable synthetic** local inventory:

```bash
node scripts/ci/restore-data-dependency-advisor.mjs --synthetic-manifest \
  /private/synthetic-inventory.json
```

The manifest is a strictly validated JSON object:
`version=1`, `scope=ISOLATED_SYNTHETIC_OFFLINE_ONLY`,
`sourceSha` (40 lowercase hex), `catalogSha256` and
`archiveTocSha256` (64 lowercase hex); four catalog counts
(`tables`, `foreignKeys`, `userTriggers`, `dataEntries`);
boolean `completeInventory` and `targetEmpty`; `tables`
(`name:"public.users", archiveData:true`), `foreignKeys`
(`name,child,parent,validated`), `userTriggers`
(`name,table,timing:"BEFORE_INSERT"|"AFTER_INSERT",enabled`). Every object
requires exactly its listed own keys, and every expected count must be a safe
integer in `0..4096`. Delimiter-containing replacement keys are invalid.
FK identity is `(child,name)` and trigger identity is `(table,name)`; names may
repeat on different tables, while duplicate identities on one table reject.

All names/fields are **synthetic schema metadata**, not student rows, File
contents, credentials or dump bytes. A locally provided digest/count/flag
is **self-declared**, never a cryptographic independent completeness receipt.

Results are only `BLOCKED` or `REVIEW_ONLY_NOT_RESTORE_VERIFIED`,
never a database restore PASS. Ordered candidate list is suppressed on
incomplete/missing table data, active user INSERT trigger, missing parent,
unvalidated FK, cross-table FK cycle, catalog/archive-count mismatch,
unverified target emptiness or incomplete inventory. Self-referential FKs
are reported; their per-table COPY semantics require independent real
PostgreSQL proof. Acyclic graph is an advisory, not an executable plan.

The tool reads **one regular, non-symlink** local JSON <=256 KiB. It checks
size before opening, opens nonblocking (and no-follow where supported), verifies
the opened descriptor's regular type, device/inode and size, then reads at most
256 KiB plus one overflow byte. Oversize, nonregular/replaced inputs, short reads
and observed descriptor size/timestamp changes reject with the fixed invalid
manifest error and no advisory. The descriptor closes on success and failure.
This bounds file data, not filesystem latency or an atomic immutable snapshot;
concurrent same-size content changes are not independent provenance evidence.
The tool opens no socket, shell,
Docker, SQL, database, backup or target. It cannot disable triggers,
constraints or RLS, migrate, seed, connect to staging or alter any
release gate. A small PR/push workflow runs offline Node built-in unit
tests. The expanded source controls include exact-key collision partitions,
table-scoped identity positives/negatives, actual CLI exit/output checks,
size/symlink/directory checks, descriptor replacement, growth/shrink and partial
reads. The Ubuntu FIFO acquisition-race control creates a synthetic FIFO with
`mkfifo` (without a shell); a loader, timeout or missing utility is a failure,
never a skipped/passing control. All fixtures are disposable local synthetic
files. These new controls remain unexecuted until exact-source qualification;
prior ten-case evidence applies only to its historical source.
Full real app restore remains blocked pending independently approved
backup strategy and target, strict schema/row/sequence/migration-history
comparison, enabled triggers/FK/roles/grants/RLS, Files/keys/Audit and
rehearsed rollback.

[Original #1355](https://github.com/NYGsatoshi/Coglatas/issues/1355)
remains **P0 OPEN**. Existing backup/restore runbook is not rewritten,
and the Owner's **Hold Both** Staging and detailed SEC-ARCH decisions
remain intact. **NO_GO**.
