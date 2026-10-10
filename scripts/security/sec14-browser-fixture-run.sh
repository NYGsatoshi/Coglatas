#!/usr/bin/env bash
# Local native-tool controls only. All traffic is loopback inside network=none.
set -euo pipefail
mode=${1:-normal}
[[ "$mode" == normal || "$mode" == weakened || "$mode" == disabled ]] || exit 2
fixture_args=()
if [[ "$mode" == weakened ]]; then fixture_args+=(--weaken); fi
python3 /fixtures/sec14-browser-fixture.py "${fixture_args[@]}" &
fixture_pid=$!
trap 'kill "$fixture_pid" 2>/dev/null || true' EXIT
plan=/fixtures/sec14-browser-fixture.yaml
if [[ "$mode" == disabled ]]; then
  python3 - <<'PY'
from pathlib import Path
import yaml
plan = yaml.safe_load(Path('/fixtures/sec14-browser-fixture.yaml').read_text())
plan['jobs'] = [job for job in plan['jobs'] if job['type'] != 'spiderAjax']
with Path('/out/disabled-plan.yaml').open('x') as handle:
    yaml.safe_dump(plan, handle)
PY
  plan=/out/disabled-plan.yaml
fi
set +e
timeout 240s /zap/zap.sh -cmd -silent -autorun "$plan" > /out/native-process.log 2>&1
scanner_status=$?
set -e
curl --silent --fail --max-time 5 http://127.0.0.1:8123/__receipt > /out/browser-counters.json
printf '%s\n' "$scanner_status" > /out/scanner-exit.txt
exit "$scanner_status"
