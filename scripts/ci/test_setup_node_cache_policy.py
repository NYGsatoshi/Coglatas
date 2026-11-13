#!/usr/bin/env python3
"""Guard reviewed setup-node cache ownership with offline negative controls.

An implicit setup-node cache writes ~/.npm across runs. These jobs install
dependencies via scripts/ci/npm-ci-retry.sh with a separate per-job cache, or
only run a Node verifier. Disable the redundant cross-run cache, not the
version-pinned npm installation, test execution or dependency protections.
"""
from __future__ import annotations

import hashlib
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]

EXPECTED = {
    ".github/workflows/ci.yml": {
        "frontend-build": "scripts/ci/npm-ci-retry.sh frontend",
        "frontend-test": "scripts/ci/npm-ci-retry.sh .",
    },
    ".github/workflows/main-validation.yml": {
        "main-frontend-test": "scripts/ci/npm-ci-retry.sh frontend",
    },
    ".github/workflows/main-build-artifacts.yml": {
        "main-frontend-build": "scripts/ci/npm-ci-retry.sh frontend",
        "qodana-cloud": "scripts/quality/check-qodana-project-model.mjs",
    },
    ".github/workflows/node-toolchain-preflight.yml": {
        "node-toolchain-preflight": "scripts/ci/verify-node-toolchain.mjs",
    },
    ".github/workflows/qodana_trusted_gate.yml": {
        "qodana-dotnet-community": "scripts/quality/check-qodana-project-model.mjs",
    },
    ".github/workflows/qodana_cloud_quality.yml": {
        "qodana-cloud": "scripts/quality/check-qodana-project-model.mjs",
    },
    ".github/workflows/qodana-signed-shadow.yml": {
        "shadow-producer": "scripts/quality/check-qodana-project-model.mjs",
    },
    ".github/workflows/sonarcloud-ci.yml": {
        "sonarcloud": "scripts/ci/npm-ci-retry.sh frontend",
    },
    ".github/workflows/real-backend-p0-preflight.yml": {
        "real-backend-p0-preflight": "tests/ui/real-backend-smoke-compose-helpers.node-test.mjs",
    },
    ".github/workflows/thread-functional-typecheck.yml": {
        "strict-thread-contract": "scripts/ci/npm-ci-retry.sh frontend",
    },
    ".github/workflows/composer-functional-typecheck.yml": {
        "strict-composer-contract": "scripts/ci/npm-ci-retry.sh frontend",
    },
    ".github/workflows/licensed-real-backend-acceptance.yml": {
        "licensed-real-backend": "scripts/ci/npm-ci-retry.sh .",
    },
}

# These are the exact reviewed step predicates; a changed/disabled step
# cannot borrow execution evidence from another conditional.
REVIEWED_OWNER_IF = {
    (".github/workflows/ci.yml", "frontend-build"): "needs.changes.outputs.frontend == 'true'",
    (".github/workflows/ci.yml", "frontend-test"): "needs.frontend-build.outputs.frontend == 'true'",
    (".github/workflows/main-build-artifacts.yml", "qodana-cloud"): "steps.qodana_scan.outcome == 'success'",
    (".github/workflows/qodana_trusted_gate.yml", "qodana-dotnet-community"): "steps.qodana_scan.outcome == 'success'",
    (".github/workflows/qodana_cloud_quality.yml", "qodana-cloud"): "steps.qodana_scan.outcome == 'success'",
    (".github/workflows/qodana-signed-shadow.yml", "shadow-producer"): "steps.qodana_scan.outcome == 'success'",
}


