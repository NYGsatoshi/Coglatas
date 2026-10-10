"""Advisory HTTP assertion accounting. Missing dimensions and approval remain UNVERIFIED."""

from __future__ import annotations

import argparse
from collections import Counter
from datetime import datetime, timezone
import json
from pathlib import Path
import re
import xml.etree.ElementTree as ET

from sec_arch_evidence import digest, instant, NS, observed_trx, reconcile_identity
from sec_arch_assembly_binding import (ASSEMBLIES, LEGACY_ASSEMBLIES,
                                      validate_local_assemblies)
from sec_arch_http_theory_cases import THEORY_CASES, THEORY_ASSERTIONS, case_id, validate_source

AUTH = "Coglatas.Tests.SecurityArchitecture.SecurityArchitectureApiAuthorizationTests.EveryComposedProtectedHttpEndpointRejectsAnonymousRequestsAfterValidCsrf"
PUBLIC = "Coglatas.Tests.SecurityArchitecture.SecurityArchitectureApiCurrentAuthorityTests.AnonymousBypassHandlersAreClassifiedAndApplicationOwnedAuthenticationStillRejects"
CAPABILITY = "Coglatas.Tests.SecurityArchitecture.SecurityArchitectureApiCurrentAuthorityTests.PersistedProjectCreateCapabilityChangesDenyRealHttpWithoutCreationEffects"
KANBAN = "Coglatas.Tests.PostgreSql.TaskV1Pr05KanbanHostedHttpTests.Snapshot_HostedPostgreSqlPipelineEnforcesAuthVisibilityDoneWindowAndMembershipRevocation"
GANTT = "Coglatas.Tests.PostgreSql.TaskV1Pr06GanttHostedHttpTests.Snapshot_RealPipelineIsCanonicalDuplicateFreeAndSafelyRejectsRevokedArchivedDeletedAndCrossScopeAccess"
GANTT_COMMANDS = "Coglatas.Tests.PostgreSql.TaskV1Pr06GanttHostedHttpTests.Commands_RealPipelineEnforcesCookieCsrfPermissionsConcurrencyAtomicityAndCanonicalPersistence"
KANBAN_CONFIG = "Coglatas.Tests.PostgreSql.TaskV1Pr05KanbanHostedHttpTests.Config_HostedPostgreSqlPipelineEnforcesCsrfAuthorizationConcurrencyPersistenceAndAtomicSideEffects"
KANBAN_MOVE = "Coglatas.Tests.PostgreSql.TaskV1Pr05KanbanHostedHttpTests.Move_HostedPostgreSqlPipelinePersistsCanonicalOrderVersionsCancellationAndAtomicSideEffects"
COOKIE_PREFIX = "Coglatas.Tests.Auth.AuthSecurityHttpTests."
COOKIE_METHODS = {
    COOKIE_PREFIX + "RevokedSessionCannotAccessAuthenticatedEndpoint": "CURRENT_SESSION_REVOKED",
    COOKIE_PREFIX + "ExpiredSessionCannotAccessAuthenticatedEndpoint": "CURRENT_SESSION_EXPIRED",
    COOKIE_PREFIX + "DisabledUserCannotContinueWithOldCookie": "CURRENT_ACCOUNT_SUSPENDED",
    COOKIE_PREFIX + "AuthenticatedLogoutReturnsSuccessContractClearsCookieAndRevokesAccess": "CURRENT_SESSION_LOGOUT",
}
MEMORY_PREFIX = "Coglatas.Tests.Tenancy.HttpTenantIsolationTests."
NOTIFICATIONS = MEMORY_PREFIX + "TaskNotificationPreferencesArePrivateTenantScopedAndFailClosedForRevokedMembership"
EXECUTION_SCOPE = MEMORY_PREFIX + "TaskExecutionScopeHttpContractUsesStrictJsonAndTheManagerOnlySafeBoundary"
MY_TASKS = MEMORY_PREFIX + "MyTasksHttpContractUsesExplicitWorkspaceScopeSafeErrorsAndRevocation"
TASK_DETAIL = MEMORY_PREFIX + "TaskDetailHttpContractUsesCanonicalRoutesSafeErrorsAndBoundedAggregate"
TASK_ACTIVITY = MEMORY_PREFIX + "TaskActivityHttpContractIsIndependentBoundedStableAndFailClosed"
COMMENT_AUTHOR = MEMORY_PREFIX + "RevokedTaskCommentAuthorReceivesSafeForbiddenForCanonicalUpdateAndDelete"
PARTICIPANT_MESSAGES = MEMORY_PREFIX + "CommunicationBodiesStayParticipantScopedAndDeniedResponsesAreGeneric"
PRIVATE_SHARING = MEMORY_PREFIX + "PrivateWorkspaceSharingReauthorizesApiReadsAndDoesNotLeakProtectedSharingMetadata"
FILE_METADATA = MEMORY_PREFIX + "FileMetadataAndDeniedResponsesDoNotExposeStorageIdentifiers"
FILE_DELETE = MEMORY_PREFIX + "WorkspaceFileDeleteCapabilityAndDirectMutationRemainOwnerScoped"
THREAD_AUTHORITY = MEMORY_PREFIX + "MessageThreadAuthorityRequiresReadPostAndCreateThreadWithoutLeakingSummary"
PROJECT_CREATE_OPTIONS = MEMORY_PREFIX + "ProjectCreateOptionsFailClosedAfterMembershipOrWorkspaceDeactivation"
TASK_CREATE_OPTIONS = MEMORY_PREFIX + "CanonicalTaskCreateRoutesResolveThroughTheInProcessHostAndPreserveSafeTenantBoundaries"
REUSED_MEMORY_METHODS = (TASK_DETAIL, TASK_ACTIVITY, COMMENT_AUTHOR, PARTICIPANT_MESSAGES, PRIVATE_SHARING,
                         FILE_METADATA, FILE_DELETE, THREAD_AUTHORITY, PROJECT_CREATE_OPTIONS, TASK_CREATE_OPTIONS)
