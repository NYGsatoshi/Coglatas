"""Strict private-data-free reader for the optional bounded Thread probe."""
import json

PHASES = {"OBSERVER_REGISTERED", "ACTION_STARTED", "ACTION_COMPLETED", "ACTION_FAILED", "REQUEST_OBSERVED",
          "REQUEST_FAILED", "WAITING_RESPONSE", "RESPONSE_OBSERVED", "RESPONSE_MISSING", "RESPONSE_FAILED"}
REQUEST_KINDS = {"REQUEST", "RESPONSE", "NETWORK_FAILURE", "REQUEST_FINISHED", "FRESH_SELECTED", "AUTHORIZED_SELECTED"}
MARKERS = {"AUTHORIZED_ACTION_STARTED", "AUTHORIZED_ACTION_COMPLETED", "ROOT_ASSERTION_STARTED", "ROOT_ASSERTION_PASSED", "ROOT_ASSERTION_FAILED"}
KINDS = REQUEST_KINDS | PHASES | MARKERS | {"AUTHORIZED_READ", "AUTHORIZED_OBSERVER_REGISTERED", "SESSION_REPLACEMENT_STARTED",
                                  "SESSION_REPLACEMENT_COMPLETED", "SESSION_VERIFIED"}
TARGET = {"singleTab", "visible", "enabled", "selected", "inViewport", "centerReceivesPointer", "workspaceExpanded"}
SHAPE = {"objectShape", "rootMatches", "conversationMatches", "summaryMatches", "replyIdentitiesMatch", "countsConsistent"}
DOM = {prefix + suffix for prefix in ("workspace", "loading", "error", "root") for suffix in ("Present", "Visible")}


def exact(value, fields):
    if not isinstance(value, dict) or set(value) != fields:
        raise ValueError("Invalid bounded Thread fields.")


def flags(value, fields):
    exact(value, fields)
    if any(type(flag) is not bool for flag in value.values()):
        raise ValueError("Invalid bounded Thread flags.")


def validate_observation(value):
    timed = isinstance(value, dict) and "elapsedMs" in value
    exact(value, ({"elapsedMs"} if timed else set()) | {"events", "truncated", "orphanResponse", "freshRequest", "outcome", "classification", "actionFailure", "failureTarget"})
    if (not isinstance(value["events"], list) or len(value["events"]) > 24 or
            any(type(value[key]) is not bool for key in ("truncated", "orphanResponse")) or
            value["outcome"] not in {"COMPLETED", "FAILED"} or
            value["actionFailure"] not in {"NONE", "UNKNOWN", "WAITING_LOCATOR", "NOT_VISIBLE", "DETACHED", "POINTER_INTERCEPTION"} or
            not (value["freshRequest"] is None or type(value["freshRequest"]) is int and 1 <= value["freshRequest"] <= 24)):
        raise ValueError("Invalid bounded Thread observation.")
    if timed:
        times = value["elapsedMs"]
        if (not isinstance(times, list) or len(times) != len(value["events"]) or
                any(time is not None and (type(time) is not int or not 0 <= time <= 3600000) for time in times)):
            raise ValueError("Invalid bounded Thread timing.")
        known = [time for time in times if time is not None]
        if known != sorted(known):
            raise ValueError("Invalid Thread timing order.")
    markers = set()
    starts, replacement_start, replacement_end, session, observer, selected = {}, -1, -1, -1, -1, None
    authorized_observer, authorized_selected = -1, None
    for index, event in enumerate(value["events"]):
        exact(event, {"kind", "request", "status"})
        kind, request, status = event["kind"], event["request"], event["status"]
        if (kind not in KINDS or not (request is None or type(request) is int and 1 <= request <= 24) or
                not (status is None or type(status) is int and 100 <= status <= 599) or
                (kind == "RESPONSE") != (status is not None) or kind not in REQUEST_KINDS and request is not None):
            raise ValueError("Invalid bounded Thread event.")
        if kind in MARKERS:
            if (kind in markers or authorized_observer < 0 or
                    kind == "AUTHORIZED_ACTION_COMPLETED" and "AUTHORIZED_ACTION_STARTED" not in markers or
                    kind == "ROOT_ASSERTION_STARTED" and not any(item["kind"] == "AUTHORIZED_READ" for item in value["events"][:index]) or
                    kind in {"ROOT_ASSERTION_PASSED", "ROOT_ASSERTION_FAILED"} and
                    ("ROOT_ASSERTION_STARTED" not in markers or markers & {"ROOT_ASSERTION_PASSED", "ROOT_ASSERTION_FAILED"})):
                raise ValueError("Invalid Thread assertion marker order.")
            markers.add(kind)
        if kind == "SESSION_REPLACEMENT_STARTED":
            if replacement_start >= 0:
                raise ValueError("Invalid replacement order.")
            replacement_start = index
        if kind == "SESSION_REPLACEMENT_COMPLETED":
            if replacement_start < 0 or replacement_end >= 0:
                raise ValueError("Invalid replacement order.")
            replacement_end = index
        if kind == "SESSION_VERIFIED":
            if replacement_end < 0 or session >= 0:
                raise ValueError("Invalid replacement order.")
            session = index
        if kind == "OBSERVER_REGISTERED":
            if session < 0 or observer >= 0:
                raise ValueError("Invalid observer order.")
            observer = index
        if kind == "AUTHORIZED_OBSERVER_REGISTERED":
            if authorized_observer >= 0 or replacement_start >= 0:
                raise ValueError("Invalid authorized observer order.")
            authorized_observer = index
        if kind == "REQUEST":
            if request is None or request in starts:
                raise ValueError("Invalid request ordinal.")
            starts[request] = index
        elif kind in REQUEST_KINDS and (not value["orphanResponse"] if request is None else request not in starts):
            raise ValueError("Unknown request ordinal.")
        if kind == "FRESH_SELECTED":
            if selected is not None or request is None or session < 0 or observer < session or starts[request] <= observer:
                raise ValueError("Invalid fresh binding.")
            selected = request
        if kind == "AUTHORIZED_SELECTED":
            if (authorized_selected is not None or request is None or authorized_observer < 0 or
                    replacement_start >= 0 or starts[request] <= authorized_observer):
                raise ValueError("Invalid authorized binding.")
            authorized_selected = request
    if (value["freshRequest"] != selected or value["outcome"] == "COMPLETED" and
            (value["actionFailure"] != "NONE" or value["failureTarget"] is not None)):
        raise ValueError("Invalid bounded Thread completion.")
    if value["failureTarget"] is not None:
        flags(value["failureTarget"], TARGET)
    if value["outcome"] == "COMPLETED" and "ROOT_ASSERTION_FAILED" in markers:
        raise ValueError("Invalid Thread assertion completion.")
    incomplete = timed and None in value["elapsedMs"] or value["truncated"] or value["orphanResponse"] or selected is None or value["outcome"] == "FAILED" and value["failureTarget"] is None
    if value["classification"] != ("INCOMPLETE" if incomplete else "UNKNOWN"):
        raise ValueError("Unsupported Thread classification.")


