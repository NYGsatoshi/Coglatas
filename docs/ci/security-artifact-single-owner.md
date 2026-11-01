# Single-owner Security CI artifacts

The PR `security-scan` job publishes a **single canonical sanitized runtime
evidence** artifact, `sec06-zap-evidence`, with the SEC-04 Schemathesis roles,
SEC-05 authorization-negative matrix and SEC-06 ZAP reports. Its original
`if: always()`, `if-no-files-found: error` and scan routing remain intact, so
a scanner failure does **not** suppress this evidence.

The failure-only `security-scan-reports` artifact holds **only scanner-independent
diagnostics** (NuGet vulnerability output, Trivy filesystem report, Gitleaks
report). It no longer duplicates Schemathesis and ZAP evidence. Historical
diagnostic tools that consume older run artifacts remain backward compatible.

`scripts/ci/test-security-artifact-single-owner.py` runs during the existing
PR preflight and rejects duplicate artifact names, second scanner owners,
missing required scanner evidence, weakened publication flags and overlapping
failure artifacts. No additional build, scanner process, runtime container,
runner, job, artifact, API permission or required check is introduced.

This is a storage and ownership guarantee, **not a speedup claim**. Only
changes on `SecurityTestImplimation` may be integrated; Main is not changed.
