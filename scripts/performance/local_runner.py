"""Bounded local orchestration with a separate non-measuring runtime preflight."""
from __future__ import annotations
import os
import platform
import secrets
import sys
import tempfile
from pathlib import Path
import api_k6
from common import write_json_atomic
from local_support import ROOT, LocalError, digest, failure, identities, invoke, load_json, now, preflight, sha256
from local_evidence import policy, require, db_compare

def local_environment(mode):
    require(not os.environ.get("GITHUB_ACTIONS") and not os.environ.get("RUNNER_ENVIRONMENT"),
            "LOCAL_EXECUTION_REQUIRED", "host")
    require(platform.system() == "Linux", "LINUX_DOCKER_REQUIRED", "host")
    require(platform.freedesktop_os_release().get("ID") == "ubuntu",
            "ENVIRONMENT_INCOMPATIBLE", "actual-os-family")
    env = dict(os.environ)
    # No RUNNER_* or GitHub-hosted identity is supplied to local measurements.
    for key in list(env):
        if key.startswith(("GITHUB_", "RUNNER_", "COGLATAS_DIAGNOSTIC_")) or key == "ImageOS":
            env.pop(key)
    env["COGLATAS_LOCAL_EVIDENCE_MODE"] = mode
    env["COGLATAS_PERFORMANCE_PASSWORD"] = "LocalPerf-" + secrets.token_urlsafe(24)
    return env

def stack(source, folder, *, profile, runtime, env, command, token, db=False, diagnostics=False):
    folder.mkdir(parents=True, exist_ok=False)
    configured = dict(env,
        COGLATAS_PERFORMANCE_TARGET_SHA=invoke(["git", "rev-parse", "HEAD"], cwd=source),
        COGLATAS_PERFORMANCE_RUNTIME_MODE=runtime,
        COGLATAS_PERFORMANCE_PROFILE=profile,
        COGLATAS_PERFORMANCE_COMPOSE_PROJECT="coglatas-performance-local-" + token,
        COGLATAS_PERFORMANCE_EVIDENCE_DIR=str(folder),
        COGLATAS_PERFORMANCE_DB_CAPTURE_ENABLED="true" if db else "false",
        COGLATAS_PERFORMANCE_API_DIAGNOSTICS_ENABLED="true" if diagnostics else "false")
    try:
        invoke(["bash", str(source / "scripts/performance/with-environment.sh"), *command],
               cwd=source, env=configured, timeout=2100, new_session=True)
    except LocalError as error:
        error.stage = "isolated-stack-or-collector"
        raise
    finally:
        # Scope teardown to this invocation's generated project, even if the
        # shell's own cleanup trap could not complete after a timeout.
        command = ["docker", "compose", "-p", configured["COGLATAS_PERFORMANCE_COMPOSE_PROJECT"],
                   "-f", str(source / "infra/compose/performance/environment.yml")]
        if runtime == "source":
            command += ["-f", str(source / "infra/compose/performance/pr.yml")]
        try:
            invoke(command + ["down", "--volumes", "--remove-orphans"], cwd=source, env=configured, timeout=120)
        except LocalError as error:
            write_json_atomic(folder / "cleanup-failure.json",
                              {"reasonCode": "CLEANUP_FAILED", "project": configured["COGLATAS_PERFORMANCE_COMPOSE_PROJECT"]})
            raise LocalError("CLEANUP_FAILED", "cleanup") from error
    return load_json(folder / "environment.json")

def sample_count(folder):
    try:
        raw = load_json(folder / "raw-samples.json")
        return len(raw["samples"])
    except Exception:
        try:
            raw = load_json(folder / "samples.json")
            return sum(len(s["samples"]) for s in raw["scenarios"])
        except Exception:
            return 0

