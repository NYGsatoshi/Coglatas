# P4 isolated environment configuration decision packet

Decision ID: `ENV-P4-ISOLATION-20261018`.
Status: **SPEC_REVIEW_PACKET_READY / OWNER_DECISION_REQUIRED** for the missing target configuration. Public HTTPS: **BLOCKED_ENV_NOT_CONFIGURED**.

OPS-01 A and OPS-02 A remain settled. This packet does not reopen those choices. It identifies the concrete configuration still missing before the approved isolated remote qualification can execute. Documentation merge is not an infrastructure approval receipt.

## Purpose and current implementation

Qualify the reviewed Product commit with the existing real-backend and Functional harnesses, synthetic data, and a separately configured protected public HTTPS route. The source-bound browser evidence and exact secret-name inventory are in [the integration record](web-integration-qa-20261018.md).

No P4 SSH alias, approved host identity, fixed HTTPS origin or synthetic remote fixture has been established. The Docker CLI is installed locally, but its Linux daemon is unavailable. No container, database, migration, seed, reset or remote deployment was started. The existing Sakura app and deployment environment paths must remain untouched until complete storage/database/volume isolation is confirmed; their existence does not establish isolation.

## Concrete configuration required

| Required input | Current value / verification obligation |
| --- | --- |
| Exact candidate | Select a reviewed full Product SHA and image digest; bind the deployed app to it independently of the workflow checkout. |
| SSH target | UNCONFIGURED. Owner supplies an approved alias and verifies host identity through the protected operator channel; never infer a host from old documents. |
| Public HTTPS origin | UNCONFIGURED. Owner supplies the isolated public root origin through `COGLATAS_PUBLIC_SMOKE_URL`; no guessed URL or production routing. |
| Synthetic fixture | UNCONFIGURED. Dedicated `@example.test` actors and positive/inaccessible/revoked fixture identities; no school, student or real user data. |
| Compose projects | Unique per candidate/run/attempt; separate smoke and Functional projects with verified project labels, isolated networks and project-prefixed volumes. |
| Database/storage/keys | Prove no production database connection, bind mount, upload store, Docker volume or data-protection key directory is shared. Record redacted identities and mount/label evidence. |
| App environment | `ASPNETCORE_ENVIRONMENT=Test`; synthetic smoke seed only in that environment. |
| Protected GitHub environment | Owner configures `public-https-gate`, its required protections and the existing secret names. Secret values, cookies and connection strings never enter Git, PRs or evidence. |
| Cleanup | Scope every cleanup/reset to the verified synthetic project. Existing demo reset has a fixed separate project; do not retarget it to Sakura or staging data. |

## Options

| Option | Effect |
| --- | --- |
| A: configure an exact isolated remote target | Owner supplies the protected target/fixture settings and artifact-bound isolation evidence; execute only after those concrete inputs are verified. No uncreated infrastructure configuration is implicitly approved. |
| B: qualify on an existing Docker-capable test executor first | Reuse the existing hosted isolated Compose/Functional lanes and their synthetic fixtures; the remote public route remains blocked. This is already within the approved test scope. |
| C: defer all remote qualification | Retain local/static/unit evidence and hosted CI without assigning a remote destination. Public HTTPS remains blocked until Owner configuration arrives. |

Recommendation: continue B now while preparing A's missing protected inputs. Do not wait for A to complete unrelated Files, Thread UI, CI or private contract review work. Choosing A without actual target settings and verified isolation is not an executable approval.

## Impact, validation and rollback

| Review dimension | Exact boundary |
| --- | --- |
| Files/API/Migration diff | This packet changes documentation only. Existing harness/configuration paths are listed in the integration record; no API, schema, seed tool or deployment script is changed. |
| Security impact | Test-only synthetic state; preserve cookie/CSRF, HTTPS/HSTS, protected secrets, and bounded artifact redaction. Never disable checks to debug a remote failure. |
| Tenant/authorization impact | Preserve current server reauthorization. Positive and inaccessible-scope fixture cases must be checked against the exact deployed candidate; mocked UI results are insufficient. |
| RLS / SEC-ARCH | No policy/role/grant/mapping change or hold release. PRE-AVALONIA SEC-ARCH remains BLOCKED. |
| Migration / compatibility | Only existing reviewed migrations may run on a newly verified synthetic test database. New Draft/Files API/DB/RLS artifacts retain separate explicit approval gates. No real-data migration. |
| Test plan | Validate Compose quietly; inspect project/mount/network isolation before any seed/migration; execute existing real-backend and Functional commands; bind run/attempt/SHA; then run the protected HTTPS workflow only on the verified target. |
| Rollback | No infrastructure change has occurred. For a later approved test run, stop only its isolated project and preserve failure evidence; remove its verified synthetic volumes only under the agreed cleanup scope. No production rollback or traffic switch. |

Approval scope: supply and confirm the named isolated target, fixture and isolation evidence. Any later concrete infrastructure diff is reviewed separately before application. This packet authorizes no provisioning, production deployment, real-user enablement, new migration/RLS, final hosting choice or production GO/NO-GO.

Contract binding: [the review manifest](assets/p4-isolated-environment-review-20261018.json) records this packet's SHA-256, exact subject path and null approval fields. An approval receipt must identify the reviewed revision/digest and supplied protected target evidence. Missing values remain missing; elapsed time and documentation merge are not approval.

Work continuing without this decision: normal Product feature PR/CI/merge qualification, private Draft/Files contract review, unit/static/client browser verification and existing hosted isolated integration lanes. Runtime remote configuration remains HOLD until its concrete prerequisites are supplied.
