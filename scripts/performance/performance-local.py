#!/usr/bin/env python3
"""Local preflight/diagnostics and signed-evidence verification; no automatic reruns."""
from __future__ import annotations
import argparse
import json
import os
import re
import sys
from pathlib import Path
from common import write_json_atomic
from local_support import ROOT, LocalError, failure, identities, invoke, load_json, preflight, sha256
import local_evidence as evidence

def audit_workflows(root=ROOT):
    """Find actual collector commands, excluding compilation and contract tests."""
    pattern = re.compile(
        r"(run-api-(?:k6|diagnostics)\.sh|announcement-diagnostic\.py|"
        r"(?:python3?|python)\s+(?:\S+/)?scripts/performance/db-probe\.py|"
        r"db_campaign\.py\s+capture|api_k6\.py\s+collect|"
        r"(?:grafana/k6[^\s]*|k6)\s+run)")
    found = []
    for file in sorted((root / ".github/workflows").glob("*.yml")):
        lines = file.read_text(encoding="utf-8").splitlines()
        in_run, indent = False, 0
        for number, line in enumerate(lines, 1):
            stripped = line.lstrip()
            if re.match(r"run:\s*", stripped):
                in_run, indent = True, len(line) - len(stripped)
            elif stripped and not stripped.startswith("#") and len(line) - len(stripped) <= indent:
                in_run = False
            if in_run and (match := pattern.search(line)):
                found.append({"path": file.relative_to(root).as_posix(), "line": number, "collector": match[0]})
    return {"schemaVersion": 1, "hostedMeasurementsRemaining": found,
            "migrationComplete": not found, "requiredContextRetained": "performance-fast"}

def seal(output, signer_identity):
    output, bundle = Path(output), Path(output) / "bundle"
    trusted = evidence.policy()
    declaration = load_json(output / "declaration.json")
    complete = load_json(bundle / "completion.json")
    evidence.require(declaration["mode"] == complete["mode"] == "LOCAL_ACCEPTANCE" and
                     complete["allCollectorsComplete"] is True, stage="seal-mode")
    campaigns = [c for c in trusted["campaigns"] if c["id"] == declaration["campaignId"]]
    evidence.require(len(campaigns) == 1, "CAMPAIGN_UNAVAILABLE", "seal")
    campaign = campaigns[0]
    evidence.require(any(s["identity"] == signer_identity and not s["revoked"] for s in trusted["allowedSigners"]),
                     stage="signer")
    evidence.require(not (bundle / "manifest.json").exists(), stage="immutable-seal")
    manifest = {k: campaign[k] for k in ("evidenceId", "repository", "prNumber", "candidateSha",
                "baseSha", "treeSha", "baselineSha", "expiresAtUtc", "baselineEnrollmentIds", "k6ImageId")}
    manifest.update({"schemaVersion": 1, "mode": "LOCAL_ACCEPTANCE", "campaignId": campaign["id"],
        "signerIdentity": signer_identity, "startedAtUtc": complete["startedAtUtc"],
        "endedAtUtc": complete["endedAtUtc"], "hardware": complete["hardware"],
        "runtimeFingerprints": {
            "api": load_json(bundle / "api/current-1/sample.json")["fingerprint"],
            "db-small": load_json(bundle / "db/small/environment.json"),
            "db-medium": load_json(bundle / "db/medium/environment.json")},
        "contractAndToolDigests": declaration["contractAndToolDigests"],
        "groupOrder": complete["groupOrder"], "complete": True,
        "attempt": complete["attempt"], "previousEvidenceIds": complete["previousEvidenceIds"]})
    evidence.campaign_for(manifest, trusted)
    for name, field in (("sourceSha", "candidateSha"), ("treeSha", "treeSha"),
                        ("baseSha", "baseSha"), ("baselineSha", "baselineSha")):
        evidence.require(complete[name] == manifest[field], stage="seal-source")
    evidence.require(manifest["contractAndToolDigests"] == identities(), stage="seal-verifier")
    result = evidence.replay(bundle, manifest, trusted, campaign, check_claim=False, require_pass=False)
    write_json_atomic(bundle / "results.json", result)
    manifest["files"] = {p.relative_to(bundle).as_posix(): sha256(p)
                         for p in sorted(bundle.rglob("*")) if p.is_file()}
    write_json_atomic(bundle / "manifest.json", manifest)
    return {"manifestDigest": sha256(bundle / "manifest.json"), "evidenceId": manifest["evidenceId"],
            "signatureValid": False, "acceptanceCredit": False}

