# Trivy canonical database acquisition

## Retained first failure

Main `03dcbbfde03dd3364e637ee8fee92d2545b5e7c1`, run
[40269319731/1](https://github.com/NYGsatoshi/Coglatas/actions/runs/40269319731),
failed its first Main Security attempt in job `121399429565`.
Trivy `0.65.0` pulled successfully, but its default database mirror returned
HTTP 404 while downloading an OCI layer. Database initialization failed before
the filesystem scan produced a report. The vulnerability verdict is
**NOT_EVALUATED**, and the subsequent Gitleaks step was skipped. The original
failure and the three failing aggregate checks remain failure evidence.

## Mechanism and change

Upstream Trivy `v0.65.0` (commit
`b2b15459bac0efef73531c73b45e80d40ddd0fec`) lists
`mirror.gcr.io/aquasec/trivy-db:2` before
`ghcr.io/aquasecurity/trivy-db:2`. Its OCI downloader tries another repository
only for a temporary transport error or the structured `BLOB_UNKNOWN` error.
The observed layer HTTP 404 did not trigger that fallback. Relevant upstream
sources are [`pkg/flag/db_flags.go`](https://github.com/aquasecurity/trivy/blob/b2b15459bac0efef73531c73b45e80d40ddd0fec/pkg/flag/db_flags.go),
[`pkg/db/db.go`](https://github.com/aquasecurity/trivy/blob/b2b15459bac0efef73531c73b45e80d40ddd0fec/pkg/db/db.go),
and [`pkg/oci/artifact.go`](https://github.com/aquasecurity/trivy/blob/b2b15459bac0efef73531c73b45e80d40ddd0fec/pkg/oci/artifact.go).

Set the native `--db-repository ghcr.io/aquasecurity/trivy-db:2` argument at
all five existing Trivy database acquisition sites: Main and PR filesystem
scans, the licensed image scan, and the SBOM and release database downloads.
The latter two retain their existing cached `--skip-db-update` consumers.
This selects the official upstream database directly. Scanner versions,
severities, unfixed handling, exit codes, database freshness checks, SBOM gates,
workflow permissions, deployment approvals, deadlines and retries are unchanged.
A failure of the canonical registry still fails the scan.

## Local execution on 2026-10-26 UTC

The managed Docker daemon was checked explicitly. The exact `0.65.0` scanner
image resolved to digest
`sha256:a22415a38938a56c379387a8163fcb0ce38b10ace73e593475d3658d578b2436`,
matching the failed hosted invocation. An empty, uniquely owned cache downloaded
the canonical database successfully with proxy settings preserved and TLS
certificate validation enabled. No database-update bypass or retry was used.
Database schema was 2, updated at `2026-10-26T01:20:01.38093772Z`, and downloaded
at `2026-10-26T05:24:55.88731781Z`. The local database file SHA-256 was
`feefbadc2ac95624d885203a353f131072c2ee4a03700d8ff4462151abc4ce0a`.
A separate anonymous registry metadata read observed manifest
`sha256:df325edea0d1f66994a99d0b40bd279d8e955489453586de27b949b958e74c8e`.
The mutable database tag is deliberately not frozen by this repair.

Synthetic npm lockfile scans, without installing or executing the packages,
used the unchanged `--scanners vuln --severity HIGH,CRITICAL --ignore-unfixed
--exit-code 1` policy. JSON output was used to inspect the results:

| Input | Actual result | Exit |
| --- | --- | --- |
| `minimist@1.2.0` | CRITICAL `CVE-2021-44906`, fixed versions reported | 1 |
| `minimist@1.2.8` | No vulnerabilities at the selected severities | 0 |
| `fast-uri@3.1.8` | No vulnerabilities at the selected severities | 0 |
| `fast-uri@3.1.9` | No vulnerabilities at the selected severities | 0 |

The fast-uri pair is not a blocking-severity positive control. These results
make no claim about advisories below the selected severities.

A Git tracked-source archive of the exact failed Main also completed the
original table-output filesystem command with the canonical argument, exit 0.
The report listed the three npm lockfile targets with zero selected findings.
This archive excluded restored .NET build outputs, installed dependencies and
runtime images; it is **not** a replacement for the hosted Main filesystem scan
or an all-dependency vulnerability verdict.

Existing local controls passed: publication parser 18, action pins 14, required
check topology 54, SBOM vulnerability gate 18, and Grype SBOM source 8 tests.
Publication readiness checked all 66 workflows. Actionlint 1.7.12 passed all five
changed workflows. Offline Zizmor 1.30.1 at medium severity reported no findings
with the repository's existing nine ignored and six suppressed findings.

## Qualification limits

The source change and local controls do not qualify a hosted PR or Main. A new
exact-head hosted scan and an independently qualified resulting Main are still
required. No old failure becomes a pass. No Owner hold, security activation,
deployment or Student Council release approval is changed; release remains
**NO_GO**.
