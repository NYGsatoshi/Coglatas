"""Allowlist every scalar k6 custom point; request/response data stays private."""
import math
import re
from pathlib import Path
from diagnostic_json import decode_json
from common import PerformanceContractError
from compare import summarize

SUFFIXES = ("requests", "errors", "timeouts", "latency", "microseconds")

def read_samples(path, contract):
    path = Path(path)
    if path.stat().st_size > 64 * 1024 * 1024:
        raise PerformanceContractError("local scalar stream is too large")
    metrics = {}
    for scenario in contract["scenarios"]:
        key = re.sub(r"[^a-zA-Z0-9]", "_", scenario["id"])
        for suffix in SUFFIXES:
            metrics["perf_" + key + "_" + suffix] = (scenario["id"], suffix)
    points = {s["id"]: {suffix: [] for suffix in SUFFIXES} for s in contract["scenarios"]}
    order = []
    for raw in path.read_bytes().splitlines():
        if len(raw) > 16384:
            raise PerformanceContractError("local scalar point is too large")
        point = decode_json(raw)
        if point.get("type") != "Point" or point.get("metric") not in metrics:
            continue
        scenario, suffix = metrics[point["metric"]]
        value = point["data"]["value"]
        if type(value) not in (int, float) or not math.isfinite(value) or value < 0:
            raise PerformanceContractError("invalid local scalar")
        points[scenario][suffix].append(value)
        if suffix == "latency":
            order.append(scenario)
        if len(points[scenario][suffix]) > contract["profile"]["iterations"]:
            raise PerformanceContractError("excess local scalar points")
    rows = []
    for ordinal in range(1, contract["profile"]["iterations"] + 1):
        for scenario in contract["scenarios"]:
            data = points[scenario["id"]]
            if all(len(data[suffix]) >= ordinal for suffix in SUFFIXES):
                rows.append({"scenario": scenario["id"], "ordinal": ordinal,
                             **{suffix: data[suffix][ordinal - 1] for suffix in SUFFIXES}})
    expected_order = [s["id"] for _ in range(contract["profile"]["iterations"])
                      for s in contract["scenarios"]]
    complete = order == expected_order and all(
        len(values) == contract["profile"]["iterations"] for data in points.values() for values in data.values())
    return {"schemaVersion": 1, "warmupSamplesExcluded": True, "complete": complete,
            "latencyPointOrder": order, "samples": rows}

def summary(samples, contract, *, auth_failures=0, health_failures=0):
    expected = [(i, s["id"]) for i in range(1, contract["profile"]["iterations"] + 1)
                for s in contract["scenarios"]]
    if (set(samples) != {"schemaVersion", "warmupSamplesExcluded", "complete", "latencyPointOrder", "samples"}
            or samples["schemaVersion"] != 1 or samples["warmupSamplesExcluded"] is not True
            or samples["complete"] is not True
            or [(r["ordinal"], r["scenario"]) for r in samples["samples"]] != expected
            or samples["latencyPointOrder"] != [s for _, s in expected]):
        raise PerformanceContractError("incomplete or reordered local API samples")
    output = []
    for s in contract["scenarios"]:
        rows = [r for r in samples["samples"] if r["scenario"] == s["id"]]
        for r in rows:
            if set(r) != {"scenario", "ordinal", *SUFFIXES} or r["requests"] != 1 or r["microseconds"] <= 0:
                raise PerformanceContractError("invalid local API counters")
            if r["errors"] not in (0, 1) or r["timeouts"] not in (0, 1) or r["timeouts"] > r["errors"]:
                raise PerformanceContractError("invalid local API failures")
            for suffix in SUFFIXES:
                if type(r[suffix]) not in (int, float) or not math.isfinite(r[suffix]) or r[suffix] < 0:
                    raise PerformanceContractError("invalid local API scalar")
        values = summarize(r["latency"] for r in rows)
        output.append({"scenario": s["id"], "requestCount": len(rows),
                       "errorCount": int(sum(r["errors"] for r in rows)),
                       "timeoutCount": int(sum(r["timeouts"] for r in rows)),
                       "p50": values["p50"], "p95": values["p95"], "p99": values["p99"],
                       "durationSeconds": sum(r["microseconds"] for r in rows) / 1000000})
    return {"schemaVersion": 1, "warmupSamplesExcluded": True, "authFailures": auth_failures,
            "healthFailures": health_failures, "scenarios": output}
