# Coglatas Frontend

This directory is the Angular source of truth for the MVP-A P0 frontend.

The workspace was generated with Angular CLI 22.0.4, standalone components,
routing, and SCSS. The legacy static SPA under `src/Coglatas.Web/wwwroot` is
not used for Angular development.

## Development

Start the ASP.NET Core backend from the repository root:

```bash
dotnet run --project src/Coglatas.Web
```

The default Development HTTP launch profile listens on
`http://localhost:5098`. The HTTPS profile also exposes
`https://localhost:7069`, but the Angular development proxy targets the HTTP
profile so local cookie authentication and CSRF can run through the same
browser origin without adding permissive CORS rules.

Start the Angular development server from this directory:

```bash
npm ci
npm run start
```

Open `http://localhost:4200/`.

`npm run start` runs `ng serve --proxy-config proxy.conf.json`. Requests to
`/api/*`, `/health`, and `/healthz` are proxied to
`http://localhost:5098`. Browser calls should use relative URLs such as
`/api/auth/status`; cookies and CSRF tokens stay on the Angular dev-server
origin while the dev server forwards requests to ASP.NET Core.

Build the production Angular output from this directory:

```bash
npm run build
npm run test -- --watch=false
```

For an artifact that includes approved Syncfusion components, first configure
`SYNCFUSION_LICENSE` outside source control, then use `npm run build:licensed`.
This invokes the Syncfusion License CLI before the normal production build and
fails closed when the variable is missing. Do not add a key to Angular
environments, browser bootstrap code, or JSON configuration. The repository
runbook describes the supported local, CI, and Docker flows:
[`docs/SYNCFUSION_LICENSE_RUNBOOK.md`](../docs/SYNCFUSION_LICENSE_RUNBOOK.md).

`npm run build` writes browser artifacts to `frontend/dist/coglatas-web`.
The build includes `angular-app.marker`, which ASP.NET Core requires before it
will serve `index.html` as the Angular fallback.

To run ASP.NET Core with the built Angular app from the repository root:

```bash
cd frontend
npm ci
npm run build:hosted
cd ..
dotnet run --project src/Coglatas.Web
```

`npm run build:hosted` copies the Angular artifacts into
`src/Coglatas.Web/wwwroot`, replacing the legacy static SPA entrypoint. Angular
source remains under `frontend/`.

Feature pages are intentionally left for follow-up frontend issues.

## Dependency governance

- Angular app dependencies live in `frontend/package.json`.
- The root `package.json` is repo-level only for Playwright, E2E, and migration scripts.
- `ag-grid-enterprise` is prohibited, and Enterprise modules must not be imported.
- AG Grid Community usage must be wrapped by `AppDataGridComponent`.
- UI-only tasks must not modify backend behavior.
- New dependency additions require a compatibility check and a passing CI run.
