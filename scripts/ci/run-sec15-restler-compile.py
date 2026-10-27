"""SEC-15 real compile-only foundation. Never runs a grammar or a target request."""
from __future__ import annotations
import hashlib
import json
import os
from pathlib import Path
import py_compile
import re
import stat
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[2]
PROFILE = "scripts/security/restler/compile-profile.json"
DOCKERFILE = "infra/docker/sec15-restler-compiler.Dockerfile"
PUBLIC = ROOT / "artifacts/sec15-restler-compile/result.json"
PIN = "6d984deedbc54aad957fa3da0c7e9e5df23a2aee"
TREE = "20fd250ba44d11da62529a0e25d08bf3ba16c951"
SDK = "mcr.microsoft.com/dotnet/sdk:8.0.413"
METHODS = {"GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS", "TRACE"}
SOURCES = (PROFILE, DOCKERFILE, "scripts/ci/run-sec15-restler-compile.py",
           "scripts/ci/generate-security-openapi-contract.sh")
LIMIT = 64 * 1024 * 1024

class Rejected(ValueError):
    pass

def require(condition):
    if not condition:
        raise Rejected("INVALID_OR_UNAVAILABLE_INPUT")

def sha(raw):
    return hashlib.sha256(raw).hexdigest()

def pairs(items):
    value = {}
    for key, item in items:
        require(key not in value)
        value[key] = item
    return value

def decode(raw):
    require(isinstance(raw, bytes) and 0 < len(raw) <= LIMIT)
    return json.loads(raw, object_pairs_hook=pairs,
                      parse_constant=lambda _: (_ for _ in ()).throw(Rejected()))

def read_regular(path):
    before = path.lstat()
    require(stat.S_ISREG(before.st_mode) and 0 < before.st_size <= LIMIT)
    flags = os.O_RDONLY | os.O_NONBLOCK | os.O_NOFOLLOW
    with os.fdopen(os.open(path, flags), "rb") as handle:
        opened = os.fstat(handle.fileno())
        require(stat.S_ISREG(opened.st_mode) and
                (opened.st_dev, opened.st_ino) == (before.st_dev, before.st_ino))
        raw = handle.read(LIMIT + 1)
    require(0 < len(raw) <= LIMIT)
    return raw

def operations(document):
    require(isinstance(document, dict) and document.get("openapi") == "3.1.0"
            and isinstance(document.get("paths"), dict))
    result = set()
    for route, item in document["paths"].items():
        require(isinstance(route, str) and len(route) <= 512 and route.startswith("/")
                and not any(c in route for c in ("?", "#", "\\", "\r", "\n"))
                and isinstance(item, dict))
        for method, operation in item.items():
            if method.upper() in METHODS:
                require(method == method.lower() and isinstance(operation, dict))
                result.add((method.upper(), route))
    require(0 < len(result) <= 5000)
    return result

