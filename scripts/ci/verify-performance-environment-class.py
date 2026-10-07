"""Fail closed on normalized baseline provenance and class selection."""
import argparse
import sys
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "scripts/performance"))
from db_class_baselines import validate
from common import PerformanceContractError

parser = argparse.ArgumentParser()
parser.add_argument("--base-ref", required=True)
parser.add_argument("--head-sha", required=True)
args = parser.parse_args()
try:
    count = validate(ROOT, args.base_ref, args.head_sha)
    print(f"EnvironmentClass baseline enrollment valid: {count}")
except (PerformanceContractError, ValueError, OSError, KeyError, TypeError, zipfile.BadZipFile):
    print("EnvironmentClass baseline enrollment rejected: invalid or untrusted evidence", file=sys.stderr)
    raise SystemExit(1)
