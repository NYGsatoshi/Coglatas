# Syncfusion license activation runbook

## Adopted method

Licensed frontend artifacts use build-time activation only:

```text
SYNCFUSION_LICENSE -> npm run syncfusion:activate -> Syncfusion License CLI -> npm run build:licensed
```

`syncfusion:activate` first checks that `SYNCFUSION_LICENSE` contains a
non-whitespace value, then runs the installed `syncfusion-license activate`
CLI. A failed or
missing activation stops the licensed build. Angular startup, `environment.*`,
dependency injection, browser globals, JSON files, and runtime containers do
not receive the license value.

No Syncfusion CLI dependency is added independently. The pinned EJ2 package
family supplies the CLI through `@syncfusion/ej2-base`; `npm ci` must create
`node_modules/.bin/syncfusion-license`, and release builds fail closed when it
is absent. When updating the pinned Syncfusion package family, verify CLI and
license compatibility and repeat the licensed CI/Docker evidence before
release.

The current fallback-only `npm run build` remains intentionally unlicensed so
existing Angular tests and local fallback development do not require a vendor
secret. Any release build that includes Syncfusion components must use
`build:licensed`, `build:hosted:licensed`, the opt-in `dotnet publish`
frontend target, or the Dockerfile path below.

## Local development

Keep the vendor-issued value in an OS secret store, an OS environment variable,
or the Git-ignored root `.env` / `.env.local`. Do not place it in
`frontend/`, Angular environments, or a file tracked by Git.

For PowerShell, set `SYNCFUSION_LICENSE` for the current process using the
vendor-issued value, then run:

```powershell
Set-Location frontend
npm run syncfusion:activate
npm run build
```

For POSIX shells, export `SYNCFUSION_LICENSE` for the current shell, then run:

```bash
cd frontend
npm run syncfusion:activate
npm run build
```

When the value is stored in the root Git-ignored `.env`, load it only in the
shell that builds the image or frontend:

```bash
set -a
. ./.env
set +a
npm --prefix frontend run build:licensed
```

Restrict deployment `.env` files to their owner, for example `chmod 600
/srv/coglatas/.env`. Never inspect the value with `echo`, shell tracing, a
debug log, or a generated artifact. A safe presence check is:

```bash
if [ -n "${SYNCFUSION_LICENSE:-}" ]; then
  echo "SYNCFUSION_LICENSE is configured."
else
  echo "SYNCFUSION_LICENSE is not configured."
fi
```

## CI

Create a GitHub repository secret or environment secret named exactly
`SYNCFUSION_LICENSE`. The CI workflow validates its presence, activates it in
the `frontend` working directory, and runs `npm run build:licensed`. The
Docker image job writes the secret to an owner-only temporary file, passes it
with `docker build --secret`, and removes the file through a shell `trap` on
both success and failure.

Do not enable command tracing, print environment variables, cache the
temporary file, upload it as an artifact, or include the value in PR text.

## Docker and Compose

The root Dockerfile declares Dockerfile syntax 1.7 and uses a required
`syncfusion_license` BuildKit secret in the frontend build stage. It removes
CR/LF line endings while reading the mounted file, reads the result only into
the command environment that invokes `npm run build:licensed`, unsets it after
the Angular build command, rejects a build output containing the raw secret
without printing it, and never declares it with `ARG` or `ENV`.
The final runtime image receives only the published ASP.NET Core output; it
does not receive `/run/secrets`, `.env`, frontend `node_modules`, or a
`SYNCFUSION_LICENSE` runtime variable.

The production, local, on-prem, and real-backend-smoke Compose files use:

```yaml
build:
  secrets:
    - syncfusion_license

secrets:
  syncfusion_license:
    environment: SYNCFUSION_LICENSE
```

Compose reads the value from the invoking environment (including its
Git-ignored `.env` file) and exposes it to BuildKit only. It is not passed to
the application service environment.

The Sakura VPS profile at `deploy/sakura/docker-compose.yml` instead uses a
file-backed Compose secret. Its default source is the owner-only file
`/srv/coglatas/app/secrets/syncfusion-license.txt`; `SYNCFUSION_LICENSE_FILE`
may select another protected path. The tracked `.gitignore` and
`.dockerignore` both exclude the secret paths, so the file is supplied through
BuildKit's secret channel and never through the build context.

## Deployment

### Sakura VPS

The Sakura VPS uses the tracked `deploy/sakura/docker-compose.yml` and
`deploy/sakura/deploy.sh`. The script requires a clean Git worktree and
owner-only deployment environment and license files, validates Compose, builds
the `web` image with the file-backed BuildKit secret, runs migrations, starts
the existing Compose project, and waits for readiness. If the primary checkout
has local changes or an unresolved merge, create a separate clean Git worktree
and set `COGLATAS_SOURCE_DIR` to it; never reset or stash operator changes just
to deploy.

The license file may contain a final LF or CRLF. The Dockerfile strips only
those line-ending bytes in the secret mount and does not rewrite the source
file.

### GCP scripts

The checked-in GCP deployment scripts use `${APP_DIR}/.env`, whose default is
`/opt/coglatas/.env`; operators may set `APP_DIR=/srv/coglatas` to use
`/srv/coglatas/.env`. The scripts preserve an existing `.env`, load it only to
drive the Compose build, validate `SYNCFUSION_LICENSE` without displaying it,
build the image, then start the already-built application image. The runtime
application container does not receive the license variable.

Before a licensed deployment, provision the Git-ignored `.env` with the
vendor-issued value and owner-only permissions. The scripts deliberately stop
if the value is missing instead of deploying an unlicensed frontend.

## Rotation and package updates

1. Obtain and authorize a replacement key through the approved vendor and
   organization process; do not add it to this repository.
2. Replace the value in the CI secret and each protected deployment `.env`.
3. Run a licensed build and verify only the success/failure status.
4. Rebuild and redeploy affected images; invalidate old credentials according
   to the vendor process.
5. When updating Syncfusion packages, confirm with Syncfusion whether the
   existing entitlement/key remains valid, then repeat the licensed build and
   record sanitized evidence.

## Troubleshooting and prohibitions

- `SYNCFUSION_LICENSE is not configured.` means the variable is unset, empty,
  or whitespace-only. Set it in the approved secret location and retry; do not
  print it to diagnose the problem.
- Activation failure is a build failure. Do not fall back to an unlicensed
  release build or use a feature flag to mask it.
- Never commit `.env`, `.env.local`, a license text file, a key-bearing npm
  configuration, test fixture, snapshot, log, artifact, or PR description.
- Never add the value to Docker `ARG`/`ENV`, Compose `environment`, Angular
  source, browser configuration, or application runtime configuration.