def admit_profile(value):
    require(isinstance(value, dict) and set(value) == {"schemaVersion", "tool", "compiler", "dictionary"}
            and type(value["schemaVersion"]) is int and value["schemaVersion"] == 1)
    require(value["tool"] == {"repository": "https://github.com/microsoft/restler-fuzzer.git",
            "commit": PIN, "tree": TREE, "declaredVersion": "9.3.1-dev", "sdkImage": SDK})
    compiler = value["compiler"]
    expected = {"SwaggerSpecFilePath": ["/input/contract.json"], "SwaggerSpecConfig": None,
        "GrammarInputFilePath": None, "GrammarOutputDirectoryPath": "/output/compiled",
        "CustomDictionaryFilePath": "/input/dictionary.json", "AnnotationFilePath": None,
        "EngineSettingsFilePath": None, "IncludeOptionalParameters": True, "UseHeaderExamples": False,
        "UsePathExamples": False, "UseQueryExamples": False, "UseBodyExamples": False,
        "UseAllExamplePayloads": False, "DiscoverExamples": False, "ExamplesDirectory": "/output/examples",
        "ExampleConfigFilePath": None, "ExampleConfigFiles": None, "DataFuzzing": True,
        "ReadOnlyFuzz": False, "ResolveQueryDependencies": True, "ResolveBodyDependencies": True,
        "ResolveHeaderDependencies": True, "UseRefreshableToken": True, "AllowGetProducers": False,
        "ApiNamingConvention": None, "TrackFuzzedParameterNames": False, "JsonPropertyMaxDepth": None}
    # Typed canonical comparison also rejects bool-as-int and unexpected config keys/paths.
    require(json.dumps(compiler, sort_keys=True) == json.dumps(expected, sort_keys=True))
    dictionary = value["dictionary"]
    expected_dictionary = {
        "restler_fuzzable_string": ["sec15-synthetic"], "restler_fuzzable_string_unquoted": [],
        "restler_fuzzable_int": ["1"], "restler_fuzzable_number": ["1.0"],
        "restler_fuzzable_bool": ["true"], "restler_fuzzable_datetime": ["2020-01-01T00:00:00Z"],
        "restler_fuzzable_date": ["2020-01-01"],
        "restler_fuzzable_uuid4": ["00000000-0000-4000-8000-000000000001"],
        "restler_fuzzable_object": ["{}"], "restler_fuzzable_datetime_unquoted": [],
        "restler_fuzzable_date_unquoted": [], "restler_fuzzable_uuid4_unquoted": [],
        "restler_custom_payload_unquoted": {}, "restler_custom_payload_header_unquoted": {},
        "restler_custom_payload": {},
        "restler_custom_payload_header": {}, "restler_custom_payload_query": {},
        "restler_custom_payload_uuid4_suffix": {}, "shadow_values": {}}
    require(dictionary == expected_dictionary)
    return compiler, dictionary

def admit_base(raw):
    values = decode(raw)
    require(isinstance(values, list) and 0 < len(values) <= 16)
    matches = [v for v in values if isinstance(v, str) and
               re.fullmatch(r"mcr\.microsoft\.com/dotnet/sdk@sha256:[0-9a-f]{64}", v)]
    require(len(matches) == 1)
    return matches[0]

def image_id(raw):
    value = raw.decode("ascii").strip()
    require(re.fullmatch(r"sha256:[0-9a-f]{64}", value) is not None)
    return value

def compiler_command(image, source, output):
    require(re.fullmatch(r"sha256:[0-9a-f]{64}", image) is not None and os.getuid() > 0)
    require(source.is_absolute() and output.is_absolute() and
            all("," not in str(p) and "\n" not in str(p) for p in (source, output)))
    return ["docker", "run", "--rm", "--network", "none", "--cap-drop", "ALL",
        "--security-opt", "no-new-privileges", "--read-only", "--user", f"{os.getuid()}:{os.getgid()}",
        "--tmpfs", "/tmp:rw,noexec,nosuid,size=128m", "--workdir", "/output",
        "--env", "RESTLER_TELEMETRY_OPTOUT=1", "--env", "DOTNET_CLI_TELEMETRY_OPTOUT=1",
        "--env", "DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1", "--env", "HOME=/tmp",
        "--mount", f"type=bind,src={source},dst=/input,readonly",
        "--mount", f"type=bind,src={output},dst=/output", image, "/input/config.json"]

def admit_grammar(raw, expected):
    document = decode(raw)
    require(isinstance(document, dict) and set(document) == {"Requests"}
            and isinstance(document["Requests"], list) and len(document["Requests"]) == len(expected))
    actual = set()
    for request in document["Requests"]:
        require(isinstance(request, dict) and isinstance(request.get("id"), dict))
        identity = request["id"]
        require(set(identity) == {"endpoint", "method"} and isinstance(identity["method"], str)
                and isinstance(identity["endpoint"], str))
        pair = (identity["method"].upper(), identity["endpoint"])
        require(pair in expected and pair not in actual)
        actual.add(pair)
    require(actual == expected)
    return len(actual)

def publish_once(result, path=PUBLIC):
    # Result is assembled only from fixed labels, validated IDs, counts, hashes, and exit codes.
    rendered = json.dumps(result, sort_keys=True, indent=2) + "\n"
    require(len(rendered.encode()) <= 32768)
    path.parent.mkdir(parents=True, exist_ok=True)
    require(not path.parent.is_symlink())
    with path.open("x", encoding="utf-8") as handle:
        handle.write(rendered)
    output = os.environ.get("GITHUB_OUTPUT")
    if output:
        with open(output, "a", encoding="utf-8") as handle:
            handle.write("published=true\n")
    print("SEC15_COMPILE_OUTCOME_BEGIN")
    sys.stdout.write(rendered)
    print("SEC15_COMPILE_OUTCOME_END")
    print("SEC-15 compile-only: " + result["decision"] + "; runtime NOT_EXECUTED")

