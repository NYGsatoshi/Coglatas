# SEC-14 isolated native browser controls

This additive #614 fixture executes the existing pinned ZAP 2.17.0 image's
real Firefox AJAX spider. The fixture server and scanner share one container
with `--network none`; the only permitted target is `127.0.0.1:8123`. No product
server, release image, deployment, authenticated account or external callback
is contacted. These executions establish native tooling and evidence handling.
They do not qualify product authorization, canonical SPEC coverage, Tier B or
release acceptance.

## Execution and evidence

Run from a clean repository root with an independently selected exact commit:

```bash
node scripts/security/run-sec14-browser-fixture.mjs \
  --candidate-sha "$EXPECTED_SHA" --case normal \
  --report artifacts/sec14-native/new-normal-receipt.json
```

`--development` permits a dirty review patch and records that state explicitly.
It does not bind uncommitted bytes to the named candidate. Unknown or duplicate
options, non-exact candidate IDs, out-of-scope output paths and arbitrary targets
are rejected before subprocess execution. Scoped LF attributes preserve the
five execution-source byte snapshots across supported Git checkouts.

The launcher checks actual HEAD, fingerprints the five owned execution sources
before and after scanning, and records a unique local execution identity. It
limits the native process to five minutes and captured subprocess output to
1 MiB. The internal scanner timeout is four minutes. Only the uniquely named,
label-matched owned container and its verified temporary directory are cleaned
up. Raw reports remain temporary and are never uploaded.

The adapter parses and hashes each native JSON from one regular-file stream,
bounded to 32 MiB plus one overflow byte. Duplicate JSON fields, non-finite
numbers, excessive nesting, unknown context fields, wrong candidate/run/tool,
changed source or native bytes, stale execution windows and native report
timestamps outside the execution window fail visibly. Positive browser and
authorized-operation counts must precede credit for a negative scope control.
Missing sites, zero AJAX discovery, disabled verification, scanner timeout,
unauthorized 200s and High scanner findings cannot yield `MATCHED`.

The exclusive `coglatas-sec14-browser-tooling-advisory-v1` receipt retains only
closed numeric rule classifications, operation counters, tool IDs, source/input
hashes and timing. It drops native URLs, headers, alert names, request/response
bodies, evidence, parameters and free-form values. The read-only consumer
recomputes the result while the original native bytes still exist and compares
both the full document and exact serialized receipt hash.

After temporary cleanup, a retained sanitized JSON alone cannot repeat full
native-byte verification or authenticate execution. Later complete verification
requires separately retained approved raw evidence or a new capture. Candidate
and local run consistency do not authenticate GitHub runs, loaded scanner code,
product ancestry, cryptographic signatures or personal owner approval; all such
fields remain `UNVERIFIED`. Release and PRE-AVALONIA verdicts remain `BLOCKED`.

## Real native positive and negative controls

The development source was based on
`f432500e28bd6a99b489016fb001066a918e2309`; each receipt explicitly identifies
the uncommitted source state and actual source hashes. ZAP's native process
exited zero in the following test-owned controls:

| Case | Root | AJAX | Authorized 200 | Scope 403 | Unauthorized 200 | Required disposition |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Normal | 2 | 3 | 2 | 2 | 0 | `MATCHED`, tooling only |
| Weakened scope | 2 | 3 | 2 | 0 | 2 | `FAILED`, unauthorized delivery detected |
| AJAX job removed | 0 | 0 | 0 | 0 | 0 | `ERROR` or `FAILED`, missing execution coverage |

The normal native report retains two Medium, two Low and one Informational rule
classifications. These deliberately incomplete fixture response headers remain
visible findings; no exception, baseline or alert filter was introduced.
The weakened fixture remains a deliberate-invalid control, never a product
security policy. The initial inline-shell attempt produced no scanner artifacts
and receives no execution credit. Test-harness import/discovery failures remain
in private development logs rather than being rewritten.

Thirty-one focused Python controls cover the producer/consumer, stale or tampered
evidence, forged approval, changed scope, sanitizer canaries, native findings,
missing coverage, bounded streams and a real POSIX FIFO rejection. Eight Node
controls reject invalid launcher inputs with an empty executable PATH. The
existing release contract lane discovers the Python controls and runs the Node
controls; it does not launch a product scanner or add a required check.

## Outstanding advanced acceptance

All 14 complete #614 Tier B acceptance categories remain outstanding. The
release Advisory inventory still reports four missing product-qualification
adapters: RESTler, browser/AJAX ZAP, image/artifact secrets and SEC-20 advanced
normalization. This isolated browser decoder is a concrete partial implementation
for the browser category, with no product or release-image provenance. It does
not silently remove that missing acceptance condition.

A production browser adapter still needs the actual composed protected UI,
approved multi-role fixtures, immutable image/build binding and measured product
coverage. Approved OAST applicability and collector authority remain separate
owner holds; no collector, schedule or infrastructure was activated. Optional
Burp remains optional and cannot substitute for ZAP.

The plan uses the official [AJAX automation parameters](https://www.zaproxy.org/docs/desktop/addons/ajax-spider/automation/)
and the [pinned ZAP Docker image](https://github.com/zaproxy/zaproxy/blob/v2.17.0/docker/Dockerfile-stable),
which contains Firefox ESR and native Selenium support.
