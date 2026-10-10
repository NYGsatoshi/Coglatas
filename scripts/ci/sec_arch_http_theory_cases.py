"""Closed identities for finite existing HTTP query and resource assertions."""

import re

PREFIX = "Coglatas.Tests.Tenancy.HttpTenantIsolationTests."
INVITE_DENIAL = PREFIX + "AdminInvitesDenyTenantOwnersAndRestrictedMembersWithoutDisclosingInvites"
INVITE_PROJECTION = PREFIX + "AdminInvitesPreserveAuthorizedEmailProjectionAndTenantIsolation"
QUERY_CASES = {
    "EMPTY_QUERY": "",
    "PAGE_FIRST_QUERY": "?page=1&pageSize=50",
    "SIZE_FIRST_QUERY": "?pageSize=50&page=1",
}
THEORY_CASES = {method: {case: method + '(query: "' + query + '")' for case, query in QUERY_CASES.items()}
                for method in (INVITE_DENIAL, INVITE_PROJECTION)}
THEORY_ASSERTIONS = {
    method: {
        **{scope + "_PLATFORM_ADMIN": (scope, "AUTHORIZED_SAME_SCOPE", 200,
            "CURRENT_PLATFORM_ADMIN_BOUNDED_TENANT_EMAIL_PROJECTION_EXCLUDES_FOREIGN_INVITES_AND_TOKEN_HASH")
           for scope in ("ALPHA", "BETA")},
        **({actor: (actor.split("_", 1)[0], "AUTHENTICATED_POLICY_ROLE_DENIED", 403,
            "ACTUAL_AUTHENTICATED_ADMIN_ROLE_REQUIREMENT_DENIED_WITHOUT_INVITE_METADATA")
            for actor in ("ALPHA_OWNER", "ALPHA_RESTRICTED_MEMBER", "BETA_OWNER")}
           if method == INVITE_DENIAL else {}),
    }
    for method in THEORY_CASES
}

PLANNING_READS = PREFIX + "PlanningProjectAndSubresourcesAreNotDisclosedBeyondProjectMembership"
PLANNING_PHASES = ("PLANNING", "SUSPENDED")
PLANNING_ACTORS = ("MEMBER", "TENANT_ADMIN", "PLATFORM_ADMIN")
PLANNING_ENTITIES = ("Project", "Task", "Artifact", "ActivityLog", "Comment", "Message")
PLANNING_OPERATIONS = {
    "PROJECT_DETAIL": ("/api/projects/{projectId}", 404, "NotFound",
        "FINITE_PROJECT_OWNER_DETAIL_HAS_EXACT_ID_AND_TITLE", "FINITE_PROJECT_NOT_FOUND_REDACTS_EXACT_TARGET_METADATA"),
    **{"SEARCH_" + entity.upper(): ("/api/search", 200, None,
        "FINITE_PROJECT_OWNER_SEARCH_HAS_EXACT_TARGET_ID_IN_BOUNDED_PAGE",
        "FINITE_PROJECT_SEARCH_EXCLUDES_EXACT_TARGET_ID_AND_PROTECTED_GRAPH_TEXT") for entity in PLANNING_ENTITIES},
    "CONVERSATION_LIST": ("/api/conversations", 200, None,
        "FINITE_PROJECT_OWNER_INBOX_HAS_EXACT_CONVERSATION_AND_MESSAGE",
        "FINITE_PROJECT_INBOX_EXCLUDES_EXACT_CONVERSATION_AND_PROTECTED_GRAPH_TEXT"),
    "MESSAGES": ("/api/conversations/{conversationId}/messages", 400, None,
        "FINITE_PROJECT_OWNER_MESSAGE_PAGE_HAS_EXACT_CURRENT_MESSAGE_AND_BODY",
        "FINITE_PROJECT_MESSAGES_HIDDEN_WITH_UNCHANGED_PRIVATE_STATE_AND_EXPECTED_DENIAL_AUDIT"),
}
# Each denial is tied to the earlier owner positive in the same persisted phase and entity/query scope.
FACT_ASSERTIONS = {PLANNING_READS: {
    phase + "_" + actor + "_" + operation:
        (phase + "_OWNER_" + operation, "GET", path,
         "AUTHORIZED_SAME_SCOPE" if actor == "OWNER" else "SAME_TENANT_RESOURCE",
         200 if actor == "OWNER" else status, None if actor == "OWNER" else code,
         positive if actor == "OWNER" else negative)
    for phase in PLANNING_PHASES for actor in ("OWNER", *PLANNING_ACTORS)
    for operation, (path, status, code, positive, negative) in PLANNING_OPERATIONS.items()
}}


def case_id(method: str, name: str) -> str:
    matches = [case for case, expected in THEORY_CASES[method].items() if expected == name]
    if len(matches) != 1:
        raise ValueError("Unclassified finite HTTP theory parameter identity.")
    return matches[0]


def validate_source(method: str, source: bytes) -> None:
    declaration = (rb'\[Theory\]\s*((?:\[InlineData\("[^"\r\n]*"\)\]\s*)+)public\s+async\s+Task\s+' +
                   re.escape(method.rsplit('.', 1)[1].encode()) + rb'\s*\(\s*string\s+query\s*\)')
    matches = re.findall(declaration, source)
    if len(matches) != 1 or re.findall(rb'\[InlineData\("([^"\r\n]*)"\)\]', matches[0]) != [
            query.encode() for query in QUERY_CASES.values()]:
        raise ValueError("Changed finite HTTP theory source or parameter cardinality.")


def validate_fact_source(method: str, source: bytes) -> None:
    if method != PLANNING_READS:
        raise ValueError("Unknown finite HTTP Fact scope.")
    declaration = rb'public\s+async\s+Task\s+' + re.escape(method.rsplit('.', 1)[1].encode()) + rb'\s*\(\s*\)'
    declarations = list(re.finditer(declaration, source))
    boundaries = list(re.finditer(rb'private\s+static\s+void\s+AssertCompleteErrorEnvelope\s*\(', source))
    if len(declarations) != 1 or len(boundaries) != 1 or declarations[0].end() >= boundaries[0].start():
        raise ValueError("Renamed, deleted, duplicated or changed finite HTTP Fact source.")
    scope = source[declarations[0].start():boundaries[0].start()]
    phases = re.findall(rb'AssertDraftGraphHiddenAsync\(app,\s*data,\s*graph,\s*ProjectStatus\.(\w+),\s*evidence\)', scope)
    actors = re.findall(rb'\(data\.(TenantAMember|TenantAAdmin|PlatformAdmin),\s*"([A-Z_]+)"\)', scope)
    searches = re.findall(rb'SearchIdsAsync\(app,\s*(data\.TenantAOwner|deniedActor),\s*data\.TenantA\.Slug,\s*"([A-Za-z]+)"', scope)
    if phases != [b'Planning', b'Suspended'] or actors != [(b'TenantAMember', b'MEMBER'),
            (b'TenantAAdmin', b'TENANT_ADMIN'), (b'PlatformAdmin', b'PLATFORM_ADMIN')] or any(
            [entity.decode() for actor, entity in searches if actor == identity] != list(PLANNING_ENTITIES)
            for identity in (b'data.TenantAOwner', b'deniedActor')) or (
            b'Assert.Equal(expectedStatus, lifecycle.Status);' not in scope):
        raise ValueError("Changed finite Project lifecycle, actor or Search entity source cardinality.")
