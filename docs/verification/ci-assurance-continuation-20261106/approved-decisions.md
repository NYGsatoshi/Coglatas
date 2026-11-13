# Approved CI assurance decisions D01–D15

Authority: the Owner's **Coglatas CI Assurance — Recover Missing Decisions and
PR-N Contract** instruction supplied in this continuation. These decisions were
already approved; this record persists their supplied wording without a new
approval request. The earlier checkpoint's statement that the text was missing
remains historical evidence of the information available at that time.

These decisions do not authorize changes to GitHub governance settings,
required-check contracts, release approval, or Main. Integration remains subject
to the [PR-N contract](pr-n-contract.md) and each unresolved mandatory hold.

### D01

All changes target SecurityTestImplimation. Never push, modify, or merge Main.

### D02

Preserve all six required check identities, enforcement semantics, and coverage. Changes require separate approval.

### D03

Preserve required and non-required failures. Never classify unexplained failures as successful or resolved.

### D04

Evaluate every capability at five levels: file presence, configuration, native execution, verified coverage, and final-gate integration.

### D05

PR tests prioritize bounded deterministic high-risk checks. Deep, long-running, and resource-intensive tests belong to isolated Nightly/Manual lanes.

### D06

Active security scanning is limited to SEC-02 isolated synthetic environments.

### D07

Critical/High findings and scanner failures block. Medium ratchet activation requires reviewed initial inventory and Owner approval.

### D08

Exceptions and baselines require Owner, rationale, tracking Issue, expiry, explicit scope, and verifiable approval. Unauthorized baseline expansion is prohibited.

### D09

Verify original test Suite/Case identity sets, including skips, retries, failures, missing cases, and duplicates. Counts alone are insufficient.

### D10

Independently verify source SHA, Run ID, Attempt, Producer, Artifact ID, and content Digest.

### D11

Preserve minimum GitHub Token permissions, PR/Trusted isolation, secret confidentiality, immutable Action pins, and allowlists.

### D12

Exclude runner allocation waiting. Measure whole-workflow critical path, total runner-seconds, and detection quality together.

### D13

Introduce new tools only for verified gaps not adequately addressed by existing tools. Prefer maintained free/open-source implementations.

### D14

Missing, empty, malformed, stale, corrupted, inconsistent, or unexecuted mandatory evidence must never count as successful.

### D15

Independent review, negative tests, exact-source native CI, evidence retention, and rollback capability are mandatory before eligible integration.
