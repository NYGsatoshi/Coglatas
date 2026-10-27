"""Retain pinned diagnostic failures; success here never grants Functional acceptance."""
import binascii
import hashlib
from datetime import datetime, timedelta, timezone
import io
import json
import os
from pathlib import Path
import re
import struct
import sys
import urllib.error
import urllib.parse
import urllib.request
import zipfile
import zlib

from functional_diagnostics import MAX_DIAGNOSTIC_BYTES, validate_diagnostics, validate_png

REPO = "NYGsatoshi/Coglatas"
API = "https://api.github.com/repos/" + REPO
NAME = "functional-execution-diagnostics-functional-fast-collaboration-1"
OUTPUT = Path("artifacts/composer-diagnostic-verification")
RECORDS = (
    dict(label="original", run=40376808589, head="4a60b13c5aab805079d2ce109b3b168fa242f3d5",
         base="e191494144628150d3d5136d7e704b270e67b342",
         merge="8831bb9b6e5f3971c48ff656a8887a075a902cd7",
         tree="e48f8557984260843e2a39377a6b5c0964fce47c",
         artifact=12675751055, size=144907,
         digest="b8f1ebba856ac3be05881dcc796c2ad687583565e42bfa7d65a22485afe483ec",
         frontend=121764134341, collaboration=121764983354, msg_ms=6767, notif_ms=836,
         expires="2026-11-09T18:20:06Z"),
    dict(label="refreshed", run=40421398598, head="897ff435bfbbac0a83bf04d2dd0db628b76ebe74",
         base="bccc119eb9f15f490c4f2e739c55eab3bcfeb4b2",
         merge="872db2b5797ee49a291d1ee02fbd948da9c198a6",
         tree="867f15e43025f8ffb30ab8523c72b3231d64b852",
         artifact=12696274966, size=138409,
         digest="2878c23d37d3c8f1c9f7f61bb6d100e0bc49cb1e96a0cfcb40832b4c36e006f1",
         frontend=121917049904, collaboration=121917936569, msg_ms=6934, notif_ms=934,
         expires="2026-11-09T23:40:59Z"),
)


class Refused(ValueError):
    """Only closed codes may cross the console boundary."""


def require(value, code):
    if not value:
        raise Refused(code)


def sha(raw):
    return hashlib.sha256(raw).hexdigest()


def unique_pairs(pairs):
    result = {}
    for key, value in pairs:
        require(key not in result, "DUPLICATE_JSON_FIELD")
        result[key] = value
    return result


def closed_json(raw):
    require(len(raw) <= 2097152, "JSON_SIZE")
    return json.loads(raw, object_pairs_hook=unique_pairs,
                      parse_constant=lambda _: (_ for _ in ()).throw(Refused("NONFINITE_JSON")))


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, request, response, code, message, headers, target):
        return None


class GitHub:
    def __init__(self, token):
        require(bool(token), "MISSING_READ_TOKEN")
        self.token = token
        self.opener = urllib.request.build_opener(NoRedirect())

    def request(self, path, bound, redirect=False):
        require(re.fullmatch(r"[A-Za-z0-9_./-]+", path) is not None and ".." not in path, "API_PATH")
        request = urllib.request.Request(API + "/" + path, headers={
            "Authorization": "Bearer " + self.token,
            "Accept": "application/vnd.github+json",
            "X-GitHub-Api-Version": "2022-11-28",
        })
        try:
            with self.opener.open(request, timeout=30) as response:
                require(response.status == 200, "API_STATUS")
                raw = response.read(bound + 1)
                require(len(raw) <= bound, "DOWNLOAD_SIZE")
                return raw
        except urllib.error.HTTPError as error:
            require(redirect and error.code == 302, "API_STATUS")
            location = error.headers.get("Location", "")
        # A separate request intentionally has NO Authorization header. Never
        # allow an HTTP client's implicit redirect to forward the API token.
        target = urllib.parse.urlsplit(location)
        require(target.scheme == "https" and target.hostname is not None
                and re.fullmatch(r"productionresultssa[a-z0-9]+\.blob\.core\.windows\.net", target.hostname)
                and target.port in (None, 443) and target.username is None
                and target.password is None and not target.fragment
                and all(ord(char) > 32 and ord(char) != 127 for char in location), "STORAGE_ORIGIN")
        with self.opener.open(urllib.request.Request(location), timeout=30) as response:
            require(response.status == 200, "STORAGE_STATUS")
            raw = response.read(bound + 1)
            require(len(raw) <= bound, "DOWNLOAD_SIZE")
            return raw

    def json(self, path):
        return closed_json(self.request(path, 2097152))

    def download(self, path, bound):
        return self.request(path, bound, redirect=True)


