"""Authenticate local bundles and independently replay the unchanged comparators."""
from __future__ import annotations
import base64
import datetime as dt
import importlib.util
import json
import re
import tempfile
from pathlib import Path
import api_k6
from compare import compare_documents
from db_gate import validate_capture
from environment_class import environment_class, digest as class_digest
from diagnostic_json import load_json
from local_support import ROOT, SHA, DIGEST, LocalError, digest, identities, invoke, sha256, utc

spec = importlib.util.spec_from_file_location("local_db_compare", ROOT / "scripts/performance/db-compare.py")
db_compare = importlib.util.module_from_spec(spec)
spec.loader.exec_module(db_compare)

MANIFEST_FIELDS = {
    "schemaVersion", "evidenceId", "mode", "repository", "prNumber", "candidateSha",
    "baseSha", "treeSha", "baselineSha", "campaignId", "signerIdentity",
    "startedAtUtc", "endedAtUtc", "expiresAtUtc", "hardware", "runtimeFingerprints",
    "contractAndToolDigests", "baselineEnrollmentIds", "groupOrder", "files",
    "complete", "attempt", "previousEvidenceIds", "k6ImageId",
}
CAMPAIGN_FIELDS = {
    "id", "evidenceId", "repository", "prNumber", "candidateSha", "baseSha", "treeSha",
    "baselineSha", "declaredAtUtc", "expiresAtUtc", "contractAndToolDigests",
    "environmentDigests", "hardwareDigest", "baselineEnrollmentIds", "k6ImageId", "approval",
}
POLICY_FIELDS = {
    "schemaVersion", "phase", "repository", "signatureNamespace", "allowedSigners",
    "environmentApprovals", "baselineEnrollments", "campaigns", "revokedEvidenceIds", "maximumBundleBytes",
}

def require(condition, code="EVIDENCE_INVALID", stage="integrity"):
    if not condition:
        raise LocalError(code, stage)

def unique(values, key):
    selected = [v[key] for v in values]
    require(len(selected) == len(set(selected)), stage="policy")

def approval(value):
    require(isinstance(value, dict) and set(value) == {"approver", "reference", "approvedAtUtc"},
            stage="policy")
    require(value["approver"] == "NYGsatoshi" and
            re.fullmatch(r"https://github.com/NYGsatoshi/Coglatas/(?:pull|issues)/[0-9]+(?:#[a-z0-9-]+)?",
                         value["reference"]) is not None, stage="policy")
    utc(value["approvedAtUtc"])

