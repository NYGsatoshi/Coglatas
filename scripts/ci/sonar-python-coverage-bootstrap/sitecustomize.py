"""Opt-in measurement only; this directory is scoped to one credential-free CI step."""
import os

if os.environ.get("COVERAGE_PROCESS_START"):
    import coverage
    coverage.process_startup()
