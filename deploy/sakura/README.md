# Sakura VPS deployment

`deploy/sakura/deploy.sh` is the canonical deployment entrypoint for the Sakura VPS.

## Persist the edge topology

The edge mode is deployment state and must live outside the Git worktree. Configure exactly one of these values in the owner-only deployment environment file (default `/srv/coglatas/deploy/.env`):

```dotenv
COGLATAS_EDGE_MODE=caddy
```

or:

```dotenv
COGLATAS_EDGE_MODE=trycloudflare
```

There is intentionally no implicit default. A deployment with no configured mode fails before Docker build/start operations so an unrelated pull cannot silently switch proxy topology.

The current host-side Cloudflare Quick Tunnel topology uses:

```dotenv
COGLATAS_EDGE_MODE=trycloudflare
```

That mode selects the tracked `docker-compose.trycloudflare.yml` overlay, keeps the origin on loopback, disables only forwarded-header count symmetry for the trusted tunnel path, and retains HTTPS-required, HSTS, Secure-cookie, trusted-network, and `ForwardLimit=1` protections.

Do not keep a second operator-side copy of `docker-compose.trycloudflare.yml` under `/srv/coglatas/deploy`; the deployment script intentionally uses the tracked overlay next to the base Compose file.

## Deploy after a pull or merge

Once `COGLATAS_EDGE_MODE` is persisted, normal deployments do not need a topology argument:

```bash
cd /srv/coglatas/app
git pull --ff-only
bash deploy/sakura/deploy.sh
```

A positional `caddy` or `trycloudflare` argument is an explicit one-run override and should be used only when intentionally changing or testing topology.

For non-mutating contract validation:

```bash
COGLATAS_DEPLOY_VALIDATE_ONLY=true bash deploy/sakura/deploy.sh
```

In TryCloudflare mode the deployment is not considered ready until both `/health/ready` and the asymmetric-forwarded-header CSRF/HSTS/Secure-cookie probe succeed.