def policy(root=ROOT):
    value = load_json(root / "performance/local-policy.json")
    require(set(value) == POLICY_FIELDS and value["schemaVersion"] == 1 and
            value["phase"] == "staged" and value["repository"] == "NYGsatoshi/Coglatas" and
            value["signatureNamespace"] == "coglatas-local-performance-v1" and
            type(value["maximumBundleBytes"]) is int and 0 < value["maximumBundleBytes"] <= 134217728,
            stage="policy")
    for key in ("allowedSigners", "environmentApprovals", "baselineEnrollments", "campaigns", "revokedEvidenceIds"):
        require(isinstance(value[key], list), stage="policy")
    for signer in value["allowedSigners"]:
        require(set(signer) == {"identity", "publicKey", "validAfterUtc", "validBeforeUtc", "revoked", "approval"},
                stage="policy")
        require(re.fullmatch(r"[a-z0-9][a-z0-9._-]{0,63}", signer["identity"]) is not None and
                type(signer["revoked"]) is bool, stage="policy")
        require(re.fullmatch(r"ssh-ed25519 [A-Za-z0-9+/]+={0,2}", signer["publicKey"]) is not None, stage="policy")
        raw = base64.b64decode(signer["publicKey"].split()[1], validate=True)
        require(len(raw) == 51 and raw.startswith(bytes.fromhex("0000000b") + b"ssh-ed25519"),
                stage="policy")
        require(utc(signer["validAfterUtc"]) < utc(signer["validBeforeUtc"]), stage="policy")
        approval(signer["approval"])
    unique(value["allowedSigners"], "identity")
    for entry in value["environmentApprovals"]:
        require(set(entry) == {"digest", "environmentClass", "approval"}, stage="policy")
        require(entry["environmentClass"]["provider"] == "local" and
                class_digest(entry["environmentClass"]) == entry["digest"], stage="policy")
        __import__("environment_class").validate_class(entry["environmentClass"])
        approval(entry["approval"])
    unique(value["environmentApprovals"], "digest")
    for entry in value["baselineEnrollments"]:
        require(set(entry) == {"id", "kind", "profile", "environmentDigest", "baselineSha", "files", "approval"},
                stage="policy")
        require(entry["kind"] in ("api", "db") and SHA.fullmatch(entry["baselineSha"]) is not None and
                DIGEST.fullmatch(entry["environmentDigest"]) is not None, stage="policy")
        require(isinstance(entry["files"], dict), stage="policy")
        if entry["kind"] == "api":
            require(entry["profile"] == "small" and not entry["files"], stage="policy")
        else:
            expected = {s["id"] for s in load_json(root / "performance/db-scenarios.json")["scenarios"]}
            require(entry["profile"] in ("small", "medium") and len(entry["files"]) == 9, stage="policy")
            directory = "performance/baselines/db/local/" + entry["profile"] + "/" + entry["environmentDigest"] + "/"
            require(set(entry["files"]) == {directory + s + ".json" for s in expected}, stage="policy")
            for name, checksum in entry["files"].items():
                require(DIGEST.fullmatch(checksum) is not None and sha256(root / name) == checksum,
                        "BASELINE_UNAVAILABLE", "baseline")
        approval(entry["approval"])
    unique(value["baselineEnrollments"], "id")
    for campaign in value["campaigns"]:
        require(set(campaign) == CAMPAIGN_FIELDS and
                campaign["repository"] == value["repository"], stage="policy")
        require(all(SHA.fullmatch(campaign[k]) for k in ("candidateSha", "baseSha", "treeSha", "baselineSha")),
                stage="policy")
        require(utc(campaign["declaredAtUtc"]) < utc(campaign["expiresAtUtc"]) and
                re.fullmatch(r"sha256:[0-9a-f]{64}", campaign["k6ImageId"]) is not None, stage="policy")
        require(set(campaign["environmentDigests"]) == {"api", "db-small", "db-medium"} and
                all(DIGEST.fullmatch(v) for v in campaign["environmentDigests"].values()), stage="policy")
        approval(campaign["approval"])
    unique(value["campaigns"], "id")
    unique(value["campaigns"], "evidenceId")
    return value

def safe_path(root, name):
    require(isinstance(name, str) and re.fullmatch(r"[a-zA-Z0-9_.\-/]+", name) is not None and
            all(p not in ("", ".", "..") for p in name.split("/")), stage="path")
    path = root / name
    require(not path.is_absolute() or path.is_relative_to(root), stage="path")
    require(path.resolve().is_relative_to(root.resolve()), stage="path")
    for node in (path, *path.parents):
        if node == root.parent:
            break
        require(not node.is_symlink() and not (hasattr(node, "is_junction") and node.is_junction()), stage="path")
    return path

def integrity(bundle, manifest, limits):
    require(set(manifest) == MANIFEST_FIELDS and manifest["schemaVersion"] == 1, stage="manifest")
    files = manifest["files"]
    require(isinstance(files, dict) and files and len(files) <= 2000, stage="inventory")
    actual = []
    for p in bundle.rglob("*"):
        require(not p.is_symlink() and not (hasattr(p, "is_junction") and p.is_junction()), stage="path")
        if p.is_file():
            actual.append(p.relative_to(bundle).as_posix())
    require(len({p.casefold() for p in actual}) == len(actual), stage="inventory")
    require(set(actual) == set(files) | {"manifest.json", "manifest.json.sig"}, stage="inventory")
    require(sum(safe_path(bundle, p).stat().st_size for p in actual) <= limits["maximumBundleBytes"],
            stage="bundle-size")
    for name, checksum in files.items():
        require(DIGEST.fullmatch(checksum) is not None and sha256(safe_path(bundle, name)) == checksum,
                stage="file-digest")

