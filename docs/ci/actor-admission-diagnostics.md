# Finite actor admission diagnostics

This slice improves diagnosis of the existing non-required real-browser actor
owner. It does not change admission, the frozen scenario bytes, its seventeen
criteria, retry/time budgets, artifact requirements, protected checks or product
code. Integration targets SecurityTestImplimation only.

Run `42131490366/1`, job `127611373281`, at Security head
`3865b09a2d3ea3cb82f450c6d19ec643ffbae01f` tested merge
`f08d37afd82be8c33e31e1fc98b4e0826b4e5bc6` and failed before browser launch.
The first receipt only says ADMISSION / INCOMPLETE / RELEASE_NO_GO. Its merge
and reviewed-head trees differ. That necessarily violates an existing admission
precondition if reached, but the old receipt cannot prove the earliest rejected
assertion. This patch does not retroactively supply that missing evidence.

Future incomplete receipts add one repository-owned `failureCode`. Admission
assertions retain their order and are wrapped in fixed categories distinguishing
source stamps, tree equality, run identity, PR/push producer identity and image
identity. Prerequisite acquisition, source cleanliness, control setup, harness and
completion rejection have separate finite fallback codes. Unknown exceptions
never supply a code, message, stack, filesystem path, credential or response body.
An unsupported phase/code maps to UNKNOWN/UNCLASSIFIED_FAILURE. Every such
receipt remains INCOMPLETE / RELEASE_NO_GO and the process exits nonzero.

The existing eight offline controls remain. New controls exercise six admission
rejections, first-rejection ordering, an unchanged positive admission, forged
codes and diagnostic-bearing exceptions, and the real CLI prerequisite refusal
in a disposable directory. These are harness regression controls, not native
browser execution. The canonical browser scenario's byte hash remains frozen.

Run `node --test tests/ui/actor-replacement-runner.node-test.mjs`. The existing
native actor job already runs that file before its real isolated Compose journey;
no workflow or runner is added. Fresh exact-head native journey, all six existing
checks and independent review remain required before integration. Historical
first-attempt failures remain failures even if a later candidate executes.

Rollback: revert this slice through a Security-targeted PR. That restores the
old minimal diagnostic receipt while retaining every admission guard. Preserve
the original failed-run evidence independently of rollback.
