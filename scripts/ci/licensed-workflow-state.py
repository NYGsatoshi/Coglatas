"""Read one fixed Actions workflow state; never changes execution policy."""
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import urllib.error
import urllib.request

WORKFLOW_ID = 348966932
WORKFLOW_PATH = ".github/workflows/licensed-real-backend-acceptance.yml"
ENDPOINT = "https://api.github.com/repos/NYGsatoshi/Coglatas/actions/workflows/348966932"
STATES = frozenset(("active", "deleted", "disabled_fork", "disabled_inactivity", "disabled_manually"))
OUTPUT = Path("artifacts/licensed-workflow-state/state.json")
CODES = frozenset(("MALFORMED_PROVIDER_RESPONSE", "RESPONSE_SIZE", "WORKFLOW_IDENTITY",
                   "UNKNOWN_PROVIDER_STATE", "MISSING_READ_TOKEN", "PROVIDER_STATUS",
                   "EXECUTION_IDENTITY", "UNCLEAN_OR_STALE_SOURCE"))


class Refused(ValueError):
    pass


def require(value, code):
    if not value:
        raise Refused(code)


def pairs(values):
    result = {}
    for key, value in values:
        require(key not in result, "MALFORMED_PROVIDER_RESPONSE")
        result[key] = value
    return result


def project(raw):
    require(isinstance(raw, bytes) and len(raw) <= 65536, "RESPONSE_SIZE")
    data = json.loads(raw.decode("utf-8"), object_pairs_hook=pairs,
                      parse_constant=lambda _: (_ for _ in ()).throw(Refused("MALFORMED_PROVIDER_RESPONSE")))
    require(isinstance(data, dict) and type(data.get("id")) is int
            and data["id"] == WORKFLOW_ID and data.get("path") == WORKFLOW_PATH,
            "WORKFLOW_IDENTITY")
    require(type(data.get("state")) is str and data["state"] in STATES, "UNKNOWN_PROVIDER_STATE")
    # Ignore and never serialize arbitrary provider names, URLs, timestamps or bodies.
    return dict(workflowId=WORKFLOW_ID, providerState=data["state"],
                rawResponseSha256=hashlib.sha256(raw).hexdigest(), responseBytes=len(raw))


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, request, response, code, message, headers, target):
        return None


def fetch(token, opener=None):
    require(type(token) is str and bool(token), "MISSING_READ_TOKEN")
    request = urllib.request.Request(ENDPOINT, headers={
        "Authorization": "Bearer " + token,
        "Accept": "application/vnd.github+json", "X-GitHub-Api-Version": "2022-11-28",
    })
    # Redirects are refused, including official-host redirects. The token is
    # attached only to this one constant API URL and is never forwarded.
    client = opener if opener is not None else urllib.request.build_opener(NoRedirect())
    try:
        with client.open(request, timeout=30) as response:
            require(response.status == 200 and response.geturl() == ENDPOINT, "PROVIDER_STATUS")
            raw = response.read(65537)
    except urllib.error.HTTPError:
        raise Refused("PROVIDER_STATUS") from None
    return project(raw)


def binding(environment, git=subprocess.check_output):
    source = environment.get("GITHUB_SHA", "")
    run = environment.get("GITHUB_RUN_ID", "")
    attempt = environment.get("GITHUB_RUN_ATTEMPT", "")
    require(re.fullmatch(r"[0-9a-f]{40}", source) is not None
            and re.fullmatch(r"[1-9][0-9]{0,19}", run) is not None
            and re.fullmatch(r"[1-9][0-9]{0,8}", attempt) is not None, "EXECUTION_IDENTITY")
    actual = git(["git", "rev-parse", "HEAD"], stderr=subprocess.DEVNULL).decode("ascii").strip()
    dirty = git(["git", "status", "--porcelain", "--untracked-files=no"], stderr=subprocess.DEVNULL)
    require(actual == source and not dirty, "UNCLEAN_OR_STALE_SOURCE")
    return dict(sourceHead=source, runId=run, runAttempt=attempt)


def execute(environment, read=fetch, git=subprocess.check_output):
    result = dict(schemaVersion=1, scope="FIXED_READ_ONLY_WORKFLOW_STATE",
                  verification="NOT_VERIFIED", functionalAcceptance="NOT_EVALUATED",
                  policyChanged=False, licenseAvailability="UNKNOWN", workflowSelectionCause="UNKNOWN")
    try:
        result.update(binding(environment, git))
        result.update(read(environment.get("GH_TOKEN", "")))
        result["verification"] = "PROVIDER_STATE_READ"
        return result, 0
    except Refused as error:
        result["failure"] = error.args[0] if error.args and error.args[0] in CODES else "UNCLASSIFIED_READ_FAILURE"
    except Exception:
        # Network/library exception strings can contain request headers or URLs.
        result["failure"] = "UNCLASSIFIED_READ_FAILURE"
    return result, 1


def main():
    result, code = execute(os.environ)
    rendered = json.dumps(result, sort_keys=True, indent=2)
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    OUTPUT.write_text(rendered + "\n", encoding="utf-8")
    print(rendered)
    return code


if __name__ == "__main__":
    sys.exit(main())
