# Synthetic data-only restore FK dependency advisor — issue #1355

**Review-only diagnostic. No database access, commands, schema change or release authorization.**

The archived isolated PostgreSQL 18.6 run retained two separate failures:
(1) ordinary logical pg_restore changes raw source/target schema-catalog
expression bytes (14 CHECK constraints and one partial unique index); and
(2) exact original-EF-DDL-first data-only replay failed on
\`FK_announcements_users_AuthorUserId\`, with announcements loaded before users.

This prospective advisor addresses **only** FK table-data ordering.
It cannot repair, waive or normalize the **strict schema comparison failure**.

## Input and output

Command on an **independently controlled disposable synthetic** local inventory:

\`\`\`bash
node scripts/ci/restore-data-dependency-advisor.mjs --synthetic-manifest \
  /private/synthetic-inventory.json
\`\`\`

The manifest is a strictly validated JSON object:
\`version=1\`, \`scope=ISOLATED_SYNTHETIC_OFFLINE_ONLY\`,
\`sourceSha\` (40 lowercase hex), \`catalogSha256\` and
\`archiveTocSha256\` (64 lowercase hex); four catalog counts
(\`tables\`, \`foreignKeys\`, \`userTriggers\`, \`dataEntries\`);
boolean \`completeInventory\` and \`targetEmpty\`; \`tables\`
(\`name:"public.users", archiveData:true\`), \`foreignKeys\`
(\`name,child,parent,validated\`), \`userTriggers\`
(\`name,table,timing:"BEFORE_INSERT"|"AFTER_INSERT",enabled\`).

All names/fields are **synthetic schema metadata**, not student rows, File
contents, credentials or dump bytes. A locally provided digest/count/flag
is **self-declared**, never a cryptographic independent completeness receipt.

Results are only \`BLOCKED\` or \`REVIEW_ONLY_NOT_RESTORE_VERIFIED\`,
never a database restore PASS. Ordered candidate list is suppressed on
incomplete/missing table data, active user INSERT trigger, missing parent,
unvalidated FK, cross-table FK cycle, catalog/archive-count mismatch,
unverified target emptiness or incomplete inventory. Self-referential FKs
are reported; their per-table COPY semantics require independent real
PostgreSQL proof. Acyclic graph is an advisory, not an executable plan.

The tool reads **one** local JSON <=256 KiB and opens no socket, shell,
Docker, SQL, database, backup or target. It cannot disable triggers,
constraints or RLS, migrate, seed, connect to staging or alter any
release gate. A small PR/push workflow runs offline Node built-in unit
tests. Full real app restore remains blocked pending independently approved
backup strategy and target, strict schema/row/sequence/migration-history
comparison, enabled triggers/FK/roles/grants/RLS, Files/keys/Audit and
rehearsed rollback.

[Original #1355](https://github.com/NYGsatoshi/Coglatas/issues/1355)
remains **P0 OPEN**. Existing backup/restore runbook is not rewritten,
and the Owner's **Hold Both** Staging and detailed SEC-ARCH decisions
remain intact. **NO_GO**.
