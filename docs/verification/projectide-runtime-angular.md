# Angular development Runtime recording lookup

When the development host maps the Compiler recording lookup, its runtime
configuration exposes the ProjectIDE link on an authorized Project detail
page. The link opens the existing Workbench at
`/app/projectide/preview?runtimeProjectId={projectId}`. The Runtime mode accepts an opaque
Compiler run ID and uses the same-origin authenticated API. The host owns
Source selection and operation authorization. The Workbench renders only the
receipt projection and clears it before a new lookup or on Project, Tenant,
actor, Workspace, session or authorization invalidation. Synthetic preview
Source and evidence remain in the existing preview mode.

## Verification

The focused Angular tests exercise request paths, display of nullable captures
and results, refused or mismatched responses, selection changes and protected
state invalidation. Run them from the repository root.

```sh
npm --prefix frontend test -- --include=src/app/features/projectide/projectide-runtime-recordings.component.spec.ts --include=src/app/features/projectide/projectide-workbench.component.spec.ts
```

For the browser case, build the Angular bundle and serve it from the actual
development Runtime host with a disposable PostgreSQL fixture. The fixture
must provide a synthetic authenticated actor, an authorized Project and an
issued Compiler receipt. Set `PLAYWRIGHT_BASE_URL`,
`COGLATAS_BROWSER_SMOKE_EMAIL`, `COGLATAS_BROWSER_SMOKE_PASSWORD`,
`COGLATAS_PROJECTIDE_RUNTIME_PROJECT_ID`,
`COGLATAS_PROJECTIDE_RUNTIME_RUN_ID` and
`COGLATAS_PROJECTIDE_RUNTIME_RECEIPT_DIGEST` from that fixture.

```sh
node node_modules/@playwright/test/cli.js test --config=playwright.projectide-runtime-real.config.ts --project=chromium-desktop --project=chromium-mobile --reporter=list
```

The browser checks use the normal cookie sign-in flow and the Project detail
link. They require a noncacheable receipt response and a subsequent empty
noncacheable unavailable response that removes the earlier receipt. The
fixture qualifies the client and development-host composition for its scoped
inputs. Operational availability depends on the host's exact Source and
operation-authorization providers.