def signature(bundle, manifest, trusted, instant):
    matches = [s for s in trusted["allowedSigners"] if s["identity"] == manifest["signerIdentity"]]
    require(len(matches) == 1, stage="signer")
    signer = matches[0]
    require(not signer["revoked"] and utc(signer["validAfterUtc"]) <= utc(manifest["startedAtUtc"]) and
            instant < utc(signer["validBeforeUtc"]), stage="signer")
    with tempfile.TemporaryDirectory(prefix="coglatas-public-signers-") as directory:
        allowed = Path(directory) / "allowed_signers"
        allowed.write_text(signer["identity"] + " " + signer["publicKey"] + "\n", encoding="utf-8")
        try:
            invoke(["ssh-keygen", "-Y", "verify", "-f", str(allowed), "-I", signer["identity"],
                    "-n", trusted["signatureNamespace"], "-s", str(bundle / "manifest.json.sig")],
                   input_bytes=(bundle / "manifest.json").read_bytes())
        except LocalError as error:
            raise LocalError("EVIDENCE_INVALID", "signature") from error

def campaign_for(manifest, trusted):
    matches = [c for c in trusted["campaigns"] if c["id"] == manifest["campaignId"]]
    require(len(matches) == 1, "CAMPAIGN_UNAVAILABLE", "campaign")
    campaign = matches[0]
    for field in ("evidenceId", "repository", "prNumber", "candidateSha", "baseSha", "treeSha",
                  "baselineSha", "contractAndToolDigests", "baselineEnrollmentIds", "expiresAtUtc", "k6ImageId"):
        require(campaign[field] == manifest[field], stage="campaign")
    require(utc(campaign["approval"]["approvedAtUtc"]) <= utc(campaign["declaredAtUtc"]) <=
            utc(manifest["startedAtUtc"]) < utc(manifest["endedAtUtc"]), stage="campaign")
    require(manifest["attempt"] == 1 and manifest["previousEvidenceIds"] == [], stage="rerun")
    return campaign

def enrollment(trusted, kind, profile, key, ids):
    records = [e for e in trusted["baselineEnrollments"] if e["id"] in ids and
               e["kind"] == kind and e["profile"] == profile and e["environmentDigest"] == key]
    require(len(records) == 1, "BASELINE_UNAVAILABLE", "baseline")
    return records[0]

def environments(manifest, campaign, trusted):
    fps = manifest["runtimeFingerprints"]
    require(set(fps) == {"api", "db-small", "db-medium"}, stage="environment")
    hardware = manifest["hardware"]
    require(hardware.get("physicalMemoryBytes", 0) > 0 and hardware.get("logicalCpus", 0) > 0 and
            hardware.get("cpuModel"), "ENVIRONMENT_INCOMPATIBLE", "physical-hardware")
    stable = {k: v for k, v in hardware.items() if k != "availableMemoryBytes"}
    require(digest(stable) == campaign["hardwareDigest"], "ENVIRONMENT_INCOMPATIBLE", "physical-hardware")
    approved = {e["digest"]: e["environmentClass"] for e in trusted["environmentApprovals"]}
    for name, fp in fps.items():
        observed = environment_class(fp)
        key = class_digest(observed)
        require(observed["provider"] == "local" and campaign["environmentDigests"][name] == key and
                approved.get(key) == observed and fp["environmentClass"] == observed,
                "ENVIRONMENT_INCOMPATIBLE", "environment")
        require(fp["runner"]["cpuCount"] <= hardware["logicalCpus"] and
                fp["runner"]["memoryBytes"] <= hardware["physicalMemoryBytes"],
                "ENVIRONMENT_INCOMPATIBLE", "physical-hardware")
    return fps