def main():
    import signal
    def interrupted(_signal, _frame):
        raise KeyboardInterrupt()
    signal.signal(signal.SIGTERM, interrupted)
    parser = argparse.ArgumentParser()
    sub = parser.add_subparsers(dest="command", required=True)
    p = sub.add_parser("preflight")
    p.add_argument("--output", type=Path, required=True)
    p.add_argument("--base-sha")
    p.add_argument("--mode", choices=("LOCAL_DIAGNOSTIC", "LOCAL_ACCEPTANCE"), default="LOCAL_DIAGNOSTIC")
    for kind, choices in (("api", ("fast", "regression", "diagnostic")), ("db", ("structural", "duration", "announcement")),
                          ("acceptance", ("all",)), ("runtime-preflight", ("api", "db", "all"))):
        p = sub.add_parser(kind)
        p.add_argument("profile", choices=choices)
        p.add_argument("--source-sha", required=True)
        p.add_argument("--base-sha", required=True)
        p.add_argument("--id", required=True)
        p.add_argument("--output-root", type=Path, required=True)
        p.add_argument("--campaign-id")
        p.add_argument("--db-runtime", choices=("source", "production"), default="source")
    p = sub.add_parser("seal")
    p.add_argument("--output", type=Path, required=True)
    p.add_argument("--signer", required=True)
    p = sub.add_parser("sign")
    p.add_argument("--bundle", type=Path, required=True)
    p.add_argument("--key", type=Path, required=True)
    for kind in ("verify", "replay"):
        p = sub.add_parser(kind)
        p.add_argument("--bundle", type=Path, required=True)
        p.add_argument("--expected-sha", required=True)
        p.add_argument("--expected-base", required=True)
        p.add_argument("--expected-tree", required=True)
        p.add_argument("--pr-number", type=int, required=True)
        p.add_argument("--output", type=Path, required=True)
    p = sub.add_parser("audit-workflows")
    p.add_argument("--require-migrated", action="store_true")
    p.add_argument("--output", type=Path)
    args = parser.parse_args()
    try:
        if args.command == "preflight":
            result = preflight(args.output, base=args.base_sha)
            if args.mode == "LOCAL_ACCEPTANCE":
                trusted = evidence.policy()
                result["acceptanceBlockers"] = [
                    code for code, key in (("SIGNER_UNAVAILABLE", "allowedSigners"),
                    ("ENVIRONMENT_INCOMPATIBLE", "environmentApprovals"),
                    ("BASELINE_UNAVAILABLE", "baselineEnrollments"), ("CAMPAIGN_UNAVAILABLE", "campaigns"))
                    if not trusted[key]]
                write_json_atomic(args.output / "host-preflight.json", result)
            print(json.dumps(result, sort_keys=True))
            return 0 if result["readyForRuntimePreflight"] and not result.get("acceptanceBlockers") else 2
        if args.command in ("api", "db", "acceptance", "runtime-preflight"):
            from local_runner import collect
            evidence.require(re.fullmatch(r"[a-z0-9][a-z0-9-]{0,79}", args.id) is not None, stage="identity")
            evidence.require(all(__import__("local_support").SHA.fullmatch(s) for s in
                             (args.source_sha, args.base_sha)), stage="source")
            output = args.output_root.resolve() / args.id
            # A directory cannot be reused even after setup failure. Changing an ID
            # never authorizes a new acceptance campaign.
            output.mkdir(parents=True, exist_ok=False)
            mode = "LOCAL_ACCEPTANCE" if args.command == "acceptance" else "LOCAL_DIAGNOSTIC"
            scope = ({"diagnostic": "api-diagnostic"}.get(args.profile, args.profile) if args.command == "api"
                     else "announcement" if args.profile == "announcement"
                     else "regression" if args.command == "runtime-preflight" and args.profile == "api"
                     else "all" if args.command == "acceptance" or (args.command == "runtime-preflight" and args.profile == "all") else "db")
            result = collect(output, source_sha=args.source_sha, base_sha=args.base_sha,
                             evidence_id=args.id, scope=scope, mode=mode, campaign_id=args.campaign_id,
                             preflight_only=args.command == "runtime-preflight", db_runtime=args.db_runtime)
            print(json.dumps(result, sort_keys=True))
            return 1 if result.get("collectorFailures") else 0
        if args.command == "seal":
            seal(args.output, args.signer)
            print('{"stage":"sealed","acceptanceCredit":false}')
            return 0
        if args.command == "sign":
            evidence.require(not os.environ.get("GITHUB_ACTIONS"), stage="separate-signer")
            evidence.require(not args.key.resolve().is_relative_to(ROOT.resolve()) and
                             not any((p / ".git").exists() for p in args.key.resolve().parents),
                             stage="private-key-boundary")
            manifest = load_json(args.bundle / "manifest.json")
            evidence.require(manifest["mode"] == "LOCAL_ACCEPTANCE" and
                             not (args.bundle / "manifest.json.sig").exists(), stage="immutable-signature")
            trusted = evidence.policy()
            # Invoke the external signing tool only. This process never reads,
            # generates, uploads, or copies the private key.
            invoke(["ssh-keygen", "-Y", "sign", "-f", str(args.key.resolve()), "-n",
                    trusted["signatureNamespace"], str((args.bundle / "manifest.json").resolve())], timeout=120)
            evidence.signature(args.bundle, manifest, trusted, __import__("datetime").datetime.now(__import__("datetime").timezone.utc))
            print(json.dumps({"signatureCreated": True, "acceptanceCredit": False}))
            return 0
        if args.command == "replay" and (args.bundle / "diagnostic-manifest.json").is_file():
            manifest = load_json(args.bundle / "diagnostic-manifest.json")
            evidence.require(manifest["mode"] == "LOCAL_DIAGNOSTIC" and manifest["acceptanceCredit"] is False and
                             manifest["baselineCredit"] is False and manifest["sourceSha"] == args.expected_sha and
                             manifest["baseSha"] == args.expected_base and manifest["treeSha"] == args.expected_tree,
                             stage="diagnostic-source")
            actual = {p.relative_to(args.bundle).as_posix() for p in args.bundle.rglob("*") if p.is_file()}
            evidence.require(actual == set(manifest["files"]) | {"diagnostic-manifest.json"},
                             stage="diagnostic-inventory")
            for name, checksum in manifest["files"].items():
                evidence.require(sha256(evidence.safe_path(args.bundle, name)) == checksum, stage="diagnostic-digest")
            result = {"mode": "LOCAL_DIAGNOSTIC", "acceptanceCredit": False, "signatureValid": False,
                      "integrityValid": True, "complete": manifest["complete"],
                      "result": load_json(args.bundle / "diagnostic-result.json")}
            write_json_atomic(args.output, result)
            print(json.dumps({"mode": "LOCAL_DIAGNOSTIC", "integrityValid": True, "acceptanceCredit": False}))
            return 0
        if args.command in ("verify", "replay"):
            result = evidence.verify(args.bundle, expected_sha=args.expected_sha, expected_base=args.expected_base,
                                     expected_tree=args.expected_tree, pr_number=args.pr_number)
            write_json_atomic(args.output, result)
            print(json.dumps({k: v for k, v in result.items() if k != "result"}, sort_keys=True))
            return 0
        result = audit_workflows()
        if args.output:
            write_json_atomic(args.output, result)
        print(json.dumps(result, sort_keys=True))
        return 1 if args.require_migrated and not result["migrationComplete"] else 0
    except Exception as error:
        report = {"decision": "blocking", "reasonCode": getattr(error, "code", "EVIDENCE_INVALID"),
                  "acceptanceCredit": False, **failure(error, component="performance-local", operation=args.command)}
        if args.command in ("verify", "replay"):
            write_json_atomic(args.output, report)
        if args.command in ("api", "db", "acceptance", "runtime-preflight") and "output" in locals() and output.is_dir():
            write_json_atomic(output / "failure.json", report)
        print(json.dumps(report, sort_keys=True))
        return 2

if __name__ == "__main__":
    raise SystemExit(main())