def verify_metadata(record, run, artifact, commit):
    require(run["id"] == record["run"] and run["run_attempt"] == 1
            and run["head_sha"] == record["head"] and run["event"] == "pull_request"
            and run["path"] == ".github/workflows/ci.yml"
            and run["status"] == "completed" and run["conclusion"] == "failure"
            and run["repository"]["full_name"] == REPO, "RUN_IDENTITY")
    require(artifact["id"] == record["artifact"] and artifact["name"] == NAME
            and artifact["size_in_bytes"] == record["size"] and artifact["expired"] is False
            and artifact["digest"] == "sha256:" + record["digest"]
            and artifact["workflow_run"]["id"] == record["run"]
            and artifact["workflow_run"]["head_sha"] == record["head"]
            and artifact["expires_at"] == record["expires"], "ARTIFACT_IDENTITY")
    require(commit["sha"] == record["merge"] and commit["tree"]["sha"] == record["tree"]
            and [parent["sha"] for parent in commit["parents"]] == [record["base"], record["head"]],
            "TESTED_MERGE_IDENTITY")


def closed_container(raw, infos):
    """Account for every byte, including ZIP metadata; reject unsupported formats."""
    require(len(raw) >= 22 and raw.startswith(b"PK\x03\x04"), "ZIP_PREFIX")
    end = len(raw) - 22
    values = struct.unpack_from("<4s4H2IH", raw, end)
    signature, disk, central_disk, disk_count, total, central_size, central_offset, comment = values
    require(signature == b"PK\x05\x06" and not disk and not central_disk and not comment
            and disk_count == total == len(infos) and central_offset + central_size == end,
            "ZIP_END")
    position, local_end = central_offset, 0
    for info in infos:
        require(position + 46 <= end, "ZIP_CENTRAL")
        fields = struct.unpack_from("<4s6H3I5H2I", raw, position)
        (sig, made, needed, flags, compression, time, date, crc, compressed, expanded,
         name_len, extra_len, comment_len, disk_start, internal, external, offset) = fields
        checks = dict(signature=sig == b"PK\x01\x02", creatorSystem=made >> 8 in (0, 3),
                      creatorVersion=(made & 255) in (10, 20), extractorVersion=needed in (10, 20),
                      flags=flags in (0, 8, 2048, 2056), compression=compression in (0, 8),
                      extraAbsent=not extra_len, commentAbsent=not comment_len, singleDisk=not disk_start,
                      internalAttributes=internal in (0, 1), externalAttributes=(external & 65535) in (0, 32),
                      contiguous=offset == local_end == info.header_offset)
        if not all(checks.values()):
            error = Refused("ZIP_METADATA")
            versions = {10: "V10", 20: "V20", 45: "V45", 63: "V63"}
            error.container_format = dict(entryIndex=infos.index(info),
                creatorSystem={0: "DOS", 3: "UNIX"}.get(made >> 8, "OTHER"),
                creatorVersion=versions.get(made & 255, "OTHER"),
                extractorVersion=versions.get(needed, "OTHER"), checks=checks)
            raise error
        name = info.filename.encode("ascii")
        require(name_len == len(name) and raw[position + 46:position + 46 + name_len] == name
                and (crc, compressed, expanded, flags, compression)
                == (info.CRC, info.compress_size, info.file_size, info.flag_bits, info.compress_type), "ZIP_CENTRAL")
        position += 46 + name_len
        require(offset + 30 + name_len <= central_offset, "ZIP_LOCAL")
        local = struct.unpack_from("<4s5H3I2H", raw, offset)
        require(local[:6] == (b"PK\x03\x04", needed, flags, compression, time, date)
                and local[9:] == (name_len, 0)
                and raw[offset + 30:offset + 30 + name_len] == name, "ZIP_LOCAL")
        require(local[6:9] in ((crc, compressed, expanded), (0, 0, 0)) if flags & 8
                else local[6:9] == (crc, compressed, expanded), "ZIP_LOCAL")
        start = offset + 30 + name_len
        local_end = start + compressed
        require(local_end <= central_offset, "ZIP_COMPRESSED_SIZE")
        data = raw[start:local_end]
        if compression == 8:
            decoder = zlib.decompressobj(-15)
            decoded = decoder.decompress(data, expanded + 1)
            require(decoder.eof and not decoder.unused_data and not decoder.unconsumed_tail,
                    "ZIP_DEFLATE_TAIL")
        else:
            decoded = data
        require(len(decoded) == expanded and binascii.crc32(decoded) & 0xffffffff == crc, "ZIP_CRC")
        if flags & 8:
            if raw[local_end:local_end + 4] == b"PK\x07\x08":
                local_end += 4
            require(local_end + 12 <= central_offset
                    and struct.unpack_from("<3I", raw, local_end) == (crc, compressed, expanded),
                    "ZIP_DESCRIPTOR")
            local_end += 12
    require(position == end and local_end == central_offset, "ZIP_UNACCOUNTED_BYTES")


