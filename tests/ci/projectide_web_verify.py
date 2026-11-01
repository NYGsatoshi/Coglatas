"""Execute and inventory the exact isolated Web candidate; preserve failures honestly."""
from __future__ import annotations
import argparse
import hashlib
import json
import os
from pathlib import Path
import platform
import subprocess
import xml.etree.ElementTree as ET
from projectide_core_isolated import inventory

def verify(root: Path, receipt: Path) -> None:
    evidence=receipt.parent
    value=json.loads(receipt.read_text(encoding="utf-8"))
    actual=subprocess.check_output(["git","rev-parse","HEAD^{tree}"],cwd=root,text=True).strip()
    if actual != value["combinedTree"]: raise ValueError("Candidate tree mismatch")
    value.update(os=platform.platform(), commands=[], status="VERIFYING")
    environment=os.environ.copy(); environment["NG_BUILD_MAX_WORKERS"]="4"; environment["PLAYWRIGHT_SKIP_BUILD"]="1"
    def execute(name: str, command: list[str], cwd: Path=root) -> None:
        with (evidence/(name+".log")).open("w",encoding="utf-8") as output:
            result=subprocess.run(command,cwd=cwd,env=environment,stdout=output,stderr=subprocess.STDOUT)
        value["commands"].append(dict(name=name,command=command,cwd=str(cwd),exitCode=result.returncode))
        print(name, result.returncode, flush=True)
        if result.returncode: raise RuntimeError(name+" failed; inspect retained log")
    try:
        value["node"]=subprocess.check_output(["node","--version"],text=True).strip()
        value["sdk"]=subprocess.check_output(["dotnet","--version"],cwd=root,text=True).strip()
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
        execute("angular-build",cli+["build","--configuration","production"],frontend)
        execute("sanitize-themes",["node","scripts/sanitize-syncfusion-theme-css.mjs"],frontend)
        command=cli+["test","--watch=false","--reporters=json","--output-file="+str(evidence/"vitest.json")]
        for pattern in ["src/app/features/projectide/*.spec.ts","src/app/features/projects/project-detail*.spec.ts","src/app/features/projects/project-detail-page/*.spec.ts","src/app/features/projects/task-table/*.spec.ts","src/app/shared/ui/adapters/syncfusion/*.spec.ts","src/app/app.routes.spec.ts","src/app/core/feature-flags/frontend-feature-flags.service.spec.ts","src/app/core/auth/auth-session.guard.spec.ts","src/app/core/i18n/i18n.service.spec.ts"]:
            command += ["--include",pattern]
        execute("vitest",command,frontend)
        tests=json.loads((evidence/"vitest.json").read_text(encoding="utf-8"))
        if not tests["success"] or tests["numTotalTests"] == 0 or tests["numFailedTests"] or tests["numPendingTests"] or tests["numTodoTests"]: raise ValueError("Incomplete Vitest results")
        value["vitestCounts"]={k:tests[k] for k in ["numTotalTests","numPassedTests","numFailedTests","numPendingTests","numTodoTests"]}
        value["vitestTests"]=[dict(file=f["name"],name=t["fullName"],status=t["status"]) for f in tests["testResults"] for t in f["assertionResults"]]
        environment["PLAYWRIGHT_JUNIT_OUTPUT_FILE"]=str(evidence/"playwright.xml")
        execute("playwright",["node","node_modules/@playwright/test/cli.js","test","tests/ui/projectide-workbench.spec.ts","tests/ui/angular-smoke.spec.ts","--grep","ProjectIDE synthetic|canonical Project Kanban|maintained Project Task List|canonical Schedule tab|maintained read-only Schedule","--reporter=list,junit","--output="+str(evidence/"browser")])
        suites=ET.parse(evidence/"playwright.xml").getroot()
        results=suites.findall(".//testcase")
        if not results or any(t.find(tag) is not None for t in results for tag in ["failure","error","skipped"]): raise ValueError("Failed, skipped or empty browser run")
        value["browserTests"]=[dict(name=t.get("name"),classname=t.get("classname")) for t in results]
        value["status"]="LOCAL_VERIFIED" if platform.system()=="Windows" else "ISOLATED_CI_VERIFIED"
    except Exception as error:
        value["status"]="FAILED"; value["failure"]=str(error); raise
    finally:
        value["artifactHashes"]={str(p.relative_to(evidence)).replace("\\","/"):hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(evidence.rglob("*")) if p.is_file() and p != receipt}
        receipt.write_text(json.dumps(value,indent=2)+"\n",encoding="utf-8")

if __name__ == "__main__":
    parser=argparse.ArgumentParser();parser.add_argument("--worktree",type=Path,required=True);parser.add_argument("--receipt",type=Path,required=True)
    args=parser.parse_args();verify(args.worktree.resolve(),args.receipt.resolve())
