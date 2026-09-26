> [!WARNING]
> **Deprecated for this repository's public configuration.** Active GitHub
> Actions workflows use GitHub-hosted runners. Do not attach a persistent
> self-hosted runner to the public repository. This document is retained only
> as historical private-operation context.

# Self-hosted GitHub Actions runner pool

## Purpose

One self-hosted runner process can execute only one GitHub Actions job at a time.
The repository currently has four independent heavy jobs that can otherwise wait
in a queue:

- `CI / build-test`
- `CI / security-scan`
- `Code Quality / Qodana Community / .NET`
- `Code Quality / Angular / TypeScript / JavaScript / HTML / SCSS / CSS`

The repository-authored workflows already use `runs-on: self-hosted`. GitHub
automatically distributes queued jobs across every online repository runner that
matches the default `self-hosted`, `Linux`, and architecture labels. No workflow
matrix or artificial job dependency is required.

This repository therefore provides an installer that adds three independent
runner services beside the existing `coglatasci` runner, giving four concurrent
execution slots in total.

## Strict self-hosted NuGet dependency submission

GitHub's Automatic dependency submission is a GitHub-managed dynamic workflow.
Even when it is configured for labeled self-hosted runners, GitHub documents that
the automatic job can use GitHub Actions infrastructure when the eligible
self-hosted runners are unavailable. That behavior does not meet this repository's
strict requirement that NuGet dependency submission never consume GitHub-hosted
runner capacity.

For that reason, this repository uses the explicit workflow:

```text
.github/workflows/nuget-dependency-submission-self-hosted.yml
```

The workflow has `runs-on: self-hosted`, uses GitHub's documented Component
Detection dependency-submission action for NuGet, and only runs from repository
workflow configuration. A job whose `runs-on` expression is `self-hosted` queues
until an eligible self-hosted runner is available; it has no GitHub-hosted runner
fallback.

### Required repository setting

After the explicit workflow is merged, open:

**Settings > Security > Advanced Security > Dependency graph > Automatic dependency submission**

and set Automatic dependency submission to **Disabled**.

This setting is required. If GitHub Automatic dependency submission remains
enabled, GitHub may continue creating `Dynamic Submit / NuGet` jobs independently
of the repository-authored self-hosted workflow, causing duplicate dependency
submissions and possible GitHub-hosted usage.

Do not remove the NuGet entry from `.github/dependabot.yml` merely to suppress the
dynamic job. Dependabot NuGet update configuration and dependency-graph submission
are separate concerns. Keep Dependabot updates configured and disable only the
GitHub-managed Automatic dependency submission feature.

### Verification

After merge and after disabling Automatic dependency submission:

1. Open **Actions > NuGet Dependency Submission (Self-Hosted)**.
2. Run the workflow with `workflow_dispatch`, or merge a NuGet manifest change to
   `main`.
3. Confirm the `Prove runner routing` step prints an `coglatasci*` runner name.
4. Confirm new `Dynamic Submit / NuGet` runs are no longer created.

If the self-hosted pool is busy or offline, the explicit job must remain queued.
It must not start on a GitHub-hosted image.

## Isolation model

Each additional runner uses:

- a distinct Linux account;
- a distinct home directory;
- a distinct runner installation directory;
- a distinct `_work` directory;
- an independent systemd service;
- membership in the host `docker` group.

Separate Linux accounts are intentional. The CI workflows use paths under
`$HOME` for the .NET SDK, while Docker-based inspections can write files as root.
Using independent accounts prevents concurrent jobs from racing over the same
`.NET`, NuGet, npm, temporary, or checkout directories.

## Install three additional runners

Generate a fresh repository registration token immediately before installation:

1. Open the repository on GitHub.
2. Open **Settings**.
3. Open **Actions > Runners**.
4. Select **New self-hosted runner**.
5. Copy the short-lived registration token from the generated `config.sh`
   command.

On the runner server, update the repository checkout containing this script and
run:

```bash
cd /home/adminhome/actions-runner/_work/Coglatas/Coglatas

git fetch origin
git checkout ci/self-hosted-runner-pool
git pull --ff-only

sudo RUNNER_TOKEN='REPLACE_WITH_FRESH_TOKEN' \
  ./scripts/ci/install-self-hosted-runner-pool.sh \
  --url https://github.com/NYGsatoshi/Coglatas
```

The default installation adds:

| Runner | Linux user | Installation directory |
|---|---|---|
| `coglatasci-2` | `aiprunner2` | `/opt/coglatas-actions-runners/coglatasci-2` |
| `coglatasci-3` | `aiprunner3` | `/opt/coglatas-actions-runners/coglatasci-3` |
| `coglatasci-4` | `aiprunner4` | `/opt/coglatas-actions-runners/coglatasci-4` |

The existing `coglatasci` service remains unchanged and supplies the fourth slot.

## Verify

Check GitHub under **Settings > Actions > Runners**. All four runners should be
online and idle before a workflow starts.

On the server:

```bash
systemctl list-units --type=service 'actions.runner.*'
```

Inspect one service when necessary:

```bash
sudo systemctl status 'actions.runner.*coglatasci-2*' --no-pager
```

Trigger a pull-request workflow and confirm that Build, Security, Qodana, and
Frontend obtain different runner names in their **Set up job** logs.

## Capacity warning

Four concurrent jobs remove queueing but increase peak CPU, memory, disk I/O,
and Docker pressure. Qodana, the Docker image build/Trivy scan, the .NET test job,
and the Angular job can all be active simultaneously.

On an 8 GB host, configure swap before enabling all four slots. A practical
baseline is 8–16 GB of swap. Monitor the first full concurrent run:

```bash
free -h
vmstat 1
sudo docker stats
```

When the host is resource-constrained, install only one or two additional
runners instead:

```bash
sudo RUNNER_TOKEN='REPLACE_WITH_FRESH_TOKEN' \
  ./scripts/ci/install-self-hosted-runner-pool.sh \
  --url https://github.com/NYGsatoshi/Coglatas \
  --count 1
```

That gives two total concurrent slots: the existing runner plus one additional
runner.

## Re-running the installer

The installer is idempotent for already configured directories. It preserves an
existing `.runner` registration and starts the service when needed. Use a new
registration token only when adding runner instances that have not yet been
configured.

The installer does not delete or reconfigure the existing
`/home/adminhome/actions-runner` instance.
