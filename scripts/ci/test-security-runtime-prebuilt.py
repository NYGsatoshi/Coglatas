#!/usr/bin/env python3
"""Verify the PR-only no-recompile Docker image contract and routing guards."""
from pathlib import Path
import json
import re
import unittest

ROOT = Path(__file__).resolve().parents[2]
WORKFLOW = ROOT / ".github/workflows/ci.yml"
DOCKERFILE = ROOT / "infra/docker/security-runtime-pr-prebuilt.Dockerfile"
IGNORE = ROOT / "infra/docker/security-runtime-pr-prebuilt.Dockerfile.dockerignore"


def docker_from_directives(source: str) -> list[str]:
    """Return all Docker FROM stages, including unsupported extra or case-varied stages."""
    return [
        line.strip() for line in source.splitlines()
        if re.match(r"(?i)^FROM(?:\s|$)", line.strip())
    ]


class SecurityRuntimePrebuiltTests(unittest.TestCase):
    def test_prebuilt_sdk_image_cannot_compile_or_publish_product(self):
        source = DOCKERFILE.read_text(encoding="utf-8")
        # Bind to the independent canonical image and pinned global.json
        # instead of freezing a superseded SDK patch version in this test.
        # Reject invalid or multiple canonical build stages fail-closed.
        toolchain = json.loads((ROOT / "global.json").read_text(encoding="utf-8"))
        sdk_version = toolchain["sdk"]["version"]
        canonical_source = (ROOT / "Dockerfile").read_text(encoding="utf-8")
        canonical_images = re.findall(
            r"(?m)^FROM (mcr[.]microsoft[.]com/dotnet/sdk:"
            r"(\d+[.]\d+[.]\d+)@sha256:[a-f0-9]{64}) AS build$",
            canonical_source,
        )
        self.assertEqual(1, len(canonical_images))
        canonical_image, canonical_version = canonical_images[0]
        self.assertEqual(sdk_version, canonical_version)
        self.assertEqual("disable", toolchain["sdk"]["rollForward"])
        self.assertIs(toolchain["sdk"]["allowPrerelease"], False)
        expected = rf"(?m)^FROM {re.escape(canonical_image)}$"
        # The prebuilt consumer is one SDK stage only. A second FROM line,
        # even if the canonical image still appears, must fail closed.
        self.assertEqual([f"FROM {canonical_image}"], docker_from_directives(source))
        self.assertRegex(source, expected)
        # A same-version digest substitution or an old SDK patch must fail.
        corrupt_digest = canonical_image[:-1] + (
            "0" if canonical_image[-1] != "0" else "1"
        )
        self.assertNotRegex(
            source.replace(canonical_image, corrupt_digest), expected
        )
        old_version = canonical_image.replace(
            f"/sdk:{sdk_version}@", "/sdk:10.0.403@"
        )
        if old_version != canonical_image:
            self.assertNotRegex(source.replace(canonical_image, old_version), expected)
        invalid_from_sources = {
            "additional Docker stage": source + "\nFROM alpine:3.21.7\n",
            "unreviewed stage alias": source.replace(
                f"FROM {canonical_image}", f"FROM {canonical_image} AS build", 1
            ),
            "unpinned SDK stage": source.replace(
                f"FROM {canonical_image}",
                f"FROM {canonical_image.split('@sha256:', 1)[0]}", 1
            ),
            "unreviewed platform flag": source.replace(
                f"FROM {canonical_image}",
                f"FROM --platform=linux/amd64 {canonical_image}", 1
            ),
        }
        for label, mutation in invalid_from_sources.items():
            with self.subTest(mutation=label):
                self.assertNotEqual(
                    [f"FROM {canonical_image}"], docker_from_directives(mutation)
                )
        self.assertIn("COPY . .", source)
        self.assertIn("dotnet tool restore", source)
        self.assertIn("dotnet restore src/Coglatas.Web/Coglatas.Web.csproj", source)
        self.assertIn("dotnet ef", (ROOT / "infra/compose/security/runtime.yml").read_text())
        self.assertNotRegex(source, r"(?m)^[^#\n]*\bdotnet\s+(build|msbuild|publish|test|run)\b")
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
        source = DOCKERFILE.read_text(encoding="utf-8")
        compile_pattern = r"(?m)^[^#\n]*\bdotnet\s+(build|msbuild|publish|test|run)\b"
        for command in (
            "RUN dotnet build src/Coglatas.Web/Coglatas.Web.csproj",
            "RUN dotnet msbuild src/Coglatas.Web/Coglatas.Web.csproj -target:Build",
            "RUN dotnet publish src/Coglatas.Web/Coglatas.Web.csproj",
        ):
            with self.subTest(command=command):
                self.assertIsNotNone(re.search(compile_pattern, source + "\n" + command))
        self.assertIsNone(re.search(compile_pattern, source))


if __name__ == "__main__":
    unittest.main()
