# Coglatas GCP Compute Engine deployment

This is a development deployment path for one Compute Engine VM running Docker Compose. It keeps PostgreSQL inside Compose. Do not put service account JSON keys or real secrets in this repository.

For production, put the app behind Nginx or another reverse proxy with HTTPS, or use Cloudflare/Google load balancing, and revisit database backup, monitoring, firewall scope, secret storage, and OS patching.

## What this repo runs

- Compose services: `app`, `migrate`, `postgres`
- App port inside Docker: `8080`
- External development port: `${COGLATAS_PORT:-8080}`
- Database host from the app container: `db` (provided as an alias for the `postgres` service)
- Database name/user defaults: `coglatas` / `coglatas`
- Database password: generated into `/opt/coglatas/.env` by `deploy-app.sh`
- EF Core migrations: applied by the `migrate` service before `app` starts
- PostgreSQL data: preserved in the `coglatas_pgdata` Docker volume

## Windows + VSCode quick start

Install the Google Cloud CLI, then open this repository in VSCode and use a PowerShell terminal. Run `gcloud init` first, select or log in with the right account, and set the project that should own the VM.

```powershell
gcloud init
gcloud auth login
gcloud config set project YOUR_GCP_PROJECT_ID

.\deploy\gcp\create-vm.ps1 -ProjectId YOUR_GCP_PROJECT_ID -Zone us-central1-a -VmName coglatas-dev
gcloud compute ssh coglatas-dev --zone us-central1-a
```

On the VM:

```bash
bash ~/coglatas-gcp/gcp/bootstrap-vm.sh
exit
```

SSH again so the Docker group membership is active:

```powershell
gcloud compute ssh coglatas-dev --zone us-central1-a
```

Then deploy:

```bash
bash ~/coglatas-gcp/gcp/deploy-app.sh
```

Open the URL printed by the script, usually:

```text
http://EXTERNAL_IP:8080
```

`create-vm.ps1` opens `tcp:80` and `tcp:8080`. The app itself listens on `8080`; port `80` is reserved for a later reverse proxy.

The script copies the helper scripts to `~/coglatas-gcp/gcp` on the VM. It does not copy your local `.env`, and it does not copy any GCP service account JSON key.

## Fully automated option

From Windows PowerShell:

```powershell
.\deploy\gcp\create-vm.ps1 -ProjectId YOUR_GCP_PROJECT_ID -Zone us-central1-a -VmName coglatas-dev -RunBootstrap -RunDeploy
```

If Docker group membership was just added, log out and back in before running manual Docker commands without `sudo`.

## Private repository fallback

If the VM cannot clone the GitHub repository, deploy the current local working tree as an archive. Replace `REMOTE_USER` with the username shown by `gcloud compute ssh coglatas-dev --zone us-central1-a --command "whoami"`.

```powershell
tar -cf coglatas-deploy.tar --exclude=.git --exclude=.env --exclude=coglatas-deploy.tar --exclude=node_modules --exclude=bin --exclude=obj --exclude=.tmp --exclude=TestResults --exclude=test-results --exclude=playwright-report --exclude=.playwright --exclude=data --exclude=src/Coglatas.Web/data .
gcloud compute scp .\coglatas-deploy.tar coglatas-dev:/tmp/coglatas-deploy.tar --zone us-central1-a
gcloud compute scp .\deploy\gcp\deploy-uploaded-archive.sh coglatas-dev:/home/REMOTE_USER/coglatas-gcp/gcp/deploy-uploaded-archive.sh --zone us-central1-a
gcloud compute ssh coglatas-dev --zone us-central1-a --command "chmod +x ~/coglatas-gcp/gcp/deploy-uploaded-archive.sh && bash ~/coglatas-gcp/gcp/deploy-uploaded-archive.sh"
```

This path does not copy `.env`, `.git`, local secrets, build outputs, app data, or service account keys.

## Updating without deleting the DB

On the VM:

```bash
bash ~/coglatas-gcp/gcp/update-app.sh
```

