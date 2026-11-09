# Composer comparator evidence: 2026-10-26

## Disposition

**OWNER_HOLD / REVIEW_ONLY / NO MERGE for Product PR #1361. DIAGNOSTIC ONLY / NEVER MERGE for PR #1447. Student Council RELEASE_NO_GO.**

This source-bound record compares the first attempts of two existing candidates. It records an observed controlled mechanism; it does not authorize integration, close P0 #1360, identify the initiating trigger of every historical failure, or qualify a later Main runtime.

Product Main was re-read at 2026-10-26 23:27 UTC as [`bccc119eb9f15f490c4f2e739c55eab3bcfeb4b2`](https://github.com/NYGsatoshi/Coglatas/commit/bccc119eb9f15f490c4f2e739c55eab3bcfeb4b2). The two compared PR runs used the earlier common base `e191494144628150d3d5136d7e704b270e67b342`. Neither run is a test of Main `bccc119...`.

## Exact sources and first attempts

| Field | Unpatched diagnostic RED | Guarded held GREEN |
| --- | --- | --- |
| PR | [#1447](https://github.com/NYGsatoshi/Coglatas/pull/1447) | [#1361](https://github.com/NYGsatoshi/Coglatas/pull/1361) |
| Source HEAD | `4a60b13c5aab805079d2ce109b3b168fa242f3d5` | `710d2926fbcfa59c8e5eca40eef7dfc06dc3c807` |
| Common base | `e191494144628150d3d5136d7e704b270e67b342` | `e191494144628150d3d5136d7e704b270e67b342` |
| Actual tested merge | `8831bb9b6e5f3971c48ff656a8887a075a902cd7` | `dedcc9afd8cfbfa253274ea6141cec8ae8949fd6` |
| HEAD and tested-merge tree | `e48f8557984260843e2a39377a6b5c0964fce47c` | `d7f38107173c9cf221386a3c4c56cf4b5c46668d` |
| PR CI | [40376808589, attempt 1](https://github.com/NYGsatoshi/Coglatas/actions/runs/40376808589) | [40376612176, attempt 1](https://github.com/NYGsatoshi/Coglatas/actions/runs/40376612176) |
| Complete PR-CI inventory | 12 SUCCESS / 4 FAILURE / 0 SKIP | 16 SUCCESS / 0 FAILURE / 0 SKIP |
| Angular job | [121764134341](https://github.com/NYGsatoshi/Coglatas/actions/runs/40376808589/job/121764134341) | [121764672926](https://github.com/NYGsatoshi/Coglatas/actions/runs/40376612176/job/121764672926) |
| Angular units | 1869 PASS / 1 FAIL | 1893 PASS |
| Real Functional collaboration job | [121764983354](https://github.com/NYGsatoshi/Coglatas/actions/runs/40376808589/job/121764983354) | [121765248753](https://github.com/NYGsatoshi/Coglatas/actions/runs/40376612176/job/121765248753) |
| FUNC-MSG-001 | FAIL, 6767 ms, attempts=1 | PASS, 6936 ms, attempts=1 |
| FUNC-NOTIF-001 positive control | PASS, 836 ms, attempts=1 | PASS, 783 ms, attempts=1 |

Git Data reads establish that each tested merge has the ordered parents [common base, respective Source HEAD], and its tree equals that candidate HEAD's tree. Job checkout logs independently name the actual tested merge. The RED failures originate in the frontend and Functional collaboration jobs; the remaining two failures are aggregates. Other workflows outside the 16-job PR-CI inventory are not included in those counts.

At this record's original source capture, the #1361 body still led with older `e1d67844...` evidence. Its actual API HEAD was `710d2926...`; old body claims did not qualify that captured HEAD or later Main.

## Comparator authenticity

The entire canonical Functional owner file, `tests/functional/messaging/collaboration-owner-journey.spec.ts`, has the identical Git blob `bb5750238883f550a7f3cba51db9440d6905dcfd` on both candidate HEADs. The 44 added Composer control lines, existing Thread diagnostics, original actions, denial assertions, deadlines and retry policy are shared unchanged.

The RED PR changes only that Functional file and a 29-line mounted continuity test in `frontend/src/app/features/messaging/message-catch-up-draft.spec.ts`: two test files, 73 additions, no product implementation, workflow, backend, policy, migration or dependency changes relative to its base. The same 29-line mounted case is present in the held candidate, whose entire unit file additionally includes its implementation-specific controls.

Two separate read-only agents inspected the source, job logs and blob equality. No local execution is claimed by this record. A Draft CodeRabbit skip is not counted as an independent review.

## Observed RED and bounded interpretation

The Angular job failed at `message-catch-up-draft.spec.ts:113`, requiring `textarea.isConnected` to be true during a pending registered routine catch-up. Before that assertion, the test required loading state, an empty conversation identity, no messages/capabilities, and no POST.

The real Functional job completed the authorized messaging prerequisites, Thread stage and denied-Thread stage, then reached `MSG-RECOVERY`. Its original assertion at `collaboration-owner-journey.spec.ts:584` reported:

```text
Expected: true
Received: false
expect(await originalComposerNode!.evaluate((node) => node.isConnected)).toBe(true)
```

The immediately preceding assertions positively observed the one-shot automatic reconnect GET barrier and the absence of the old protected message. The failure therefore observes removal of the original editable node in this controlled routine recovery interval. It did not reach the later continuation-input, fresh-authority and Send re-enable assertions. The independent Notification owner succeeded. Neither aggregate converted that failure into PASS.

The route barrier awaits a promise before `route.continue()`. It delays network continuation of a genuine automatic GET; it does **not** establish that an HTTP response had already arrived at Angular while paused. Backend, database and SignalR execution remain real, with synthetic test identities. This controlled interleaving is not proof that every historical lost-input report used this trigger.

The held GREEN run executes the identical owner and passes its pending-GET node/draft retention, Send-disabled, continued native input and later fresh-authority checks. Its unit controls additionally cover DM/channel bindings, synthetic IME, ReadOnly/removal, session/Tenant/authorization/Workspace boundaries, stale actor/scope/generation events, denial/error responses and pending sends. Hosted success does not establish physical IME or complete browser/device acceptance.

## Artifact identity and preservation limits

The RED collaboration diagnostics artifact is [12675751055](https://github.com/NYGsatoshi/Coglatas/actions/runs/40376808589/artifacts/12675751055), named `functional-execution-diagnostics-functional-fast-collaboration-1`, 144907 bytes.

GitHub advertises ZIP digest `sha256:b8f1ebba856ac3be05881dcc796c2ad687583565e42bfa7d65a22485afe483ec`. This is **provider-advertised metadata, not an independently verified byte digest**. Byte retention, ZIP-content inspection and independently computed digest status: **NOT_VERIFIED**. The connector returned a reusable file reference, but no usable local executor was available to retain, inspect or hash the ZIP. Do not claim that its contents or bytes were verified, and do not close #1447 as evidence-complete until bytes and first-attempt evidence are retained.

The failing assertion and source attribution above were independently inspected in native GitHub job logs. No signed download URL, credential, cookie, user identity, draft content or private specification is reproduced here. Original failures, provider logs and artifacts are not deleted or rerun.

## Latest Main delta and next qualification

[Common base to current Main comparison](https://github.com/NYGsatoshi/Coglatas/compare/e191494144628150d3d5136d7e704b270e67b342...bccc119eb9f15f490c4f2e739c55eab3bcfeb4b2) changes only:

- `coglatas-frontend/package.json`
- `coglatas-frontend/package-lock.json`
- `frontend/package.json`
- `frontend/package-lock.json`

These contain the merged fast-uri 4, webpack-dev-server 6 and uuid 14 dependency changes. Exact Git-tree blob comparison confirms no other current-Main production source delta from the comparator's base. Dependency changes still prevent automatic transfer of the earlier runtime result to Main `bccc119...`.

Before any new integration judgment: retain and hash the diagnostic bytes; execute the same comparison against the fresh dependency/source combination; review the held candidate's actual HEAD and current authority boundaries; keep all mandatory assertions/deadlines/retries unchanged. Do not move held implementation into another PR. #1447 remains diagnostic and must ultimately close **without merge** after evidence retention. #1361 remains Draft/Owner-held until explicit applicable Owner authorization. Security, RLS, recovery, target/operator, public endpoint, real-user and school-data gates remain separate and unapproved by these technical results.

## Subsequent branch qualification: 2026-10-26 23:42 UTC

After the historical comparison above was recorded, independently reviewed normal two-parent merges brought both existing Draft branches onto pinned Main `bccc119eb9f15f490c4f2e739c55eab3bcfeb4b2`. Full untruncated Git-tree comparisons established that Main's four dependency files were disjoint from the held candidate's twelve paths and the diagnostic candidate's two paths. Only those four dependency blobs changed; all candidate source/test blobs and the identical whole Functional owner blob `bb5750238883f550a7f3cba51db9440d6905dcfd` were preserved. The existing branch refs advanced with expected-SHA leases and force disabled.

| Field | Held #1361 qualification | Diagnostic #1447 qualification |
| --- | --- | --- |
| New Source HEAD | `d528bb5df566ffdc0b1b4cae20dbf37470204845` | `897ff435bfbbac0a83bf04d2dd0db628b76ebe74` |
| Ordered source parents | [`710d2926fbcfa59c8e5eca40eef7dfc06dc3c807`, pinned Main `bccc119...`] | [`4a60b13c5aab805079d2ce109b3b168fa242f3d5`, pinned Main `bccc119...`] |
| Source tree | `599e34ae55c1a5c3534689f57775110715180915` | `867f15e43025f8ffb30ab8523c72b3231d64b852` |
| Actual tested merge | `a4fa0df7b7d97e792b5786fc6b438e0e3a72ef0e` | `872db2b5797ee49a291d1ee02fbd948da9c198a6` |
| New PR CI | [40421397385, attempt 1](https://github.com/NYGsatoshi/Coglatas/actions/runs/40421397385) | [40421398598, attempt 1](https://github.com/NYGsatoshi/Coglatas/actions/runs/40421398598) |
| Observed at this note | IN_PROGRESS; no qualification result | IN_PROGRESS; no qualification result |

Each new actual tested merge has ordered parents [pinned Main, respective new Source HEAD] and the same tree as that Source HEAD. These facts bind the pending runs without predicting their result. The original first-attempt tables and failures above remain unchanged; no old success or failure is relabeled, deleted or rerun. Artifact-byte retention remains NOT_VERIFIED.

This is branch qualification only. Neither held branch was marked ready, no Main integration occurred, and #1361's OWNER_HOLD / NO MERGE and #1447's DIAGNOSTIC ONLY / NEVER MERGE remain binding.
