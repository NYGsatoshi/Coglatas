# SEC-15 tool-admission assessment

Decision: **ASSESSMENT_ONLY / NOT_ADMITTED / NOT_EXECUTED**. This does not close
#688, approve a tool build, activate scanning or change protected checks.

## Existing capability and overlap

At Security source `3865b09a2d3ea3cb82f450c6d19ec643ffbae01f`,
`scripts/security/schemathesis-runner.sh` pins Schemathesis 4.25.2 by image digest.
PR retains examples/coverage/fuzzing for its three original roles. Deep retains
five roles and enables the stateful phase only for alpha-restricted, protecting
the canary Workspace/Project from administrative mutation. No budget, role,
phase or operation is reduced by this assessment.

Consequently, a new tool is not justified merely by saying existing testing is
single-request-only. [Schemathesis documents operation chaining through links
and learned relationships](https://schemathesis.readthedocs.io/en/stable/explanations/stateful/).
That general upstream capability does not prove actual phase/transition coverage
in this repository's pinned version. The current successful PR observation ran
the PR phases, not the deep stateful phase. Exact-source deep transition evidence
and its current limitations must be reconciled first.

## Candidate and costs

[RESTler's upstream README](https://github.com/microsoft/restler-fuzzer) describes
producer/consumer inference, sequence exploration, compile/test/fuzz-lean/fuzz
modes and specialized bug checkers. These overlap existing stateful capabilities;
the concrete additional Coglatas detection/sequence gap is **UNVERIFIED**.
The candidate must demonstrate a useful current transition that existing controls
cannot adequately verify, rather than merely execute another scanner.

Read-only upstream metadata on 2026-11-06 reports an unarchived repository,
MIT license and last push 2026-06-10. Observed tag `v9.3.1` points to commit
`eaacd92733771d35e90acfb2040fe37d4e5d09b1`; this is a potential review input,
not an approved installation pin or evidence of current compatibility. The
[MIT license](https://github.com/microsoft/restler-fuzzer/blob/eaacd92733771d35e90acfb2040fe37d4e5d09b1/LICENSE)
permits reuse subject to its terms.

Maintenance would include a separately pinned build/container and dependencies,
compiler/grammar/dictionary review, authenticated cookie/CSRF refresh, synthetic
fixture lifecycle, deterministic budgets, coverage reconciliation, secret-safe
failure/replay handling, vulnerability maintenance and evidence adapters. No
measured runner/storage estimate or successful local/native compile is available.
Compatibility with the exact canonical OpenAPI 3.1.1 artifact remains UNVERIFIED;
no lossy downgrade or operation removal is accepted as a compatibility repair.

## Bounded admission work still required

1. Map current Workspace/Project creation/activation, membership/role changes and
   File/download-grant lifecycle sequences against existing deep Schemathesis and
   domain-specific controls. Representative grant and realtime tests already
   exist; RESTler's HTTP sequences do not qualify SignalR transport isolation.
2. Establish at least one additional detection/coverage requirement and compare
   existing-tool extension with the candidate's licensing, trust and cost burden.
3. Review an immutable compiler/runtime build and compile the unmodified exact
   SEC-01 artifact. Account for every admitted/excluded/unresolved operation and
   dependency; missing/failed compilation is not a successful zero-finding run.
4. Qualify SEC-02 isolation, synthetic role separation, auth/CSRF lifetime,
   finite execution/coverage states and SEC-08-compatible sanitization before any
   active execution. Unknown targets, boundaries or identities fail closed.
5. Start with bounded isolated Manual test/setup qualification. Deeper exploration
   belongs to isolated Manual/Nightly. Protected PR work must stay deterministic
   and short, with measured cost and separately approved topology changes.

No tool was installed or executed and no active scanner target was contacted.
Owner Medium governance, SEC-20 adapters and SEC-14 exact-candidate release
qualification retain their independent holds.
