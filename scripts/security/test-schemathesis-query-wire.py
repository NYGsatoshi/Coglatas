#!/usr/bin/env python3
"""Replay Main's Boolean query false positive with the pinned SEC-04 checker."""

from __future__ import annotations

import copy
import json
import sys
from pathlib import Path
from types import SimpleNamespace

import schemathesis
from schemathesis.core.failures import AcceptedNegativeData
from schemathesis.core.parameters import ParameterLocation
from schemathesis.generation import GenerationMode
from schemathesis.generation.meta import (
    CaseMetadata,
    ComponentInfo,
    FuzzingPhaseData,
    GenerationInfo,
    PhaseInfo,
    TestPhase,
)
from schemathesis.specs.openapi.checks import negative_data_rejection


def check(document: dict, archived: object, extra: bool = True) -> None:
    operation = schemathesis.openapi.from_dict(document)["/api/projects"]["GET"]
    query = {
        "Archived": archived,
        "Page": -100000000,
        "PageSize": "-0",
        "Search": "\U0001f1fa\U0001f1f8",
        "WorkspaceId": "3c8c8897-7f8a-4d83-a5eb-73d3eccfb1a6",
    }
    if extra:
        # This minimized extra is omitted by request serialization. The query
        # actually transmitted to MVC contains only modeled valid parameters.
        query[""] = []
    meta = CaseMetadata(
        generation=GenerationInfo(time=0, mode=GenerationMode.NEGATIVE),
        components={ParameterLocation.QUERY: ComponentInfo(mode=GenerationMode.NEGATIVE)},
        phase=PhaseInfo(
            name=TestPhase.FUZZING,
            data=FuzzingPhaseData(
                description="violates `additionalProperties`",
                parameter=None,
                parameter_location=ParameterLocation.QUERY,
                location=None,
            ),
        ),
    )
    case = operation.Case(query=query, _meta=meta)
    # Retain the recorded generation metadata, as the engine does while
    # preparing/checking a request. Do not reclassify a fixture as positive.
    case._freeze_metadata = True
    config = SimpleNamespace(expected_statuses=["400", "401", "403", "404", "422", "5xx"])
    context = SimpleNamespace(config=SimpleNamespace(negative_data_rejection=config))
    negative_data_rejection(context, SimpleNamespace(status_code=200), case)


def require_rejection(document: dict, value: object, extra: bool = True) -> None:
    try:
        check(document, value, extra)
    except AcceptedNegativeData:
        return
    raise AssertionError("Pinned checker accepted an invalid Boolean query fixture")


def main() -> None:
    document = json.loads(Path(sys.argv[1]).read_text(encoding="utf-8"))
    original = copy.deepcopy(document)
    parameter = next(p for p in original["paths"]["/api/projects"]["get"]["parameters"] if p["name"] == "Archived")
    parameter["schema"] = {"type": "boolean"}
    require_rejection(original, "false")
    for value in ("false", "FALSE", "True", " false ", "\tFALSE\r\n", "\0false\0", True, False):
        check(document, value)
    for value in ("invalid", "0", "1", 0, "false-extra"):
        require_rejection(document, value)
        require_rejection(document, value, extra=False)
    print("SEC-04 Boolean query replay passed: original failure reproduced; valid wire values pass; invalid values fail.")


if __name__ == "__main__":
    main()
