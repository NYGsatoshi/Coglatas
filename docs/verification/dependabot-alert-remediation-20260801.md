# Dependabot / dependency alert remediation audit — 2026-08-01

## Scope and evidence boundary

The connected GitHub API used for this remediation does not expose the repository Security tab's individual Dependabot alert records. The current vulnerable dependency trees were therefore reconstructed from:

- `.github/dependabot.yml` monitored ecosystems and directories;
- the repository's `npm Security Audit` workflow output;
- the main CI security scan for NuGet, Gitleaks, and Trivy;
- the exact `package.json` and `package-lock.json` files on `main` and this branch.

This report does not claim that Security-tab alert IDs, alert numbers, or dismissal states were read directly.

## Baseline on main

| Scope | Low | Moderate | High | Critical | Total |
| --- | ---: | ---: | ---: | ---: | ---: |
| Root npm workspace | 0 | 0 | 0 | 0 | 0 |
| Active `frontend/` | 3 | 7 | 10 | 0 | 20 |
| Inactive `coglatas-frontend/` | 0 | 5 | 7 | 0 | 12 |

The npm totals include parent packages and transitive dependency paths. They are not 32 independent vulnerability records.

Additional security scan results on main:

- NuGet vulnerable packages: 0
- Runtime container Trivy HIGH/CRITICAL: 0
- Gitleaks findings: 0
- `xunit` 2.9.3 is reported as deprecated/legacy, not as a known vulnerable package; migration to xUnit v3 is separate major work.

## Applied changes

### Active `frontend/`

- Align Angular framework packages from 21.2.17 to 21.2.19.
- Align `@angular/build`, `@angular/cli`, and `@angular/compiler-cli` from 21.2.17 to 21.2.19.
- Add reviewed transitive overrides for:
  - `undici` 7.28.0
  - `@babel/core` 7.29.7
  - `http-proxy-middleware` 3.0.7
  - `esbuild` 0.28.1
  - `ajv` 8.18.0
  - `picomatch` 4.0.4
  - `http-auth` 4.2.1
  - `uuid` 11.1.1
  - `body-parser` 1.20.6

### Inactive `coglatas-frontend/`

- Align Angular framework, compiler, and platform-browser-dynamic packages from 21.2.18 to 21.2.19.
- Preserve the existing reviewed security overrides.

### Lockfiles

Both npm lockfiles were regenerated from the reviewed manifests with npm 11.17.0 and Node.js 24. Dependency lifecycle scripts were disabled during generation and deterministic-install validation.

Validation completed successfully:

```text
npm --prefix frontend ci --ignore-scripts --no-audit --no-fund
npm --prefix coglatas-frontend ci --ignore-scripts --no-audit --no-fund
```

## Result after validated remediation

| Scope | Low | Moderate | High | Critical | Total | Reduction |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Root npm workspace | 0 | 0 | 0 | 0 | 0 | unchanged |
| Active `frontend/` | 0 | 4 | 2 | 0 | 6 | 20 → 6 |
| Inactive `coglatas-frontend/` | 0 | 4 | 2 | 0 | 6 | 12 → 6 |

The same six dependency-tree entries remain in both Angular workspaces.

## Residual-alert classification

| Severity | Dependency-tree entry | Vulnerable range reported by npm audit | Required response |
| --- | --- | --- | --- |
| High | `postcss` | `<=8.5.17` | A patched 8.5.x version exists, but Angular 21.2.19 pins the dependency through `@angular-devkit/build-angular`. Test a reviewed transitive override in a separate PR, or take the Angular 22 migration after its compatibility plan is approved. |
| High | `@angular-devkit/build-angular` | `<=22.1.0-rc.0` | Parent entry caused by `postcss` and `webpack-dev-server`. Do not treat this as a second independent exploit. npm proposes 22.1.2, which is a major framework migration and must not be auto-applied here. |
| Moderate | `webpack-dev-server` | `<=5.2.5` | A compatible 5.2.6 patch is available. Apply through a separate lockfile/override PR and run Angular build, tests, Storybook, and local-dev-server checks. |
| Moderate | `@hono/node-server` | `<2.0.5` | Transitive through the MCP SDK bundled by Angular CLI. Do not force a Hono major override independently; update the MCP SDK or Angular CLI through a tested follow-up PR. |
| Moderate | `@modelcontextprotocol/sdk` | `1.25.0 - 1.29.0` | A fixed 1.30.x line exists. A transitive override is plausible but must be tested because Angular CLI owns this dependency. |
| Moderate | `@angular/cli` | `20.3.14 - 20.3.32 || 21.0.5 - 22.1.2` | Parent entry caused by the MCP/Hono chain. npm's suggested downgrade to 21.0.4 is not an acceptable remediation. Test MCP 1.30.x under Angular CLI 21.2.19 or wait for an Angular 21 LTS patch that updates the chain. |

### Exposure notes

- These remaining packages are development/build tooling; they are not shipped as the ASP.NET Core runtime container's application dependencies.
- The PostCSS advisory can affect builds that process attacker-controlled source maps. CI and local builds must not consume untrusted CSS/source-map inputs.
- The webpack-dev-server advisories affect development-server endpoints. The dev server must remain bound to trusted/local interfaces and must not be exposed publicly.
- The Hono advisory concerns static-file path handling on Windows inside a transitive CLI/MCP tool. It is still tracked; development-only placement is not a reason to dismiss it silently.

## Prohibited automatic fixes

The following audit suggestions are not accepted automatically:

- `npm audit fix --force`
- Angular 22 major migration inside this remediation PR
- Angular CLI downgrade to 21.0.4
- Storybook downgrade to 6.5.x
- untested direct override of Hono 2.x

These suggestions can change framework contracts or are audit-resolution artifacts rather than safe security patches.

## CI status and operational blocker

- Regenerated lockfiles passed deterministic `npm ci --ignore-scripts` validation for both workspaces.
- The normal npm audit reproduced the 6-entry result above.
- The self-hosted runner then remained occupied in the `actions/setup-node` post-job npm-cache operation. Later full CI jobs were queued or cancelled by newer commits before a complete build/test/Storybook/Playwright result was available.
- Experimental overrides for PostCSS, webpack-dev-server, and the MCP SDK were intentionally removed because their lockfile and full-CI validation could not be completed while the runner was blocked.

## Merge gate

Current recommendation: **No-Go / Draft** until all of the following are complete:

- active Angular production build passes;
- Angular unit and architecture tests pass;
- Storybook build passes;
- Playwright checks pass;
- backend and security CI pass on the final commit;
- residual High advisories have either a tested follow-up fix or an explicit, time-bounded risk disposition;
- the self-hosted runner cache-post-step issue is cleared so final CI evidence can be collected.
