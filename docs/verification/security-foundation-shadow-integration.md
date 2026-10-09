# Pre-Avalonia Security Foundation Shadow integration

Issue: #1121 (SEC-FND-05). Integration status: **SEAM_ONLY**.
Production compiler call site: **NOT_INTEGRATED**. This status must change only
when #905 supplies and verifies its real analysis pipeline.

## Implemented seam

`IRevisionSecurityGate` accepts the existing immutable Security request and
independent host evidence. `RevisionSecurityGate` uses the existing canonical
binding, coordinator and authorized durable store. It awaits Pending creation,
evaluation and terminalization, then returns only safe non-authoritative metadata.
It neither modifies Source nor invokes review, permission, promotion or Merge.

The future #905 host calls this seam after obtaining frozen validated
Source/Revision/Candidate input and applicable trusted policy/compiler evidence,
then continues its existing decision path. The headless fixture exercises that
call order with both existing allowed and denied decisions, existing failed
required checks/conflicts/head metadata, and unchanged serialized decision bytes.
This proves the seam, not a production compiler or the full #906 Merge engine.

Application composition defaults to Disabled. Application-only hosts register
an explicitly unavailable store with no access or
write capability; Infrastructure replaces it with the current-authorized EF store.
Web composition reads effective `IOptions<SecurityOptions>`, including
programmatic overrides, and supplies the
mode to the gate. A request cannot activate Shadow against Disabled host options.
Enforce and a pre-enabled enforcement flag fail configuration/gate validation;
there is no runtime enforcement implementation.

## Failure and attachment semantics

Disabled invokes neither the evaluator nor persistence. All four Shadow outcomes
remain nonblocking, including Deny and Quarantine. Evaluator exceptions produce
a finite Failed reason without exception text; caller cancellation and cooperative
coordinator timeout retain their distinct execution states.

Recording status is independent of execution. `Recorded` means this call committed
this terminal result. A committed Pending record is never advertised as a recorded
final decision. An existing terminal record may belong to a competing call and is
not credited to the current result. Failed/unavailable creation still permits
Shadow analysis. Failed terminalization leaves truthful recording metadata and
does not throw into the existing decision path.

Cleanup after a committed Pending record uses a separate five-second cooperative
token and remains awaited, including caller cancellation. There is no detached
task, fire-and-forget write or timeout race. The timeout cannot forcibly stop an
adapter that ignores cancellation; existing adapters must cooperate.

`Matches` compares the existing canonical binding to a freshly supplied host
binding. It includes Tenant/Project/Branch/Revision/Candidate, canonical input,
claims and independently supplied policy/compiler identities. Equality is an
integrity comparison, never authority or proof that missing evidence exists.
Historical authorized freshness/read semantics remain #1122.

## Verification checkpoint

The initial 39 gate cases pass, including all outcome/failure paths for both
existing allowed and denied decisions, exact binding differences, awaited
terminal persistence, cancelled cleanup, configured Disabled/Shadow and Enforce
rejection. Provider coverage connects the real three-rule coordinator and store,
checks committed safe metadata and unchanged Source, and forces a PostgreSQL
terminal-write failure with rollback to Pending and no false terminal durability.
The final local focused suite, architecture and protected PR/Main qualification
are recorded in the issue completion comment after execution.

PR #1142 passed protected checks at head
`a47de9cd501d470752609fd738d3fbacce0f69c1`, run 37876713032 attempt 1:
2,032 backend tests and 12 architecture checks on PostgreSQL 18.6; all six
required app-15368 contexts, four Functional domains, Security and ReSharper
passed. It merged normally as `23f09b53424516e5385684e929c8cec935bf5d8a`.
The original PR run's two test warnings were repaired in a forward commit;
failed SARIF artifact 11592925664 was downloaded and SHA-256 verified as
`5b81881de964981d51594d47643b39df89b42bbccea6e761265f193ee08612ae`.

The first exact-Main run 37879082862 passed backend, frontend and licensed
acceptance but failed full Qodana: `UseAwaitUsing` increased from budget 2 to 4.
The two new findings are the test service providers in
`CompositionRootUsesEffectiveHostOptions` and
`ApplicationOnlyHostHasNoPersistenceAuthorityAndDefaultsToDisabled`.
Inventory artifact 11593727791 was downloaded and its published SHA-256 matched:
`d45bcdd51ff5910c3cdc8cede08cb2c4cabb6aa284169ffdad72156d5ad21e29`.
Two existing unrelated disposal findings remain unchanged. The repair adds
`await` to those test providers' disposal; production code, budgets and
suppression policy remain unchanged. Repair PR checks/merge and final Main
qualification are still pending; #1121 remains open.

No new package, schema/migration, external Revision API, production compiler,
#906 Merge engine, enforcement, policy DSL, Semantic Firewall or Avalonia is added.
The private Draft #86 is unratified; fixed S selections and future public API/
retention/enforcement HOLDs remain intact. Performance remains
SUSPENDED / NOT_EVALUATED, with #1128/#1046 separate.
