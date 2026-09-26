# Reproducible synthetic demo dataset

Issue #483 provides a small, Test-only dataset for repeating a product demo
without using production, student, teacher, or other personal data. It is not
a production seed, tenant migration, backup, or restore tool.

## Safety boundary

The fixture runs only when both conditions are true:

- the host environment is exactly `Test`; and
- `COGLATAS_DEMO_DATASET_ENABLED=true` (or `DemoDataset:Enabled=true`) is supplied.

The supported script also requires `COGLATAS_DEMO_MODE=1`, a locally supplied
`COGLATAS_DEMO_PASSWORD`, a synthetic `@example.test` demo email, and the fixed
Compose project name `coglatas-issue483-demo`. It binds the application to
`127.0.0.1` only. A request to enable the dataset in Development, Staging, or
Production fails closed: no dataset is seeded.

The password is never printed or committed. All fixture identities are
synthetic and use the locally supplied password:

| Identity | Role in the demo |
| --- | --- |
| `demo-operator@example.test` (or `COGLATAS_DEMO_EMAIL`) | Project owner and authorized Task-execution user |
| `demo-observer@example.test` | Test user with Workspace visibility but no Project membership; used for the protected-data denial check |

Every resource created by this fixture carries the `issue-483-demo` namespace
through its slug, title, storage provider/key, description, or audit
correlation ID. Existing resources with the reserved Workspace or Project
slug but a different ownership marker cause provisioning to stop rather than
be overwritten.

## Fixture contents

The fixture provisions the following synthetic, presentation-safe state:

- one Tenant-scoped Workspace and two minimal test users;
- an active members-only Project with a Project-file-only source policy;
- In progress, Review, and Completed Tasks;
- an execution Task with a Task Brief, current Research Plan revision, clean
  text attachment, and no outbound-Web source policy;
- a direct Conversation and Message;
- Published, Draft, and future Scheduled Announcement states;
- an Audit event whose claim/evidence metadata explicitly identifies the
  synthetic dataset.

The provision command creates one idempotent Task execution run from the
authorized synthetic attachment, then verifies that its durable result is
readable. This makes the stored result a reproducible demonstration artifact,
not a claim about a live provider or Web retrieval.

## Provision or reset

From the repository root in PowerShell, set the two explicit local opt-ins.
Use a throwaway local password; do not put it in a shell profile, `.env`, or a
committed file.

```powershell
$env:COGLATAS_DEMO_MODE = '1'
$env:COGLATAS_DEMO_PASSWORD = '<local throwaway password>'
.\scripts\demo\reset.ps1 -Mode Provision
```

`Provision` keeps the isolated volume and reconciles only the reserved demo
records. It uses a fixed idempotency key for the Golden Path check, so reruns
do not create additional demo execution results.

To discard the isolated database and storage volumes and rebuild the exact
fixture from a clean migrated database:

```powershell
$env:COGLATAS_DEMO_MODE = '1'
$env:COGLATAS_DEMO_PASSWORD = '<local throwaway password>'
.\scripts\demo\reset.ps1 -Mode Reset -KeepRunning
```

`Reset` invokes `docker compose down --volumes` only with the fixed
`coglatas-issue483-demo` project name, then starts the same Test-only Compose
stack, applies migrations, provisions the synthetic fixture, and verifies it.
It never targets an arbitrary database, tenant, deployment, or Compose
project. Omit `-KeepRunning` to stop the stack after verification while
retaining the newly provisioned isolated volume.

When it is kept running, open <http://127.0.0.1:8088/app/login> (or the value
of `COGLATAS_DEMO_PORT`) and sign in with the locally supplied credentials.

## Automated verification

The command fails on any partial success. Before it prints a safe summary, it
checks:

1. Compose configuration and application readiness;
2. the namespaced Workspace, Project, task-state, Research Plan, attachment,
   Conversation/Message, Announcement, and Audit invariants;
3. login for the authorized test account;
4. the no-Web, Project-file-only Task execution policy, execution request, and
   durable result read; and
5. a `403` or redacted `404` when the observer asks for the protected Task
   execution scope.

If this command cannot run because Docker, build secrets, or local runtime
dependencies are unavailable, it reports the failure and leaves no claim that
the fixture was successfully provisioned.
