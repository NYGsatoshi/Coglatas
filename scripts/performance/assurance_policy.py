#!/usr/bin/env python3
"""Fail-closed temporary numerical performance assurance suspension."""
from __future__ import annotations
import argparse
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
POLICY = ROOT / "performance/assurance-state.json"
EXPECTED = {
    "schemaVersion": 1,
    "state": "SUSPENDED",
    "effectiveDate": "2026-10-08",
    "reasonCode": "SHARED_RUNNER_VARIANCE_NO_AFFORDABLE_FIXED_HOST",
    "resumeIssue": 1128,
    "apiNumericAssurance": "NOT_EVALUATED",
    "dbDurationAssurance": "NOT_EVALUATED",
    "dbStructuralGate": "ENFORCED",
    "contractGate": "ENFORCED",
}

def unique(pairs):
    doc = {}
    for key, value in pairs:
        if key in doc:
            raise ValueError("duplicate policy key")
        doc[key] = value
    return doc

def load_policy(path=POLICY):
    value = json.loads(Path(path).read_text(encoding="utf-8"), object_pairs_hook=unique)
    if not isinstance(value, dict) or value != EXPECTED:
        raise ValueError("not the reviewed suspension; reactivation needs a separate implementation PR")
    return value

def receipt(suite, sha):
    data = load_policy()
    if suite not in ("api", "db") or not re.fullmatch(r"[0-9a-f]{40}", sha):
        raise ValueError("invalid source SHA or suite")
    return {
        "schemaVersion": 1, "sourceSha": sha, "suite": suite,
        "policyState": data["state"], "numericalDecision": "NOT_EVALUATED",
        "numericalAcceptanceCredit": False, "baselineQualificationCredit": False,
        "structuralGate": "ENFORCED_SEPARATELY" if suite == "db" else "NOT_APPLICABLE",
        "contractGate": "ENFORCED", "reasonCode": data["reasonCode"],
        "resumeIssue": data["resumeIssue"],
    }

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("operation", choices=("status", "receipt"))
    parser.add_argument("--suite", choices=("api", "db"))
    parser.add_argument("--sha")
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    try:
        load_policy()
        if args.operation == "status":
            if args.suite or args.sha or args.output:
                raise ValueError("status has no extra options")
            print("suspended")
        else:
            if not args.suite or not args.sha or not args.output:
                raise ValueError("receipt needs suite, sha, output")
            value = receipt(args.suite, args.sha)
            args.output.parent.mkdir(parents=True, exist_ok=True)
            args.output.write_text(json.dumps(value, sort_keys=True, indent=2) + "\n", encoding="utf-8")
            print("SUSPENDED / NOT_EVALUATED: zero numerical acceptance credit")
        return 0
    except (ValueError, OSError, json.JSONDecodeError) as error:
        print("Assurance suspension verification failed: " + str(error), file=sys.stderr)
        return 1

if __name__ == "__main__":
    raise SystemExit(main())
