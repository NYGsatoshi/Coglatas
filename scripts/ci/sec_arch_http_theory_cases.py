"""Closed identities for two existing, finite Admin invite HTTP theories."""

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
