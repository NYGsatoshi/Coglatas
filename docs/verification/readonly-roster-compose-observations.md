# ReadOnly roster Compose startup observations

The retained quality89ee roster attempt41153391617 failed before readiness,
fixture or browser qualification. Its captured inventories were empty and its
structural receipt ended at STACK_CAPTURE_COMPLETE/CLEANUP_VALIDATION with FAIL
and partial/unverified retained cleanup. The original Compose error is UNKNOWN;
the former silent capture was not retained by an approved post-job interface.
This change does not retroactively establish that error or its cause.

The existing CONFIG and UP callers now retain their normalized result. A roster
nonzero result calls the existing failure recorder before later capture advances
the execution phase. CONFIG records CONSTRUCTED, and admitted UP records ADMITTED;
the existing failure class, later capture, ownership validation and fail-closed
cleanup/verdict remain authoritative. The execution.json schema is unchanged.

One `UI07_ROSTER_COMPOSE_OBSERVATION` console record can publish only operation
CONFIG/UP, normalized exitCode, outputState, capped capturedBytes and pattern.
Its four wording classes are DAEMON_CONNECTION_TEXT, MANIFEST_UNKNOWN_TEXT,
RATE_LIMIT_TEXT and NO_SPACE_TEXT, with UNKNOWN fallback. Only one distinct
wording class can match; unknown, overlapping, incomplete or overflow evidence
stays UNKNOWN. Byte length above the existing 2MiB observation bound also means
OVERFLOW, including multibyte input. A successful command emits no observation.

These are literal wording observations, not proven root causes. In particular,
no-space wording can reflect namespace limits rather than a full disk. The
documented sources are [Docker daemon troubleshooting](https://docs.docker.com/engine/daemon/troubleshoot/),
[Distribution API errors](https://distribution.github.io/distribution/spec/api/#errors)
and [Docker rootless troubleshooting](https://docs.docker.com/engine/security/rootless/troubleshoot/).
There are no capture groups or exported message fragments, URLs, image names,
paths, configuration, credentials or raw output hashes. Existing captured command
output stays in memory and silent; no new private storage, upload or log-export
path is added. Observation/formatting failure cannot replace command failure.

Controlled tests exercise the actual wrapper source through the existing VM and
owned fake-Docker/real roster-control mechanisms. They preserve first-failure
phase through later capture/cleanup failure, enforce no cleanup of uncaptured
resources, prove success produces no observation and test unknown/ambiguous,
multibyte/retained-prefix overflow and secret-marker privacy. These are synthetic
controls, not actual Docker startup or browser acceptance. The 43/42 roster,
native-key, oracle, timeout, retry, flag and protected-check contracts are unchanged.

Main eba has 24 fixture/runner controls; quality89ee has 34 after its independent
qualification-helper expansion. A work-only source-bound overlay checks that
expanded cohort against this diagnostic candidate without promoting that helper
or dropping its ten tests. Both baselines and their source identities are retained.
