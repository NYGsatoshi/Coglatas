"""Supplemental prototype results; no production, trust or numerical assurance credit."""
from __future__ import annotations

import argparse
import json
from pathlib import Path

from projectide_core_isolated import inventory


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--results", type=Path, required=True)
    parser.add_argument("--baseline", type=Path, required=True)
    parser.add_argument("--require-acquisition", action="store_true")
    parser.add_argument("--require-receipt-proof", action="store_true")
    args = parser.parse_args()
    baseline = json.loads(args.baseline.read_text(encoding="utf-8-sig"))
    for lane, name in (("projectIde", "projectide"), ("architecture", "architecture")):
        actual = inventory(args.results / f"{name}.trx")
        if not set(baseline[lane]).issubset(actual):
            raise ValueError(f"Retained {lane} identities are missing")
        if lane == "projectIde" and sum(".VersionedArtifactTests." in identity for identity in actual) < 36:
            raise ValueError("Prototype artifact regression inventory is incomplete")
        if lane == "projectIde" and args.require_acquisition and sum(".SyntheticDependencyAcquisitionTests." in identity for identity in actual) < 39:
            raise ValueError("Synthetic acquisition inventory is incomplete")
        if lane == "projectIde" and args.require_receipt_proof and sum(".SyntheticReceiptAuthenticationTests." in identity for identity in actual) < 23:
            raise ValueError("Synthetic receipt proof inventory is incomplete")
        (args.results / f"{name}-inventory.json").write_text(json.dumps(actual, indent=2) + "\n", encoding="utf-8")
        print(f"{lane}: {len(actual)} complete passing identities; retained baseline present")


if __name__ == "__main__":
    main()
