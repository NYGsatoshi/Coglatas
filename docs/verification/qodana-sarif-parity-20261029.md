# Qodana Community/Cloud SARIF parity qualification — 2026-10-29

Security integration target: **`SecurityTestImplimation`**. Related issue: [#1500](https://github.com/NYGsatoshi/Coglatas/issues/1500).

## Observed historical results

The two existing Main Qodana jobs use the same QDNETC analyzer image but start separate containerized scans. Their full, original SARIF `qodana.sarif.json` artifacts were compared, not merely the counts or GitHub's normalized Code Scanning upload.

| Main run | Commit | Community artifact ID | Cloud artifact ID | Results | Rules | Artifacts | Normalized SARIF SHA-256 |
| --- | --- | ---: | ---: | ---: | ---: | ---: | --- |
| [40649034671](https://github.com/NYGsatoshi/Coglatas/actions/runs/40649034671) | `cc83b0b6f2607e4705885ac6c1a0fed995dcfc0b` | 12800071093 | 12799926762 | 2,278 | 3,184 | 274 | `9839256b3c5fc1fd239edd6b45bdf8fac5364309a3300b04129bf0074e4ec0a7` |
| [40613581287](https://github.com/NYGsatoshi/Coglatas/actions/runs/40613581287) | `7014b971f7c3613754572bf329a3bce166116ba2` | 12785322037 | 12784854623 | 2,278 | 3,184 | 274 | `74860087bdad96027952607a643c9843613376da9f1a08e315a26d6de19c4aa1` |
| [40600623868](https://github.com/NYGsatoshi/Coglatas/actions/runs/40600623868) | `15e9c49b6d15934717ca790c4c3b33b16a59bd1f` | 12779986352 | 12780370124 | 2,278 | 3,184 | 274 | `1c97a0c604ccac5174e05f8acc80f22c8a192f1073ad43b4d2ad7522dd86c0b5` |

The only differences in the complete SARIF documents were `runs[0].automationDetails.guid`, unique to each independently executed analyzer. Per-run `report/results/result-allProblems.json` also matched byte-for-byte within each pair.

## New strict advisory verifier

`scripts/quality/qodana_sarif_parity.py` compares **all** JSON fields after removing only the well-formed, canonical, differing execution GUIDs. The validator requires: exact expected Git SHA, branch, GitHub Actions run URL and repository provenance; one successful QDNETC invocation; valid nonempty result/rule/artifact arrays; unique rule IDs; bounded UTF-8 SARIF; no duplicate JSON keys, symlinks, reuse of the same inode, or nonfinite JSON values.

Usage:

```bash
python3 -B scripts/quality/qodana_sarif_parity.py \
  --community /path/to/community/qodana.sarif.json \
  --cloud /path/to/cloud/qodana.sarif.json \
  --source-sha <40-hex-source-sha> \
  --run-id <GitHub-Actions-run-id> \
  --branch main \
  --output /path/to/new/advisory.json
```

It emits only a bounded, sanitized JSON summary after a successful comparison. Even a successful comparison is **`PARITY_OBSERVED_NOT_ATTESTED`** with `qualifiedForGateReuse=false` and `runAttemptVerified=false`: it is **not** proof of artifact producer authenticity or GitHub Actions run attempt binding. Failing or stale reports do not create successful output.

The synthetic no-network negative controls execute from `tests/ci/test_qodana_sarif_parity.py` in GitHub Actions Static Analysis when the verifier or tests change.

## Remaining work before engine deduplication

1. Add independently verified same-run **attempt-bound** Community and Cloud artifact receipts (SHA, GitHub run/attempt, producer job identity and artifact digest) with false-green controls. Do not trust user-supplied sidecars merely because the fields match.
2. Qualify the official `qodana send` command or other supported mechanism for **publishing the same existing report** to Qodana Cloud, without rerunning analysis. Require successful Cloud ingestion and preserve existing project-model guard and GitHub SARIF upload categories.
3. Compare exact findings on additional representative candidate commits, including changes with new issues, resolved issues, and nonzero failing gates.
4. Preserve original Community/Cloud jobs and existing enforcement until the independently verified replacement passes and the owner reviews the result. No Main change or release approval is authorized here.
