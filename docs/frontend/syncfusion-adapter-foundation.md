# Syncfusion adapter foundation (v0.4 PR02)

Status: `PENDING_VENDOR_CONFIRMATION` for package adoption; license activation path implemented

This foundation keeps complex UI contracts owned by Coglatas. Feature code uses
the contracts in `frontend/src/app/shared/ui/contracts/`; only future code in
`frontend/src/app/shared/ui/adapters/syncfusion/` or
`frontend/src/app/shared/vendor/syncfusion/` may import Syncfusion packages.

## Current safe behavior

- Every complex adapter resolves to its Coglatas fallback shell.
- `frontend.syncfusionGrid` and `frontend.syncfusionUploader` default to
  `false`; there is deliberately no duplicate `frontend.syncfusionAdapters`
  runtime key because the canonical amendment names the two per-adapter keys.
- Runtime configuration never contains a Syncfusion license key. Package
  rollout remains independently controlled by the existing feature flags.
- No production, school-shared, or public deployment may enable a Syncfusion
  implementation while this status remains pending.

## License/configuration contract

The only secret name is `SYNCFUSION_LICENSE`. It is supplied through the local
developer secret store, CI secret store, or a Git-ignored deployment `.env`;
the repository, `.env.example`, fixtures, snapshots, logs, evidence, and PR
body must not contain a real value.

Registration uses the Syncfusion License CLI before a licensed build:

```text
npm run syncfusion:activate
npm run build:licensed
```

The activation script validates that `SYNCFUSION_LICENSE` is non-empty before
calling `npx syncfusion-license activate`. The browser bootstrap, Angular
environment files, dependency injection, and JSON configuration never receive
the key. `frontend.syncfusionGrid` and `frontend.syncfusionUploader` remain
rollout controls only; they do not suppress activation failure for a licensed
build.

See [the license runbook](../SYNCFUSION_LICENSE_RUNBOOK.md) for local, CI,
Docker, and deployment handling.

Before enabling either rollout key, record the confirmed Community/education
or commercial license basis for the actual organization and intended
deployment. This document does not assert eligibility.

## Deferred vendor implementation

`BLOCKED: SYNCFUSION_LICENSE_BASIS_CONFIRMATION`

The actual Syncfusion package dependencies, registrar, lazy vendor factories,
and Syncfusion-backed implementations for Data Grid, Dialog, File Uploader,
Date/Time Picker, Kanban, Gantt, Tree Grid, and Scheduler remain blocked.
Their public Coglatas contracts and fallback shells are present now, so feature
screens remain unchanged and AG Grid remains active.