# Source-byte attestation for the original executable owner steps. Deliberate
# changes to these steps require a reviewed update of this fixed map; the
# policy does not infer whether arbitrary Bash or YAML is executable.
REVIEWED_OWNER_STEP_SHA256 = {
    (".github/workflows/ci.yml", "frontend-build"): "123ccb6742603e4c1ff0e4432f13d6b64b8b70be8446e2377baaac8b0d66758d",
    (".github/workflows/ci.yml", "frontend-test"): "cac20af64fd6fe3a0e6191ff1c2e14e9f17e6aa39564cccda1fd5852435ac179",
    (".github/workflows/main-validation.yml", "main-frontend-test"): "88c37339a48c241760916572ac6779c5b08ed2d7f28108cb048467e972f37cd2",
    (".github/workflows/main-build-artifacts.yml", "main-frontend-build"): "dc667e5fd6cb351cb92a7e0e49ad94b0224b6a6070cf80049fbca49403f20853",
    (".github/workflows/main-build-artifacts.yml", "qodana-cloud"): "812b06ba91caf89db73969b188e0fb5acc4e3f448e2b7d6ce9937126c88db905",
    (".github/workflows/node-toolchain-preflight.yml", "node-toolchain-preflight"): "a3296559cc67d75c9f55eea79643180ce81f1d424127c1a9decd5a330aaa0b9f",
    (".github/workflows/qodana_trusted_gate.yml", "qodana-dotnet-community"): "505d3bd0aad3120d47b03f20670f2ca86907b301d7797c0b95747697560ed650",
    (".github/workflows/qodana_cloud_quality.yml", "qodana-cloud"): "988295ce32b77c9c82c909dff1d0e2889f4876de1473da18f8fef14b771a32fa",
    (".github/workflows/qodana-signed-shadow.yml", "shadow-producer"): "4e738fe4bffbc3d81bb9b92df520d2591b1326882ff2e54a998ec34a9b0e7bab",
    (".github/workflows/sonarcloud-ci.yml", "sonarcloud"): "88f4a9a41cb174ab0cb3f44fd38b9270852c83d36c234c7a9219141e2d171409",
    (".github/workflows/real-backend-p0-preflight.yml", "real-backend-p0-preflight"): "de1ab8511e0ffcf6c338392786c6f0d8362cb541310a30268f6fa62ef9917b0b",
    (".github/workflows/thread-functional-typecheck.yml", "strict-thread-contract"): "f8e36440f8bc8d2ccc9e008f0d595bc568b0e7e94e630bbc0cab86d0e86f38c0",
    (".github/workflows/composer-functional-typecheck.yml", "strict-composer-contract"): "cd760cd04f9cebaa28139a2082b12d875bfa61f4a72b29818423e49426220968",
    (".github/workflows/licensed-real-backend-acceptance.yml", "licensed-real-backend"): "21c78ccfedf4d24bdae7d66af08cd5189092f800509c80fad1899aa38d8d2c15",
}

JOB = re.compile(r"(?m)^  ([A-Za-z][A-Za-z0-9_-]*):\s*$")
SETUP_NODE = re.compile(r"(?m)^        uses: actions/setup-node@([0-9a-f]{40})\s*(?:#.*)?$")
# Count *all* setup-node refs, not only SHA-pinned ones. A second unpinned
# action must not be invisible to the fixed-source policy.
SETUP_NODE_REFERENCES = re.compile(r"(?m)^(?:        |      -[ \t]+)(?:uses|['\"]uses['\"])[ \t]*:[ \t]*['\"]?actions/setup-node@")
INPUT_CACHE = re.compile(r"(?m)^          package-manager-cache: (\S+)\s*$")
# YAML accepts quoted or spaced mapping keys. Count every recognized variant
# so an additional input cannot silently override the reviewed false value.
INPUT_CACHE_KEYS = re.compile(r"(?m)^          (?:package-manager-cache|['\"]package-manager-cache['\"])[ \t]*:")
INPUT_EXPLICIT_CACHE = re.compile(r"(?m)^          (?:cache|['\"]cache['\"])[ \t]*:")
# A flow-mapping step can contain an unpinned setup-node action invisible to
# line-oriented uses scanners. Reject unsupported step forms in guarded jobs.
FLOW_STEPS = re.compile(r"(?m)^      -[ \t]*\{")
# YAML block scalar action references evade exact one-setup-node counting.
# Reject unsupported folded or literal uses scalars in reviewed job steps.
BLOCK_USES = re.compile(
    r"(?m)^(?:        |      -[ \t]+)(?:uses|['\"]uses['\"])[ \t]*:[ \t]*[>|][-+]?[ \t]*(?:#.*)?$"
)


def job_blocks(source: str) -> dict[str, str]:
    """Split the fixed workflow jobs into bounded source blocks for policy inspection."""
    marker = re.search(r"(?m)^jobs:\s*$", source)
    if marker is None:
        return {}
    section = source[marker.end():]
    headings = list(JOB.finditer(section))
    return {
        header.group(1): section[header.end():headings[index + 1].start() if index + 1 < len(headings) else len(section)]
        for index, header in enumerate(headings)
    }


def unsafe_literal_shell_fragment(fragment: str) -> bool:
    """Refuse unreviewed grouping and multiline quotes in Bash literal blocks."""
    quote: str | None = None
    escaped = False
    for index, char in enumerate(fragment):
        if escaped:
            escaped = False
            continue
        if char == "\\" and quote != "'":
            escaped = True
            continue
        if char in ("'", '"'):
            if quote is None:
                quote = char
            elif quote == char:
                quote = None
            continue
        if quote is None:
            if char == "#" and (index == 0 or fragment[index - 1].isspace()):
                break
            if char in "(){}":
                return True
    return escaped or quote is not None