MEMORY_METHODS = (NOTIFICATIONS, EXECUTION_SCOPE, MY_TASKS, *REUSED_MEMORY_METHODS, *THEORY_CASES)
SIGNALR_PREFIX = "Coglatas.Tests.SecurityArchitecture.SecurityArchitectureSignalRTests."
MESSAGE_ROLE = SIGNALR_PREFIX + "ProductTransportPreservesReadButRejectsPostingAfterRoleDowngrade"
MESSAGE_CATCH_UP = SIGNALR_PREFIX + "ProductTransportReconnectUsesCurrentHttpCatchUpAuthority"
MESSAGE_PRODUCER = "Coglatas.Tests.SecurityArchitecture.SecurityArchitectureSignalRProducerTests.ActualMessagingHttpProducersReauthorizeCurrentResourceWithoutMutationEffects"
DOMAIN_PRODUCER = "Coglatas.Tests.SecurityArchitecture.SecurityArchitectureDomainProducerTests.ActualProjectTaskFileAndAuthorizationProducersUseCurrentHttpAuthority"
COMMUNICATION_PRODUCER = "Coglatas.Tests.SecurityArchitecture.SecurityArchitectureDomainProducerTests.ActualAnnouncementAndNotificationProducersPreserveRecipientAndResourceAuthority"
METHOD_SOURCES = {
    method: "tests/Coglatas.Tests/" + folder + "/" + method.rsplit(".", 2)[-2] + ".cs"
    for method, folder in ((AUTH, "SecurityArchitecture"), (PUBLIC, "SecurityArchitecture"),
                           (CAPABILITY, "SecurityArchitecture"), (KANBAN, "PostgreSql"), (GANTT, "PostgreSql"), (GANTT_COMMANDS, "PostgreSql"),
                           (KANBAN_CONFIG, "PostgreSql"), (KANBAN_MOVE, "PostgreSql"),
                           *((method, "Auth") for method in COOKIE_METHODS),
                           (MESSAGE_ROLE, "SecurityArchitecture"), (MESSAGE_CATCH_UP, "SecurityArchitecture"), (MESSAGE_PRODUCER, "SecurityArchitecture"),
                           (DOMAIN_PRODUCER, "SecurityArchitecture"), (COMMUNICATION_PRODUCER, "SecurityArchitecture"),
                           *((method, "Tenancy") for method in MEMORY_METHODS))
}
ENTRY_POINT = "ACTUAL_TEST_WEB_ENTRY_POINT_AND_MIGRATED_POSTGRESQL"
POSTGRES_COMPOSITION = "KESTREL_CURRENT_HTTP_POSTGRESQL_COMPOSITION"
COOKIE_MEMORY = "KESTREL_CURRENT_COOKIE_INMEMORY_COMPOSITION"
SYNTHETIC_MEMORY = "KESTREL_CURRENT_CONTROLLERS_INMEMORY_SYNTHETIC_AUTH"
METHOD_ENVIRONMENTS = {method: ENTRY_POINT if method in {AUTH, PUBLIC, CAPABILITY, MESSAGE_ROLE, MESSAGE_CATCH_UP, MESSAGE_PRODUCER, DOMAIN_PRODUCER, COMMUNICATION_PRODUCER} else COOKIE_MEMORY if method in COOKIE_METHODS
                       else SYNTHETIC_MEMORY if method in MEMORY_METHODS
                       else POSTGRES_COMPOSITION for method in METHOD_SOURCES}
PUBLIC_PATHS = {
    ("POST", "/api/auth/login"), ("POST", "/api/auth/register-by-invite"),
    ("POST", "/api/invites/accept"), ("GET", "/api/invites/validate"),
    ("GET", "/api/auth/status"), ("GET", "/api/security/csrf-token"),
    ("GET", "/api/ui/runtime-config.js"), ("GET", "/health/live"),
    ("GET", "/health/ready"), ("GET", "/health/realtime"), ("GET", "/health/task-deadline-digests"),
}
APP_PATHS = {
    ("GET", "/api/projects/{projectId}/gantt"), ("GET", "/api/tasks/{taskItemId}/dependencies"),
    ("POST", "/api/tasks/{taskItemId}/dependencies"),
    ("DELETE", "/api/tasks/{taskItemId}/dependencies/{dependencyId}"),
    ("PATCH", "/api/tasks/{taskItemId}/progress"), ("PATCH", "/api/tasks/{taskItemId}/schedule"),
}
POSITIVE = {"AUTHORIZED_SAME_SCOPE", "AUTHORIZED_RESTORED_SCOPE", "AUTHORIZED_SESSION_PIPELINE"}
CAPABILITY_CONTROLS = {"CURRENT_CAPABILITY_REVOKED", "CURRENT_CAPABILITY_EXPIRED", "CURRENT_CAPABILITY_NOT_YET_VALID",
                       "CURRENT_CAPABILITY_WRONG_SCOPE", "CURRENT_CAPABILITY_WRONG_SUBJECT", "CURRENT_CAPABILITY_UNKNOWN_KEY"}
RESOURCE_CONTROLS = {"SAME_TENANT_RESOURCE", "CROSS_TENANT", "CURRENT_WORKSPACE_MEMBERSHIP_REVOKED", "CURRENT_TENANT_MEMBERSHIP_REVOKED", "CURRENT_PROJECT_MEMBERSHIP_REVOKED", "CURRENT_RESOURCE_ROLE_DENIED", "CURRENT_CONVERSATION_READ_DENIED", "CURRENT_CONVERSATION_AUTHORITY_REVOKED", "CURRENT_FILE_SHARING_GRANT_REVOKED"}
PUBLIC_CONTROLS = {"PUBLIC_CREDENTIAL_REJECTED", "PUBLIC_HANDLER_RESPONSE"}
PROJECTION_CONTROLS = {"CURRENT_WORKSPACE_REVOKED_EMPTY_PAGE", "CURRENT_WORKSPACE_REVOKED_ZERO_CREATED_COUNT"}
LEGACY_BODY_CONTROLS = {"CURRENT_RESOURCE_ROLE_DENIED", "CURRENT_CONVERSATION_READ_DENIED", "CURRENT_CONVERSATION_AUTHORITY_REVOKED"}
SESSION_CONTROLS = set(COOKIE_METHODS.values())
POLICY_ROLE_CONTROLS = {"AUTHENTICATED_POLICY_ROLE_DENIED"}
CONTROLS = POSITIVE | CAPABILITY_CONTROLS | RESOURCE_CONTROLS | PUBLIC_CONTROLS | PROJECTION_CONTROLS | SESSION_CONTROLS | POLICY_ROLE_CONTROLS | {"ANONYMOUS", "ANONYMOUS_WITH_VALID_CSRF"}


