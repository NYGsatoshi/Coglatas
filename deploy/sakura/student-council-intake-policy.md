# Student Council file intake policy

Status: **TARGET UNQUALIFIED**. No Sakura host, tenant, stable HTTPS origin,
cookie, live setting, database or storage has been accessed by this change.
The supported bounded state is `INTAKE_CLOSED`, not global read-only Files.
Scanning remains incomplete and arbitrary general-user upload remains NO_GO.

## Existing enforcement and configuration

At source `8a4967a4beba11b6a07cd3c81d5577496c38d9f8`, Web exposes three
client-file-byte intake routes. They are existing Main routes, including the
legacy Artifact version route; this work imports or activates no held Shared,
Filters or Artifacts implementation.

| Route | Existing application gate, before storage/metadata |
| --- | --- |
| `POST /api/files` | `FileService.UploadAsync`, `FeatureKeys.FileSharing` |
| `POST /api/attachments` | The same service/gate, for every owner type |
| `POST /api/artifacts/{artifactId}/versions` | `ArtifactService.UploadVersionAsync`, the same feature key |

The current tenant's `TenantSettings.FeatureFlagsJson` must contain boolean
`"FileSharing":false`. Feature evaluation starts with registered defaults,
replaces them with an active/trial subscription's plan features when present,
then applies tenant overrides. Therefore saved false wins over an enabled plan
or default. Feature evaluation reads untracked persisted snapshots. Missing,
malformed or wrong-typed override JSON does not establish denial: the service
can retain its enabled defaults/plan. Ambiguous case/whitespace-normalized keys
must be resolved before acceptance.

Neither Sakura Compose nor a `Features__FileSharing` environment value changes
this database-backed feature. No new application environment switch is added.
Sakura keeps its existing LocalFileSystem provider and volumes. Unsupported
object-storage aliases break reads as well as writes; zero maximum size and
empty allowlists fail startup. Read-only volume permissions also break storage
readiness probes. None is a supported substitute for the existing intake gate.

Only an authorized Tenant Owner/Admin or PlatformAdmin may change Tenant
settings. A Tenant Member retains no such authority when they are a Workspace
Admin. The setting remains administratively reversible; it is not a host-wide
invariant against privileged re-enablement. Default/student-role parity on the
target remains unqualified.

The Owner's separately approved deployment stage must follow the existing
[pilot runbook](student-council-pilot.md): preserve all unrelated overrides,
PATCH the merged JSON **string** through the existing cookie/CSRF settings API,
retain its saved response, and obtain the effective feature DTO for that same
tenant. Do not send an empty override object or a string-valued `"false"`.
`GET /api/tenant/settings` is not a guaranteed mutation-free probe: its existing
service creates/saves settings if absent. A deployment must drain prior intake
requests before asserting closure; the feature gate is checked at admission,
not atomically again after storage. Never use these instructions to mutate an
unapproved host or broaden the task's authorization.

## Validate local captures without network or mutation

`validate-pilot-intake-policy.py` only reads one operator-supplied private file.
It does not curl an endpoint, execute Compose, read credentials, change flags,
write a patch body, or print full settings/features/cookies. It checks both
saved false and effective absence for the explicitly intended tenant and source,
strict version/DTO shapes, boolean flags, duplicate normalized keys and capture
age. Its output always retains `targetPolicy=UNQUALIFIED` and
`liveStateVerified=false`. It is a local contract guard, not a deployment gate
or proof that the supplied data came from the target.

Construct this version-1 wrapper privately using the complete, direct existing
DTO responses, with no success-envelope wrapper or omitted settings fields:

```text
{
  "version": 1,
  "capturedAtUtc": "<UTC timestamp ending in Z>",
  "source": {"commit": "<deployed 40-character lowercase Git SHA>", "tree": "<its 40-character source tree SHA>"},
  "tenantId": "<intended canonical Tenant UUID>",
  "settingsResponse": <complete TenantSettingsResponse from the approved saved response>,
  "featuresResponse": <complete TenantFeaturesResponse from GET /api/tenant/features>
}
```

The settings DTO has `id`, `tenantId`, `displayName`, `logoFileId`, `themeColor`,
`defaultLocale`, `timeZone`, numeric `invitationMode`, `storageQuotaBytes`,
`userLimit`, `projectLimit`, `fileUploadLimitBytes`, string `featureFlagsJson`,
string `notificationSettingsJson`, `createdAt`, and nullable `updatedAt`.
The features DTO has `tenantId` and string-array `enabledFeatures`.
Protect this capture outside the Git worktree; never commit live settings or
authentication material. Source/time fields are supplied evidence assertions,
not authenticated provenance. After any source, flag, plan or actor change,
capture again and repeat the actual denial/read controls.

```bash
python3 deploy/sakura/validate-pilot-intake-policy.py /private/intake-capture.json \
  --tenant-id "$APPROVED_TENANT_ID" \
  --source-commit "$REVIEWED_DEPLOYED_SHA" --source-tree "$REVIEWED_DEPLOYED_TREE"
```

The default maximum age is 15 minutes, bounded to 1–60 minutes. Future or stale
captures fail. `--now-utc` accepts an explicit validation instant for deterministic
replay; neither clock mode authenticates capture provenance. Unknown input
versions, source mismatches, wrong tenant, malformed/absent/wrong-typed DTO data,
and an enabled effective FileSharing key fail closed without dumping input.
No production deployment entrypoint behavior changes.

## Required execution evidence and exclusions

The focused owning test `FileIntakePolicyHostedHttpTests` uses a disposable
migrated PostgreSQL database, actual controllers and Application/Infrastructure
services, Kestrel, DB-session cookies, CSRF and local synthetic storage. It
prepares valid synthetic bytes through all three real upload paths while
enabled, then uses the actual Tenant Admin settings endpoint to save false over
an enabled plan. Every valid multipart path must then deny without changing
file/attachment/Artifact/version metadata, audit rows or stored bytes. Missing
CSRF denies independently. Tenant Member and Workspace Admin settings PATCH
attempts must not re-enable the flag. Existing authorized list/detail and both
grant/download families, direct attachment download and Artifact version bytes
remain positive controls after closure. No feature fake bypass is used.

```bash
dotnet test tests/Coglatas.Tests/Coglatas.Tests.csproj \
  --filter FullyQualifiedName~FileIntakePolicyHostedHttpTests
```

Provider configuration is mandatory; an unconfigured/skipped run is not proof.
Record the exact source/head/tree, native Web assembly hash/MVID, PostgreSQL
18.6 version, test counts/zero skips and logs. This synthetic Development HTTP
host uses development tenant resolution and HTTP cookies; it is not Sakura's
Production TLS/proxy, full Program startup, or public qualification. Target
qualification still requires the approved deployment identity, effective saved
settings and all three valid multipart denials plus authorized byte readback.

The flag does **not** close deletion, selection deletion, sharing changes,
Artifact metadata/version deletion, or canonical Message/Task attachment
associations. Those retain their existing authorization. Generated Audit export
ZIPs and readiness probes can still write storage; Task runtime/refinement
records can still mutate their existing database state. Test/demo file seeds
are separately Test-only, not general-user upload routes. Thus no global
storage-write or read-only invariant is claimed.

There is also an existing side effect: `AnnouncementAttachmentService.ResolveAsync`
requires FileSharing, so new/resolved Announcement attachment metadata and
publication resolution become unavailable while false. Direct authorized Files
reads remain separately available. Preserve this limitation in pilot admission
and communications; changing its permission contract is outside this task.