def run_command_lines(job: str) -> list[str]:
    """Decode reviewed plain, literal and flat folded run scalars fail-closed.

    Folded (>-) YAML joins adjacent non-empty physical lines with spaces.
    Treat blank-separated and more-indented folded forms as unsupported
    rather than trusting a physical line that YAML does not execute alone.
    """
    # Reject heredoc/here-string syntax in the raw step *before* shell comment
    # removal: a quoted '#' can otherwise hide the '<<' operator.
    if any("<<" in line for line in job.splitlines()):
        return []
    commands: list[str] = []
    style: str | None = None
    folded: list[str] = []
    pending_blank = False
    unsupported = False
    shell_depth = 0
    shell_continuation = False

    def finish() -> None:
        nonlocal style, folded, pending_blank, unsupported, shell_depth
        if style == "folded" and folded and not unsupported:
            # Strip shell comments only AFTER YAML folding. Earlier removal
            # could falsely promote a later folded line to an invocation.
            command = " ".join(folded).split("#", 1)[0].strip()
            if command:
                commands.append(command)
        style = None
        folded = []
        pending_blank = False
        unsupported = False
        shell_depth = 0

    for line in job.splitlines():
        run = re.match(r"^        run:[ \t]*(.*)$", line)
        if run is not None:
            finish()
            value = run.group(1).strip()
            if value in ("|", "|-", "|+"):
                style = "literal"
            elif value in (">", ">-", ">+"):
                style = "folded"
            elif value and not value.startswith(("&", "*", "!", "{", "[")):
                command = value.split("#", 1)[0].strip()
                if command:
                    commands.append(command)
            continue

        if style is None:
            continue
        if not line.strip():
            if style == "folded":
                pending_blank = True
            continue
        if not line.startswith("          "):
            finish()
            continue
        fragment = line[10:]
        if style == "literal":
            if unsafe_literal_shell_fragment(fragment):
                shell_continuation = True
            # Inspect raw Bash syntax before stripping comments: a quoted
            # hash is ordinary data, so it must not conceal an early exit.
            # Conservative rejection is intentional for unsupported grammar.
            if re.search(
                r"(?:^|[;&|])[ \t]*(?:exit|return|break|continue|exec)(?:[ \t]|;|$)",
                fragment,
            ):
                # The reviewed Qodana guard exits nonzero if SARIF is absent.
                # That inner refusal is safe: it cannot skip a successful owner.
                if not (shell_depth and fragment.strip() == "exit 1"):
                    shell_continuation = True
            command = fragment.split("#", 1)[0].strip()
            if not command:
                continue
            # A multiline Bash AND/OR list, pipe or escaped newline changes
            # the reachability of the next physical line. Refuse to credit any
            # owner command in this unsupported shell grammar.
            # Inspect raw literal content: a quoted '#' can hide a trailing
            # continuation operator from naive shell-comment stripping.
            if re.search(r"(?:&&|\|\||\||\\)$", fragment.rstrip()):
                shell_continuation = True
            # A textual command inside if/for/case/while blocks does not prove
            # unconditional execution. Never promote such nested lines.
            if re.match(r"^(?:if|for|while|until|case|select)(?:\s|$)", command):
                shell_depth += 1
                continue
            if re.match(r"^(?:fi|done|esac)(?:\s|$)", command):
                if shell_depth:
                    shell_depth -= 1
                else:
                    unsupported = True
                continue
            if shell_depth or fragment.startswith((" ", "\t")):
                continue
            commands.append(command)
        else:
            # A nested indent or a separated folded paragraph may have
            # different YAML newline semantics: decline unsupported forms.
            if pending_blank or fragment.startswith((" ", "\t")):
                unsupported = True
            folded.append(fragment.strip())
            pending_blank = False

    finish()
    if shell_continuation:
        return []
    # Heredoc bodies are inert shell input, even if they contain what looks
    # like a canonical owner invocation. Reject the entire run step rather
    # than accidentally treating heredoc contents as executable commands.
    # This fail-closed guard also declines unreviewed here-string constructs.
    if any("<<" in command for command in commands):
        return []
    # Early exit or control transfer makes a later owner guard unreachable.
    # Decline unsupported shell grammar rather than inferring execution.
    if any(re.search(r"(?:^|[;&|])[ \t]*(?:exit|return|break|continue|exec)(?:[ \t]|;|$)",
                     command) for command in commands):
        return []
    return commands