def rules(denial_code: str, *denials: str) -> dict:
    return {"AUTHORIZED_SAME_SCOPE": (200, None, None),
            **{control: (403 if control == "CURRENT_RESOURCE_ROLE_DENIED" else 404, denial_code, None) for control in denials}}


# Explicit reviewed assertion scopes; these fixtures do not confer provider or startup equivalence.
EXTRA_RULES = {
    PROJECT_CREATE_OPTIONS: {("GET", "/api/workspaces/{workspaceId}/projects/create-options"):
        {**{control: (200, None, "CURRENT_OWNER_PROJECT_CREATE_OPTIONS_HAVE_BOUNDED_WORKSPACE_GROUP_AND_VISIBILITY")
            for control in ("AUTHORIZED_SAME_SCOPE", "AUTHORIZED_RESTORED_SCOPE")},
         "CURRENT_WORKSPACE_MEMBERSHIP_REVOKED": (404, "NotFound", "REVOKED_WORKSPACE_PROJECT_OPTIONS_HIDE_GROUP_AND_AUTHORITY_METADATA")}},
    TASK_CREATE_OPTIONS: {("GET", "/api/projects/{projectId}/tasks/create-options"):
        {**{control: (200, None, "CURRENT_CONTRIBUTOR_TASK_CREATE_OPTIONS_KEEP_MANAGER_FIELDS_UNAVAILABLE")
            for control in ("AUTHORIZED_SAME_SCOPE", "AUTHORIZED_RESTORED_SCOPE")},
         **{control: (404, "NotFound", "TASK_CREATE_OPTIONS_HIDDEN_WITHOUT_PROJECT_OR_TENANT_METADATA")
            for control in ("CROSS_TENANT", "CURRENT_WORKSPACE_MEMBERSHIP_REVOKED")}}},
    FILE_METADATA: {("GET", path):
        {"AUTHORIZED_SAME_SCOPE": (200, None, positive),
         "CROSS_TENANT": (400, code, "FOREIGN_FILE_HIDDEN_WITHOUT_NAME_OR_STORAGE_IDENTIFIERS")}
        for path, code, positive in (
            ("/api/files/{fileObjectId}", "FileMetadataFailed", "REDACTED_FILE_METADATA_WITHOUT_NAME_OR_STORAGE_IDENTIFIERS"),
            ("/api/files/{fileObjectId}/download", "FileDownloadFailed", "SYNTHETIC_STORAGE_FILE_BYTES_WITH_PRIVATE_CACHE_HEADERS"))},
    FILE_DELETE: {("DELETE", "/api/files/{fileObjectId}"):
        {"AUTHORIZED_SAME_SCOPE": (200, None, "OWNER_FILE_SOFT_DELETE_PERSISTED_WITH_DELETION_AUDIT"),
         **{control: (400, "FileOperationFailed", "UNCHANGED_FILE_ATTACHMENTS_AUDIT_OUTBOX_AFTER_DENIED_DELETE")
            for control in ("CROSS_TENANT", "SAME_TENANT_RESOURCE")}}},
    THREAD_AUTHORITY: {
        ("GET", "/api/messages/{messageId}/thread"):
            {"AUTHORIZED_SAME_SCOPE": (200, None, "READ_ONLY_PARTICIPANT_RETAINS_CURRENT_THREAD_ROOT_REPLIES_AND_SUMMARY"),
             **{control: (400, None, "THREAD_HIDDEN_ERROR_WITHOUT_BODY_REPLY_SUMMARY_OR_SENDER")
                for control in ("CROSS_TENANT", "SAME_TENANT_RESOURCE", "CURRENT_CONVERSATION_AUTHORITY_REVOKED",
                                "CURRENT_WORKSPACE_MEMBERSHIP_REVOKED")}},
        ("POST", "/api/messages/{messageId}/thread/messages"):
            {"AUTHORIZED_SAME_SCOPE": (200, None, "PARTICIPANT_THREAD_REPLY_PERSISTED_WITH_CURRENT_ROOT_AND_SUMMARY"),
             **{control: (400, None, "UNCHANGED_MESSAGES_NOTIFICATIONS_OUTBOX_WITH_NEW_THREAD_DENIAL_AUDIT")
                for control in ("CURRENT_CONVERSATION_AUTHORITY_REVOKED", "CURRENT_WORKSPACE_MEMBERSHIP_REVOKED")}}},
    TASK_DETAIL: {("GET", "/api/tasks/{taskItemId}"):
        {"AUTHORIZED_SAME_SCOPE": (200, None, "BOUNDED_TASK_AGGREGATE_WITHOUT_STORAGE_OR_TOKEN_FIELDS"),
         "ANONYMOUS": (401, None, None),
         "CROSS_TENANT": (404, "TASK_NOT_FOUND", "TASK_HIDDEN_WITHOUT_FOREIGN_TITLE_OR_STORAGE_KEY")}},
    TASK_ACTIVITY: {("GET", "/api/tasks/{taskItemId}/activity"):
        {"AUTHORIZED_SAME_SCOPE": (200, None, "STABLE_BOUNDED_TASK_ACTIVITY_PAGE_AND_CURRENT_AUTHOR"),
         "AUTHORIZED_RESTORED_SCOPE": (200, None, "STABLE_BOUNDED_TASK_ACTIVITY_PAGE_AND_CURRENT_AUTHOR"),
         "ANONYMOUS": (401, None, None),
         **{control: (404, "TASK_NOT_FOUND", "TASK_ACTIVITY_HIDDEN_WITHOUT_PROTECTED_ACTIVITY_BODY")
            for control in ("CROSS_TENANT", "CURRENT_WORKSPACE_MEMBERSHIP_REVOKED")}}},
    COMMENT_AUTHOR: {(verb, "/api/task-comments/{commentId}"):
        {"AUTHORIZED_SAME_SCOPE": (200, None, assertion),
         "CURRENT_WORKSPACE_MEMBERSHIP_REVOKED": (403, "TASK_COMMENT_FORBIDDEN",
            "UNCHANGED_COMMENT_TASK_AUDIT_OUTBOX_AFTER_AUTHOR_MEMBERSHIP_REVOCATION")}
        for verb, assertion in (("PATCH", "AUTHOR_COMMENT_EDIT_PERSISTED_WITH_ADVANCED_VERSION"),
                                ("DELETE", "AUTHOR_COMMENT_SOFT_DELETE_PERSISTED_WITH_ADVANCED_VERSION"))},
    PARTICIPANT_MESSAGES: {("GET", "/api/conversations/{conversationId}/messages"):
        {"AUTHORIZED_SAME_SCOPE": (200, None, "PARTICIPANT_MESSAGE_BODY_PRESENT_WITHOUT_FOREIGN_BODY"),
         **{control: (400, None, "CONVERSATION_HIDDEN_NO_BODY_OR_PARTICIPANT_DATA_WITH_DENIAL_AUDIT")
            for control in ("CROSS_TENANT", "SAME_TENANT_RESOURCE")}}},
    PRIVATE_SHARING: {("GET", "/api/files/{fileObjectId}/sharing"):
        {"AUTHORIZED_SAME_SCOPE": (200, None, "GRANTED_PRIVATE_READER_WITHOUT_MANAGEMENT_OR_RECIPIENT_METADATA"),
         "SAME_TENANT_RESOURCE": (400, "FILE_NOT_FOUND", "PRIVATE_SHARING_HIDDEN_WITHOUT_RECIPIENT_OR_EXTERNAL_COUNT"),
         "CURRENT_FILE_SHARING_GRANT_REVOKED": (400, "FILE_NOT_FOUND", "REVOKED_RECIPIENT_CANNOT_READ_PRIVATE_SHARING_METADATA")}},
    DOMAIN_PRODUCER: {key: {"AUTHORIZED_SAME_SCOPE": (200, None, None), "AUTHORIZED_RESTORED_SCOPE": (200, None, None),
        "CURRENT_PROJECT_MEMBERSHIP_REVOKED": (status, code, "UNCHANGED_PROJECT_TASK_RELATIONSHIPS_COMMENTS_FILES_AUDIT_OUTBOX")}
        for key, status, code in (
            (("PATCH", "/api/tasks/{taskItemId}"), 404, "TASK_NOT_FOUND"),
            (("PUT", "/api/tasks/{taskItemId}/assignee"), 404, "TASK_NOT_FOUND"),
            (("POST", "/api/tasks/{taskItemId}/comments"), 403, "TASK_FORBIDDEN"),
            (("PATCH", "/api/projects/{projectId}"), 400, "BadRequest"),
            (("POST", "/api/files"), 400, "FileMetadataFailed"))},
    COMMUNICATION_PRODUCER: {
        ("PATCH", "/api/notifications/{notificationId}/read"): {
            "AUTHORIZED_SAME_SCOPE": (200, None, None), "AUTHORIZED_RESTORED_SCOPE": (200, None, None),
            "SAME_TENANT_RESOURCE": (400, "NotificationUpdateFailed", "UNCHANGED_NOTIFICATION_READ_AUDIT_OUTBOX"),
            "CURRENT_CONVERSATION_READ_DENIED": (400, "NotificationUpdateFailed", "UNCHANGED_NOTIFICATION_READ_AUDIT_OUTBOX")},
        ("POST", "/api/announcements/{announcementId}/read"): {
            "AUTHORIZED_SAME_SCOPE": (200, None, None), "AUTHORIZED_RESTORED_SCOPE": (200, None, None),
            "CURRENT_WORKSPACE_MEMBERSHIP_REVOKED": (404, None, "ANNOUNCEMENT_HIDDEN_UNCHANGED_NOTIFICATION_READ_AUDIT_OUTBOX")}},
    MESSAGE_PRODUCER: {key: {"AUTHORIZED_SAME_SCOPE": (200, None, None), "AUTHORIZED_RESTORED_SCOPE": (200, None, None),
        "CURRENT_CONVERSATION_AUTHORITY_REVOKED": (400, None, "CURRENT_PERMISSION_ERROR_UNCHANGED_MESSAGE_READ_STATE_OUTBOX_WITH_NEW_DENIAL_AUDIT")}
        for key in (("PATCH", "/api/messages/{messageId}"), ("DELETE", "/api/messages/{messageId}"),
                    ("POST", "/api/messages/{messageId}/thread/messages"), ("POST", "/api/conversations/{conversationId}/read"))},
    MESSAGE_ROLE: {("POST", "/api/conversations/{conversationId}/messages"):
        {"AUTHORIZED_SAME_SCOPE": (200, None, None), "CURRENT_RESOURCE_ROLE_DENIED":
            (403, None, "PERMISSION_ERROR_NO_MESSAGE_OR_OUTBOX_WITH_NEW_DENIAL_AUDIT")}},
    MESSAGE_CATCH_UP: {("GET", "/api/conversations/{conversationId}/messages"):
        {"AUTHORIZED_SAME_SCOPE": (200, None, None), "CURRENT_CONVERSATION_READ_DENIED": (400, None, "CONVERSATION_HIDDEN_ERROR")}},
    KANBAN_CONFIG: {("PUT", "/api/projects/{projectId}/kanban/config"):
        {**rules("KANBAN_NOT_FOUND", "CROSS_TENANT", "CURRENT_WORKSPACE_MEMBERSHIP_REVOKED"),
         "CURRENT_RESOURCE_ROLE_DENIED": (403, "KANBAN_FORBIDDEN", None)}},
    KANBAN_MOVE: {("POST", "/api/tasks/{taskId}/kanban-move"):
        {**rules("KANBAN_NOT_FOUND", "CURRENT_WORKSPACE_MEMBERSHIP_REVOKED"),
         "CURRENT_RESOURCE_ROLE_DENIED": (403, "KANBAN_FORBIDDEN", None)}},
    NOTIFICATIONS: {
        ("GET", "/api/me/workspaces/{workspaceId}/task-notification-preferences"):
            rules("TASK_NOTIFICATION_PREFERENCE_NOT_FOUND", "CROSS_TENANT", "CURRENT_WORKSPACE_MEMBERSHIP_REVOKED"),
        ("PATCH", "/api/me/workspaces/{workspaceId}/task-notification-preferences"):
            rules("TASK_NOTIFICATION_PREFERENCE_NOT_FOUND", "CURRENT_WORKSPACE_MEMBERSHIP_REVOKED")},
    EXECUTION_SCOPE: {
        ("GET", "/api/tasks/{taskItemId}/execution-scope"):
            {**rules("TASK_EXECUTION_NOT_FOUND", "CROSS_TENANT"), "ANONYMOUS": (401, None, None)},
        ("PUT", "/api/projects/{projectId}/execution-scope"):
            rules("TASK_EXECUTION_NOT_FOUND", "SAME_TENANT_RESOURCE")},
    MY_TASKS: {
        ("GET", "/api/me/tasks"): {**rules("MY_TASKS_PROJECT_NOT_FOUND", "CROSS_TENANT", "SAME_TENANT_RESOURCE"),
            "ANONYMOUS": (401, None, None),
            "CURRENT_WORKSPACE_MEMBERSHIP_REVOKED": (403, "MY_TASKS_WORKSPACE_FORBIDDEN", None),
            "CURRENT_WORKSPACE_REVOKED_EMPTY_PAGE": (200, None, "TOTAL_COUNT_ZERO_AND_ITEMS_EMPTY")},
        ("GET", "/api/me/tasks/counts"): {"AUTHORIZED_SAME_SCOPE": (200, None, None),
            "CURRENT_WORKSPACE_REVOKED_ZERO_CREATED_COUNT": (200, None, "CREATED_VIEW_COUNT_ZERO")}},
}
for cookie_method, cookie_control in COOKIE_METHODS.items():
    EXTRA_RULES[cookie_method] = {("GET", "/api/auth/me"):
        {"AUTHORIZED_SAME_SCOPE": (200, None, None), cookie_control: (401, "AuthenticationRequired", None)}}
    if cookie_control == "CURRENT_SESSION_LOGOUT":
        EXTRA_RULES[cookie_method][("POST", "/api/auth/logout")] = {"AUTHORIZED_SAME_SCOPE": (200, None, None)}
