#!/usr/bin/env python3
"""Bind a new advisory context to its actual immutable PR workflow commit."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess


REPOSITORY = "NYGsatoshi/Coglatas"
FETCH_URL = f"https://github.com/{REPOSITORY}.git"
ROOT = "tests/candidates/workflow-provenance"
VERIFIER_PATH = f"{ROOT}/verify_workflow_provenance.py"
CONTROLS_PATH = f"{ROOT}/test_workflow_provenance.py"
FLAGS = (
    "requiredCheckQualified", "independentReviewComplete", "nativeUiQualified",
    "productionQualified", "trustQualified", "releaseQualified",
)
SHA = re.compile(r"[0-9a-f]{40}")
WORKFLOW = re.compile(r"\.github/workflows/projectide-[a-z0-9-]+-advisory\.yml")


class Refusal(ValueError):
    """Source identities do not establish the reviewed executing workflow."""


def git(repo: Path, *args: str, check: bool = True) -> subprocess.CompletedProcess:
    result = subprocess.run(
        ["git", "--no-replace-objects", "-C", str(repo), *args],
        capture_output=True, check=False,
        env={key: value for key, value in os.environ.items()
             if not key.upper().startswith("GIT_")},
    )
    if check and result.returncode:
        raise Refusal(f"Git source operation failed: {args[0]}")
    return result


def exact_sha(value: str, name: str) -> str:
    if not isinstance(value, str) or not SHA.fullmatch(value):
        raise Refusal(f"{name} must be a complete lowercase Git SHA")
    return value


def oid(repo: Path, expression: str) -> str:
    return exact_sha(git(repo, "rev-parse", "--verify", expression).stdout.decode().strip(), "Git object")


def ensure_workflow_commit(repo: Path, sha: str) -> bool:
    result = git(repo, "cat-file", "-t", sha, check=False)
    fetched = result.returncode != 0
    if fetched:
        # A moving merge ref or the checked-out Test head is never a substitute.
        git(repo, "-c", "protocol.version=2", "fetch", "--no-tags",
            "--no-recurse-submodules", FETCH_URL, sha)
        result = git(repo, "cat-file", "-t", sha)
    if result.stdout != b"commit\n" or oid(repo, f"{sha}^{{commit}}") != sha:
        raise Refusal("Executing workflow object is not the exact immutable commit")
    return fetched


def blob(repo: Path, revision: str, path: str) -> str:
    rows = git(repo, "ls-tree", "-z", revision, "--", path).stdout.split(b"\0")
    if len(rows) != 2 or rows[1] != b"":
        raise Refusal("Expected exactly one committed workflow or verifier file")
    metadata, actual_path = rows[0].split(b"\t", 1)
    mode, kind, sha = metadata.split(b" ")
    if mode != b"100644" or kind != b"blob" or actual_path != path.encode():
        raise Refusal("Workflow and verifier inputs must be exact regular 100644 blobs")
    return exact_sha(sha.decode(), "File blob")


def bind_source_file(repo: Path, revision: str, path: str, expected: str) -> None:
    expected = exact_sha(expected, "Pinned verifier blob")
    source = repo / path
    if source.is_symlink() or not source.is_file() or blob(repo, revision, path) != expected:
        raise Refusal("Pinned verifier source does not match the reviewed Test commit")
    data = source.read_bytes()
    actual = hashlib.sha1(b"blob " + str(len(data)).encode() + b"\0" + data).hexdigest()
    if actual != expected:
        raise Refusal("Checked-out verifier bytes differ from the pinned Git blob")


def verify(repo: Path, workflow_path: str, context: dict, env: dict) -> dict:
    if not WORKFLOW.fullmatch(workflow_path):
        raise Refusal("Workflow path is not a canonical ProjectIDE advisory path")
    test = exact_sha(env.get("TEST_HEAD"), "Test head")
    base = exact_sha(env.get("BASE_HEAD"), "Security base")
    workflow = exact_sha(env.get("WORKFLOW_HEAD"), "Executing workflow head")
    pr = env.get("PULL_REQUEST_NUMBER", "")
    if not isinstance(pr, str) or not re.fullmatch(r"[1-9][0-9]*", pr):
        raise Refusal("Pull request number is not canonical")
    expected_ref = f"{REPOSITORY}/{workflow_path}@refs/pull/{pr}/merge"
    if env.get("WORKFLOW_REF") != expected_ref:
        raise Refusal("Executing workflow ref does not match the repository, path and PR")
    if not isinstance(context, dict) or context.get("testHead") != test:
        raise Refusal("Existing source context does not belong to this Test head")
    if any(context.get(flag) is not False for flag in FLAGS):
        raise Refusal("All qualification holds must remain explicitly false")
    if any(key.startswith("workflow") for key in context):
        raise Refusal("Refusing to restamp a context that already has workflow provenance")
    if oid(repo, "HEAD^{commit}") != test:
        raise Refusal("Checkout differs from the immutable Test head")
    for revision in (test, base):
        if git(repo, "cat-file", "-t", revision).stdout != b"commit\n":
            raise Refusal("Test and base identities must be commits")
    if git(repo, "merge-base", "--is-ancestor", base, test, check=False).returncode != 0:
        raise Refusal("The reviewed Test source does not contain the exact Security base")
    bind_source_file(repo, test, VERIFIER_PATH, env.get("PROVENANCE_BLOB"))
    bind_source_file(repo, test, CONTROLS_PATH, env.get("PROVENANCE_CONTROLS_BLOB"))
    fetched = ensure_workflow_commit(repo, workflow)
    parents = git(repo, "show", "-s", "--format=%P", workflow).stdout.decode().strip().split()
    if parents != [base, test]:
        raise Refusal("Executing workflow must have exactly the expected base and Test parents")
    workflow_blob = blob(repo, workflow, workflow_path)
    test_blob = blob(repo, test, workflow_path)
    if workflow_blob != test_blob:
        raise Refusal("Executing workflow content differs from the reviewed Test workflow")
    workflow_tree = oid(repo, f"{workflow}^{{tree}}")
    test_tree = oid(repo, f"{test}^{{tree}}")
    if workflow_tree != test_tree:
        raise Refusal("Executing workflow tree differs from the complete reviewed Test tree")
    return {
        "workflowProvenance": "EXECUTING_WORKFLOW_SHA_V1",
        "workflowHead": workflow,
        "workflowRef": expected_ref,
        "workflowPath": workflow_path,
        "workflowTree": workflow_tree,
        "workflowBlob": workflow_blob,
        "workflowMode": "100644",
        "workflowParents": parents,
        "workflowBase": base,
        "workflowFetched": fetched,
        "testTree": test_tree,
        "testWorkflowBlob": test_blob,
        "testWorkflowMode": "100644",
        "baseTree": oid(repo, f"{base}^{{tree}}"),
        "provenanceBlob": env["PROVENANCE_BLOB"],
        "provenanceControlsBlob": env["PROVENANCE_CONTROLS_BLOB"],
    }


def qualify(repo: Path, workflow_path: str, context_path: Path, env: dict) -> dict:
    if context_path.is_symlink() or not context_path.is_file():
        raise Refusal("A new regular source context is required")
    context = json.loads(context_path.read_text(encoding="utf-8-sig"))
    binding = verify(repo, workflow_path, context, env)
    complete = {**context, **binding}
    temporary = context_path.with_suffix(".provenance.tmp")
    with temporary.open("x", encoding="utf-8", newline="\n") as output:
        json.dump(complete, output, indent=2)
        output.write("\n")
    temporary.replace(context_path)
    return complete


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--workflow-path", required=True)
    parser.add_argument("--context", type=Path, required=True)
    args = parser.parse_args()
    try:
        result = qualify(Path.cwd(), args.workflow_path, args.context, dict(os.environ))
    except (Refusal, OSError, ValueError) as error:
        parser.exit(1, f"Workflow provenance refused: {error}\n")
    print(json.dumps({"status": "VERIFIED_EXECUTING_WORKFLOW", **{
        key: result[key] for key in ("workflowHead", "workflowRef", "workflowTree", "testHead")
    }}))


if __name__ == "__main__":
    main()
