# Same-document actor replacement: canonical producer qualification

The Auth/Realtime repair now has a fixed browser consumer for its canonical PR
runtime and for the resulting Main runtime. Both use the same run's existing
artifact producer; neither builds an application or accepts an external run,
source ref, command, fixture target or secret. The six protected check identities
and their aggregation are unchanged. The new job must independently succeed
before this repair is considered qualified.

The exact frozen journey is copied to `tests/ui/actor-replacement-real.mjs`,
SHA-256 `b8eaf7577da1503db15ac85f57331090b40c981d932727f648dd2216907ba942`.
Its mixed CRLF/LF bytes are intentional frozen evidence; a path-specific
.gitattributes entry prevents normalization and recognizes CR at end of line.
The initial cached whitespace check reported those CR characters; no assertion
or global whitespace policy was changed. Its 17 criteria, one native `history.go(delta)`, same-document sentinel/CDP
checks, 15-second continuously live A-only negative observation, assertions,
wait budgets and 240-second watchdog are unchanged. This is one real browser
journey with 17 criteria, not 17 Playwright Test cases. Browser login and browser
receive/render/socket observation are real. C originates the business messages
through the authorized API. The selected MessageCreated/body exclusion does not
establish complete Hub confidentiality, every authorization path or UI send.

## Source and runtime admission

The consumer checks out the actual producer revision. A PR runtime must retain
its separate PR-head stamp and its actual merge parent; its complete tree must
match the reviewed PR head tree. A merge revision is never relabeled as that
head. Main admits only an exact-source Main push. Both frontend and .NET producer
stamps, runtime tag, producer/engine image identities, exact harness/helper bytes,
and before/after app-container continuity are checked. The existing strict image
binding verifier handles differing Docker manifest identities. The finite
receipt includes source/head/tree, run/attempt, frozen-script and runtime-archive
hashes, actual tool versions and image identities.

`functional-compose-harness.sh` and `acceptance-image-reuse.sh` own the isolated
stack lifecycle, migrations, image checks and teardown. The actor wrapper does
not implement a second stack. Harness exit zero includes successful cleanup;
child failure, signal, cleanup failure, missing/stale/malformed browser report,
missing image record, source drift or any false/missing criterion fails closed.

## Synthetic configuration and publication

The fixed overlay uses Test mode, the existing smoke seed and documented initial
admin bootstrap. Fixture/owner passwords are fresh and masked, including derived
recipient/operator forms, before spawning the harness. There are no published
host ports. The probe shares the app network namespace and uses loopback HTTP.
It explicitly disables and verifies FileSharing through the existing real owner
API. This is not upload admission or scanner acceptance.

The immutable Noble Playwright image uses digest
`sha256:06a9939e57531807f8d5fd76ce44b53165ffb7d7501d87ab10e285c20b1e971f`.
Locked host-installed dependencies and source mount read-only. A fresh private
host-owned control directory is the only writable probe mount; the probe runs as
the host UID/GID so its mode-0600 report remains privately readable by the host.
HOME/TMP and raw diagnostics stay under that directory. No raw report, browser
or service logs, frame data, URLs, cookies, identifiers, body/proof hashes,
credentials, traces or screenshots are uploaded. Publication selects one finite
receipt path only; failure publication contains only a fixed phase and status.

## Verification and retained history

Local Windows Node 24.22.2 syntax checks and eight control-plane tests pass,
with zero failures/skips. They exercise each missing/false criterion, stale
receipts, changed source/head/stamps/selection, altered observation/watchdog,
raw diagnostic fields, child failures/signals and image/cleanup evidence.
They are not browser, Docker, PostgreSQL or wire acceptance. No local Docker or
global SDK operation was attempted for this candidate. The current hosted
canonical consumer and resulting Main execution remain unverified until run.

Historical Main88 actor continuity RED, two incomplete history-harness attempts,
and the earlier A315/4cf bounded 17-criterion GREEN remain source-bound history.
They do not qualify this new producer. Mobile, HTTPS, approved target operation,
independent Pilot acceptance and formal Owner release authority remain separate.
RELEASE_NO_GO remains.

## Minimal runner quality repair

The official #1575 Gate for source 548130aca3ffaf20f7fbd508b26bc52fe52262cc,
check 124352652357, remains FAIL: 22.3 percent new coverage, Reliability D,
Security B and 24 open issues. The separately recorded actual 17-criterion
browser PASS does not waive this Gate and is not transferred to a new wrapper
revision. The eight pure control-plane tests are a third, separate concern.

Two introduced findings receive a narrow repair: S2871
AaGO_hmVmz04xYEW3Udy now uses an explicit UTF-16 code-unit string comparator
for Object.keys results; S4036 AaGO_hksmz04xYEW3Udx now uses /usr/bin/bash.
Every command invocation in this Linux-only wrapper uses its fixed absolute
/usr/bin/git or /usr/bin/tar path as well. Argument arrays, admission guards,
child environment, signal/cleanup handling and receipt publication are unchanged;
there is no PATH fallback or new executable authority.

Pinned portable Windows Node24.22.2 passes all eight existing pure controls
with zero failures/cancellations/skips, and both edited modules pass syntax
checks. These controls do not invoke the actual Linux programs. Reverse-edit
source guards recover both prior modules byte-for-byte. The four Auth/Realtime
implementation/test blobs, frozen scenario SHA-256 b8eaf7577da1503db15ac85f57331090b40c981d932727f648dd2216907ba942,
Bash harness, seventeen criteria, fifteen-second observation, watchdog, protected
checks and workflow permissions are unchanged. No dependency or tool was installed.

This does not repair or predict coverage. Current-head Linux/browser acceptance,
new official Sonar findings/Gate and resulting Main qualification remain pending.
No issue disposition or style/await-loop suppression is made. RELEASE_NO_GO remains.