"""Execute and inventory the exact isolated Web candidate; preserve failures honestly."""
from __future__ import annotations
import argparse
import hashlib
import json
import os
from pathlib import Path
import platform
import re
import subprocess
import sys
import xml.etree.ElementTree as ET
from projectide_core_isolated import inventory

def assert_candidate(root: Path, value: dict) -> None:
    """A matching HEAD tree alone does not prove the tracked checkout was executed."""
    def git(*arguments: str) -> str:
        return subprocess.check_output(["git", *arguments], cwd=root, text=True).strip()
    if git("rev-parse", "HEAD") != value["combinedCommit"]:
        raise ValueError("Candidate commit mismatch")
    if git("rev-parse", "HEAD^{tree}") != value["combinedTree"]:
        raise ValueError("Candidate tree mismatch")
    if git("status", "--porcelain", "--untracked-files=no"):
        raise ValueError("Candidate tracked checkout contains uncommitted changes")

def vitest_inventory(value: dict) -> list[dict]:
    tests = [dict(file=f["name"], name=t["fullName"], status=t["status"])
             for f in value["testResults"] for t in f["assertionResults"]]
    if (not value["success"] or not tests or value["numTotalTests"] != len(tests) or
            value["numPassedTests"] != len(tests) or any(value[key] for key in
                ["numFailedTests", "numPendingTests", "numTodoTests"]) or
            any(not t["file"] or not t["name"] or t["status"] != "passed" for t in tests) or
            len({(t["file"], t["name"]) for t in tests}) != len(tests)):
        raise ValueError("Incomplete, duplicated or inconsistent Vitest results")
    return tests

def browser_inventory(path: Path) -> list[dict]:
    root = ET.parse(path).getroot()
    tests = []
    for suite in root.findall(".//testsuite"):
        cases = suite.findall("testcase")
        if int(suite.get("tests", "-1")) != len(cases) or any(int(suite.get(key, "0")) for key in ["failures", "errors", "skipped"]):
            raise ValueError("Browser suite counters disagree with executed cases")
        for case in cases:
            if any(case.find(tag) is not None for tag in ["failure", "error", "skipped"]):
                raise ValueError("Failed or skipped browser result")
            tests.append(dict(project=suite.get("hostname"), name=case.get("name"), classname=case.get("classname")))
    if (not tests or int(root.get("tests", "-1")) != len(tests) or
            any(int(root.get(key, "0")) for key in ["failures", "errors", "skipped"]) or
            any(not all(t.values()) for t in tests) or
            {t["project"] for t in tests} != {"chromium-desktop", "chromium-mobile"} or
            len({(t["project"], t["classname"], t["name"]) for t in tests}) != len(tests)):
        raise ValueError("Incomplete, duplicated or unbound desktop/mobile browser results")
    return tests

