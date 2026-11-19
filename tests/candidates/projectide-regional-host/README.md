# P0 #1966 — independent regional host and session ownership

This Security-owned cohort executes the real public local regional host/session
protocol with independently authored Source, constraints, cross-Project
relations, fixed snapshots and explicit host profiles. It imports no Product
test helpers, substitutes no host response and constructs no internal receipt
or compiler graph.

Its existing scope is the [regional host handoff](https://github.com/NYGsatoshi/Coglatas/issues/1966#issuecomment-6942141583)
and [earlier output completion boundary](https://github.com/NYGsatoshi/Coglatas/issues/1966#issuecomment-6944207630).
The Domain collector/snapshot and the older Compiler-output cohorts remain
separately source-bound. This candidate has no dependency on another Security
candidate and changes no Product implementation.

## Frozen composition

- Product commit: `09347c1113f5f9c007bbbc5052278d5b5151feb5`.
- Product tree: `5abb6b4f4389f200a289a84b99a0dd13e60072cc`.
- SDK: the original Product .NET `10.0.403`.
- Scope: `SYNTHETIC_REGIONAL_HOST_SESSION_ONLY`.
- Composed destination: `tests/Coglatas.Tests/ProjectIde/SecurityRegionalHostCandidateTests.cs`.

Only that exact C# source blob is added to a fresh detached Product checkout.
The separate Ubuntu 24.04 and Windows advisory pins source, runner and verifier
control blobs, retains their original workflow context and runs the actual
compiled xUnit cohort under the Product SDK. Every Git subprocess strips
inherited `GIT_*` overrides. Actual source bytes, full staged
delta, original Product HEAD, absence of unrelated tracked edits and unexpected
untracked inputs are rechecked after composition, restore, test and discovery.

Native admission requires exactly the frozen 24 fully qualified facts with zero
failed, skipped, unexecuted or incomplete cases. The supported original
TeamTest/2010 results, test definitions, entries, test/execution IDs and compiled
discovery must agree bijectively. Original C# bytes, source/composed-tree
receipts, native TRX/discovery, command logs and SHA256 hashes are preserved
independently for both hosts.

## Independent contract coverage

| Contract boundary | Required result |
| --- | --- |
| Declaration before execution | Exact Core execution/materialization, all policy dimensions, client generation and owned run are bound before reservation. |
| Actual diagnostic rerun | Manifest-only original and later observed invocation have distinct run/attempt identities and an explicit origin; the later graph cannot promote or rewrite the original receipt. |
| True/false/Unknown execution | Real two-region runs preserve ordinary Compiler bytes and logical operations; host status, Compiler execution/coverage/verdict/evidence and capture state remain separate. |
| Equal request substitution | A new equal-content request cannot execute the earlier manifest even when all digests match; the exact original request remains usable. |
| Every capture policy dimension | Mode, evaluation count, node count and body bytes alter regional execution binding while preserving the older Core input. |
| Client generation | Equal execution hashes cannot erase the separately bound generation. A new owned diagnostic declaration can intentionally advance generation. |
| Materializations | Missing, wrong-version, denied and incomplete snapshots plus independently authored unit/calendar materializations change binding and cannot borrow the old manifest or diagnostic origin. |
| Source replacement | A genuinely changed Source/input cannot use the prior input's owned receipt. |
| Preparation versus reservation | Sixty-four preparations reserve nothing; an original declaration remains unchanged and can still execute. This is not a heap-retention measurement. |
| Direct host versus session | Direct attempts get different actual attempt IDs without reserving the session or creating a paired session receipt; the later recorded session attempt cannot borrow either earlier response. |
| Genuine older invocation | Equal original result/observation hashes do not satisfy the exact newly intended manifest or receipt/response pair. |
| Equal recreated manifest | A second genuine declaration with the same run and equal metadata has a different ownership token; it cannot replace the first response's manifest or execute the run twice. |
| Foreign sessions | Other sessions cannot execute an owned declaration or borrow a genuine origin. |
| Concurrent duplicate execution | Four concurrently released attempts yield one actual recorded invocation and one reservation; the other three fail as duplicates. |
| Actual bounded lineage | Thirty-two executed local runs retain exactly 31 ancestors on the final manifest; lineage overflow, run reuse, cycles and further capacity are refused. |
| Cancellation and retry | A precancelled actual attempt has no Compiler envelope/observation metadata. Only a new run/attempt retries it, with the cancelled receipt retained as its explicit origin. |
| Disposal | Disposal clears reservation metadata and prevents further preparation/execution; previously returned caller-owned frames remain unchanged and no physical erasure is asserted. |
| Capture gaps and Compiler failure | A completed host can carry Compiler Fail and an evaluation/node/body capture gap, or a resource-exhausted Compiler with interrupted capture. None becomes Compiler PASS. |
| Unsupported or malformed input | An incompatible host profile returns no invented envelope; invalid Source/manifest bytes or wrong input digest fail before reservation. |
| Metadata and immutable copies | Automatic serialization omits Core/Execution and original operator bodies. Detached byte/JSON edits cannot rewrite request/declaration/response/receipt data; an old region cannot open a new run's graph. |
| Older protocols and input buffers | Ordinary and Proof-array host results remain unchanged and separate. Caller byte buffers, recovered copies and a cleared input list cannot rewrite the already constructed immutable transfer/request. |

All fixtures remain within the original 1,000,000-operation Compiler profile.
The 32-run and 31-ancestor checks are actual public session executions under
their existing local limits. No private state mutation, reflection-created
receipt or fabricated Compiler PASS is used.

## Verifier controls and limits

Sixteen portable Python unittest methods cover synthetic TRX/discovery positives
and exact-error mutations, including foreign/aliased class or method identities,
nonbijective/duplicate execution IDs, incomplete counters and extra result
containers. Real disposable Git controls cover hostile environment redirection,
HEAD/index/working-byte drift, unrelated staged-only edits hidden by restored
working bytes and untracked C# injection. These tests qualify evidence admission
and source composition; their success does not claim C# execution.

```text
python -B -m unittest discover -s tests/candidates/projectide-regional-host -p test_compose_and_verify.py -v
python -B tests/candidates/projectide-regional-host/compose_and_verify.py --product-head 09347c1113f5f9c007bbbc5052278d5b5151feb5 --worktree <fresh-product-worktree> --evidence <fresh-evidence-directory>
```

No native .NET execution is claimed by this source preparation. Exact-source
hosted execution and independent inspection of original artifact ZIPs,
TRX/discovery inventories and source/member hashes are required separately.
Precancelled tests establish that explicit boundary; they do not establish
deterministic mid-evaluation cancellation/disposal race timing.

Local object ownership does not grant current actor/Tenant/Project permission,
session liveness, client selection, freshness or remote producer attestation.
No runtime query/open/export authority, persisted revocation, retention quota,
redaction/deletion, durable storage, physical memory erasure, Web/Avalonia or
actual Native UI is qualified. The unit/calendar tests establish binding and
origin refusal, not separate unit/calendar runtime correctness.

Every receipt retains `requiredCheckQualified`,
`independentReviewComplete`, `nativeUiQualified`,
`productionQualified`, `trustQualified` and
`releaseQualified` as **false**. #1966 and broader Product
parents stay OPEN; #1144 remains BLOCKED, #1128 remains
SUSPENDED/NOT_EVALUATED and Release remains NO_GO. Only this new candidate folder
and its dedicated read-only advisory are added.

## Ignored compiler-input correction

The original byte guard used Git ignore exclusions, so an ignored C# file in
`tests/Coglatas.Tests/data/` could enter SDK compile globs without rejection.
The corrected guard enumerates every untracked path, including ignored paths,
with NUL-delimited names. Initial composition admits no generated outputs; after
restore, only `bin/` and `obj/` under the nine projects in the frozen test
reference graph are allowed. Nested lookalikes, data, artifacts and node_modules
remain refused. Two additional controls reproduce the ignored-input refusal and
separately admit legitimate SDK outputs only after restore. The 24 C# facts and
Product pin are unchanged; old-source execution is retained as historical evidence.

## Executing workflow revision

Fresh runs use the shared [executing workflow provenance contract](../workflow-provenance/README.md)
from #2089. Actual workflow SHA/ref, exact merge parents, workflow mode/blob and
complete tree are checked independently from the Test checkout and Product pin.
The additional provenance controls grant no native execution or qualification
credit. Earlier receipts remain unchanged with the precise limitation documented
in that contract; they are never retroactively restamped.

## Passing native reports

The 24 expected unit results must be direct children of the TRX `Results` element.
Nested result groups, additional test results and error details invalidate the
receipt even when the counters and all 24 fact identities match. The native
summary may retain informational messages under `RunInfos`. A warning or any other outcome
makes this positive-only receipt incomplete. Ordinary adapter output is preserved.