def ordered_db(profile, contract):
    """Reconstruct duration streams from every ordered sanitized command capture."""
    selected = []
    require([s["id"] for s in profile["scenarios"]] == [s["id"] for s in contract["scenarios"]],
            stage="db-order")
    for scenario, record in zip(contract["scenarios"], profile["scenarios"]):
        sizes = contract["policy"]["pageSizes"] if scenario["paged"] else [0]
        order = [(size, iteration, page) for size in sizes for iteration in
                 range(1, contract["policy"]["samples"] + 1) for page in ((1, 2) if scenario["paged"] else (1,))]
        require([(s["pageSize"], s["iteration"], s["page"]) for s in record["samples"]] == order,
                stage="db-order")
        for sample in record["samples"]:
            validate_capture(sample["capture"])
        for size in sizes:
            samples = [s["capture"]["totalDurationMs"] for s in record["samples"]
                       if s["pageSize"] == size and s["page"] == 1]
            matches = [m for m in profile["measurements"] if m["scenario"] == scenario["id"]
                       and m["pageSize"] == size]
            require(len(matches) == 1 and matches[0]["samples"] == samples, stage="db-raw-replay")
            selected.append(matches[0])
    require(len(selected) == len(profile["measurements"]), stage="db-inventory")

def replay(bundle, manifest, trusted, campaign, *, check_claim=True, require_pass=True):
    contract = api_k6.load_contract()
    preflight = load_json(bundle / "runtime-preflight.json")
    require(set(preflight) == {"api-current", "api-baseline", "db-small-current", "db-medium-current"},
            stage="runtime-preflight")
    for name, item in preflight.items():
        require(item["phase"] == "runtime-preflight" and item["measuredSamples"] == 0 and
                all(item[k] is True for k in ("readiness", "authentication", "volumeIsolation", "networkIsolation")),
                stage="runtime-preflight")
        if name.startswith("db-"):
            require(item["queryCaptureVerified"] is True, stage="runtime-preflight")
        require(item["sourceSha"] == (manifest["baselineSha"] if name == "api-baseline" else manifest["candidateSha"]),
                stage="runtime-preflight-source")
    complete = load_json(bundle / "completion.json")
    require(complete["mode"] == "LOCAL_ACCEPTANCE" and complete["allCollectorsComplete"] is True and
            complete["preflightComplete"] is True and complete["attempt"] == 1 and
            complete["previousEvidenceIds"] == [] and complete["k6ImageId"] == manifest["k6ImageId"] and complete["sourceSha"] == manifest["candidateSha"] and
            complete["baseSha"] == manifest["baseSha"] and complete["treeSha"] == manifest["treeSha"] and
            complete["startedAtUtc"] == manifest["startedAtUtc"] and complete["endedAtUtc"] == manifest["endedAtUtc"],
            stage="completion")
    require(manifest["baselineSha"] == contract["baseline"]["sha"], "BASELINE_UNAVAILABLE", "api-baseline")
    expected_order = [{"group": i, "side": side} for i in range(1, 6) for side in ("baseline", "current")]
    require(manifest["groupOrder"] == expected_order, stage="api-order")
    current, baseline = [], []
    fps = environments(manifest, campaign, trusted)
    for side, array in (("current", current), ("baseline", baseline)):
        for ordinal in range(1, 6):
            folder = bundle / "api" / (side + "-" + str(ordinal))
            run = load_json(folder / "sample.json")
            raw = load_json(folder / "raw-summary.json")
            from local_api_samples import summary
            samples = load_json(folder / "raw-samples.json")
            require(raw == summary(samples, contract, auth_failures=raw["authFailures"],
                                   health_failures=raw["healthFailures"]), stage="api-scalar-replay")
            require(run["measurements"] == api_k6.normalize(raw, contract), stage="api-raw-replay")
            require(run["k6ImageId"] == complete["k6ImageId"] and
                    re.fullmatch(r"sha256:[0-9a-f]{64}", run["k6ImageId"]) is not None, stage="k6-image")
            require(run["headSha"] == (manifest["candidateSha"] if side == "current" else manifest["baselineSha"]),
                    stage="api-source")
            require(environment_class(run["fingerprint"]) == environment_class(fps["api"]), stage="api-environment")
            require(run["fingerprint"]["runner"]["provider"] == "local", stage="api-provider")
            status = load_json(folder / "status.json")
            require(status == {"group": ordinal, "side": side, "exitStatus": 0}, stage="api-status")
            array.append(run)
    api_entry = enrollment(trusted, "api", "small", campaign["environmentDigests"]["api"],
                           manifest["baselineEnrollmentIds"])
    require(api_entry["baselineSha"] == manifest["baselineSha"], "BASELINE_UNAVAILABLE", "api-baseline")
    api = api_k6.evaluate(current, baseline, "regression")
    require(len(api["results"]) == 78, stage="api-inventory")
    db_contract = load_json(ROOT / "performance/db-scenarios.json")
    profiles = {name: load_json(bundle / "db" / name / "samples.json") for name in ("small", "medium")}
    for name, profile in profiles.items():
        ordered_db(profile, db_contract)
        require(profile["headSha"] == manifest["candidateSha"], stage="db-source")
    structural = db_compare.evaluate(profiles["small"], profiles["medium"], db_contract, manifest["candidateSha"])
    require(len(structural["results"]) == 28, stage="db-inventory")
    durations = {}
    for name, profile in profiles.items():
        fp = load_json(bundle / "db" / name / "environment.json")
        require(fp == fps["db-" + name] and fp["commitSha"] == manifest["candidateSha"] and
                fp["fixture"]["hash"] == profile["fixtureHash"], stage="db-environment")
        entry = enrollment(trusted, "db", name, campaign["environmentDigests"]["db-" + name],
                           manifest["baselineEnrollmentIds"])
        docs = {Path(p).stem: load_json(ROOT / p) for p in entry["files"]}
        results = []
        for m in profile["measurements"]:
            if m["pageSize"] not in (0, 5):
                continue
            b = docs[m["scenario"]]
            require(b["approved"] is True and b["baselineSha"] == entry["baselineSha"] and
                    b["baselineSha"] != manifest["candidateSha"], "BASELINE_UNAVAILABLE", "baseline")
            results.append(compare_documents(m, b, fp, load_json(ROOT / "performance/scenarios.json"),
                           load_json(ROOT / "performance/budgets.json"), load_json(ROOT / "performance/environment.json"),
                           load_json(ROOT / "performance/comparison-policy.json")))
        require(len(results) == 9, stage="db-duration-inventory")
        durations[name] = results
    result = {"api": api, "structural": structural, "durations": durations}
    if check_claim:
        require(result == load_json(bundle / "results.json"), stage="decision-replay")
    require(not require_pass or (api["decision"] == "pass" and structural["decision"] == "pass" and
            all(r["decision"] == "pass" for rows in durations.values() for r in rows)),
            "PERFORMANCE_REJECTED", "statistics")
    return result