This runs `git pull`, validates Compose, rebuilds the app, starts PostgreSQL, runs `dotnet ef database update` through the `migrate` service, starts the app, and preserves Docker volumes.

## Logs and status

On the VM:

```bash
bash ~/coglatas-gcp/gcp/check-status.sh
```

Useful direct commands:

```bash
cd /opt/coglatas
docker compose ps
docker compose logs --tail=100 app
docker compose logs --tail=100 postgres
curl -i http://localhost:8080/health/ready
```

## Resetting development containers

Recreate containers but keep PostgreSQL data:

```bash
bash ~/coglatas-gcp/gcp/reset-app.sh
# type: restart
```

Delete the database and uploaded-file volumes:

```bash
bash ~/coglatas-gcp/gcp/reset-app.sh
# type: volumes
# then type: DELETE-DATABASE
```

The volume reset is destructive and intended only for development.

## Environment variables

`deploy-app.sh` creates `/opt/coglatas/.env` if it does not exist. Existing `.env` files are not overwritten.

Important values:

```text
DB_HOST=db
DB_NAME=coglatas
DB_USER=coglatas
DB_PASSWORD=<generated>
COGLATAS_PORT=8080
ASPNETCORE_ENVIRONMENT=Development
TENANCY_APP_MODE=OnPremSingleTenant
TENANCY_DEFAULT_TENANT_SLUG=default
TENANCY_RESOLUTION_STRATEGY=ConfigDefault
SECURITY_REQUIRE_HTTPS=false
SECURITY_ENABLE_HSTS=false
LOCAL_ADMIN_EMAIL=admin@example.com
LOCAL_ADMIN_PASSWORD=<generated>
```

Change `DB_PASSWORD` only when creating a fresh database volume, or update the PostgreSQL user password inside the existing database first.

The repository `.env.example` contains only dummy local-development values. On GCP, prefer letting `deploy-app.sh` generate `.env` on the VM.

## Common errors

`DB_PASSWORD` is not set:
Run `bash ~/coglatas-gcp/gcp/deploy-app.sh`; it creates `.env`. If you write `.env` manually, include `DB_PASSWORD`.

PostgreSQL connection fails:
In Docker, the DB host must be `db`, not `localhost`. Check `ConnectionStrings__DefaultConnection` in `docker-compose.yml` and confirm `DB_USER` matches the existing database owner/user.

App waits forever or exits during startup:
Check `docker compose logs migrate` first. The `app` service waits for PostgreSQL health and completed EF Core migrations.

Port is not reachable from the internet:
Check the VM external IP and the firewall rule created by `create-vm.ps1`. The app is on `http://EXTERNAL_IP:8080`, not port 80 unless you add a reverse proxy.

GCP firewall is not open:
Confirm the VM has the `coglatas-web` network tag, then run `gcloud compute firewall-rules list --filter=coglatas` from Windows PowerShell.

App is on 8080, not 80:
Use `http://EXTERNAL_IP:8080` for this development deployment. Port 80 is opened only so a future Nginx/HTTPS setup can be added without recreating the VM.

Docker permission denied:
Run `bash ~/coglatas-gcp/gcp/bootstrap-vm.sh`, then exit SSH and reconnect. Until then, use `sudo docker compose ...`.

`docker compose` is missing:
Rerun `bootstrap-vm.sh` and confirm `docker compose version` works.

Migration not applied:
Run `cd /opt/coglatas && docker compose logs migrate`. `update-app.sh` and `deploy-app.sh` start the `migrate` service automatically.

HTTPS redirect prevents access:
This development Compose sets `SECURITY_REQUIRE_HTTPS=false` and `SECURITY_ENABLE_HSTS=false`. For production, re-enable HTTPS behind a proxy or load balancer.

VM restarted and containers did not come back:
Run `cd /opt/coglatas && docker compose ps`. The `app` and `postgres` services use `restart: unless-stopped`; if they are stopped, run `docker compose up -d`.

Weak or placeholder secrets:
Do not use `.env.example` as production secrets. `deploy-app.sh` generates passwords and saves them only on the VM.