def inspect_archive(record, raw):
    require(len(raw) == record["size"] and len(raw) <= 262144
            and sha(raw) == record["digest"], "ORIGINAL_ZIP_DIGEST")
    with zipfile.ZipFile(io.BytesIO(raw)) as archive:
        infos = archive.infolist()
        require(1 <= len(infos) <= 4 and not archive.comment, "ZIP_ENTRY_COUNT")
        names = []
        expanded = 0
        for info in infos:
            require(info.filename == info.orig_filename
                    and re.fullmatch(r"(?:diagnostics-collaboration\.json|structural-collaboration-FUNC-(?:MSG|NOTIF)-001-[0-9a-f]{40}-[1-9][0-9]{0,19}-1-0\.png)",
                                     info.filename) is not None, "ZIP_MEMBER_NAME")
            require(info.filename not in names and not info.extra and not info.comment
                    and not info.flag_bits & 1 and info.compress_type in (0, 8)
                    and info.external_attr >> 16 & 0o170000 in (0, 0o100000)
                    and info.file_size <= 1048576
                    and info.file_size <= max(1, info.compress_size) * 200, "ZIP_MEMBER_METADATA")
            expanded += info.file_size
            require(expanded <= 2097152, "ZIP_EXPANSION")
            names.append(info.filename)
        closed_container(raw, infos)
        require("diagnostics-collaboration.json" in names, "MISSING_DIAGNOSTICS")
        diagnostic = archive.read("diagnostics-collaboration.json")
        require(len(diagnostic) <= MAX_DIAGNOSTIC_BYTES, "DIAGNOSTIC_SIZE")
        data = closed_json(diagnostic)
        lane = dict(commitSha=record["merge"], gate="functional-fast", suite="collaboration",
                    runId=str(record["run"]), runAttempt="1",
                    journeys=[dict(journeyId="FUNC-MSG-001", status="FAIL", attempts=1),
                              dict(journeyId="FUNC-NOTIF-001", status="PASS", attempts=1)])
        validate_diagnostics(data, lane)
        referenced = {"diagnostics-collaboration.json"}
        for journey in data["journeys"]:
            message = journey["journeyId"] == "FUNC-MSG-001"
            require(journey["evidenceState"] == "COMPLETE" and len(journey["attempts"]) == 1,
                    "DIAGNOSTIC_COMPLETENESS")
            attempt = journey["attempts"][0]
            require(attempt["retry"] == 0 and attempt["status"] == ("failed" if message else "passed")
                    and attempt["failureKind"] == ("ASSERTION" if message else "NONE")
                    and attempt["failedStepId"] == ("MSG-RECOVERY" if message else None)
                    and attempt["browserEvidenceState"] == "COMPLETE", "ORIGINAL_FAILURE_CONTRACT")
            browser = attempt["browser"]
            snapshot = browser["structuralSnapshot"]
            if snapshot is not None:
                require(snapshot["name"] in names, "MISSING_SNAPSHOT")
                png = archive.read(snapshot["name"])
                require(len(png) <= 262144 and sha(png) == snapshot["sha256"], "PNG_DIGEST")
                validate_png(png)
                referenced.add(snapshot["name"])
        require(set(names) == referenced, "UNREFERENCED_ZIP_MEMBER")
        return dict(message="FAIL", failedStep="MSG-RECOVERY", failureKind="ASSERTION",
                    notification="PASS", attempts=1, retries=0, members=len(names))


