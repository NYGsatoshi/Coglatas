# Node24 production builder digest qualification

PR#1506 changes only the root Dockerfile frontend-deps base from the approved
Node24 digestd862bb5d082c5ae0210ac07e8c0344cb8cf7bef6d78e9b8c50c179da923108ea
toe7add2a490bb17d97b485b8641bd88455fff7839ade0f5eba493c3508124d413.
The provider-generated change is retained exactly. No package manifest/lock,
major toolchain policy, installer/license control, server authorization,
DB/schema/RLS, Functional oracle, timeout, retry or release gate changes.

## Independent registry identity

The official Docker Hub library/node repository serves the candidate OCI index.
Recomputed SHA-256, Docker-Content-Digest header and live tag24 all match the
candidate at2026-10-29T03:15Z. The linux/amd64 manifest is
8544b46b91d1a8c8bf8fc0d53ae9f276ed07d0ffa9098dc4a9c2f627363a13a0;
its separately hashed configuration is
9214e73b07d3828f371e8d5d8bfebdfa1b0625c3c82253a4cbd09d9c00e20b4a.
Configuration reports NODE_VERSION=24.22.2 and docker-entrypoint.sh.
This proves registry identity/version, not an application build or acceptance.
The separate development-only Alpine digest update#1505 is not conflated here.

## Source-bound qualification requirements

Provider source7f621fe7b620f6054cece7a609e95293d2177b59/tree
6b0dffbd7d746a0cbd1abd3680e33702b7fc7c10 has sole parentMain
769cf63890aa3479552357bcfd221f8b23a4b3a2. Its PRCI40810126059/1 succeeded,
but native Sonar40810143822/1 analysis123238409222 was SKIPPED following the
trusted-target resolver. Overall workflow SUCCESS is not actual native
analysis or an official Gate PASS. Existing environment policy stays unchanged.

This maintainer documentation commit records the concrete registry proof;
new-head required checks, independent review and native analysis are pending.
A full licensed candidate build should confirm that the changed frontend-deps
stage is actually exercised, rather than relying on a prebuilt-stage bypass.
No local Docker build is claimed: the daemon/disk environment remains blocked.
After a normal merge the resulting Main requires its own first-attempt
backend/frontend/Functional/security/Qodana/Sonar/source-image qualification.

Current Main769 qualification remains in progress. Its additional Mobile
40810432359/1 passed4owners/23steps/attempt1/retry0, independently source/image/
artifact bound; previousMainf14 first-attempt denial failure remains historical.
Neither result qualifies this Node candidate. FCI06defaultC, all OwnerHOLDs,
mainline Sonar36.1%/D/E historical failure and RELEASE_NO_GO remain unchanged.