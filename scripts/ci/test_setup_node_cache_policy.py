#!/usr/bin/env python3
"""Guard reviewed setup-node cache ownership with offline negative controls.

An implicit setup-node cache writes ~/.npm across runs. These jobs install
dependencies via scripts/ci/npm-ci-retry.sh with a separate per-job cache, or
only run a Node verifier. Disable the redundant cross-run cache, not the
version-pinned npm installation, test execution or dependency protections.
"""
from __future__ import annotations

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

JOB = re.compile(r"(?m)^  ([A-Za-z][A-Za-z0-9_-]*):\s*$")
SETUP_NODE = re.compile(r"(?m)^        uses: actions/setup-node@([0-9a-f]{40})\s*(?:#.*)?$")
# Count *all* setup-node refs, not only SHA-pinned ones. A second unpinned
# action must not be invisible to the fixed-source policy.
SETUP_NODE_REFERENCES = re.compile(r"(?m)^(?:        |      -[ \t]+)(?:uses|['\"]uses['\"])[ \t]*:[ \t]*['\"]?actions/setup-node@")
INPUT_CACHE = re.compile(r"(?m)^          package-manager-cache: (\S+)\s*$")
INPUT_EXPLICIT_CACHE = re.compile(r"(?m)^          cache:.*$")


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


def run_command_lines(job: str) -> list[str]:
    """Decode reviewed plain, literal and flat folded run scalars fail-closed.

    Folded (>-) YAML joins adjacent non-empty physical lines with spaces.
    Treat blank-separated and more-indented folded forms as unsupported
    rather than trusting a physical line that YAML does not execute alone.
    """
    commands: list[str] = []
    style: str | None = None
    folded: list[str] = []
    pending_blank = False
    unsupported = False

    def finish() -> None:
        nonlocal style, folded, pending_blank, unsupported
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
            command = fragment.split("#", 1)[0].strip()
            if command:
                commands.append(command)
        else:
            # A nested indent or a separated folded paragraph may have
            # different YAML newline semantics: decline unsupported forms.
            if pending_blank or fragment.startswith((" ", "\t")):
                unsupported = True
            folded.append(fragment.strip())
            pending_blank = False

    finish()
    return commands

def owner_invoked(job: str, owner: str) -> bool:
    """Require an active canonical Bash/Node command, never echo or path mentions."""
    interpreter = "node" if owner.endswith(".mjs") else "bash"
    canonical = re.compile(rf"^{interpreter}[ \t]+{re.escape(owner)}(?:[ \t]|$)")
    # Evaluate per actual YAML step, so an unrelated command cannot lend
    # execution authority to an inert echo or a test path in another step.
    for step in re.split(r"(?m)(?=^      - )", job):
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
        jobs = job_blocks(source)
        for job_id, owner in targets.items():
            job = jobs.get(job_id)
            if job is None:
                issues.append(f"{path}: missing job {job_id}")
                continue
            if not owner_invoked(job, owner):
                issues.append(f"{path}/{job_id}: expected executable package owner missing")
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
            if flags != ["false"]:
                issues.append(f"{path}/{job_id}: implicit npm cache must be disabled exactly once")
            if INPUT_EXPLICIT_CACHE.search(step):
                issues.append(f"{path}/{job_id}: explicit setup-node cache not permitted")
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
    """Verify real workflow policy and fail closed on all twenty-three mutation controls."""
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
    }
    for label, changed in controls.items():
        if not violations(changed, installer):
            raise AssertionError(f"{label} mutation passed the cache ownership check")
    if not violations(workflows, installer.replace('export NPM_CONFIG_CACHE="$secure_cache"', '')):
        raise AssertionError("missing isolated npm cache boundary was accepted")
    if not violations(workflows, installer.replace('export NPM_CONFIG_STRICT_ALLOW_SCRIPTS=', '')):
        raise AssertionError("missing npm lifecycle policy boundary was accepted")

    print("Setup-node unused-cache policy PASS; 23 mutation controls rejected.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