def owner_invoked(
    job: str, owner: str, required_if: str | None, reviewed_step_sha256: str
) -> bool:
    """Require an active canonical Bash/Node command, never echo or path mentions."""
    interpreter = "node" if owner.endswith(".mjs") else "bash"
    canonical = re.compile(rf"^{interpreter}[ \t]+{re.escape(owner)}(?:[ \t]|$)")
    # Evaluate per actual YAML step, so an unrelated command cannot lend
    # execution authority to an inert echo or a test path in another step.
    # No reviewed owner job has a run-shell defaults override.
    if re.search(r"(?m)^    (?:defaults|['\"]defaults['\"])[ \t]*:", job):
        return False
    for step in re.split(r"(?m)(?=^      - )", job):
        # Bind the complete reviewed step, not merely an owner-shaped substring.
        if hashlib.sha256(step.rstrip().encode("utf-8")).hexdigest() != reviewed_step_sha256:
            continue
        observed_ifs = re.findall(r"(?m)^(?:        |      -[ \t]+)if:[ \t]*(.*?)\s*$", step)
        # Alternate YAML step keys are not the reviewed grammar; in
        # particular quoted/escaped or spaced `if` disables an owner.
        if re.search(r"(?m)^(?:        |      -[ \t]+)(?:['\"]|\?|[A-Za-z][A-Za-z0-9_-]*[ \t]+:)", step):
            continue
        # Exact reviewed conditions allow necessary frontend routing and
        # Qodana scan-success gates, but never an inserted false/foreign if.
        if observed_ifs != ([required_if] if required_if is not None else []):
            continue
        # Only the reviewed Bash shell can execute these canonical owners.
        shells = re.findall(r"(?m)^        shell:[ \t]*(.*?)\s*$", step)
        if shells not in ([], ["bash"]) or re.search(r"(?m)^      -[ \t]+shell[ \t]*:", step):
            continue
        commands = run_command_lines(step)
        if any(canonical.match(command) for command in commands):
            return True
        # The original real-backend step has one folded node --test
        # invocation with a fixed list of .mjs test arguments. Evaluate
        # the *decoded* command, never each physical YAML line.
        if (owner == "tests/ui/real-backend-smoke-compose-helpers.node-test.mjs"
                and re.search(r"(?m)^        run: >[-+]?[ \t]*$", step)
                and len(commands) == 1):
            args = commands[0].split()
            if (len(args) >= 3 and args[:2] == ["node", "--test"]
                    and owner in args[2:]
                    and all(re.fullmatch(r"[A-Za-z0-9_./-]+\.mjs", arg)
                            for arg in args[2:])):
                return True
    return False


def violations(workflows: dict[str, str], installer: str) -> list[str]:
    """Return all fixed-owner, Node 26 and isolated npm-cache policy violations."""
    issues = []
    # The isolation contract has to remain true before disabling setup-node's
    # own global cache. Do not assume scripts remain unchanged forever.
    if (
        'export NPM_CONFIG_CACHE="$secure_cache"' not in installer
        or 'secure_cache="$secure_temp_root/npm-cache-' not in installer
        or "export NPM_CONFIG_STRICT_ALLOW_SCRIPTS=" not in installer
        or 'export NPM_CONFIG_REGISTRY="https://registry.npmjs.org/"' not in installer
    ):
        issues.append("Per-job authenticated npm install boundary is missing")
    for path, targets in EXPECTED.items():
        source = workflows.get(path)
        if not isinstance(source, str):
            issues.append(f"{path}: missing workflow")
            continue
        # No reviewed workflow allows a top-level run-shell defaults override.
        if re.search(r"(?m)^(?:defaults|['\"]defaults['\"])[ \t]*:", source):
            issues.append(f"{path}: unreviewed workflow shell defaults")
        # YAML-escaped job/workflow mapping keys can decode into "defaults".
        # All reviewed guarded workflows use unescaped top-level/job keys.
        if re.search(r'(?m)^ {0,6}"[^"\n]*\\[^"\n]*"[ \t]*:', source):
            issues.append(f"{path}: unreviewed escaped workflow/job YAML key")
        jobs = job_blocks(source)
        for job_id, owner in targets.items():
            job = jobs.get(job_id)
            if job is None:
                issues.append(f"{path}: missing job {job_id}")
                continue
            reviewed_step_sha256 = REVIEWED_OWNER_STEP_SHA256.get((path, job_id))
            if (reviewed_step_sha256 is None
                    or not owner_invoked(job, owner, REVIEWED_OWNER_IF.get((path, job_id)),
                                         reviewed_step_sha256)):
                issues.append(f"{path}/{job_id}: expected executable package owner missing")
            if FLOW_STEPS.search(job):
                issues.append(f"{path}/{job_id}: unsupported YAML flow-mapping step")
            if BLOCK_USES.search(job):
                issues.append(f"{path}/{job_id}: unsupported folded/literal uses scalar")
            uses = list(SETUP_NODE.finditer(job))
            references = list(SETUP_NODE_REFERENCES.finditer(job))
            if (len(uses) != 1 or len(references) != 1
                    or uses[0].start() != references[0].start()):
                issues.append(f"{path}/{job_id}: expected exactly one SHA-pinned setup-node")
                continue
            # Limit inspection to this specific setup-node step, not an
            # unrelated later step with a similar input spelling.
            offset = job.find("\n      - ", uses[0].end())
            step = job[uses[0].start(): offset if offset != -1 else len(job)]
            if not re.search(r"(?m)^        with:\s*$", step):
                issues.append(f"{path}/{job_id}: missing setup-node with block")
            versions = re.findall(r"(?m)^          node-version: (\S+)\s*$", step)
            if versions not in (['"26"'], ["'26'"]):
                issues.append(f"{path}/{job_id}: Node 26 toolchain not pinned")
            flags = INPUT_CACHE.findall(step)
            if flags != ["false"] or len(INPUT_CACHE_KEYS.findall(step)) != 1:
                issues.append(f"{path}/{job_id}: implicit npm cache must be disabled exactly once")
            if INPUT_EXPLICIT_CACHE.search(step):
                issues.append(f"{path}/{job_id}: explicit setup-node cache not permitted")
            # Fail closed on every noncanonical mapping spelling in with.
            # This includes double-quoted YAML escape sequences that decode
            # to 'cache', anchors, aliases, extra/duplicate keys and flow maps.
            with_lines = step.splitlines()
            locations = [i for i, line in enumerate(with_lines) if line == "        with:"]
            if len(locations) != 1:
                issues.append(f"{path}/{job_id}: exactly one canonical setup-node with block required")
            else:
                values = []
                valid_layout = True
                for raw in with_lines[locations[0] + 1:]:
                    if not raw.strip() or raw.lstrip().startswith("#"):
                        continue
                    if not raw.startswith("          ") or raw[10:].startswith((" ", "\t")):
                        valid_layout = False
                        break
                    values.append(raw[10:].strip())
                expected_versions = ('node-version: "26"', "node-version: '26'")
                if (not valid_layout or len(values) != 2
                        or values.count("package-manager-cache: false") != 1
                        or sum(value in expected_versions for value in values) != 1):
                    issues.append(f"{path}/{job_id}: unreviewed setup-node with key or YAML syntax")
    return issues


