"""Bounded versioned assembly identity; byte reconciliation grants no acceptance authority."""

from __future__ import annotations

import hashlib
from pathlib import Path, PurePosixPath
import re

LEGACY_ASSEMBLIES = ("Coglatas.Tests", "Coglatas.Web", "Coglatas.Application",
                     "Coglatas.Infrastructure", "Coglatas.Domain")
ASSEMBLIES = (*LEGACY_ASSEMBLIES, "Coglatas.SecurityArchitecture")
SIX_ASSEMBLY_SCOPE = "SIX_ASSEMBLIES_WITH_LOADED_COPIES"
LEGACY_SCOPE = "HISTORICAL_FIVE_ASSEMBLIES"
MAX_ASSEMBLY_BYTES = 32 * 1024 * 1024


def assembly_member(name: str) -> str:
    if name not in ASSEMBLIES:
        raise ValueError("Unsupported assembly identity.")
    if name == "Coglatas.SecurityArchitecture":
        return "tools/Coglatas.SecurityArchitecture/bin/Release/net10.0/" + name + ".dll"
    if name == "Coglatas.Tests":
        return "tests/Coglatas.Tests/bin/Release/net10.0/" + name + ".dll"
    return "src/" + name + "/bin/Release/net10.0/" + name + ".dll"


def loaded_assembly_member(name: str) -> str:
    if name not in ASSEMBLIES:
        raise ValueError("Unsupported loaded assembly identity.")
    return "tests/Coglatas.Tests/bin/Release/net10.0/" + name + ".dll"


def assembly_path(root: Path, name: str) -> Path:
    return root / PurePosixPath(assembly_member(name))


def loaded_assembly_path(root: Path, name: str) -> Path:
    return root / PurePosixPath(loaded_assembly_member(name))


def file_digest(path: Path) -> str:
    result, total = hashlib.sha256(), 0
    with path.open("rb") as source:
        while chunk := source.read(1024 * 1024):
            total += len(chunk)
            if total > MAX_ASSEMBLY_BYTES:
                raise ValueError("Compiled assembly exceeds the bounded input size.")
            result.update(chunk)
    if total == 0:
        raise ValueError("Empty compiled assembly identity.")
    return result.hexdigest()


def receipt_assemblies(receipt: dict, execution: bool = False) -> tuple[str, ...]:
    version = receipt.get("schemaVersion")
    if type(version) is not int or version not in (1, 2):
        raise ValueError("Unsupported assembly receipt version.")
    if execution and (receipt.get("verifierId") != "SEC-ARCH-EXECUTION-COVERAGE" or
                      receipt.get("verifierVersion") != str(version)):
        raise ValueError("Unsupported execution verifier version.")
    expected_scope = SIX_ASSEMBLY_SCOPE if version == 2 else LEGACY_SCOPE
    if (version == 2 and receipt.get("assemblyBindingScope") != expected_scope or
            version == 1 and receipt.get("assemblyBindingScope", LEGACY_SCOPE) != LEGACY_SCOPE):
        raise ValueError("Receipt version and assembly binding scope disagree.")
    names = ASSEMBLIES if version == 2 else LEGACY_ASSEMBLIES
    claimed = receipt.get("assemblyDigests")
    if (not isinstance(claimed, dict) or set(claimed) != set(names) or
            any(not isinstance(value, str) or not re.fullmatch(r"[a-f0-9]{64}", value)
                for value in claimed.values())):
        raise ValueError("Incomplete versioned assembly identity.")
    return names


def capture_assemblies(root: Path) -> dict[str, str]:
    result = {name: file_digest(assembly_path(root, name)) for name in ASSEMBLIES}
    for name, expected in result.items():
        if file_digest(loaded_assembly_path(root, name)) != expected:
            raise ValueError("Loaded dependency copy differs from the producer build.")
    return result


def validate_local_assemblies(root: Path, receipt: dict, execution: bool = False) -> None:
    names = receipt_assemblies(receipt, execution)
    for name in names:
        expected = receipt["assemblyDigests"][name]
        if file_digest(assembly_path(root, name)) != expected:
            raise ValueError("Changed producer build identity.")
        if receipt["schemaVersion"] == 2 and file_digest(loaded_assembly_path(root, name)) != expected:
            raise ValueError("Changed loaded dependency identity.")
