#!/usr/bin/env python3
"""Check isolated CI PostgreSQL diagnostics without publishing captured content."""

from __future__ import annotations

import argparse
from datetime import datetime, timedelta, timezone
import json
import re
import subprocess
import sys
import time
import uuid


EXPECTED = {
    "log_error_verbosity": "terse",
    "log_min_error_statement": "panic",
    "log_statement": "none",
    "log_min_duration_statement": "-1",
    "log_min_duration_sample": "-1",
    "log_transaction_sample_rate": "0",
    "log_parameter_max_length": "0",
    "log_parameter_max_length_on_error": "0",
    "log_destination": "stderr",
    "logging_collector": "off",
    "log_min_messages": "warning",
}


class PrivacyError(RuntimeError):
    """A bounded diagnostic safe to publish; never include child process output."""


def run(arguments: list[str], *, sql: str | None = None) -> subprocess.CompletedProcess[str]:
    try:
        return subprocess.run(arguments, input=sql, capture_output=True, text=True,
                              timeout=30, check=False)
    except (OSError, subprocess.SubprocessError):
        raise PrivacyError("PostgreSQL privacy control could not execute.") from None


def validate_settings(value: object) -> None:
    if not isinstance(value, dict) or any(value.get(key) != expected for key, expected in EXPECTED.items()):
        raise PrivacyError("PostgreSQL diagnostic privacy settings are missing or unsafe.")


def validate_logs(logs: str, table: str, forbidden: tuple[str, str]) -> bool:
    if any(marker in logs for marker in forbidden):
        raise PrivacyError("PostgreSQL diagnostic privacy probe exposed protected canary content.")
    # A real constraint error is required: empty/unavailable/silent logs cannot pass.
    return any("ERROR:" in line and table in line and "violates check constraint" in line
               for line in logs.splitlines())


def verify(container: str) -> None:
    if not re.fullmatch(r"[0-9a-f]{12,64}", container):
        raise PrivacyError("An isolated PostgreSQL service container identity is required.")
    psql = ["docker", "exec", "-i", container, "psql", "-X", "-U", "coglatas_ci",
            "-d", "coglatas_ci", "-v", "ON_ERROR_STOP=1", "-A", "-t"]
    names = ", ".join("'" + key + "'" for key in EXPECTED)
    settings = run(psql, sql="SELECT json_object_agg(name, setting) FROM pg_settings "
                   "WHERE name = ANY (ARRAY[" + names + "]);\n")
    if settings.returncode:
        raise PrivacyError("PostgreSQL diagnostic privacy settings could not be read.")
    try:
        validate_settings(json.loads(settings.stdout))
    except (ValueError, TypeError):
        raise PrivacyError("PostgreSQL diagnostic privacy settings were not valid JSON.") from None

    identifier = uuid.uuid4().hex
    table = "ci_log_privacy_" + identifier
    forbidden = ("ci_private_row_" + identifier, "ci_private_statement_" + identifier)
    since = (datetime.now(timezone.utc) - timedelta(seconds=1)).isoformat()
    probe = run(psql, sql="\\set VERBOSITY sqlstate\n"
                f"CREATE TEMP TABLE {table} (body text CHECK (body = 'allowed'));\n"
                f"INSERT INTO {table} VALUES ('{forbidden[0]}') /* {forbidden[1]} */;\n")
    # psql's ON_ERROR_STOP SQL failure is 3. SQLSTATE-only client output stays private.
    if probe.returncode != 3 or not re.search(r"\b23514\b", probe.stderr):
        raise PrivacyError("PostgreSQL privacy probe did not observe the required constraint denial.")
    for _ in range(20):
        observed = run(["docker", "logs", "--since", since, container])
        if observed.returncode:
            raise PrivacyError("PostgreSQL privacy probe could not inspect service diagnostics.")
        if validate_logs(observed.stdout + observed.stderr, table, forbidden):
            return
        time.sleep(0.1)
    raise PrivacyError("PostgreSQL privacy probe had no live primary error positive control.")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--container", required=True)
    args = parser.parse_args()
    try:
        verify(args.container)
    except PrivacyError as error:
        print(str(error), file=sys.stderr)
        return 1
    print("PostgreSQL diagnostic privacy settings and live constraint control passed.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