def verify_log(record, raw, kind):
    require(len(raw) <= 16777216, "NATIVE_LOG_SIZE")
    text = re.sub(r"\x1b\[[0-?]*[ -/]*[@-~]", "", raw.decode("utf-8-sig"))
    checkout = re.findall(r"HEAD is now at ([a-f0-9]{7,40}) Merge ([a-f0-9]{40}) into ([a-f0-9]{40})", text)
    require(len(checkout) == 1 and record["merge"].startswith(checkout[0][0])
            and checkout[0][1:] == (record["head"], record["base"]), "LOG_SOURCE")
    if kind == "frontend":
        require(re.search(r"Tests\s+1 failed\s*\|\s*1869 passed\s*\(1870\)", text)
                and "message-catch-up-draft.spec.ts:113" in text
                and "isConnected" in text and "expected false to be true" in text, "UNIT_RED")
    else:
        require("originalComposerNode!.evaluate((node) => node.isConnected)" in text
                and re.search(r">\s*584\s*\|\s*expect\(await originalComposerNode!\.evaluate\(\(node\) => node\.isConnected\)\)\.toBe\(true\)", text)
                and "MSG-RECOVERY" in text and "Expected: true" in text and "Received: false" in text
                and re.findall(r"- FUNC-(MSG|NOTIF)-001: (PASS|FAIL); attempts=([0-9]+); duration=([0-9]+) ms", text)
                    == [("MSG", "FAIL", "1", str(record["msg_ms"])),
                        ("NOTIF", "PASS", "1", str(record["notif_ms"]))]
                and text.count("Browser observation: COMPLETE; retry=0") == 2
                and "failed steps=MSG-RECOVERY / ASSERTION" in text, "FUNCTIONAL_RED")
    return dict(sha256=sha(raw), size=len(raw), retained="ORIGINAL_PROVIDER_ONLY")


def verify_record(client, record):
    run = client.json("actions/runs/" + str(record["run"]))
    artifact = client.json("actions/artifacts/" + str(record["artifact"]))
    commit = client.json("git/commits/" + record["merge"])
    verify_metadata(record, run, artifact, commit)
    raw = client.download("actions/artifacts/" + str(record["artifact"]) + "/zip", 262144)
    projection = inspect_archive(record, raw)
    logs = {}
    for kind in ("frontend", "collaboration"):
        job = client.json("actions/jobs/" + str(record[kind]))
        require(job["id"] == record[kind] and job["run_id"] == record["run"]
                and job["run_attempt"] == 1 and job["status"] == "completed"
                and job["conclusion"] == "failure", "JOB_IDENTITY")
        log = client.download("actions/jobs/" + str(record[kind]) + "/logs", 16777216)
        logs[kind] = verify_log(record, log, kind)
    return dict(label=record["label"], sourceHead=record["head"], base=record["base"],
                testedMerge=record["merge"], tree=record["tree"], run=record["run"], attempt=1,
                artifact=record["artifact"], artifactName=NAME, originalExpiresAt=artifact["expires_at"], originalZipSha256=sha(raw),
                originalZipSize=len(raw), projection=projection, logs=logs), raw


def main():
    # Fixed output and immutable records; no user-controlled URL/ref/record inputs.
    report = dict(schemaVersion=1, verification="NOT_VERIFIED", functionalResult="FAIL",
                  releaseStatus="RELEASE_NO_GO", rawLogsRepublished=False, archives=[],
                  retention=dict(repository=REPO, requestedDays=14,
                                 expiry="PROVIDER_API_READBACK_REQUIRED",
                                 permanent=False, offHost=False))
    try:
        OUTPUT.mkdir(parents=True, exist_ok=True)
        client = GitHub(os.environ.get("GH_TOKEN", ""))
        for record in RECORDS:
            result, raw = verify_record(client, record)
            # Publish an original ZIP only after its ENTIRE container and every
            # closed JSON/structural PNG member pass; never copy native logs.
            with (OUTPUT / (record["label"] + ".zip")).open("xb") as output:
                output.write(raw)
            report["archives"].append(result)
        require(re.fullmatch(r"[a-f0-9]{40}", os.environ.get("GITHUB_SHA", "")), "VERIFIER_SOURCE")
        report["verifierCommit"] = os.environ["GITHUB_SHA"]
        report["verification"] = "PINNED_ARCHIVES_AND_NATIVE_FAILURES_VERIFIED"
        code = 0
    except Refused as error:
        report["failure"] = error.args[0]
        if hasattr(error, "container_format"):
            report["containerFormat"] = error.container_format
        code = 1
    except Exception:
        # Provider/library exception strings can contain signed URLs and bodies.
        report["failure"] = "UNCLASSIFIED_VERIFICATION_FAILURE"
        code = 1
    OUTPUT.mkdir(parents=True, exist_ok=True)
    rendered = json.dumps(report, sort_keys=True, indent=2)
    (OUTPUT / "verification.json").write_text(rendered + "\n", encoding="utf-8")
    print(rendered)
    return code