def execute():
    os.umask(0o077)
    result = {"schemaVersion": 1, "control": "SEC-15", "scope": "CURRENT_CONTRACT_COMPILE_ONLY",
        "decision": "FAIL", "phase": "SOURCE", "sourceSha": None, "treeSha": None,
        "runId": None, "attempt": None, "toolCommit": PIN, "toolTree": TREE,
        "declaredToolVersion": "9.3.1-dev", "sdkVersion": None, "baseRepoDigest": None,
        "toolImageId": None, "bindings": {}, "commandExitCodes": {}, "failedOutputHashes": {}, "outputHashes": {},
        "operationCount": 0, "compiledOperationCount": 0, "activeTargetTraffic": "NOT_EXECUTED",
        "authenticatedRuntime": "NOT_EXECUTED", "fuzzLean": "NOT_EXECUTED",
        "findingPolicyActivation": "NOT_GRANTED", "securityProgramAcceptance": "NOT_GRANTED",
        "rawArtifacts": "NOT_RETAINED", "sourceUnchanged": False}
    codes = result["commandExitCodes"]
    tool_image = None
    def command(args, phase, timeout=300):
        result["phase"] = phase
        try:
            with tempfile.TemporaryFile() as stdout, tempfile.TemporaryFile() as stderr:
                process = subprocess.run(args, cwd=ROOT, stdout=stdout, stderr=stderr,
                                         timeout=timeout, check=False)
                codes[phase] = process.returncode
                require(stdout.tell() <= LIMIT and stderr.tell() <= LIMIT)
                stdout.seek(0)
                raw = stdout.read(LIMIT + 1)
                if process.returncode != 0:
                    stderr.seek(0)
                    result["failedOutputHashes"] = {"stdoutSha256": sha(raw),
                        "stderrSha256": sha(stderr.read(LIMIT + 1))}
        except subprocess.TimeoutExpired:
            codes[phase] = 124
            raise Rejected() from None
        codes[phase] = process.returncode
        require(process.returncode == 0)
        return raw
    try:
        require(os.environ.get("COGLATAS_SEC15_RUNNER_ENVIRONMENT") == "github-hosted")
        source = os.environ.get("GITHUB_SHA", "")
        run_id, attempt = os.environ.get("GITHUB_RUN_ID", ""), os.environ.get("GITHUB_RUN_ATTEMPT", "")
        require(re.fullmatch(r"[0-9a-f]{40}", source) and re.fullmatch(r"[1-9][0-9]{0,19}", run_id)
                and re.fullmatch(r"[1-9][0-9]{0,5}", attempt))
        result.update(sourceSha=source, runId=int(run_id), attempt=int(attempt))
        require(command(["git", "rev-parse", "HEAD"], "SOURCE").decode("ascii").strip() == source)
        tree = command(["git", "rev-parse", "HEAD^{tree}"], "TREE").decode("ascii").strip()
        require(re.fullmatch(r"[0-9a-f]{40}", tree))
        result["treeSha"] = tree
        require(command(["git", "status", "--porcelain", "--untracked-files=no"], "CLEAN_SOURCE") == b"")
        result["bindings"] = {p: sha(read_regular(ROOT / p)) for p in SOURCES}
        profile = decode(read_regular(ROOT / PROFILE))
        compiler, dictionary = admit_profile(profile)
        command(["bash", "scripts/ci/generate-security-openapi-contract.sh"], "OPENAPI", 900)
        contract = read_regular(ROOT / "artifacts/openapi/coglatas-openapi.json")
        expected = operations(decode(contract))
        result["operationCount"] = len(expected)
        result["bindings"]["contractSha256"] = sha(contract)
        with tempfile.TemporaryDirectory(prefix="sec15-") as temp:
            private = Path(temp)
            inputs, outputs, upstream = private / "input", private / "output", private / "upstream"
            inputs.mkdir(mode=0o700)
            outputs.mkdir(mode=0o700)
            upstream.mkdir(mode=0o700)
            (inputs / "contract.json").write_bytes(contract)
            (inputs / "config.json").write_text(json.dumps(compiler), encoding="utf-8")
            (inputs / "dictionary.json").write_text(json.dumps(dictionary), encoding="utf-8")
            command(["git", "init", "--quiet", str(upstream)], "TOOL_INIT")
            command(["git", "-C", str(upstream), "remote", "add", "origin", profile["tool"]["repository"]], "TOOL_REMOTE")
            command(["git", "-C", str(upstream), "-c", "credential.helper=", "fetch", "--depth=1", "origin", PIN], "TOOL_FETCH", 300)
            command(["git", "-C", str(upstream), "checkout", "--quiet", "--detach", "FETCH_HEAD"], "TOOL_CHECKOUT")
            require(command(["git", "-C", str(upstream), "rev-parse", "HEAD"], "TOOL_SOURCE").decode("ascii").strip() == PIN)
            require(command(["git", "-C", str(upstream), "rev-parse", "HEAD^{tree}"], "TOOL_TREE").decode("ascii").strip() == TREE)
            command(["docker", "pull", SDK], "SDK_PULL", 600)
            base = admit_base(command(["docker", "image", "inspect", SDK, "--format", "{{json .RepoDigests}}"], "SDK_DIGEST"))
            result["baseRepoDigest"] = base
            tag = "coglatas-sec15-compiler:" + source
            command(["docker", "build", "--file", str(ROOT / DOCKERFILE), "--build-arg",
                     "RESTLER_SDK_BASE=" + base, "--tag", tag, str(upstream)], "TOOL_BUILD", 1200)
            tool_image = image_id(command(["docker", "image", "inspect", tag, "--format", "{{.Id}}"], "TOOL_IMAGE"))
            result["toolImageId"] = tool_image
            version = command(["docker", "run", "--rm", "--network", "none", "--cap-drop", "ALL",
                "--security-opt", "no-new-privileges", "--read-only", "--entrypoint", "dotnet",
                tool_image, "--version"], "SDK_VERSION").decode("ascii").strip()
            require(version == "8.0.413")
            result["sdkVersion"] = version
            command(compiler_command(tool_image, inputs, outputs), "COMPILE", 600)
            result["phase"] = "GRAMMAR"
            compiled = outputs / "compiled"
            require(compiled.is_dir() and not compiled.is_symlink())
            grammar_json = read_regular(compiled / "grammar.json")
            grammar_py = read_regular(compiled / "grammar.py")
            generated_dictionary = read_regular(compiled / "dict.json")
            result["compiledOperationCount"] = admit_grammar(grammar_json, expected)
            # Compile Python syntax without importing/executing generated grammar code.
            py_compile.compile(str(compiled / "grammar.py"), cfile=str(private / "grammar.pyc"), doraise=True)
            result["outputHashes"] = {"grammarJsonSha256": sha(grammar_json),
                "grammarPythonSha256": sha(grammar_py), "dictionarySha256": sha(generated_dictionary)}
            require(contract == read_regular(inputs / "contract.json"))
        result["phase"] = "SOURCE_UNCHANGED"
        require(result["bindings"] == {**{p: sha(read_regular(ROOT / p)) for p in SOURCES},
                "contractSha256": sha(read_regular(ROOT / "artifacts/openapi/coglatas-openapi.json"))})
        require(command(["git", "status", "--porcelain", "--untracked-files=no"], "SOURCE_UNCHANGED") == b"")
        result["sourceUnchanged"] = True
        result["phase"] = "COMPLETE"
        result["decision"] = "PASS_COMPILE_ONLY"
    except (Rejected, OSError, ValueError, TypeError, KeyError, UnicodeError, RecursionError, py_compile.PyCompileError):
        # Never print child output, paths, schema examples, arbitrary errors, or generated source.
        pass
    finally:
        if tool_image:
            try:
                subprocess.run(["docker", "image", "rm", tool_image], cwd=ROOT, capture_output=True,
                               timeout=120, check=False)
            except (OSError, subprocess.TimeoutExpired):
                pass
    publish_once(result)
    return 0 if result["decision"] == "PASS_COMPILE_ONLY" else 1

if __name__ == "__main__":
    sys.exit(execute())