def verify(root: Path, receipt: Path) -> None:
    evidence=receipt.parent
    value=json.loads(receipt.read_text(encoding="utf-8"))
    value.update(os=platform.platform(), commands=[], status="VERIFYING")
    environment=os.environ.copy(); environment["NG_BUILD_MAX_WORKERS"]="4"; environment["PLAYWRIGHT_SKIP_BUILD"]="1"
    def execute(name: str, command: list[str], cwd: Path=root) -> None:
        with (evidence/(name+".log")).open("w",encoding="utf-8") as output:
            result=subprocess.run(command,cwd=cwd,env=environment,stdout=output,stderr=subprocess.STDOUT)
        value["commands"].append(dict(name=name,command=command,cwd=str(cwd),exitCode=result.returncode))
        print(name, result.returncode, flush=True)
        if result.returncode: raise RuntimeError(name+" failed; inspect retained log")
    try:
        assert_candidate(root, value)
        value["node"]=subprocess.check_output(["node","--version"],text=True).strip()
        value["sdk"]=subprocess.check_output(["dotnet","--version"],cwd=root,text=True).strip()
        execute("composition-controls",[sys.executable,"-m","unittest","discover","-s","tests/ci","-p","test_projectide*.py","-v"])
        execute("fixture-export",["dotnet","run","--project","tools/ProjectIde.Preview","-c","Release","--",str(evidence/"preview-reproduced.json")])
        if (evidence/"preview-reproduced.json").read_text(encoding="utf-8") != (root/"frontend/public/assets/projectide/preview-v1.json").read_text(encoding="utf-8"):
            raise ValueError("Committed fixture differs from actual Core exporter")
        execute("dotnet-build",["dotnet","build","Coglatas.slnx","-c","Release","--nologo","-warnaserror","--disable-build-servers","-m:1"])
        for name,project,filter_value in [("projectide","tests/Coglatas.Tests/Coglatas.Tests.csproj","FullyQualifiedName~ProjectIde"),("architecture","tests/Coglatas.Architecture.Tests/Coglatas.Architecture.Tests.csproj",None)]:
            command=["dotnet","test",project,"-c","Release","--no-build","--logger",f"trx;LogFileName={name}.trx","--results-directory",str(evidence)]
            if filter_value:command += ["--filter",filter_value]
            execute(name,command)
            value[name+"Tests"]=inventory(evidence/(name+".trx"))
        cli=["node","node_modules/@angular/cli/bin/ng.js"]
        frontend=root/"frontend"
        execute("typecheck",["node","node_modules/typescript/bin/tsc","--noEmit","-p","tsconfig.app.json"],frontend)
        execute("frontend-architecture",["node","scripts/check-architecture.mjs"],frontend)
        execute("route-inventory",["node","scripts/check-av-mig-source.mjs"],frontend)
        execute("frontend-node-tests",["node","--test","--test-reporter=tap",*[str(p.relative_to(frontend)) for p in sorted((frontend/"scripts").glob("*.node-test.mjs"))]],frontend)
        node_tests=(evidence/"frontend-node-tests.log").read_text(encoding="utf-8")
        counts={key:int(re.search(r"^# "+key+r" (\d+)$",node_tests,re.M)[1]) for key in ["tests","pass","fail","cancelled","skipped","todo"]}
        if not counts["tests"] or counts["pass"] != counts["tests"] or any(counts[key] for key in ["fail","cancelled","skipped","todo"]): raise ValueError("Incomplete frontend Node results")
        value["frontendNodeCounts"]=counts
        value["frontendNodeTests"]=re.findall(r"^# Subtest: (.+)$",node_tests,re.M)
        execute("angular-build",cli+["build","--configuration","production"],frontend)
        execute("sanitize-themes",["node","scripts/sanitize-syncfusion-theme-css.mjs"],frontend)
        command=cli+["test","--watch=false","--reporters=json","--output-file="+str(evidence/"vitest.json")]
        for pattern in ["src/app/features/projectide/*.spec.ts","src/app/features/projects/project-detail*.spec.ts","src/app/features/projects/project-detail-page/*.spec.ts","src/app/features/projects/task-table/*.spec.ts","src/app/shared/ui/adapters/syncfusion/*.spec.ts","src/app/app.routes.spec.ts","src/app/core/feature-flags/frontend-feature-flags.service.spec.ts","src/app/core/auth/auth-session.guard.spec.ts","src/app/core/i18n/i18n.service.spec.ts"]:
            command += ["--include",pattern]
        execute("vitest",command,frontend)
        tests=json.loads((evidence/"vitest.json").read_text(encoding="utf-8"))
        value["vitestCounts"]={k:tests[k] for k in ["numTotalTests","numPassedTests","numFailedTests","numPendingTests","numTodoTests"]}
        value["vitestTests"]=vitest_inventory(tests)
        environment["PLAYWRIGHT_JUNIT_OUTPUT_FILE"]=str(evidence/"playwright.xml")
        execute("playwright",["node","node_modules/@playwright/test/cli.js","test","tests/ui/projectide-workbench.spec.ts","tests/ui/angular-smoke.spec.ts","--grep","ProjectIDE synthetic|canonical Project Kanban|maintained Project Task List|canonical Schedule tab|maintained read-only Schedule","--reporter=list,junit","--output="+str(evidence/"browser")])
        value["browserTests"]=browser_inventory(evidence/"playwright.xml")
        assert_candidate(root, value)
        value["status"]="LOCAL_VERIFIED" if platform.system()=="Windows" else "ISOLATED_CI_VERIFIED"
    except Exception as error:
        value["status"]="FAILED"; value["failure"]=str(error); raise
    finally:
        value["artifactHashes"]={str(p.relative_to(evidence)).replace("\\","/"):hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(evidence.rglob("*")) if p.is_file() and p != receipt}
        receipt.write_text(json.dumps(value,indent=2)+"\n",encoding="utf-8")

if __name__ == "__main__":
    parser=argparse.ArgumentParser();parser.add_argument("--worktree",type=Path,required=True);parser.add_argument("--receipt",type=Path,required=True)
    args=parser.parse_args();verify(args.worktree.resolve(),args.receipt.resolve())