def collect(output, *, source_sha, base_sha, evidence_id, scope, mode, campaign_id=None, preflight_only=False, db_runtime="source"):
    require(scope in ("fast", "regression", "api-diagnostic", "db", "announcement", "all"), stage="command")
    root_head = invoke(["git", "rev-parse", "HEAD"])
    host = preflight(output / "preflight", base=base_sha)
    require(host["readyForRuntimePreflight"], "PREFLIGHT_FAILED", "host-preflight")
    env = local_environment(mode)
    trusted = policy()
    pinned = api_k6.load_contract()["baseline"]["sha"]
    invoke(["git", "merge-base", "--is-ancestor", pinned, base_sha])
    invoke(["git", "merge-base", "--is-ancestor", base_sha, source_sha])
    require(pinned != source_sha, "SOURCE_INVALID", "baseline")
    campaign = None
    if mode == "LOCAL_ACCEPTANCE":
        require(scope == "all" and not preflight_only, "COMPLETE_ACCEPTANCE_REQUIRED", "campaign")
        matches = [c for c in trusted["campaigns"] if c["id"] == campaign_id]
        require(len(matches) == 1, "CAMPAIGN_UNAVAILABLE", "campaign")
        campaign = matches[0]
        require(campaign["candidateSha"] == source_sha and campaign["baseSha"] == base_sha and
                campaign["baselineSha"] == pinned and campaign["evidenceId"] == evidence_id and
                campaign["contractAndToolDigests"] == identities(), stage="campaign")
    declaration = {"schemaVersion": 1, "evidenceId": evidence_id, "mode": mode, "scope": scope,
                   "candidateSha": source_sha, "baseSha": base_sha, "baselineSha": pinned,
                   "campaignId": campaign_id, "declaredAtUtc": now(), "attempt": 1,
                   "previousEvidenceIds": [], "retainAllSamples": True,
                   "acceptanceCredit": False, "historicalReplacement": False,
                   "verifierSourceSha": root_head, "contractAndToolDigests": identities()}
    write_json_atomic(output / "declaration.json", declaration)
    public_preflight = {}
    bundle = output / "bundle"
    bundle.mkdir()
    k6_id = None
    if scope in ("fast", "regression", "api-diagnostic", "all"):
        k6_id = invoke(["docker", "image", "inspect", "--format", "{{.Id}}", api_k6.load_contract()["k6Image"]])
        version = invoke(["docker", "run", "--rm", api_k6.load_contract()["k6Image"], "version"], timeout=120)
        require("k6 v" + api_k6.load_contract()["k6Version"] + " " in version, stage="k6-version")
        env["COGLATAS_LOCAL_K6_IMAGE_ID"] = k6_id
    if campaign:
        require(campaign["k6ImageId"] == k6_id, "ENVIRONMENT_INCOMPATIBLE", "preflight-k6-image")
    begun = False
    created = []
    try:
        with tempfile.TemporaryDirectory(prefix="coglatas-local-sources-") as temporary:
            sources = {}
            for side, sha in (("current", source_sha), ("baseline", pinned)):
                location = Path(temporary) / side
                invoke(["git", "worktree", "add", "--detach", str(location), sha])
                created.append(location)
                sources[side] = location
            tree = invoke(["git", "rev-parse", source_sha + "^{tree}"])
            jobs = []
            if scope in ("fast", "regression", "api-diagnostic", "all"):
                jobs.append(("api", "current", "small", "source", False))
                if scope != "fast":
                    jobs.append(("api", "baseline", "small", "source", False))
            if scope in ("db", "all", "announcement"):
                runtime = "production" if mode == "LOCAL_ACCEPTANCE" else db_runtime
                for profile in (("medium",) if scope == "announcement" else ("small", "medium")):
                    jobs.append(("db-" + profile, "current", profile, runtime, True))
            observed = {}
            for name, side, profile, runtime, db in jobs:
                folder = output / "preflight" / (name + "-" + side)
                fp = stack(sources[side], folder, profile=profile, runtime=runtime, env=env,
                           command=[sys.executable, str(ROOT / "scripts/performance/local-runtime-preflight.py")],
                           token=secrets.token_hex(10), db=db)
                require(load_json(folder / "runtime-preflight.json")["measuredSamples"] == 0,
                        "PREFLIGHT_FAILED", "runtime-preflight")
                observed[name + "-" + side] = fp
                public_preflight[name + "-" + side] = load_json(folder / "runtime-preflight.json")
            if preflight_only:
                proposal = {"schemaVersion": 1, "mode": "LOCAL_DIAGNOSTIC", "sourceSha": source_sha,
                            "treeSha": tree, "baseSha": base_sha, "hardware": host["hardware"],
                            "runtimeFingerprints": observed, "runtimePreflight": public_preflight,
                            "k6ImageId": k6_id, "measurementStarted": False,
                            "measurementAttemptsConsumed": 0, "acceptanceCredit": False,
                            "baselineStatus": "BASELINE_UNAVAILABLE"}
                write_json_atomic(output / "runtime-preflight-proposal.json", proposal)
                return proposal
            if mode == "LOCAL_ACCEPTANCE":
                require(__import__("local_support").utc(campaign["declaredAtUtc"]) <=
                        __import__("local_support").utc(declaration["declaredAtUtc"]) <
                        __import__("local_support").utc(campaign["expiresAtUtc"]), stage="campaign-time")
                approvals = {e["digest"] for e in trusted["environmentApprovals"]}
                baseline_ids = {e["id"] for e in trusted["baselineEnrollments"]}
                require(set(campaign["baselineEnrollmentIds"]) <= baseline_ids and
                        len(campaign["baselineEnrollmentIds"]) == 3, "BASELINE_UNAVAILABLE", "baseline")
                for name in ("api", "db-small", "db-medium"):
                    key = __import__("environment_class").digest(observed[name + "-current"]["environmentClass"])
                    require(key in approvals and campaign["environmentDigests"][name] == key,
                            "ENVIRONMENT_INCOMPATIBLE", "preflight-environment")
                stable = {k: v for k, v in host["hardware"].items() if k != "availableMemoryBytes"}
                require(digest(stable) == campaign["hardwareDigest"], "ENVIRONMENT_INCOMPATIBLE", "physical-hardware")
            # This exclusive marker is created only after every independent preflight passes.
            # A collector failure never removes it or makes the campaign reusable.
            marker = output / "measurement-started.json"
            with marker.open("x", encoding="utf-8") as handle:
                __import__("json").dump({"evidenceId": evidence_id, "startedAtUtc": now(),
                                        "attempt": 1, "retries": 0}, handle)
            begun = True
            started = now()
            write_json_atomic(bundle / "runtime-preflight.json", public_preflight)
            order, failures = [], []
            for group in range(1, 2 if scope == "fast" else 6) if scope in ("fast", "regression", "api-diagnostic", "all") else ():
                for side in (("current",) if scope == "fast" else ("baseline", "current")):
                    relative = "api/" + side + "-" + str(group)
                    folder = bundle / relative
                    runtime_out = output / "runtime" / (side + "-" + str(group))
                    runtime_env = dict(env, COGLATAS_PERFORMANCE_TRIAL_ORDINAL=str(group))
                    status = 0
                    try:
                        folder.mkdir(parents=True)
                        fp = stack(sources[side], runtime_out, profile="small", runtime="source",
                                   env=runtime_env, token=secrets.token_hex(10),
                                   diagnostics=scope == "api-diagnostic" and side == "current",
                                   command=[sys.executable, str(ROOT / "scripts/performance/api_k6.py"),
                                            "collect", "--output", str(folder / "sample.json")])
                        reference = observed["api-" + side]
                        require(fp["containerImages"] == reference["containerImages"] and
                                fp.get("environmentClass") == reference.get("environmentClass"),
                                "ENVIRONMENT_INCOMPATIBLE", "runtime-drift")
                    except Exception as error:
                        status = getattr(error, "exit_status", 2) or 2
                        record = (load_json(folder / "collection-failure.json") if (folder / "collection-failure.json").is_file()
                                  else failure(error, component="api", operation=relative, sample_count=sample_count(folder)))
                        write_json_atomic(folder / "failure.json", record)
                        failures.append(record)
                    write_json_atomic(folder / "status.json", {"group": group, "side": side, "exitStatus": status})
                    order.append({"group": group, "side": side})
            for name, side, profile, runtime, db in jobs:
                if not db:
                    continue
                folder = bundle / "db" / profile
                runtime_out = output / "runtime" / ("db-" + profile)
                folder.mkdir(parents=True)
                try:
                    if scope == "announcement":
                        configured = dict(env, COGLATAS_LOCAL_ANNOUNCEMENT_OUTPUT=str(folder),
                                          COGLATAS_LOCAL_PRODUCT_SOURCE_SHA=source_sha)
                        command = [sys.executable, str(ROOT / "scripts/performance/local-announcement-diagnostic.py")]
                    else:
                        configured = env
                        command = [sys.executable, str(ROOT / "scripts/performance/db-probe.py"),
                                   "--profile", profile, "--output", str(folder / "samples.json")]
                    fp = stack(sources[side], runtime_out, profile=profile, runtime=runtime, env=configured,
                               token=secrets.token_hex(10), db=True, diagnostics=scope == "announcement",
                               command=command)
                    require(fp["containerImages"] == observed[name + "-" + side]["containerImages"] and
                            fp.get("environmentClass") == observed[name + "-" + side].get("environmentClass"),
                            "ENVIRONMENT_INCOMPATIBLE", "runtime-drift")
                    write_json_atomic(folder / "environment.json", fp)
                except Exception as error:
                    record = failure(error, component="db", operation=name, sample_count=sample_count(folder))
                    write_json_atomic(folder / "failure.json", record)
                    failures.append(record)
            result = {"mode": mode, "acceptanceCredit": False, "collectorFailures": failures,
                      "historicalReplacement": False, "baselineStatus": "BASELINE_UNAVAILABLE"}
            if not failures and scope in ("fast", "regression", "api-diagnostic", "all"):
                try:
                    current = [load_json(bundle / "api" / ("current-" + str(i)) / "sample.json")
                               for i in range(1, 2 if scope == "fast" else 6)]
                    baseline = [] if scope == "fast" else [
                        load_json(bundle / "api" / ("baseline-" + str(i)) / "sample.json") for i in range(1, 6)]
                    result["apiDiagnosticReplay"] = api_k6.evaluate(current, baseline, "fast" if scope == "fast" else "regression")
                except Exception as error:
                    result["apiReplayFailure"] = failure(error, component="comparator", operation="api-replay")
            if not failures and scope in ("db", "all"):
                result["dbStructuralDiagnosticReplay"] = db_compare.evaluate(
                    load_json(bundle / "db/small/samples.json"), load_json(bundle / "db/medium/samples.json"),
                    load_json(ROOT / "performance/db-scenarios.json"), source_sha)
            write_json_atomic(bundle / "diagnostic-result.json", result)
            completion = {"schemaVersion": 1, "mode": mode, "sourceSha": source_sha, "baseSha": base_sha,
                          "treeSha": tree, "baselineSha": pinned, "startedAtUtc": started, "endedAtUtc": now(),
                          "allCollectorsComplete": not failures, "acceptanceCredit": False,
                          "groupOrder": order, "hardware": host["hardware"], "k6ImageId": k6_id,
                          "preflightComplete": True, "attempt": 1, "previousEvidenceIds": []}
            write_json_atomic(bundle / "completion.json", completion)
            if mode == "LOCAL_DIAGNOSTIC":
                write_json_atomic(bundle / "diagnostic-manifest.json", {
                    "schemaVersion": 1, "mode": mode, "sourceSha": source_sha,
                    "baseSha": base_sha, "treeSha": tree, "baselineSha": pinned,
                    "evidenceId": evidence_id, "complete": not failures,
                    "contractAndToolDigests": identities(), "acceptanceCredit": False,
                    "baselineCredit": False, "historicalReplacement": False,
                    "files": {p.relative_to(bundle).as_posix(): sha256(p)
                              for p in sorted(bundle.rglob("*")) if p.is_file()}})
            # Acceptance sealing is separate from collection and requires owner-enrolled baselines.
            return result
    except Exception as error:
        write_json_atomic(output / "failure.json", {
            **failure(error, component="local-runner", operation="collection" if begun else "preflight"),
            "measurementStarted": begun, "measurementAttemptsConsumed": int(begun)})
        raise
    finally:
        for location in created:
            invoke(["git", "worktree", "remove", "--force", str(location)])
