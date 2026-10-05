#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
OUT="$ROOT/artifacts/performance/api-diagnostics"
# A rerun is not a new investigation campaign and cannot provide rescue evidence.
test "${GITHUB_RUN_ATTEMPT:-1}" == 1
mkdir -p "$OUT"
test ! -e "$OUT/cohort-manifest.json"
export COGLATAS_PERFORMANCE_RUNTIME_MODE=source
export COGLATAS_PERFORMANCE_PROFILE=small
export COGLATAS_PERFORMANCE_DB_CAPTURE_ENABLED=false
export COGLATAS_PERFORMANCE_API_DIAGNOSTICS_ENABLED=true
python3 "$ROOT/scripts/performance/api_k6.py" validate
HEAD_SHA="$(git -C "$ROOT" rev-parse HEAD)"
export COGLATAS_DIAGNOSTIC_SOURCE_SHA="$HEAD_SHA"
export COGLATAS_DIAGNOSTIC_OUTPUT="$OUT"
python3 - <<'PY'
import datetime, hashlib, json, os
from pathlib import Path
root = Path.cwd()
contract = json.loads((root / 'performance/api-k6.json').read_text())
assert contract['mainRuns'] == 5
created = datetime.datetime.now(datetime.timezone.utc)
manifest = {
    'schemaVersion': 1,
    'cohortId': 'perf04-diagnostic-' + os.environ.get('GITHUB_RUN_ID', 'local') + '-' + os.environ['COGLATAS_DIAGNOSTIC_SOURCE_SHA'],
    'sourceSha': os.environ['COGLATAS_DIAGNOSTIC_SOURCE_SHA'],
    'createdUtc': created.isoformat(), 'expiresUtc': (created + datetime.timedelta(hours=4)).isoformat(),
    'workflowRun': os.environ.get('GITHUB_RUN_ID', 'local'), 'workflowAttempt': 1,
    'purpose': 'investigation-only-not-gate-or-baseline-evidence',
    'captureGroups': 5, 'earlyStop': False, 'retries': 0, 'selection': 'retain-all-in-original-order',
    'fixtureProfile': 'small', 'dbScenarioFixture': False,
    'datasetDigest': hashlib.sha256((root / 'performance/datasets.json').read_bytes()).hexdigest(),
    'apiContractDigest': hashlib.sha256((root / 'performance/api-k6.json').read_bytes()).hexdigest(),
    'toolVersions': {'k6': contract['k6Version'], 'dotnetSdk': json.loads((root / 'global.json').read_text())['sdk']['version']},
    'scenarioSet': [scenario['id'] for scenario in contract['scenarios']],
    'sampleCountPerScenarioPerGroup': contract['profile']['iterations'],
    'measurementSemantics': 'existing-metrics-unchanged-observer-overhead-present',
    'policyVersion': 'api-diagnostics-v1',
}
(Path(os.environ['COGLATAS_DIAGNOSTIC_OUTPUT']) / 'cohort-manifest.json').write_text(json.dumps(manifest, indent=2) + '\n')
PY
failed=0
for (( run=1; run<=5; run++ )); do
  # Execute the full prospectively declared cohort, even after a failed group.
  # Never replay a group or discard an unstable sample.
  set +e
  GITHUB_SHA="$HEAD_SHA" \
    COGLATAS_PERFORMANCE_TRIAL_ORDINAL="$run" \
    COGLATAS_PERFORMANCE_COMPOSE_PROJECT="coglatas-performance-diagnostic-${GITHUB_RUN_ID:-local}-$run" \
    COGLATAS_PERFORMANCE_EVIDENCE_DIR="$OUT/current-environment-$run" \
    bash "$ROOT/scripts/performance/with-environment.sh" \
    python3 "$ROOT/scripts/performance/api_k6.py" collect --output "$OUT/current-$run.json"
  status=$?
  set -e
  python3 - "$OUT/group-$run-status.json" "$run" "$status" <<'PY'
import json, sys
from pathlib import Path
Path(sys.argv[1]).write_text(json.dumps({'groupOrdinal': int(sys.argv[2]), 'exitCode': int(sys.argv[3]), 'replayed': False}) + '\n')
PY
  if (( status != 0 )); then failed=1; fi
done
exit "$failed"