def retention_projection(artifact, artifact_id, name, digest, run, head):
    """Publish actual finite provider expiry, never a promised permanent copy."""
    require(type(artifact_id) is int and artifact_id > 0
            and re.fullmatch(r"[a-f0-9]{64}", digest) is not None, "RETENTION_INPUT")
    require(artifact["id"] == artifact_id and artifact["name"] == name
            and artifact["expired"] is False and artifact["digest"] == "sha256:" + digest
            and artifact["workflow_run"]["id"] == run
            and artifact["workflow_run"]["head_sha"] == head, "RETENTION_IDENTITY")
    times = []
    for key in ("created_at", "expires_at"):
        value = artifact[key]
        require(isinstance(value, str) and re.fullmatch(r"\d{4}-\d\d-\d\dT\d\d:\d\d:\d\dZ", value), "RETENTION_TIME")
        times.append(datetime.strptime(value, "%Y-%m-%dT%H:%M:%SZ").replace(tzinfo=timezone.utc))
    require(timedelta(0) < times[1] - times[0] <= timedelta(days=14)
            and times[1] > datetime.now(timezone.utc), "RETENTION_BOUND")
    return dict(artifactId=artifact_id, artifactName=name, artifactSha256=digest,
                actualCreatedAt=artifact["created_at"], actualExpiresAt=artifact["expires_at"],
                requestedDays=14, permanent=False, offHost=False)


def confirm_retention():
    report = dict(schemaVersion=1, verification="NOT_VERIFIED", functionalResult="FAIL",
                  releaseStatus="RELEASE_NO_GO", rawLogsRepublished=False)
    try:
        require(os.environ.get("GITHUB_EVENT_NAME") == "pull_request", "EVENT")
        run_text = os.environ.get("GITHUB_RUN_ID", "")
        artifact_text = os.environ.get("COGLATAS_ARCHIVE_ARTIFACT_ID", "")
        attempt = os.environ.get("GITHUB_RUN_ATTEMPT", "")
        head = os.environ.get("COGLATAS_SOURCE_HEAD_SHA", "")
        checkout = os.environ.get("GITHUB_SHA", "")
        require(all(re.fullmatch(r"[1-9][0-9]*", value) for value in (run_text, artifact_text, attempt))
                and all(re.fullmatch(r"[a-f0-9]{40}", value) for value in (head, checkout)), "RETENTION_INPUT")
        name = "composer-diagnostic-archives-" + checkout + "-" + attempt
        client = GitHub(os.environ.get("GH_TOKEN", ""))
        artifact = client.json("actions/artifacts/" + artifact_text)
        report["retention"] = retention_projection(
            artifact, int(artifact_text), name, os.environ.get("COGLATAS_ARCHIVE_ARTIFACT_DIGEST", ""),
            int(run_text), head)
        report["verification"] = "FINITE_PROVIDER_RETENTION_VERIFIED"
        report["verifierCommit"] = checkout
        report["run"] = int(run_text)
        report["attempt"] = int(attempt)
        code = 0
    except Refused as error:
        report["failure"] = error.args[0]
        code = 1
    except Exception:
        report["failure"] = "RETENTION_VERIFICATION_FAILED"
        code = 1
    OUTPUT.mkdir(parents=True, exist_ok=True)
    rendered = json.dumps(report, sort_keys=True, indent=2)
    with (OUTPUT / "retention.json").open("x", encoding="utf-8") as output:
        output.write(rendered + "\n")
    print(rendered)
    return code


if __name__ == "__main__":
    sys.exit(confirm_retention() if sys.argv[1:] == ["--confirm-retention"]
             else main() if not sys.argv[1:] else 1)