def one_replacement(
    workflows: dict[str, str], before: str, after: str, path: str = ".github/workflows/ci.yml"
) -> dict[str, str]:
    """Mutate one controlled workflow anchor without modifying other source owners."""
    changed = workflows.copy()
    assert changed[path].count(before) >= 1, f"Mutation anchor missing: {before!r}"
    changed[path] = changed[path].replace(before, after, 1)
    return changed


def main() -> int:
    """Verify reviewed policy and reject all fifty-three bounded source mutations."""
    workflows = {path: (ROOT / path).read_text(encoding="utf-8") for path in EXPECTED}
    installer = (ROOT / "scripts/ci/npm-ci-retry.sh").read_text(encoding="utf-8")
    errors = violations(workflows, installer)
    if errors:
        print("Setup-node cache ownership rejected:\n" + "\n".join(errors))
        return 1

    controls = {
        "auto-cache enabled": one_replacement(
            workflows, "          package-manager-cache: false", "          package-manager-cache: true"
        ),
        "auto-cache omitted": one_replacement(
            workflows, "          package-manager-cache: false", "          # cache setting missing"
        ),
        "manual cache enabled": one_replacement(
            workflows, "          package-manager-cache: false",
            "          package-manager-cache: false\n          cache: npm",
        ),
        "duplicate cache input": one_replacement(
            workflows, "          package-manager-cache: false",
            "          package-manager-cache: false\n          package-manager-cache: false",
        ),
        "spaced explicit npm cache key": one_replacement(
            workflows,
            "          package-manager-cache: false",
            "          package-manager-cache: false\n          cache : npm",
        ),
        "double-quoted explicit npm cache key": one_replacement(
            workflows,
            "          package-manager-cache: false",
            '          package-manager-cache: false\n          "cache": npm',
        ),
        "spaced duplicate auto-cache key": one_replacement(
            workflows,
            "          package-manager-cache: false",
            "          package-manager-cache: false\n          package-manager-cache : true",
        ),
        "single-quoted duplicate auto-cache key": one_replacement(
            workflows,
            "          package-manager-cache: false",
            "          package-manager-cache: false\n          'package-manager-cache': true",
        ),
        # Target a job in EXPECTED, not the unrelated first setup-node
        # occurrence in ci.yml (the actor-replacement helper).
        "lost Node version": one_replacement(
            workflows, '          node-version: "26"', '          node-version: "23"',
            ".github/workflows/qodana-signed-shadow.yml",
        ),
        "lost fixed owner": one_replacement(
            workflows, "scripts/ci/npm-ci-retry.sh frontend",
            "scripts/ci/UNREVIEWED-INSTALL.sh frontend",
        ),
        "owner retained as inline comment": one_replacement(
            workflows, "        run: bash scripts/ci/npm-ci-retry.sh frontend",
            "        run: echo disabled # bash scripts/ci/npm-ci-retry.sh frontend",
        ),
        "owner retained as YAML comment": one_replacement(
            workflows, "        run: bash scripts/ci/npm-ci-retry.sh frontend",
            "        # run: bash scripts/ci/npm-ci-retry.sh frontend\n"
            "        run: echo disabled",
        ),
        "owner path in inert echo": one_replacement(
            workflows, "        run: bash scripts/ci/npm-ci-retry.sh frontend",
            "        run: echo bash scripts/ci/npm-ci-retry.sh frontend",
        ),
        "qodana guard cache regression": one_replacement(
            workflows, "          package-manager-cache: false", "          package-manager-cache: true",
            ".github/workflows/qodana_trusted_gate.yml",
        ),
        "single-quoted typecheck Node drift": one_replacement(
            workflows, "          node-version: '26'", "          node-version: '23'",
            ".github/workflows/thread-functional-typecheck.yml",
        ),
        "signed shadow cache reenabled": one_replacement(
            workflows, "          package-manager-cache: false", "          package-manager-cache: true",
            ".github/workflows/qodana-signed-shadow.yml",
        ),
        "signed shadow cache input omitted": one_replacement(
            workflows, "          package-manager-cache: false", "          # cache setting missing",
            ".github/workflows/qodana-signed-shadow.yml",
        ),
        "extra unpinned setup-node": one_replacement(
            workflows,
            "      - name: Require original SARIF and enforce existing project model gate",
            "      - name: Unreviewed extra setup-node\n"
            "        uses: actions/setup-node@v4\n\n"
            "      - name: Require original SARIF and enforce existing project model gate",
            ".github/workflows/qodana-signed-shadow.yml",
        ),
        "unpinned replacement action": one_replacement(
            workflows,
            "        uses: actions/setup-node@820762786026740c76f36085b0efc47a31fe5020 # v7",
            "        uses: actions/setup-node@v4",
            ".github/workflows/qodana-signed-shadow.yml",
        ),
        "inline unpinned setup-node step": one_replacement(
            workflows,
            "      - name: Require original SARIF and enforce existing project model gate",
            "      - uses: actions/setup-node@v4\n\n"
            "      - name: Require original SARIF and enforce existing project model gate",
            ".github/workflows/qodana-signed-shadow.yml",
        ),
        # Valid YAML allows whitespace before the mapping colon, including
        # inline step declarations. Do not let that evade the broad action count.
        "spaced-key inline unpinned setup-node": one_replacement(
            workflows,
            "      - name: Require original SARIF and enforce existing project model gate",
            "      - uses : actions/setup-node@v4\n\n"
            "      - name: Require original SARIF and enforce existing project model gate",
            ".github/workflows/qodana-signed-shadow.yml",
        ),
        "spaced-key mapped unpinned setup-node": one_replacement(
            workflows,
            "      - name: Require original SARIF and enforce existing project model gate",
            "      - name: Unreviewed spaced-key setup-node\n"
            "        uses : actions/setup-node@v4\n\n"
            "      - name: Require original SARIF and enforce existing project model gate",
            ".github/workflows/qodana-signed-shadow.yml",
        ),
        "double-quoted uses key on extra setup-node": one_replacement(
            workflows,
            "      - name: Require original SARIF and enforce existing project model gate",
            "      - \"uses\": actions/setup-node@v4\n\n"
            "      - name: Require original SARIF and enforce existing project model gate",
            ".github/workflows/qodana-signed-shadow.yml",
        ),
        "single-quoted uses key on extra setup-node": one_replacement(
            workflows,
            "      - name: Require original SARIF and enforce existing project model gate",
            "      - 'uses': actions/setup-node@v4\n\n"
            "      - name: Require original SARIF and enforce existing project model gate",
            ".github/workflows/qodana-signed-shadow.yml",
        ),
        "folded echo is not an owner execution": one_replacement(
            workflows,
            "        run: bash scripts/ci/npm-ci-retry.sh frontend",
            "        run: >-\n"
            "          echo\n"
            "          bash scripts/ci/npm-ci-retry.sh frontend",
        ),
        "heredoc text is not an executed owner": one_replacement(
            workflows,
            '          node scripts/quality/check-qodana-project-model.mjs "$SARIF"',
            "          cat <<'INERT_GUARD_TEXT'\n"
            '          node scripts/quality/check-qodana-project-model.mjs "$SARIF"\n'
            "          INERT_GUARD_TEXT",
            ".github/workflows/qodana-signed-shadow.yml",
        ),
        "flow mapping with unpinned action": one_replacement(
            workflows,
            "      - name: Require original SARIF and enforce existing project model gate",
            "      - {uses: actions/setup-node@v4}\n\n"
            "      - name: Require original SARIF and enforce existing project model gate",
            ".github/workflows/qodana-signed-shadow.yml",
        ),
        "escaped double-quoted explicit cache key": one_replacement(
            workflows,
            "          package-manager-cache: false",
            '          package-manager-cache: false\n          "c\\\\x61che": npm',
        ),
        "inactive shell branch is not owner execution": one_replacement(
            workflows,
            "        run: bash scripts/ci/npm-ci-retry.sh frontend",
            "        run: |\n"
            "          if false; then\n"
            "            bash scripts/ci/npm-ci-retry.sh frontend\n"
            "          fi",
        ),
        "disabled owner step cannot qualify": one_replacement(
            workflows,
            "        if: needs.changes.outputs.frontend == 'true'\n"
            "        run: bash scripts/ci/npm-ci-retry.sh frontend",
            "        if: ${{ false }}\n"
            "        run: bash scripts/ci/npm-ci-retry.sh frontend",
        ),
        "quoted hash before inert heredoc": one_replacement(
            workflows,
            '          node scripts/quality/check-qodana-project-model.mjs "$SARIF"',
            "          printf '%s#' <<'INERT_GUARD_TEXT'\n"
            '          node scripts/quality/check-qodana-project-model.mjs "$SARIF"\n'
            "          INERT_GUARD_TEXT",
            ".github/workflows/qodana-signed-shadow.yml",
        ),
        "folded mutable setup-node action": one_replacement(
            workflows,
            "      - name: Require original SARIF and enforce existing project model gate",
            "      - name: Unreviewed folded setup-node\n"
            "        uses: >-\n"
            "          actions/setup-node@main\n"
            "        with:\n"
            "          cache: npm\n\n"
            "      - name: Require original SARIF and enforce existing project model gate",
            ".github/workflows/qodana-signed-shadow.yml",
        ),
        "early exit before Qodana guard": one_replacement(
            workflows,
            '          node scripts/quality/check-qodana-project-model.mjs "$SARIF"',
            '          exit 0\n          node scripts/quality/check-qodana-project-model.mjs "$SARIF"',
            ".github/workflows/qodana-signed-shadow.yml",
        ),
        "inactive multiline AND-list owner": one_replacement(
            workflows,
            "        run: bash scripts/ci/npm-ci-retry.sh frontend",
            "        run: |\n"
            "          false &&\n"
            "          bash scripts/ci/npm-ci-retry.sh frontend\n"
            "          true",
        ),
        "inactive multiline OR-list owner": one_replacement(
            workflows,
            "        run: bash scripts/ci/npm-ci-retry.sh frontend",
            "        run: |\n"
            "          true ||\n"
            "          bash scripts/ci/npm-ci-retry.sh frontend\n"
            "          true",
        ),
        "double-quoted hash masks early exit before Qodana guard": one_replacement(
            workflows,
            '          node scripts/quality/check-qodana-project-model.mjs "$SARIF"',
            '          printf "%s#" value; exit 0\n'
            '          node scripts/quality/check-qodana-project-model.mjs "$SARIF"',
            ".github/workflows/qodana-signed-shadow.yml",
        ),
        "single-quoted hash masks early exit before Qodana guard": one_replacement(
            workflows,
            '          node scripts/quality/check-qodana-project-model.mjs "$SARIF"',
            "          printf '%s#' value; exit 0\n"
            '          node scripts/quality/check-qodana-project-model.mjs "$SARIF"',
            ".github/workflows/qodana-signed-shadow.yml",
        ),
        "quoted hash masks multiline AND-list owner": one_replacement(
            workflows,
            "        run: bash scripts/ci/npm-ci-retry.sh frontend",
            "        run: |\n"
            "          false && printf '%s#' &&\n"
            "          bash scripts/ci/npm-ci-retry.sh frontend\n"
            "          true",
        ),
        "quoted hash masks escaped newline owner": one_replacement(
            workflows,
            "        run: bash scripts/ci/npm-ci-retry.sh frontend",
            "        run: |\n"
            "          false && printf '%s#' \\\n"
            "          bash scripts/ci/npm-ci-retry.sh frontend\n"
            "          true",
        ),
        "multiline quoted owner is inert": one_replacement(
            workflows,
            "        run: bash scripts/ci/npm-ci-retry.sh frontend",
            "        run: |\n"
            '          echo "\n'
            "          bash scripts/ci/npm-ci-retry.sh frontend\n"
            '          "\n'
            "          true",
        ),
        "function declaration does not invoke installer": one_replacement(
            workflows,
            "        run: bash scripts/ci/npm-ci-retry.sh frontend",
            "        run: |\n"
            "          unused() {\n"
            "          bash scripts/ci/npm-ci-retry.sh frontend\n"
            "          }\n"
            "          true",
        ),
        "skipped brace group does not invoke installer": one_replacement(
            workflows,
            "        run: bash scripts/ci/npm-ci-retry.sh frontend",
            "        run: |\n"
            "          false && {\n"
            "          bash scripts/ci/npm-ci-retry.sh frontend\n"
            "          }\n"
            "          true",
        ),
        "skipped subshell does not invoke installer": one_replacement(
            workflows,
            "        run: bash scripts/ci/npm-ci-retry.sh frontend",
            "        run: |\n"
            "          false && (\n"
            "          bash scripts/ci/npm-ci-retry.sh frontend\n"
            "          )\n"
            "          true",
        ),
        "owner step shell override is rejected": one_replacement(
            workflows,
            "        run: bash scripts/ci/npm-ci-retry.sh frontend",
            "        shell: cat {0}\n"
            "        run: bash scripts/ci/npm-ci-retry.sh frontend",
            ".github/workflows/main-validation.yml",
        ),
        "job default shell override is rejected": one_replacement(
            workflows,
            "  main-frontend-test:\n    name: Main Frontend\n    runs-on: ubuntu-24.04",
            "  main-frontend-test:\n    name: Main Frontend\n    runs-on: ubuntu-24.04\n"
            "    defaults:\n      run:\n        shell: cat {0}",
            ".github/workflows/main-validation.yml",
        ),
        "workflow default shell override is rejected": one_replacement(
            workflows,
            "jobs:\n",
            "defaults:\n  run:\n    shell: cat {0}\njobs:\n",
            ".github/workflows/main-validation.yml",
        ),
        "dash-line false condition on unconditional owner": one_replacement(
            workflows,
            "      - name: Install Angular dependencies\n"
            "        run: bash scripts/ci/npm-ci-retry.sh frontend",
            "      - if: ${{ false }}\n"
            "        name: Install Angular dependencies\n"
            "        run: bash scripts/ci/npm-ci-retry.sh frontend",
            ".github/workflows/main-validation.yml",
        ),
        "dash-line quoted false condition on unconditional owner": one_replacement(
            workflows,
            "      - name: Install Angular dependencies\n"
            "        run: bash scripts/ci/npm-ci-retry.sh frontend",
            '      - "if": ${{ false }}\n'
            "        name: Install Angular dependencies\n"
            "        run: bash scripts/ci/npm-ci-retry.sh frontend",
            ".github/workflows/main-validation.yml",
        ),
        "dash-line spaced false condition on unconditional owner": one_replacement(
            workflows,
            "      - name: Install Angular dependencies\n"
            "        run: bash scripts/ci/npm-ci-retry.sh frontend",
            "      - if : ${{ false }}\n"
            "        name: Install Angular dependencies\n"
            "        run: bash scripts/ci/npm-ci-retry.sh frontend",
            ".github/workflows/main-validation.yml",
        ),
        "quoted false condition on toolchain owner": one_replacement(
            workflows,
            "        run: node scripts/ci/verify-node-toolchain.mjs",
            '        "if": ${{ false }}\n'
            "        run: node scripts/ci/verify-node-toolchain.mjs",
            ".github/workflows/node-toolchain-preflight.yml",
        ),
        "spaced false condition on toolchain owner": one_replacement(
            workflows,
            "        run: node scripts/ci/verify-node-toolchain.mjs",
            "        if : ${{ false }}\n"
            "        run: node scripts/ci/verify-node-toolchain.mjs",
            ".github/workflows/node-toolchain-preflight.yml",
        ),
    }
    for label, changed in controls.items():
        if not violations(changed, installer):
            raise AssertionError(f"{label} mutation passed the cache ownership check")
    if not violations(workflows, installer.replace('export NPM_CONFIG_CACHE="$secure_cache"', '')):
        raise AssertionError("missing isolated npm cache boundary was accepted")
    if not violations(workflows, installer.replace('export NPM_CONFIG_STRICT_ALLOW_SCRIPTS=', '')):
        raise AssertionError("missing npm lifecycle policy boundary was accepted")

    print("Setup-node unused-cache policy PASS; 53 mutation controls rejected.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
