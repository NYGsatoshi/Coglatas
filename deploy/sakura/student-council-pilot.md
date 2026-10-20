# Student Council pilot gate and recovery drill

This is a separate pilot qualification procedure. The existing Issue #481
Project/Task public HTTPS gate and canonical `deploy.sh`, Caddy and TryCloudflare
topologies remain authoritative. No command in this document has been executed
against Sakura by preparing this change. See
[`student-council-pilot-qualification.json`](student-council-pilot-qualification.json)
for the preparation-only inaccessible-target receipt. It is not a
runtime pass.

## Decision boundary

The core gate covers Workspace reads, Conversation-backed `WorkspaceChannel`,
same-Conversation MessageThread, existing DirectDM, and File read/download. It
uses real browser login and message writes, then browser-origin API reads and
negative controls. It uses no API mocks. It does not create accounts or bypass
the official administrator bootstrap described in `docs/DEPLOYMENT.md`.

`ConversationType.Group = 7` is unsupported by the current backend. `SCOPE=full`
fails before execution. `SCOPE=core` may complete with `PILOT_PARTIAL` and
`GroupDM=NO_GO`; exit code 0 means the selected execution passed, never full
pilot approval. Core release remains subject to the other exact-target release
evidence. The Group negative control is a denial check, not a Group positive
journey. Group admission needs a separately reviewed implementation and gate.

