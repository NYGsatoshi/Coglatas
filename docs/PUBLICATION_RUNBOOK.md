# Public repository cutover runbook

This runbook prepares Coglatas for public visibility without open-sourcing
repository-owned material. The repository must remain private until every
blocking item below is complete.

## 1. Intellectual-property decision

- Confirm that publishing the implementation and documentation will not harm a
  planned patent filing, contest submission, confidential disclosure, or
  contractual obligation.
- Obtain any required school, team, employer, contest, or co-author approval.
- Confirm ownership or written permission for every non-trivial contribution
  already merged into the repository.
- Confirm that the copyright-holder name in `COPYRIGHT.md` is the intended legal
  or public identifier.

**Stop the cutover if any point is unresolved.**

## 2. Secret, personal-data, and Git-metadata review

Run the `Publication Readiness` workflow and require a clean full-history
Gitleaks result. Also review material that a source scan cannot reliably
classify:

- commit messages, author/committer names, and author/committer e-mail
  addresses;
- issues, pull requests, reviews, comments, and uploaded images;
- Actions logs, summaries, caches, and artifacts;
- releases, release assets, tags, branches, wiki pages, and discussions;
- screenshots, Playwright traces/videos, test reports, database dumps, and
  demo fixtures; and
- deployment documentation, hostnames, IP addresses, usernames, internal URLs,
  backup locations, and school-identifying data.

The 2026-09-03 audit confirmed that existing commit metadata contains
school-domain author e-mail addresses. Before publication:

1. enable GitHub e-mail privacy and the option that blocks command-line pushes
   exposing a private address;
2. decide whether publication of historical author metadata is acceptable;
3. if it is not acceptable, rewrite the complete history intended for
   publication, including every branch and tag that will remain reachable;
4. remove or replace pull-request/branch references that retain pre-rewrite
   commits where necessary; and
5. inspect the rewritten repository from a fresh clone before continuing.

A `.mailmap` alone does not remove the original address from Git commit
objects. Do not treat display remapping as sanitization.

If a credential may ever have been committed, logged, attached, or shared,
revoke or rotate it first. Rewriting Git history is not a substitute for
rotation.

Delete unsafe historical workflow runs and artifacts before changing
visibility. Remove stale branches and tags that contain material not intended
for publication.

## 3. Syncfusion boundary

Create a GitHub Environment named `syncfusion-licensed-build`.

- Add `SYNCFUSION_LICENSE` as an **environment secret**, not a repository
  secret.
- Require approval from the repository owner before environment deployment.
- Remove any repository-level or organization-level
  `SYNCFUSION_LICENSE` available to this repository.
- Run licensed workflows only after inspecting the selected commit.
- Confirm that logs and artifacts do not contain the key.
- Confirm current Syncfusion license eligibility and terms before publication.

## 4. Actions and runners

- Keep pull-request workflows on GitHub-hosted runners.
- Do not register persistent self-hosted runners to the public repository.
- Remove existing repository runner registrations before visibility changes.
- Set the default `GITHUB_TOKEN` permission to read-only.
- Disable the option allowing GitHub Actions to create and approve pull
  requests unless a reviewed workflow specifically requires it.
- Require approval for workflows from fork pull requests.
- Keep protected deployment credentials in environments with required
  reviewers.
- Do not add `paths` or `paths-ignore` filters to a workflow whose check is
  required for pull requests. A required workflow must produce its check on
  every pull request; put optional routing inside jobs instead.

The repository guard rejects active `self-hosted` workflow routing, including
multi-line runner-label lists; `pull_request_target`; pull-request jobs that
reference or inherit Actions secrets; write permissions in pull-request
workflows; and secret-bearing jobs without a static job-local protected
environment.

## 5. Pull-request and merge policy

Set pull-request creation policy to **Collaborators only**.

Create rulesets for `main`:

### Review ruleset

- require a pull request;
- require at least one approval for non-bypass actors;
- require Code Owner review;
- dismiss stale approvals;
- require all review conversations to be resolved;
- block force pushes and branch deletion; and
- allow only the repository administrator to bypass, preferably for pull
  requests only.

### CI ruleset

Require these stable check-run contexts:

- `build-test`;
- `frontend-test`;
- `security-scan`;
- `publication-readiness`; and
- any release-specific licensed verification selected by the owner.

Do not give collaborators or the repository owner a CI bypass. GitHub may show
workflow and job names together in the web UI, but ruleset status-check contexts
must match the actual check-run names above. Verify the names again after this
pull request runs before activating the ruleset.

## 6. Repository features

Before cutover:

- disable Discussions and Wiki unless intentionally maintained;
- disable blank issues or disable Issues entirely if public support is not
  offered;
- enable dependency graph and Dependabot alerts/security updates;
- enable secret scanning and push protection;
- enable code scanning where available;
- enable private vulnerability reporting;
- review installed GitHub Apps, deploy keys, webhooks, environments, and
  Actions secrets; and
- ensure the separately maintained specification repository is intentionally
  public or intentionally private.

## 7. Final anonymous verification

After all previous steps pass:

1. make the repository public during a controlled maintenance window;
2. open it in a signed-out browser;
3. inspect README, source, commit history, branches, tags, releases, issues,
   pull requests, Actions, artifacts, security policy, and contributor policy;
4. verify unsolicited users cannot open pull requests under the selected
   creation policy;
5. run public GitHub-hosted CI;
6. verify licensed/manual jobs still require environment approval; and
7. immediately return the repository to private and rotate affected
   credentials if unexpected sensitive material is visible.

Public-to-private reversal cannot make already copied material secret again.