for theory_method, assertions in THEORY_ASSERTIONS.items():
    EXTRA_RULES[theory_method] = {("GET", "/api/admin/invites"):
        {control: (status, None, assertion) for _, control, status, assertion in assertions.values()}}


def read_bounded(path: Path, maximum: int) -> bytes:
    with path.open("rb") as source:
        data = source.read(maximum + 1)
    if len(data) > maximum:
        raise ValueError("Oversized evidence input.")
    return data


def read_json(path: Path, maximum: int = 2 * 1024 * 1024) -> dict:
    def unique_object(pairs):
        result = {}
        for key, value in pairs:
            if key in result:
                raise ValueError("Duplicate JSON authority field.")
            result[key] = value
        return result
    def invalid_constant(_value):
        raise ValueError("Nonfinite JSON evidence value.")
    try:
        result = json.loads(read_bounded(path, maximum), object_pairs_hook=unique_object, parse_constant=invalid_constant)
    except RecursionError as error:
        raise ValueError("Excessively nested evidence input.") from error
    if not isinstance(result, dict):
        raise ValueError("JSON evidence object required.")
    return result


def account(root: Path, inventory: dict, recordings: list[dict], trx: bytes, now: datetime,
            execution_receipt: dict | None = None, identity: tuple[str, str, str, str] | None = None) -> dict:
    if inventory.get("catalogScope") != "ACTUAL_COMPOSED_TEST_HOST" or inventory.get("schemaVersion") != 1:
        raise ValueError("Actual composed inventory required.")
    endpoints = {(row["method"], row["normalizedPath"]): row for row in inventory["endpoints"]}
    if len(endpoints) != inventory["endpointCount"] or len(endpoints) != len(inventory["endpoints"]):
        raise ValueError("Duplicate or incomplete endpoint inventory.")
    methods = {record["verifierMethod"] for record in recordings}
    identities = [(record["verifierMethod"], record.get("verifierCaseId")) for record in recordings]
    if len(set(identities)) != len(identities) or not methods or methods - METHOD_SOURCES.keys():
        raise ValueError("Duplicate or unsupported response verifier.")
    for method, case in identities:
        if method in THEORY_CASES and case not in THEORY_CASES[method] or method not in THEORY_CASES and case is not None:
            raise ValueError("Missing or unsupported finite verifier case identity.")
    execution = observed_trx(trx, now, {method: len(THEORY_CASES[method]) if method in THEORY_CASES else 1 for method in methods})
    passed = {(row["method"], row.get("verifierCaseId")) for row in execution["cases"] if row["outcome"] == "PASS"}
    xml = ET.fromstring(trx)
    intervals = {}
    definitions = {row.attrib["id"]: row for row in xml.findall("t:TestDefinitions/t:UnitTest", NS)}
    for result in xml.findall("t:Results/t:UnitTestResult", NS):
        test = definitions[result.attrib["testId"]]
        definition = test.find("t:TestMethod", NS)
        method = definition.attrib["className"] + "." + definition.attrib["name"]
        if method in methods:
            case = case_id(method, test.attrib["name"]) if method in THEORY_CASES else None
            intervals[(method, case)] = (instant(result.attrib["startTime"]), instant(result.attrib["endTime"]))
    candidate = "UNVERIFIED"
    if execution_receipt is not None:
        if identity is None:
            raise ValueError("Independent candidate/run identity required.")
        reconcile_identity(execution_receipt, *identity)
        validate_local_assemblies(root, execution_receipt, execution=True)
        if execution_receipt["executionDigest"] != digest(trx):
            raise ValueError("Wrong execution artifact.")
        candidate = "EXACT_RECEIPT_RECONCILED_TRUSTED_ATTESTATION_PENDING"
    observations = []
    for record in recordings:
        method = record["verifierMethod"]
        verifier_case = record.get("verifierCaseId")
        verifier_identity = (method, verifier_case)
        if record.get("ownerApproval") is not None or record.get("environment") != METHOD_ENVIRONMENTS[method]:
            raise ValueError("Unsupported receipt or self-declared approval.")
        source = METHOD_SOURCES[method]
        source_bytes = read_bounded(root / source, 4 * 1024 * 1024)
        if method in THEORY_CASES:
            validate_source(method, source_bytes)
        else:
            declaration = rb"public\s+async\s+Task\s+" + re.escape(method.rsplit(".", 1)[1].encode()) + rb"\s*\(\s*\)"
            if len(re.findall(declaration, source_bytes)) != 1:
                raise ValueError("Renamed, deleted or changed verifier source.")
        if record["sourcePath"] != source or record["sourceDigest"] != digest(source_bytes):
            raise ValueError("Renamed, deleted or changed verifier source.")
        assemblies = record["assemblyDigests"]
        validate_local_assemblies(root, record)
        if inventory["webAssemblyDigest"] != assemblies["Coglatas.Web"]:
            raise ValueError("Inventory from a different build.")
        if execution_receipt is not None and (execution_receipt["schemaVersion"] != record["schemaVersion"] or
                                             execution_receipt["assemblyDigests"] != assemblies):
            raise ValueError("Wrong candidate assemblies.")
        seen = Counter()
        for row in record["observations"]:
            if set(row) - {"path", "method", "control", "observedStatus", "expectedStatus", "errorCode", "responseAssertion", "assertionCase", "observedAtUtc"}:
                raise ValueError("Unrecognized or unsafe observation fields.")
            key = (row["method"], row["path"])
            control = row["control"]
            assertion_case = row.get("assertionCase")
            if method in THEORY_CASES:
                expected_case = THEORY_ASSERTIONS[method].get(assertion_case)
                if expected_case is None or key != ("GET", "/api/admin/invites") or (
                        control, row["observedStatus"], row.get("responseAssertion")) != expected_case[1:] or row.get("errorCode") is not None:
                    raise ValueError("Unclassified finite theory assertion or actor/scope substitution.")
            elif assertion_case is not None:
                raise ValueError("Parameterized assertion outside finite theory scope.")
            if key not in endpoints or control not in CONTROLS or row["observedStatus"] != row["expectedStatus"]:
                raise ValueError("Unclassified endpoint/control or failed assertion.")
            if any(type(row[field]) is not int for field in ("observedStatus", "expectedStatus")):
                raise ValueError("Invalid HTTP status identity.")
            reviewed_assertion = EXTRA_RULES.get(method, {}).get(key, {}).get(control)
            if control not in PROJECTION_CONTROLS | LEGACY_BODY_CONTROLS and row.get("responseAssertion") is not None and (
                    reviewed_assertion is None or reviewed_assertion[2] is None):
                raise ValueError("Unreviewed body assertion.")
            if control in POSITIVE | PUBLIC_CONTROLS and row.get("errorCode") is not None:
                raise ValueError("Unexpected authority code on positive/public observation.")
            timestamp = instant(row["observedAtUtc"])
            if verifier_identity not in intervals or not intervals[verifier_identity][0] <= timestamp <= intervals[verifier_identity][1]:
                raise ValueError("Observation outside actual verifier execution.")
            if control in POSITIVE and not 200 <= row["observedStatus"] < 300:
                raise ValueError("Positive operation failed.")
            if control in {"ANONYMOUS", "ANONYMOUS_WITH_VALID_CSRF"} and row["observedStatus"] != 401:
                raise ValueError("Unrelated permission/input rejection is not authentication evidence.")
            if method in {PUBLIC, GANTT_COMMANDS} and key in APP_PATHS and control == "ANONYMOUS_WITH_VALID_CSRF":
                expected_code = "TASK_DEPENDENCY_AUTHENTICATION_REQUIRED" if "/dependencies" in key[1] else "GANTT_AUTHENTICATION_REQUIRED"
                if row.get("errorCode") != expected_code:
                    raise ValueError("Application-owned authentication lacks typed authority result.")
            if control in CAPABILITY_CONTROLS and (row["observedStatus"] != 403 or row.get("errorCode") != "CapabilityDenied"):
                raise ValueError("Capability denial lacks typed authority result.")
            if control in RESOURCE_CONTROLS and method not in EXTRA_RULES:
                expected_status = 401 if control == "CURRENT_TENANT_MEMBERSHIP_REVOKED" else 403 if control == "CURRENT_RESOURCE_ROLE_DENIED" else 404
                expected_code = "KANBAN_NOT_FOUND" if method == KANBAN else "GANTT_PROJECT_NOT_FOUND"
                if method == GANTT_COMMANDS:
                    expected_code = "GANTT_FORBIDDEN" if control == "CURRENT_RESOURCE_ROLE_DENIED" else (
                        "TASK_DEPENDENCY_NOT_FOUND" if key[1].endswith("/dependencies") else "GANTT_WORK_ITEM_NOT_FOUND")
                if row["observedStatus"] != expected_status or (expected_status != 401 and row.get("errorCode") != expected_code):
                    raise ValueError("Resource rejection lacks asserted authority result.")
            if method in EXTRA_RULES:
                expected = EXTRA_RULES[method].get(key, {}).get(control)
                allowed = expected is not None
                if expected is not None and (row["observedStatus"], row.get("errorCode"), row.get("responseAssertion")) != expected:
                    raise ValueError("Fixture assertion lacks its exact reviewed authority/body result.")
            elif method == AUTH:
                allowed = control == "ANONYMOUS_WITH_VALID_CSRF" and endpoints[key]["authorizationRequired"] or (
                    control == "AUTHORIZED_SESSION_PIPELINE" and key == ("GET", "/api/auth/me"))
            elif method == PUBLIC:
                allowed = key in PUBLIC_PATHS and control in PUBLIC_CONTROLS or key in APP_PATHS and control == "ANONYMOUS_WITH_VALID_CSRF" or (
                    key == ("GET", "/api/auth/me") and control == "AUTHORIZED_SESSION_PIPELINE")
            elif method == CAPABILITY:
                allowed = key == ("POST", "/api/workspaces/{workspaceId}/projects") and control in CAPABILITY_CONTROLS | {"AUTHORIZED_SAME_SCOPE", "AUTHORIZED_RESTORED_SCOPE"}
            elif method == GANTT_COMMANDS:
                allowed = key in APP_PATHS - {("GET", "/api/projects/{projectId}/gantt"), ("GET", "/api/tasks/{taskItemId}/dependencies")} and control in (
                    RESOURCE_CONTROLS | {"ANONYMOUS_WITH_VALID_CSRF", "AUTHORIZED_SAME_SCOPE"})
            else:
                path = "/api/projects/{projectId}/kanban" if method == KANBAN else "/api/projects/{projectId}/gantt"
                allowed = key == ("GET", path) and control in RESOURCE_CONTROLS | {"ANONYMOUS", "AUTHORIZED_SAME_SCOPE", "AUTHORIZED_RESTORED_SCOPE"}
            if not allowed:
                raise ValueError("Assertion outside reviewed verifier scope.")
            observation_identity = (key, control, assertion_case)
            seen[observation_identity] += 1
            maximum = 6 if method == CAPABILITY and control == "AUTHORIZED_RESTORED_SCOPE" else 1
            if seen[observation_identity] > maximum:
                raise ValueError("Duplicate observation cannot multiply coverage.")
            observations.append({**row, "verifierMethod": method, "environment": record["environment"],
                                 "verifierCaseId": verifier_case,
                                 "executionOutcome": "PASS" if verifier_identity in passed else "UNVERIFIED"})
    for row in observations:
        if row["control"] in POLICY_ROLE_CONTROLS:
            scope = THEORY_ASSERTIONS[row["verifierMethod"]][row["assertionCase"]][0]
            positive = any(peer["verifierMethod"] == row["verifierMethod"] and peer["verifierCaseId"] == row["verifierCaseId"] and
                           peer["assertionCase"] == scope + "_PLATFORM_ADMIN" and peer["control"] == "AUTHORIZED_SAME_SCOPE" and
                           peer["executionOutcome"] == "PASS" and instant(peer["observedAtUtc"]) < instant(row["observedAtUtc"])
                           for peer in observations)
        elif row["control"] in RESOURCE_CONTROLS | CAPABILITY_CONTROLS | SESSION_CONTROLS | PROJECTION_CONTROLS:
            positive = any(peer["verifierMethod"] == row["verifierMethod"] and peer["method"] == row["method"] and
                           peer["path"] == row["path"] and peer["control"] == "AUTHORIZED_SAME_SCOPE" and
                           peer["executionOutcome"] == "PASS" for peer in observations)
        elif row["control"] in {"ANONYMOUS", "ANONYMOUS_WITH_VALID_CSRF"}:
            positive = any(peer["verifierMethod"] == row["verifierMethod"] and peer["control"] in POSITIVE and
                           (row["verifierMethod"] not in EXTRA_RULES or (peer["method"], peer["path"]) == (row["method"], row["path"])) and
                           peer["executionOutcome"] == "PASS" for peer in observations)
        else:
            positive = True
        row["accountingOutcome"] = "PASS" if positive and row["executionOutcome"] == "PASS" else "UNVERIFIED"
    rows = []
    for key, endpoint in sorted(endpoints.items()):
        controls = [row for row in observations if (row["method"], row["path"]) == key]
        rows.append({"surfaceId": endpoint["surfaceId"], "method": key[0], "path": key[1], "controls": controls,
                     "sourceObservation": endpoint.get("accessPathObservation"),
                     "specIds": [], "specMappingOutcome": "UNVERIFIED", "resourceCoverageOutcome": "UNVERIFIED",
                     "approvedActorRoleMappingOutcome": "UNVERIFIED", "databaseIdentityOutcome": "UNVERIFIED",
                     "apiToRlsOutcome": "UNVERIFIED", "contractCompletion": "UNVERIFIED"})
    protected = {key for key, row in endpoints.items() if row["authorizationRequired"] and row["kind"] != "HUB"} | (APP_PATHS & endpoints.keys())
    provider_observations = [row for row in observations if row["environment"] in {ENTRY_POINT, POSTGRES_COMPOSITION}]
    anonymous = {(row["method"], row["path"]) for row in provider_observations
                 if row["accountingOutcome"] == "PASS" and row["control"] in {"ANONYMOUS", "ANONYMOUS_WITH_VALID_CSRF"}}
    dimension_names = {
        "authorizedSameScope": {"AUTHORIZED_SAME_SCOPE", "AUTHORIZED_RESTORED_SCOPE"},
        "crossTenant": {"CROSS_TENANT"}, "sameTenantResource": {"SAME_TENANT_RESOURCE"},
        "currentWorkspaceMembershipRevocation": {"CURRENT_WORKSPACE_MEMBERSHIP_REVOKED"},
        "currentTenantMembershipRevocation": {"CURRENT_TENANT_MEMBERSHIP_REVOKED"},
        "currentProjectMembershipRevocation": {"CURRENT_PROJECT_MEMBERSHIP_REVOKED"},
        "currentFileSharingGrantRevocation": {"CURRENT_FILE_SHARING_GRANT_REVOKED"},
        "currentResourceRoleDenial": {"CURRENT_RESOURCE_ROLE_DENIED"},
        "staticAuthenticatedPolicyRoleDenial": POLICY_ROLE_CONTROLS,
        "currentConversationAuthority": {"CURRENT_CONVERSATION_READ_DENIED", "CURRENT_CONVERSATION_AUTHORITY_REVOKED"},
        "currentCapability": CAPABILITY_CONTROLS,
        "currentSessionOrAccountInvalidation": SESSION_CONTROLS,
        "currentWorkspaceRevocationProjection": PROJECTION_CONTROLS,
    }
    def dimension_counts(selected):
        dimensions = {}
        for dimension, names in dimension_names.items():
            observed = {(row["method"], row["path"]) for row in selected
                        if row["accountingOutcome"] == "PASS" and row["control"] in names} & protected
            dimensions[dimension] = {"observedEndpointCount": len(observed), "unobservedEndpointCount": len(protected - observed),
                                     "unobservedApplicability": "UNVERIFIED"}
        return dimensions
    fixture_dimensions = {environment: dimension_counts([row for row in observations if row["environment"] == environment])
                          for environment in sorted(set(METHOD_ENVIRONMENTS.values()))}
    missing_scoped_controls = [{"verifierMethod": method, "method": key[0], "path": key[1], "control": control, "outcome": "UNVERIFIED"}
                              for method, scopes in EXTRA_RULES.items() for key, controls in scopes.items() for control in controls
                              if not any(row["verifierMethod"] == method and (row["method"], row["path"]) == key and row["control"] == control
                                         and row["accountingOutcome"] == "PASS" for row in observations)]
    def observed_operations(selected, controls):
        return {(row["method"], row["path"]) for row in selected
                if row["accountingOutcome"] == "PASS" and row["control"] in controls} & protected
    resource_negative = observed_operations(observations, RESOURCE_CONTROLS)
    authority_negative = observed_operations(observations, RESOURCE_CONTROLS | CAPABILITY_CONTROLS | SESSION_CONTROLS | PROJECTION_CONTROLS | POLICY_ROLE_CONTROLS)
    operation_summary = {
        "protectedOperationCount": len(protected),
        "observedSuccessfulOperationCount": len(observed_operations(observations, POSITIVE)),
        "observedResourceNegativeOperationCount": len(resource_negative),
        "withoutObservedResourceNegativeOperationCount": len(protected - resource_negative),
        "observedAuthorityNegativeOperationCount": len(authority_negative),
        "observedStaticPolicyRoleNegativeOperationCount": len(observed_operations(observations, POLICY_ROLE_CONTROLS)),
        "withoutObservedAuthorityNegativeOperationCount": len(protected - authority_negative),
        "resourceNegativeOperationCountByEnvironment": {
            environment: len(observed_operations([row for row in observations if row["environment"] == environment], RESOURCE_CONTROLS))
            for environment in sorted(set(METHOD_ENVIRONMENTS.values()))},
        "completeResourceContractCount": 0, "allRoleOperationCoverage": "UNVERIFIED",
        "unobservedNegativeApplicability": "UNVERIFIED",
    }
    return {"schemaVersion": 2, "verifierId": "SEC-ARCH-HTTP-ASSERTION-ACCOUNTING", "mode": "ADVISORY",
            "inputReceiptSchemaVersions": sorted({record["schemaVersion"] for record in recordings}),
            "fullDependencyQualification": "SIX_ASSEMBLY_LOCAL_BYTES_RECONCILED" if all(record["schemaVersion"] == 2 for record in recordings) else "UNVERIFIED",
            "inventoryDigest": digest(json.dumps(inventory, sort_keys=True).encode()), "executionDigest": digest(trx),
            "candidateBinding": candidate, "executionOutcome": execution["outcome"],
            "endpointCount": len(rows), "observedControlCount": len(observations),
            "observedVerifierCount": len(methods), "reviewedVerifierCount": len(METHOD_SOURCES),
            "observedVerifierCaseReceiptCount": len(identities),
            "unrecordedFiniteVerifierCases": [{"verifierMethod": method, "verifierCaseId": case, "outcome": "UNVERIFIED"}
                for method, cases in THEORY_CASES.items() for case in cases if (method, case) not in identities],
            "unobservedFiniteAssertionCases": [{"verifierMethod": method, "verifierCaseId": case,
                "assertionCase": assertion, "control": definition[1], "outcome": "UNVERIFIED"}
                for method, cases in THEORY_CASES.items() for case in cases
                for assertion, definition in THEORY_ASSERTIONS[method].items()
                if not any(row["verifierMethod"] == method and row["verifierCaseId"] == case and
                           row.get("assertionCase") == assertion and row["accountingOutcome"] == "PASS" for row in observations)],
            "fixtureObservationCounts": dict(sorted(Counter(row["environment"] for row in observations).items())),
            "protectedHttpEndpointCount": len(protected), "anonymousObservedEndpointCount": len(anonymous & protected),
            "anonymousOutstandingEndpointCount": len(protected - anonymous),
            "controlDimensions": dimension_counts(provider_observations), "controlDimensionsScope": "POSTGRESQL_HTTP_FIXTURES_ONLY",
            "fixtureControlDimensions": fixture_dimensions,
            "operationEvidenceSummary": operation_summary,
            "unrecordedVerifierMethods": sorted(METHOD_SOURCES.keys() - set(methods)),
            "unobservedScopedControls": missing_scoped_controls,
            "resourceCoverageOutstandingEndpointCount": len(protected), "unmappedSurfaceCount": len(rows),
            "apiToRlsOutstandingAdapterCount": 1, "endpoints": rows,
            "trustedAttestation": "UNVERIFIED", "ownerApproval": None,
            "preAvaloniaVerdict": "PRE-AVALONIA SEC-ARCH: BLOCKED",
            "limits": ["Names, source references and metadata alone receive no execution credit.",
                       "Passed explicit observations cover only their named controls; full endpoint coverage remains UNVERIFIED.",
                       "Receipt fields are not authentic owner approval or trusted execution attestations.",
                       "Historical version 1 recordings bind five assemblies and leave full dependency qualification UNVERIFIED.",
                       "Cookie/InMemory and synthetic-auth/InMemory observations cannot establish PostgreSQL or composed-startup behavior.",
                       "Seeded role denials do not prove current-state role-change reauthorization.",
                       "Declared public paths remain observations pending canonical classification approval."]}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--inventory", required=True, type=Path)
    parser.add_argument("--observations", required=True, nargs="+", type=Path)
    parser.add_argument("--trx", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--execution-receipt", type=Path)
    parser.add_argument("--candidate-sha")
    parser.add_argument("--environment-fingerprint")
    parser.add_argument("--run-id")
    parser.add_argument("--run-attempt")
    args = parser.parse_args()
    identity = (args.candidate_sha, args.environment_fingerprint, args.run_id, args.run_attempt)
    if args.execution_receipt is not None and not all(identity):
        raise ValueError("Independent candidate/run binding missing.")
    root = Path(__file__).resolve().parents[2]
    report = account(root, read_json(args.inventory, 16 * 1024 * 1024), [read_json(path) for path in args.observations],
                     read_bounded(args.trx, 64 * 1024 * 1024), datetime.now(timezone.utc),
                     read_json(args.execution_receipt) if args.execution_receipt else None,
                     identity if args.execution_receipt else None)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    with args.output.open("x", encoding="utf-8") as output:
        output.write(json.dumps(report, indent=2) + "\n")
    print("SEC-ARCH HTTP accounting: " + report["executionOutcome"] + "; pre-Avalonia BLOCKED")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (ValueError, KeyError, TypeError, OSError, ET.ParseError):
        print("SEC-ARCH HTTP accounting ERROR: invalid or unavailable evidence inputs.")
        raise SystemExit(1)