def validate_probe(value):
    if not isinstance(value, dict):
        raise ValueError("Invalid bounded Thread probe.")
    fields = {"phases", "target"} | ({"observation"} if "observation" in value else set()) | ({"initial"} if "initial" in value else set())
    exact(value, fields)
    phases = value["phases"]
    if not isinstance(phases, list) or len(phases) > len(PHASES) or any(not isinstance(phase, str) or phase not in PHASES for phase in phases) or len(set(phases)) != len(phases):
        raise ValueError("Invalid Thread phases.")
    if value["target"] is not None or "observation" not in value:
        flags(value["target"], TARGET)
    if "observation" in value:
        validate_observation(value["observation"])
    if "initial" in value:
        if "observation" not in value:
            raise ValueError("Missing initial observation provenance.")
        initial = value["initial"]
        has_interleaving = "preActionDeniedDom" in initial
        exact(initial, {"responseShape", "failureDom"} | ({"preActionDeniedDom"} if has_interleaving else set()))
        if initial["responseShape"] is not None:
            flags(initial["responseShape"], SHAPE)
            events = value["observation"]["events"]
            selected = next((event for event in events if event["kind"] == "AUTHORIZED_SELECTED"), None)
            if selected is None or not any(event["kind"] == "RESPONSE" and event["request"] == selected["request"] and event["status"] == 200 for event in events) or not any(event["kind"] == "AUTHORIZED_READ" for event in events):
                raise ValueError("Missing initial response provenance.")
        if initial["failureDom"] is not None:
            fields = DOM | {"pagePresent", "conversationPresent", "threadOpenClass", "groupClass"}
            flags(initial["failureDom"], DOM if set(initial["failureDom"]) == DOM else fields)
            if value["observation"]["outcome"] != "FAILED" or any(initial["failureDom"][prefix + "Visible"] and not initial["failureDom"][prefix + "Present"] for prefix in ("workspace", "loading", "error", "root")):
                raise ValueError("Invalid precleanup DOM provenance.")
        if has_interleaving and initial["preActionDeniedDom"] is not None:
            dom = initial["preActionDeniedDom"]
            flags(dom, DOM | {"pagePresent", "conversationPresent", "threadOpenClass", "groupClass"})
            if any(dom[prefix + "Visible"] and not dom[prefix + "Present"]
                   for prefix in ("workspace", "loading", "error", "root")):
                raise ValueError("Invalid pre-action denied-response DOM.")
            events = value["observation"]["events"]
            observer = next((i for i, event in enumerate(events)
                             if event["kind"] == "OBSERVER_REGISTERED"), -1)
            action = next((i for i, event in enumerate(events)
                           if event["kind"] == "ACTION_STARTED"), -1)
            def started_before_observer(request_id):
                return any(event["kind"] == "REQUEST" and event["request"] == request_id
                           and index < observer for index, event in enumerate(events))
            if not (0 <= observer < action and any(
                    event["kind"] == "RESPONSE" and event["status"] == 400
                    and index > action and started_before_observer(event["request"])
                    for index, event in enumerate(events))):
                raise ValueError("Unbound pre-action denied-response DOM.")
    if len(json.dumps(value, separators=(",", ":"), ensure_ascii=False).encode("utf-8")) > 4096:
        raise ValueError("Unbounded Thread probe.")


def validate_thread_projection(probe, secondary):
    exact(probe, {"state", "data"})
    if probe["state"] == "AVAILABLE":
        validate_probe(probe["data"])
    elif probe["state"] not in {"MISSING", "INVALID"} or probe["data"] is not None:
        raise ValueError("Invalid Thread projection state.")
    exact(secondary, {"state", "stages"})
    stages = secondary["stages"]
    if not isinstance(stages, list) or len(stages) > 3 or any(stage not in {"CAPTURE", "PUBLISH", "RESTORE"} for stage in stages) or len(set(stages)) != len(stages):
        raise ValueError("Invalid secondary Thread projection.")
    if secondary["state"] == "AVAILABLE":
        if not stages:
            raise ValueError("Missing secondary stage.")
    elif secondary["state"] not in {"MISSING", "INVALID"} or stages:
        raise ValueError("Invalid secondary projection state.")
