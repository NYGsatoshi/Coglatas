# Local composer input during routine catch-up

## Latest Main synchronization: local review only

Status: **OWNER_HOLD / REVIEW_ONLY / IMPLEMENTED_NOT_VERIFIED** for hosted
qualification. The product candidate must remain Draft and unmerged until its
explicit approval boundary is satisfied. Student Council remains RELEASE_NO_GO.

Local normal merge `f209da07b12c1cb20defc04cab94b8aa7c1c8942` has ordered parents
held head `3de9eb782cdbc1f795f8c10ceebab4cf4b73ab10` and current Main
`81e40dcbf419ba16fb5b03a89869baaf5f5269b0`. No branch history was reset. The
only shared changed path was `messaging.facade.ts`; its only textual conflict
was the adjacent state declarations. Resolution preserves the candidate's
Composer binding and editable-draft state alongside Main's whole-catch-up
completion record. Main's catch-up coordination function is byte-identical;
the current Thread intent, fresh-request, close, scope and denial guards remain.

All 30 Main-only changed paths and all 11 candidate non-facade paths preserve
their respective source blobs. This retains the current Files download,
Workspace-member reauthorization, Thread controls and bounded lifecycle
diagnostics. The Main-to-candidate delta stays within the same 12 intended
Composer/test/documentation paths; no workflow, backend, RLS, migration,
deployment, permission contract or held diagnostic integration was added.

At the exact merged code source, clean lock-matching Vitest 5.0.5 executes the
62 owning Composer/input/draft controls successfully. The expanded selection
including current Main Thread and Realtime controls passes **213/213 tests in
five files**, with no skipped tests or test retries. Production build, strict
Functional messaging TypeScript, architecture and diff checks pass. Existing
Vite/jsdom, npm-option and bundle/style warnings remain visible.

These are local Angular controls and build/type checks, not new real-backend,
physical IME/device or hosted exact-head qualification. Historical head 3de's
CI `40174839257/1`, tested merge `411c75c2d79f5ba6d6ecc50b0ebd8906bf9e7637`
and the immutable failed comparator #1402 remain separate evidence. No old-head
or Main CI result is transferred to this synchronized head. Fresh required CI,
the unchanged real-backend owner and independent review remain necessary.
Existing explicit Owner holds and all security/deployment/release gates remain.

## Original mechanism and candidate scope

Issue [#1360](https://github.com/NYGsatoshi/Coglatas/issues/1360) retains the current hosted pre-send empty-draft failure. Its initiating cause remains unknown. The earlier Main381 draft failure and issues #1353 and #1342 are separate observations; this repair does not establish a common cause or close any release blocker.

A controlled local diagnostic on unchanged Main `f6feb88754473455e04553c911fc6380895f2e52` mounted the real DM page and composer in an Angular development build. Ordinary native Playwright fill kept the textarea and emitted an input event. Invoking the existing registered routine Messaging catch-up callback exactly at focus removed the textarea before keyboard insertion. Native fill returned without an input event; a new unfocused empty textarea appeared and the unchanged 15-second value assertion failed. The predeclared matrix had one actual pass and one actual baseline failure, with no retries or skips. Mock HTTP and SignalR responses, development inspection and the directly triggered callback qualify this isolated mechanism only, not the hosted failure's cause or real backend authority.

The repair keeps only an already editable local input component during the existing same-scope routine catch-up interval. Server messages, member information and capabilities still clear immediately. The textarea can accept local text; Send, mention controls, keyboard send and application command authorization still use current authority and remain blocked until the fresh aggregate succeeds. A previously ReadOnly or sending composer does not gain temporary editing. Fresh ReadOnly, removal, denial, errors and hard session, Tenant, Workspace or authorization boundaries end the local interval.

Each composer has an opaque local component identity. Edits and sends consume that identity only after checking its current request generation, actor, Tenant, Workspace, Conversation and route. Routine catch-up can explicitly carry the same identity for the same local editing context; hard boundaries replace it. Keying the mounted component by this identity prevents a queued old-node event from acquiring a replacement scope even when Angular coalesces visibility changes. The identity is not a server credential or authorization decision. Existing persistent draft and retry semantics are preserved; the repair adds no deletion policy.

Focused controls cover mounted DM and channel input continuity, empty server projections, disabled posting and mention controls, pending sends, ReadOnly and removed participants, denial/error handling, four hard boundaries, and old DOM input/Enter events across scope or generation changes before a new render. The native controlled diagnostic must retain its baseline failure and run independently against the committed candidate; configured tests alone are not execution evidence.

No API, backend authority, schema, migration, RLS, security mapping, licensed acceptance or deployment contract changes. Exact execution receipts are source-bound and retained separately. Both Owner HOLDs, the full SEC-ARCH/SEC-16/SEC-17 gates and NO_GO remain.