The core bundle also requires Workspace suspension to deny a previously readable
unbound DirectDM's persisted Message body and sends. A pre-repair provider-backed
reproduction failed this boundary. The approved read-lifecycle repair merged in
[#1283](https://github.com/NYGsatoshi/Coglatas/pull/1283) with separately qualified
provider/transport controls; the original failure remains historical evidence.
The exact target still requires its own execution. The gate must issue no pass
receipt if that boundary fails there. It does not substitute participant removal,
drop Direct paths, or infer new persisted Direct affiliation semantics.

An exercise phase proves reload and logout/login persistence only. It cannot
prove process restart. The second phase checks the same durable message bodies
and File bytes after an operator-attested restart. Neither phase proves backup
restoration, image provenance, production migration safety, or #481 completion.

The negative HTTP helper checks denial status, listed protected IDs and debug
markers. It does not compare known protected Message/File content or require an
exact error envelope. Second-user receive, mentions/unread, delayed responses,
reconnect behavior and the complete prohibited File-operation matrix also need
separate exact-target evidence. A core pass does not qualify these missing checks
or grant a per-feature GO verdict.

## Protected synthetic prerequisites

The Owner must identify the existing SSH alias/host/user and public HTTPS origin
before a target operation. Do not guess an endpoint, scrape credentials, change
existing keys/volumes, deploy, run production migrations, or admit real users
from this preparation procedure. Read-only qualification may start after the
target is identified; downtime, backup, restore or rollback requires its approved
execution window. Keep the persisted edge mode and origin binding unchanged.

Provision only synthetic `@example.test` identities through supported bootstrap
and current application APIs. Do not write password hashes or membership rows
with SQL. Disable bootstrap after provisioning. The private inventory must
identify pre-existing records, not random nonexistent UUIDs:

- A member account, a separate authorized operator, and a distinct ordinary
  Member revokee. Record their exact user IDs. The member must lack access to
  the denied scopes. Do not use a PlatformAdmin as the member.
- `allowed`: active Workspace, a `WorkspaceChannel` Conversation with no Project,
  existing DirectDM and non-deleted Direct Message with a nonempty persisted body,
  Channel root Message, and a uniquely named small synthetic
  File visible on the first Files page. Record File SHA-256 and byte length
  (1 byte to 1 MiB). Member can post and create Message threads.
- `deniedSameTenant`: another Workspace, Channel, DirectDM, Channel root Message,
  persisted Direct Message body, and File. The operator must be authorized to read all these exact resources;
  the gate checks those positive controls before member denials.
- `deniedCrossTenant`: real, known synthetic IDs for the same five resource kinds
  plus a known Direct Message ID in Tenant B. Retain the authenticated Tenant B inventory verification privately
  under `inventoryReference`. The single-tenant target cannot positively read
  Tenant B resources, so this gate records existence as operator-attested;
  provider-backed cross-Tenant tests remain independent evidence.
- `revocation`: a dedicated active Workspace, WorkspaceChannel, root Message,
  Workspace-shared File, a dedicated unbound DirectDM with a known non-deleted
  Message body, and revokee `userId`. Each Direct fixture must have `ProjectId=null`
  and the fixture Workspace ID; a different allowed Direct cannot be substituted.
  The operator can manage this
  Workspace; the revokee is an active ordinary Member (`role=3`, `status=1`),
  with no alternative capability/grant that would preserve access. The gate
  suspends this membership and restores that same membership with PATCH in a
  `finally` block. Interrupted execution can leave it suspended; the operator
  must inspect and restore it before reuse.

General-user File upload intake must be disabled for this pilot. A current Tenant
Owner/Admin must use
`GET /api/tenant/settings` and CSRF-protected `PATCH /api/tenant/settings` on the
qualified synthetic Tenant. Parse its existing `featureFlagsJson`, preserve
every other entry, set `FileSharing` to `false`, then submit the merged object
as the JSON **string** in `featureFlagsJson`. For example, if no other overrides
exist, the request body is `{"featureFlagsJson":"{\"FileSharing\":false}"}`.
Verify the saved settings and `GET /api/tenant/features` no longer includes
`FileSharing`. The UI shell module key `Files` is distinct and remains visible.
This denies File/Artifact upload intake server-side while preserving separately
authorized existing File reads/downloads. Do not replace all feature flags with
an empty/default object or enable uploads to make a test pass. The gate submits
one valid `text/plain` `.txt` multipart upload and requires a redacted denial,
then requires real authorized File bytes. A misconfigured server accepting that
synthetic upload fails the gate; inspect its returned synthetic attachment in
the private operator session and remove it through the supported API.

This flag and intake control do not deny File/attachment deletion, selection
deletion, sharing changes or other mutations. Their current server authorization
remains in force. Qualify every prohibited operation independently before assigning
Files `GO_READ_ONLY`; neither hidden controls nor a core gate pass establishes it.

The complete existing three-route byte-intake inventory, strict local capture
validator, ordinary-member/Workspace-Admin authority controls, Announcement
attachment side effect and execution limits are in
[the intake policy supplement](student-council-intake-policy.md).
Its `INTAKE_CLOSED` state does not establish global read-only Files. The
supplement and local validation do not qualify the unavailable Sakura target.

Create a private JSON fixture file with this schema; substitute only verified
synthetic UUIDs. All IDs must be distinct:

```json
{
  "version": 1,
  "syntheticOnly": true,
  "existingIdsVerified": true,
  "inventoryReference": "pilot/synthetic-inventory-01",
  "memberUserId": "<UUID>",
  "operatorUserId": "<UUID>",
  "allowed": {
    "workspaceId": "<UUID>", "channelId": "<UUID>", "directId": "<UUID>", "directMessageId": "<UUID>",
    "rootMessageId": "<UUID>", "fileId": "<UUID>",
    "fileSha256": "<64 lowercase hex>", "fileSizeBytes": 42
  },
  "deniedSameTenant": {
    "workspaceId": "<UUID>", "channelId": "<UUID>", "directId": "<UUID>", "directMessageId": "<UUID>",
    "rootMessageId": "<UUID>", "fileId": "<UUID>"
  },
  "deniedCrossTenant": {
    "workspaceId": "<UUID>", "channelId": "<UUID>", "directId": "<UUID>", "directMessageId": "<UUID>",
    "rootMessageId": "<UUID>", "fileId": "<UUID>"
  },
  "revocation": {
    "workspaceId": "<UUID>", "channelId": "<UUID>", "directId": "<UUID>", "directMessageId": "<UUID>",
    "rootMessageId": "<UUID>", "fileId": "<UUID>", "userId": "<UUID>"
  }
}
```

Use an owner-only directory outside the checkout for fixture/evidence files
(`0700` directory, `0600` files on Linux; equivalent ACL on Windows). Set protected
environment variables without command echo or shell history containing secrets:

```text
COGLATAS_STUDENT_PILOT_ENABLED=1
COGLATAS_STUDENT_PILOT_SYNTHETIC_FIXTURE=1
COGLATAS_STUDENT_PILOT_SCOPE=core
COGLATAS_STUDENT_PILOT_PHASE=exercise
COGLATAS_STUDENT_PILOT_URL=<public root HTTPS origin>
COGLATAS_STUDENT_PILOT_COMMIT=<exact reviewed 40-character commit>
COGLATAS_STUDENT_PILOT_FIXTURE=<absolute private inventory JSON path>
COGLATAS_STUDENT_PILOT_EVIDENCE_DIR=<absolute fresh private directory>
COGLATAS_STUDENT_PILOT_MEMBER_EMAIL=<synthetic @example.test>
COGLATAS_STUDENT_PILOT_MEMBER_PASSWORD=<protected value>
COGLATAS_STUDENT_PILOT_OPERATOR_EMAIL=<synthetic @example.test>
COGLATAS_STUDENT_PILOT_OPERATOR_PASSWORD=<protected value>
COGLATAS_STUDENT_PILOT_REVOKEE_EMAIL=<synthetic @example.test>
COGLATAS_STUDENT_PILOT_REVOKEE_PASSWORD=<protected value>
```

Install the locked root dependencies and the pinned Playwright browser in the
approved runner. Run from the reviewed checkout:

```bash
node --test tests/ui/student-council-pilot-helpers.node-test.mjs
# Run this transport regression in the pinned Linux Playwright runner:
node --test tests/ui/student-council-pilot-origin-guard.node-test.mjs
node tests/ui/student-council-pilot-run.mjs --validate-only
node tests/ui/student-council-pilot-run.mjs
```

The runner fixes the single test and rejects selector/config/reporter overrides.
Every credential-bearing page installs Chromium CDP Fetch interception before
its first navigation. It pauses each redirect hop before transmission and aborts
requests outside the configured origin. Playwright `context.route` alone does
not observe redirected destinations and is insufficient for this boundary.
Navigation, credential fields, API calls and response predicates also bind to
the exact configured HTTPS origin; explicit helper fetches reject redirects.
Guard installation failure fails qualification. The transport regression uses
two real loopback HTTP servers and synthetic XHR login bodies: route-only 307
and 308 controls demonstrate forwarding, the CDP guard prevents every foreign
body, and same-origin POST controls still succeed. This transport-only test
does not run product APIs or qualify public HTTPS or the pilot.
There are no traces, screenshots, video, HTML/JUnit, HAR, network logging, cookie
exports, or response-body artifacts. Do not add debugging flags or run the gate
through a network-recording proxy. Failure output is deliberately generic.
`exercise-receipt.json` is sanitized; `checkpoint.json` contains only synthetic
opaque IDs and a fixture binding, and stays private. The checkpoint is created
only after all exercise checks pass. Repeated exercise requires a fresh output
directory; do not overwrite evidence to hide a prior failure.

## Restart phase

Before an authorized restart, record `docker inspect` container ID and
`State.StartedAt` for `coglatas-web` privately. Restart only the existing web
service using the current canonical Compose selection; retain PostgreSQL,
uploads and Data Protection volumes. Record the observed ID and start time
afterward, the exact image digest and source commit. `docker restart` can retain
the container ID; its start time must advance. Do not run `deploy.sh` merely to
restart because it also builds and applies migrations.

Create private `restart.json` in the same evidence directory:

```json
{
  "version": 1,
  "commit": "<same exact reviewed commit>",
  "checkpointRunId": "<runId from checkpoint.json>",
  "operatorAttested": true,
  "beforeContainerId": "<64 hex from docker inspect>",
  "afterContainerId": "<64 hex from docker inspect>",
  "beforeStartedAtUtc": "<observed UTC timestamp ending Z>",
  "afterStartedAtUtc": "<observed UTC timestamp ending Z>"
}
```

Within 24 hours, set `COGLATAS_STUDENT_PILOT_PHASE=verify-restart` and run the same
runner. It rejects a different origin, fixture or commit, missing attestation,
missing successful exercise receipt, or a restart predating the exercise. It rechecks login, positive/negative reads,
read-only intake, persisted root/reply/DirectDM bodies and actual File bytes.
Its receipt says `VERIFIED_AFTER_OPERATOR_ATTESTED_RESTART`; the browser cannot
independently observe Docker or establish image provenance. Retain the actual
operator restart evidence separately. A missing restart record gives no restart
credit. Both phases retain `GroupDM=NO_GO` and `backupRestore=NOT_EVALUATED`.

## Consistent backup during an Owner-approved window

These commands run on the identified Linux host as its authorized operator.
They are a procedure, not authorization to touch an existing deployment. Before
starting, inspect the selected Compose service names, exact images, volume mounts,
available disk space and deployment environment file permissions without printing
environment values. Resolve every path and volume from that inventory; stop if
the topology differs. Do not use `down -v`, volume prune, SQL deletes, or reset
Data Protection keys. Disable command tracing (`set +x`).

Select the canonical Compose files using the **recorded existing** edge mode;
do not guess or change it:

```bash
set -euo pipefail
umask 077
SOURCE=/srv/coglatas/app
DEPLOY_ENV=/srv/coglatas/deploy/.env
EDGE_MODE=<recorded-caddy-or-trycloudflare>
compose=(docker compose --env-file "$DEPLOY_ENV" --project-name deploy -f "$SOURCE/deploy/sakura/docker-compose.yml")
case "$EDGE_MODE" in
  caddy) ;;
  trycloudflare) compose+=(-f "$SOURCE/deploy/sakura/docker-compose.trycloudflare.yml") ;;
  *) exit 1 ;;
esac
BACKUP=/srv/coglatas/backups/pilot-$(date -u +%Y%m%dT%H%M%SZ)
mkdir -m 700 "$BACKUP"
test "$(docker inspect -f '{{.State.Running}}' coglatas-web)" = true
UPLOADS=$(docker inspect -f '{{range .Mounts}}{{if eq .Destination "/app/storage/uploads"}}{{.Name}}{{end}}{{end}}' coglatas-web)
KEYS=$(docker inspect -f '{{range .Mounts}}{{if eq .Destination "/app/data/protection-keys"}}{{.Name}}{{end}}{{end}}' coglatas-web)
PG_IMAGE=$(docker inspect -f '{{.Image}}' coglatas-db)
WEB_IMAGE=$(docker inspect -f '{{.Image}}' coglatas-web)
test -n "$UPLOADS" && test -n "$KEYS"
printf '%s\n' "$WEB_IMAGE" > "$BACKUP/web-image-id.txt"
printf '%s\n' "$PG_IMAGE" > "$BACKUP/postgres-image-id.txt"
git -C "$SOURCE" rev-parse HEAD > "$BACKUP/source-commit.txt"
docker image inspect "$WEB_IMAGE" --format '{{json .RepoDigests}}' > "$BACKUP/web-repo-digests.json"
# All app writers must stop for the entire DB + files + key capture.
"${compose[@]}" stop web
trap '"${compose[@]}" start web' EXIT
"${compose[@]}" exec -T postgres sh -c 'pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" --format=custom --no-owner --no-privileges' > "$BACKUP/database.dump"
for pair in "$UPLOADS:uploads.tar" "$KEYS:protection-keys.tar"; do
  volume=${pair%%:*}; archive=${pair#*:}
  docker run --rm --network none --read-only -v "$volume:/capture:ro" -v "$BACKUP:/backup" "$PG_IMAGE" sh -c 'tar -cpf "/backup/$1" -C /capture .' sh "$archive"
done
"${compose[@]}" exec -T postgres pg_restore --list < "$BACKUP/database.dump" > "$BACKUP/database-toc.txt"
for archive in uploads.tar protection-keys.tar; do
  docker run --rm --network none --read-only -v "$BACKUP:/backup:ro" "$PG_IMAGE" tar -tf "/backup/$archive" > /dev/null
done
(cd "$BACKUP" && sha256sum database.dump uploads.tar protection-keys.tar > SHA256SUMS)
"${compose[@]}" start web
trap - EXIT
```

Record the checkpoint/time, operator, exact source/image IDs, volume names,
database migration history, protected configuration recovery reference and RPO.
Copy the matching tracked Compose and Caddy configuration to protected recovery
storage. Recover external environment, license, TLS/tunnel credentials through
the existing secret recovery process; never print or commit them. In Caddy mode
also capture the inventoried Caddy `/data` and `/config` volumes while its writers
are quiesced in the approved window. These contain TLS material. In TryCloudflare
mode preserve the host service/configuration and the approved route record.
Encrypt and retain the complete backup off-host according to the Owner's policy.
Checksums and `pg_restore --list` prove readability, not a successful restore.

## Isolated restore drill

Use a separate approved host or an unused project name and **new empty volumes**.
The tracked `student-council-pilot-restore.compose.yml` has no canonical container
names, migrations, seed, edge service, published ports or automatic deployment.
Its PostgreSQL and web images must be the recorded exact images, not rebuilt tags.
Preserve the backup set. Never attach original `deploy_*` volumes to this drill.

In a private owner-only environment file, set `PILOT_RESTORE_WEB_IMAGE`,
`PILOT_RESTORE_POSTGRES_IMAGE`, `DB_NAME`, `DB_USER`, `DB_PASSWORD` and
`TENANCY_DEFAULT_TENANT_SLUG` from approved recovery records. Use recorded immutable
image IDs on the same daemon or retained digests when transferring hosts.

```bash
DRILL_PROJECT=coglatas-pilot-restore-<unique-lowercase-id>
DRILL_ENV=<absolute-private-recovery-env>
case "$DRILL_PROJECT" in coglatas-pilot-restore-*) ;; *) exit 1 ;; esac
restore=(docker compose --env-file "$DRILL_ENV" --project-name "$DRILL_PROJECT" -f "$SOURCE/deploy/sakura/student-council-pilot-restore.compose.yml")
# Stop if this project or any of its three volumes already exists.
test -z "$(docker ps -aq --filter "label=com.docker.compose.project=$DRILL_PROJECT")"
for volume in pgdata uploads protection_keys; do
  if docker volume inspect "${DRILL_PROJECT}_${volume}" >/dev/null 2>&1; then exit 1; fi
done
(cd "$BACKUP" && sha256sum -c SHA256SUMS)
"${restore[@]}" create
"${restore[@]}" up -d --wait postgres
"${restore[@]}" exec -T postgres sh -c 'pg_restore -U "$POSTGRES_USER" -d "$POSTGRES_DB" --no-owner --no-privileges --exit-on-error' < "$BACKUP/database.dump"
for pair in uploads:uploads.tar protection_keys:protection-keys.tar; do
  volume=${pair%%:*}; archive=${pair#*:}
  docker run --rm --network none --read-only -v "${DRILL_PROJECT}_${volume}:/restore" -v "$BACKUP:/backup:ro" "$PG_IMAGE" sh -c 'tar -xpf "/backup/$1" -C /restore' sh "$archive"
done
"${restore[@]}" up -d --no-build --no-deps web
"${restore[@]}" exec -T web curl --fail --silent --show-error http://localhost:8080/health/ready > /dev/null
```

No HTTP port is published. To run browser qualification, arrange a separately
approved isolated HTTPS proxy with explicit trust and host configuration; retain
HTTPS/HSTS/Secure cookies and keep the production edge untouched. Do not disable
security settings to use the public gate on localhost. Run the core gate against
the restored synthetic data, and independently compare migration history, Audit
continuity and the known synthetic File SHA-256 to the backup checkpoint. Record
restore duration/RTO, observed RPO, actual commands, UTC times, errors and receipts.
Readiness alone is not a restore pass. Stop the drill with `restore stop`; any
later deletion must name and verify only this isolated project and its volumes.

## Rollback

Before an approved release, retain the prior web image and exact reviewed commit,
the configuration/edge record and a verified consistent backup. Review migration
compatibility in advance. The canonical deploy entrypoint runs migrations, so a
pull or an old checkout alone is not a database rollback.

If the new schema remains compatible with the prior image, stop admissions and
use a private Compose override containing only `services.web.image` set to the
recorded prior immutable image ID. Append that override to the existing canonical
Compose selection, then run `up -d --no-deps --no-build web`. This deliberately
does not invoke `migrate`, change proxy mode, recreate PostgreSQL, or replace
uploads/keys. Record the exact container/image IDs and run health, login, read,
download, negative and persistence checks. Keep the failed image and evidence.

If schema compatibility is unknown or the rollback checks fail, keep admissions
closed. Restore the consistent database/uploads/keys into isolated new volumes,
prove recovery with this drill, and obtain the Owner's explicit recovery/cutover
decision before replacing any existing deployment. Do not auto-downgrade EF
migrations or overwrite the live database. This runbook is not a rehearsed
rollback until its target-specific receipt records actual execution.
