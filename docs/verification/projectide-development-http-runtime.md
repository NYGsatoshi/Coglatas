# Development ProjectIDE Runtime HTTP

The ASP.NET Core host can expose a read-only Compiler recording receipt in an
explicit Development composition. Registration requires
`ProjectIde:DevelopmentRuntime:Enabled` and host-supplied implementations of
`IProjectIdeRecordingSourceResolver` and `IProjectIdeOperationAuthorization`.
The normal composition supplies neither adapter. With either dependency absent,
the route and its `projectide.developmentRuntime` UI flag are absent. Production,
Staging and Test keep the route and registration absent.

Use a fresh isolated development database. Apply the regular application
migrations, then call `CompilerRecordingDevelopmentSchema.ProvisionAsync`
explicitly once before starting the host. The host does not provision the
development schema at startup. The recording producer and resolver must use the
exact same authorized Source and its digest. The operation provider separately
checks the current `coglatas.projectide.compiler-recording.read` permission over
that Source and its protected premises.

Provision a Tenant with an RFC UUID version 4 for this fixture. ProjectIDE
Source identity validation rejects the fixed default Tenant ID installed by
the ordinary development seed.

`GET /api/projectide/runtime/projects/{projectId}/compiler-recordings/{runId}`
uses the existing cookie authentication and request Tenant. The route IDs are
selectors. The resolver supplies the exact Source identity, which the
PostgreSQL receipt store checks again under current persisted viewer authority.
The endpoint returns the same empty, non-cacheable 404 for missing or denied
metadata. Cookie authentication uses the standard API challenge.

On success, the JSON response contains `runId`, `receiptDigest`, `hostStatus`,
`captureMode`, nullable `captureState` and nullable `result`. Result fields are
`resultDigest`, `execution`, `coverage`, `verdict` and `evidenceComplete`.
Status axes use their PascalCase contract names. The response carries no
Source/context identifiers, capture bodies, diagnostic content or actor details.
This history is a recording receipt. It carries no currentness, evidence
eligibility, Merge or Release authority.

The Kestrel/PostgreSQL HTTP test supplies exact synthetic Source resolution
and operation decisions only inside the disposable fixture. It exercises the
normal cookie authentication event, Tenant resolution, persisted viewer and
Compiler receipt store. An operational development journey requires the approved
Source provider, operation authorization and a server-owned recording producer.