def verify(bundle, *, expected_sha, expected_base, expected_tree, pr_number, root=ROOT, instant=None):
    bundle = Path(bundle)
    require(bundle.is_dir() and (bundle / "manifest.json").is_file(), "EVIDENCE_MISSING", "bundle")
    trusted = policy(root)
    require((bundle / "manifest.json").stat().st_size <= 1048576, stage="manifest-size")
    manifest = load_json(bundle / "manifest.json")
    instant = instant or dt.datetime.now(dt.timezone.utc)
    integrity(bundle, manifest, trusted)
    require(manifest["mode"] == "LOCAL_ACCEPTANCE" and manifest["complete"] is True, stage="mode")
    require(manifest["repository"] == trusted["repository"] and manifest["candidateSha"] == expected_sha and
            manifest["baseSha"] == expected_base and manifest["treeSha"] == expected_tree and
            manifest["prNumber"] == pr_number, stage="source")
    require(manifest["evidenceId"] not in trusted["revokedEvidenceIds"] and
            utc(manifest["endedAtUtc"]) <= instant < utc(manifest["expiresAtUtc"]), stage="expiry")
    signature(bundle, manifest, trusted, instant)
    campaign = campaign_for(manifest, trusted)
    require(manifest["contractAndToolDigests"] == identities(root), stage="trusted-verifier")
    result = replay(bundle, manifest, trusted, campaign)
    return {"decision": "pass", "evidenceId": manifest["evidenceId"], "candidateSha": expected_sha,
            "signatureValid": True, "bundleDigest": digest({"manifest": sha256(bundle / "manifest.json"),
              "signature": sha256(bundle / "manifest.json.sig"), "files": manifest["files"]}),
            "requiredCheckCredit": False,
            "counts": {"api": 78, "structural": 28, "small": 9, "medium": 9}, "result": result}
