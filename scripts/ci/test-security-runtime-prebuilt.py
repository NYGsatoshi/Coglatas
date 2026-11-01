#!/usr/bin/env python3
"""Verify the PR-only no-recompile Docker image contract and routing guards."""
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[2]
WORKFLOW = ROOT / ".github/workflows/ci.yml"
DOCKERFILE = ROOT / "infra/docker/security-runtime-pr-prebuilt.Dockerfile"
IGNORE = ROOT / "infra/docker/security-runtime-pr-prebuilt.Dockerfile.dockerignore"


class SecurityRuntimePrebuiltTests(unittest.TestCase):
    def test_prebuilt_sdk_image_cannot_compile_or_publish_product(self):
        source = DOCKERFILE.read_text(encoding="utf-8")
        self.assertRegex(source, r"(?m)^FROM mcr[.]microsoft[.]com/dotnet/sdk:10[.]0[.]403@sha256:[a-f0-9]{64}$")
        self.assertIn("COPY . .", source)
        self.assertIn("dotnet tool restore", source)
        self.assertIn("dotnet restore src/Coglatas.Web/Coglatas.Web.csproj", source)
        self.assertIn("dotnet ef", (ROOT / "infra/compose/security/runtime.yml").read_text())
        self.assertNotRegex(source, r"(?m)^[^#\\n]*\\bdotnet\\s+(build|publish|test|run)\\b")
        for required in (
            "src/Coglatas.Web/bin/Release/net10.0/Coglatas.Web.dll",
            "src/Coglatas.Infrastructure/bin/Release/net10.0/Coglatas.Infrastructure.dll",
            "src/Coglatas.Web/obj/project.assets.json",
        ):
            self.assertIn("test -s " + required, source)

    def test_only_intended_compiled_source_may_enter_docker_context(self):
        patterns = [line.strip() for line in IGNORE.read_text().splitlines()
                    if line.strip() and not line.startswith("#")]
        self.assertEqual("**", patterns[0])
        for required in ("!src/", "!src/**", "!global.json",
                         "!.config/", "!.config/dotnet-tools.json"):
            self.assertIn(required, patterns)
        for excluded in ("**/.env", "**/.env.*", "**/secrets/**",
                         "**/storage/**", "**/data/**", "**/*.log"):
            self.assertIn(excluded, patterns)
        # The only top-level admission is source + .NET toolchain config.
        allowed = [line for line in patterns if line.startswith("!")]
        self.assertEqual({"!src/", "!src/**", "!global.json",
                          "!.config/", "!.config/dotnet-tools.json"}, set(allowed))

    def test_pr_route_requires_existing_source_stamp_and_exact_binaries(self):
        workflow = WORKFLOW.read_text(encoding="utf-8")
        scope = workflow.split("      - name: Build reusable security runtime image", 1)[1].split("\n      - name:", 1)[0]
        for expression in (
            'if [[ "$GITHUB_EVENT_NAME" == "pull_request" ]]',
            '[[ "$expected" == "$GITHUB_SHA" ]]',
            'stamp="artifacts/ci/dotnet-build-sha"',
            '[[ -f "$stamp" && ! -L "$stamp" ]]',
            '"$(tr -d \'\\r\\n\' < "$stamp")" == "$expected"',
            '[[ -s src/Coglatas.Web/bin/Release/net10.0/Coglatas.Web.dll ]]',
            '[[ -f src/Coglatas.Web/obj/project.assets.json ]]',
            'dockerfile="infra/docker/security-runtime-pr-prebuilt.Dockerfile"',
            'docker build --file "$dockerfile" --tag "$image" .',
        ):
            self.assertIn(expression, scope)
        self.assertIn('dockerfile="infra/docker/security-runtime.Dockerfile"', scope)
        self.assertIn('run: python3 -B scripts/ci/test-security-runtime-prebuilt.py', workflow)

    def test_no_build_negative_mutation_is_detected(self):
        source = DOCKERFILE.read_text()
        self.assertTrue(re.search(r"(?m)^[^#\\n]*\\bdotnet\\s+(build|publish)\\b",
                                  source + "\nRUN dotnet build src/Coglatas.Web/Coglatas.Web.csproj"))
        self.assertFalse(re.search(r"(?m)^[^#\\n]*\\bdotnet\\s+(build|publish)\\b", source))


if __name__ == "__main__":
    unittest.main()
