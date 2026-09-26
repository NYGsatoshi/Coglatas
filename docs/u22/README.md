# U-22 2026 submission materials

This directory holds the repository-owned preparation material for the
U-22 2026 submission baseline. It is intentionally a release aid, not a claim
that every planned Coglatas capability is complete.

The submission scope is the authenticated Workspace -> Project -> Task path:

1. select or create a Workspace;
2. create and activate a Project;
3. create a Task with metadata, an optional structured Brief, source-policy
   configuration, and an advisory quality checklist; and
4. inspect the Task's current state, current phase, and available Activity.

Read [submission-checklist.md](submission-checklist.md) before declaring a
baseline. The final freeze records the raw commit SHA, release identifier,
date, and final evidence in the annotated `u22-2026-submission` tag and its
matching GitHub Release. The tag target is deliberately not copied into the
same commit's Markdown because that would be self-referential; use the raw SHA
in the annotation and release metadata as the immutable identity.

## Contents

- [submission-checklist.md](submission-checklist.md): release gate and
  evidence record.
- [demo-script.md](demo-script.md): truthful three-minute presentation.
- [architecture-summary.md](architecture-summary.md): submission-path design
  and trust boundaries.
- [implementation-summary.md](implementation-summary.md): implemented scope
  and issue boundaries.
- [install-or-access-instructions.md](install-or-access-instructions.md):
  contributor, verifier, and test-demo setup.
- [demo-data.md](demo-data.md): test-only, deterministic U-22 fixture and
  loopback-demo safety boundary.
- [oss-and-third-party-inventory.md](oss-and-third-party-inventory.md):
  component inventory and license-review procedure.
- [known-limitations.md](known-limitations.md): product and release limits.

When the test-only U-22 demo fixture is included in the selected baseline,
[demo-data.md](demo-data.md) is its authoritative setup and safety guide. It
is intentionally maintained with that fixture rather than duplicated here.
