#!/usr/bin/env bash
# SEC-05 multi-tenant authorization negative matrix.
#
# This file is sourced by the real PostgreSQL security runtime after SEC-03 has
# established four authenticated synthetic sessions. Protected response bodies
# remain inside the harness-owned temporary state directory and are never copied
# into CI artifacts; only machine-readable outcome metadata is retained.

SEC05_FAILURES=0
SEC05_CASES=0
SEC05_JSONL=""
SEC05_ROUTE_INVENTORY=""
SEC05_EVIDENCE_PATH=""

sec05_fail() {
  printf 'SEC-05 authorization matrix failed: %s\n' "$*" >&2
  return 1
}

sec05_require_runtime() {
  security_scan_require_no_xtrace || return 1
  declare -F security_scan_http >/dev/null 2>&1 || sec05_fail "SEC-03 HTTP harness is not loaded" || return 1
  declare -F security_scan_cookie_jar >/dev/null 2>&1 || sec05_fail "SEC-03 cookie helper is not loaded" || return 1
  declare -F security_scan_fetch_csrf >/dev/null 2>&1 || sec05_fail "SEC-03 CSRF helper is not loaded" || return 1
  declare -F db_scalar >/dev/null 2>&1 || sec05_fail "real PostgreSQL db_scalar helper is required" || return 1
  [[ -n "${SECURITY_SCAN_STATE_DIR:-}" && -d "${SECURITY_SCAN_STATE_DIR:-}" ]] ||
    sec05_fail "SEC-03 owned state directory is required" || return 1
}

sec05_db_one() {
  local sql=$1 value
  value="$(db_scalar "$sql")" || return 1
  value="${value//$'\r'/}"
  while [[ "$value" == *$'\n' ]]; do value="${value%$'\n'}"; done
  [[ -n "$value" && "$value" != *$'\n'* ]] || {
    sec05_fail "fixture query did not resolve exactly one scalar value"
    return 1
  }
  printf '%s\n' "$value"
}

sec05_db_exec() {
  db_scalar "$1" >/dev/null
}

sec05_status_category() {
  case "$1" in
    2??) printf '%s\n' 'success' ;;
    400) printf '%s\n' 'denied' ;;
    401) printf '%s\n' 'unauthenticated' ;;
    403) printf '%s\n' 'forbidden' ;;
    404) printf '%s\n' 'safe-not-found' ;;
    409) printf '%s\n' 'denied-conflict' ;;
    000) printf '%s\n' 'transport-failure' ;;
    5??) printf '%s\n' 'server-error' ;;
    *) printf '%s\n' 'unexpected' ;;
  esac
}

sec05_expected_matches() {
  local expected=$1 status=$2
  case "$expected" in
    unauthenticated) [[ "$status" == "401" ]] ;;
    denied) [[ "$status" == "400" || "$status" == "403" || "$status" == "404" || "$status" == "409" ]] ;;
    forbidden) [[ "$status" == "403" ]] ;;
    safe-not-found) [[ "$status" == "404" ]] ;;
    csrf-rejected) [[ "$status" == "403" ]] ;;
    mass-assignment-rejected) [[ "$status" == "400" ]] ;;
    allowed-no-disclosure) [[ "$status" == "200" ]] ;;
    *) return 1 ;;
  esac
}

sec05_scope_markers() {
  local scope=$1
  case "$scope" in
    alpha)
      printf '%s\n' \
        'SEC-02 Alpha Workspace Canary' \
        'SEC-02 Alpha Private Project Canary' \
        'SEC02 ALPHA PRIVATE TASK CANARY' \
        'sec02-alpha-private.txt' \
        'SEC02_ALPHA_FILE_CANARY_DO_NOT_LEAK' \
        'SEC02 ALPHA PRIVATE CONVERSATION CANARY' \
        'SEC02_ALPHA_MESSAGE_CANARY_DO_NOT_LEAK' \
        'SEC05 ALPHA ANNOUNCEMENT CANARY' \
        'SEC05_ALPHA_ANNOUNCEMENT_DO_NOT_LEAK' \
        'SEC05 ALPHA NOTIFICATION CANARY' \
        'SEC05_ALPHA_NOTIFICATION_DO_NOT_LEAK' \
        "${SEC05_ALPHA_TENANT_ID:-}" \
        "${SEC05_ALPHA_WORKSPACE_ID:-}" \
        "${SEC05_ALPHA_PROJECT_ID:-}" \
        "${SEC05_ALPHA_TASK_ID:-}" \
        "${SEC05_ALPHA_FILE_ID:-}" \
        "${SEC05_ALPHA_CONVERSATION_ID:-}" \
        "${SEC05_ALPHA_NOTIFICATION_ID:-}" \
        "${SEC05_ALPHA_ANNOUNCEMENT_ID:-}"
      ;;
    beta)
      printf '%s\n' \
        'SEC-02 Beta Workspace Canary' \
        'SEC-02 Beta Private Project Canary' \
        'SEC02 BETA PRIVATE TASK CANARY' \
        'sec02-beta-private.txt' \
        'SEC02_BETA_FILE_CANARY_DO_NOT_LEAK' \
        'SEC02 BETA PRIVATE CONVERSATION CANARY' \
        'SEC02_BETA_MESSAGE_CANARY_DO_NOT_LEAK' \
        'SEC05 BETA ANNOUNCEMENT CANARY' \
        'SEC05_BETA_ANNOUNCEMENT_DO_NOT_LEAK' \
        'SEC05 BETA NOTIFICATION CANARY' \
        'SEC05_BETA_NOTIFICATION_DO_NOT_LEAK' \
        "${SEC05_BETA_TENANT_ID:-}" \
        "${SEC05_BETA_WORKSPACE_ID:-}" \
        "${SEC05_BETA_PROJECT_ID:-}" \
        "${SEC05_BETA_TASK_ID:-}" \
        "${SEC05_BETA_FILE_ID:-}" \
        "${SEC05_BETA_CONVERSATION_ID:-}" \
        "${SEC05_BETA_NOTIFICATION_ID:-}" \
        "${SEC05_BETA_ANNOUNCEMENT_ID:-}"
      ;;
    alpha-shadow)
      printf '%s\n' \
        'SEC05 ALPHA SHADOW CONVERSATION CANARY' \
        'SEC05_ALPHA_SHADOW_MESSAGE_DO_NOT_LEAK' \
        "${SEC05_ALPHA_SHADOW_CONVERSATION_ID:-}" \
        "${SEC05_ALPHA_SHADOW_MESSAGE_ID:-}"
      ;;
    none) ;;
    *) sec05_fail "unknown disclosure marker scope '$scope'"; return 1 ;;
  esac
}

sec05_disclosure_observed() {
  local response=$1 marker_scope=$2 marker
  [[ "$marker_scope" != "none" ]] || { printf '%s\n' 'false'; return 0; }
  while IFS= read -r marker; do
    [[ -n "$marker" ]] || continue
    if grep -Fq -- "$marker" "$response"; then
      printf '%s\n' 'true'
      return 0
    fi
  done < <(sec05_scope_markers "$marker_scope")
  printf '%s\n' 'false'
}

sec05_append_case() {
  local case_id=$1 family=$2 negative_class=$3 actor=$4 source=$5 target=$6 method=$7 route_class=$8
  local expected=$9 status=${10} actual=${11} disclosure=${12} mutation=${13} csrf_mode=${14} passed=${15}
  python3 - "$SEC05_JSONL" \
    "$case_id" "$family" "$negative_class" "$actor" "$source" "$target" \
    "$method" "$route_class" "$expected" "$status" "$actual" \
    "$disclosure" "$mutation" "$csrf_mode" "$passed" <<'PY'
import json
import sys
(
    path, case_id, family, negative_class, actor, source, target, method,
    route_class, expected, status, actual, disclosure, mutation, csrf_mode, passed,
) = sys.argv[1:]
record = {
    "caseId": case_id,
    "resourceFamily": family,
    "negativeClass": negative_class,
    "actorRole": actor,
    "sourceTenantScope": source,
    "targetTenantScope": target,
    "method": method,
    "routeClass": route_class,
    "expectedOutcomeCategory": expected,
    "actualStatus": int(status) if status.isdigit() else status,
    "actualCategory": actual,
    "disclosureObserved": disclosure == "true",
    "mutationObserved": mutation == "true",
    "csrfMode": csrf_mode,
    "passed": passed == "true",
}
with open(path, "a", encoding="utf-8") as handle:
    json.dump(record, handle, separators=(",", ":"), sort_keys=True)
    handle.write("\n")
PY
}

# Args:
# case_id family negative_class actor source target method route_class path
# expected csrf_mode body probe_sql marker_scope [extra_header]
sec05_case() {
  local case_id=$1 family=$2 negative_class=$3 actor=$4 source=$5 target=$6 method=$7 route_class=$8 path=$9
  local expected=${10} csrf_mode=${11} body=${12} probe_sql=${13} marker_scope=${14} extra_header=${15:-}
  local response_host response_http tenant jar status actual disclosure mutation=false before='' after='' passed=true
  local -a args

  SEC05_CASES=$((SEC05_CASES + 1))
  response_host="$(security_scan_host_path "sec05-${case_id}.response")"
  response_http="$(security_scan_http_path "sec05-${case_id}.response")"
  rm -f -- "$response_host"
  args=(--silent --show-error -o "$response_http" -w '%{http_code}' -X "$method")

  if [[ "$actor" == "anonymous" ]]; then
    tenant="security-alpha"
  else
    tenant="$(security_scan_role_tenant "$actor")" || return 1
    jar="$(security_scan_cookie_jar "$actor")" || return 1
    args+=(-c "$jar" -b "$jar")
  fi
  args+=(-H "X-Tenant-Slug: $tenant")

  case "$csrf_mode" in
    none|missing) ;;
    valid)
      [[ "$actor" != "anonymous" ]] || return 1
      security_scan_fetch_csrf "$actor" || return 1
      args+=(-H "$SECURITY_SCAN_CSRF_HEADER_NAME: $SECURITY_SCAN_CSRF_TOKEN")
      ;;
    invalid)
      args+=(-H 'X-CSRF-Token: sec05-intentionally-invalid')
      ;;
    *) sec05_fail "unknown CSRF mode '$csrf_mode'"; return 1 ;;
  esac

  if [[ -n "$extra_header" ]]; then
    args+=(-H "$extra_header")
  fi
  if [[ "$body" != "__NO_BODY__" ]]; then
    args+=(-H 'Content-Type: application/json' --data-binary "$body")
  fi

  if [[ -n "$probe_sql" ]]; then
    before="$(sec05_db_one "$probe_sql")" || return 1
  fi

  if ! status="$(security_scan_http "${args[@]}" "$SECURITY_SCAN_TARGET$path")"; then
    status='000'
  fi
  [[ -f "$response_host" ]] || : > "$response_host"

  if [[ -n "$probe_sql" ]]; then
    after="$(sec05_db_one "$probe_sql")" || return 1
    [[ "$before" == "$after" ]] || mutation=true
  elif [[ "$method" != "GET" && "$method" != "HEAD" && "$status" == 2?? ]]; then
    # Conservative blocker classification when a denied mutation unexpectedly
    # succeeds but no narrower database probe was supplied.
    mutation=true
  fi

  actual="$(sec05_status_category "$status")"
  disclosure="$(sec05_disclosure_observed "$response_host" "$marker_scope")" || return 1
  if ! sec05_expected_matches "$expected" "$status" || [[ "$disclosure" == "true" ]] || [[ "$mutation" == "true" ]]; then
    passed=false
    SEC05_FAILURES=$((SEC05_FAILURES + 1))
    printf 'SEC-05 blocker: %s (%s %s) status=%s category=%s disclosure=%s mutation=%s\n' \
      "$case_id" "$method" "$route_class" "$status" "$actual" "$disclosure" "$mutation" >&2
  fi

  sec05_append_case \
    "$case_id" "$family" "$negative_class" "$actor" "$source" "$target" "$method" "$route_class" \
    "$expected" "$status" "$actual" "$disclosure" "$mutation" "$csrf_mode" "$passed"
}

sec05_resolve_fixture() {
  SEC05_ALPHA_TENANT_ID="$(sec05_db_one "SELECT \"Id\" FROM tenants WHERE \"Slug\"='security-alpha';")" || return 1
  SEC05_BETA_TENANT_ID="$(sec05_db_one "SELECT \"Id\" FROM tenants WHERE \"Slug\"='security-beta';")" || return 1
  SEC05_ALPHA_OWNER_ID="$(sec05_db_one "SELECT \"Id\" FROM users WHERE lower(\"Email\")='security-alpha-owner@example.test';")" || return 1
  SEC05_BETA_OWNER_ID="$(sec05_db_one "SELECT \"Id\" FROM users WHERE lower(\"Email\")='security-beta-owner@example.test';")" || return 1
  SEC05_ALPHA_RESTRICTED_ID="$(sec05_db_one "SELECT \"Id\" FROM users WHERE lower(\"Email\")='security-alpha-restricted@example.test';")" || return 1

  SEC05_ALPHA_WORKSPACE_ID="$(sec05_db_one "SELECT \"Id\" FROM workspaces WHERE \"TenantId\"='$SEC05_ALPHA_TENANT_ID' AND \"Slug\"='sec02-alpha-workspace';")" || return 1
  SEC05_BETA_WORKSPACE_ID="$(sec05_db_one "SELECT \"Id\" FROM workspaces WHERE \"TenantId\"='$SEC05_BETA_TENANT_ID' AND \"Slug\"='sec02-beta-workspace';")" || return 1
  SEC05_ALPHA_PROJECT_ID="$(sec05_db_one "SELECT \"Id\" FROM projects WHERE \"TenantId\"='$SEC05_ALPHA_TENANT_ID' AND \"Slug\"='sec02-alpha-project';")" || return 1
  SEC05_BETA_PROJECT_ID="$(sec05_db_one "SELECT \"Id\" FROM projects WHERE \"TenantId\"='$SEC05_BETA_TENANT_ID' AND \"Slug\"='sec02-beta-project';")" || return 1
  SEC05_ALPHA_TASK_ID="$(sec05_db_one "SELECT \"Id\" FROM task_items WHERE \"TenantId\"='$SEC05_ALPHA_TENANT_ID' AND \"Title\"='SEC02 ALPHA PRIVATE TASK CANARY';")" || return 1
  SEC05_BETA_TASK_ID="$(sec05_db_one "SELECT \"Id\" FROM task_items WHERE \"TenantId\"='$SEC05_BETA_TENANT_ID' AND \"Title\"='SEC02 BETA PRIVATE TASK CANARY';")" || return 1
  SEC05_ALPHA_FILE_ID="$(sec05_db_one "SELECT \"Id\" FROM file_objects WHERE \"TenantId\"='$SEC05_ALPHA_TENANT_ID' AND \"OriginalFileName\"='sec02-alpha-private.txt';")" || return 1
  SEC05_BETA_FILE_ID="$(sec05_db_one "SELECT \"Id\" FROM file_objects WHERE \"TenantId\"='$SEC05_BETA_TENANT_ID' AND \"OriginalFileName\"='sec02-beta-private.txt';")" || return 1
  SEC05_ALPHA_ATTACHMENT_ID="$(sec05_db_one "SELECT \"Id\" FROM attachments WHERE \"TenantId\"='$SEC05_ALPHA_TENANT_ID' AND \"OwnerId\"='$SEC05_ALPHA_TASK_ID' AND \"FileObjectId\"='$SEC05_ALPHA_FILE_ID' AND \"DeletedAt\" IS NULL;")" || return 1
  SEC05_BETA_ATTACHMENT_ID="$(sec05_db_one "SELECT \"Id\" FROM attachments WHERE \"TenantId\"='$SEC05_BETA_TENANT_ID' AND \"OwnerId\"='$SEC05_BETA_TASK_ID' AND \"FileObjectId\"='$SEC05_BETA_FILE_ID' AND \"DeletedAt\" IS NULL;")" || return 1
  SEC05_ALPHA_CONVERSATION_ID="$(sec05_db_one "SELECT \"Id\" FROM conversations WHERE \"TenantId\"='$SEC05_ALPHA_TENANT_ID' AND \"Title\"='SEC02 ALPHA PRIVATE CONVERSATION CANARY';")" || return 1
  SEC05_BETA_CONVERSATION_ID="$(sec05_db_one "SELECT \"Id\" FROM conversations WHERE \"TenantId\"='$SEC05_BETA_TENANT_ID' AND \"Title\"='SEC02 BETA PRIVATE CONVERSATION CANARY';")" || return 1
  SEC05_ALPHA_MESSAGE_ID="$(sec05_db_one "SELECT \"Id\" FROM messages WHERE \"TenantId\"='$SEC05_ALPHA_TENANT_ID' AND \"ConversationId\"='$SEC05_ALPHA_CONVERSATION_ID' AND \"Body\"='SEC02_ALPHA_MESSAGE_CANARY_DO_NOT_LEAK' AND \"DeletedAt\" IS NULL;")" || return 1
  SEC05_BETA_MESSAGE_ID="$(sec05_db_one "SELECT \"Id\" FROM messages WHERE \"TenantId\"='$SEC05_BETA_TENANT_ID' AND \"ConversationId\"='$SEC05_BETA_CONVERSATION_ID' AND \"Body\"='SEC02_BETA_MESSAGE_CANARY_DO_NOT_LEAK' AND \"DeletedAt\" IS NULL;")" || return 1
  SEC05_ALPHA_SHADOW_CONVERSATION_ID="$(sec05_db_one "SELECT \"Id\" FROM conversations WHERE \"TenantId\"='$SEC05_ALPHA_TENANT_ID' AND \"Title\"='SEC05 ALPHA SHADOW CONVERSATION CANARY';")" || return 1
  SEC05_ALPHA_SHADOW_MESSAGE_ID="$(sec05_db_one "SELECT \"Id\" FROM messages WHERE \"TenantId\"='$SEC05_ALPHA_TENANT_ID' AND \"ConversationId\"='$SEC05_ALPHA_SHADOW_CONVERSATION_ID' AND \"Body\"='SEC05_ALPHA_SHADOW_MESSAGE_DO_NOT_LEAK' AND \"DeletedAt\" IS NULL;")" || return 1
  SEC05_ALPHA_NOTIFICATION_ID="$(sec05_db_one "SELECT \"Id\" FROM notifications WHERE \"TenantId\"='$SEC05_ALPHA_TENANT_ID' AND \"LogicalKey\"='sec05-alpha-task-open-canary' AND \"DeletedAt\" IS NULL;")" || return 1
  SEC05_BETA_NOTIFICATION_ID="$(sec05_db_one "SELECT \"Id\" FROM notifications WHERE \"TenantId\"='$SEC05_BETA_TENANT_ID' AND \"LogicalKey\"='sec05-beta-task-open-canary' AND \"DeletedAt\" IS NULL;")" || return 1
  SEC05_ALPHA_ANNOUNCEMENT_ID="$(sec05_db_one "SELECT \"Id\" FROM announcements WHERE \"TenantId\"='$SEC05_ALPHA_TENANT_ID' AND \"Title\"='SEC05 ALPHA ANNOUNCEMENT CANARY' AND \"DeletedAt\" IS NULL;")" || return 1
  SEC05_BETA_ANNOUNCEMENT_ID="$(sec05_db_one "SELECT \"Id\" FROM announcements WHERE \"TenantId\"='$SEC05_BETA_TENANT_ID' AND \"Title\"='SEC05 BETA ANNOUNCEMENT CANARY' AND \"DeletedAt\" IS NULL;")" || return 1

  export SEC05_ALPHA_TENANT_ID SEC05_BETA_TENANT_ID SEC05_ALPHA_OWNER_ID SEC05_BETA_OWNER_ID SEC05_ALPHA_RESTRICTED_ID
  export SEC05_ALPHA_WORKSPACE_ID SEC05_BETA_WORKSPACE_ID SEC05_ALPHA_PROJECT_ID SEC05_BETA_PROJECT_ID
  export SEC05_ALPHA_TASK_ID SEC05_BETA_TASK_ID SEC05_ALPHA_FILE_ID SEC05_BETA_FILE_ID
  export SEC05_ALPHA_ATTACHMENT_ID SEC05_BETA_ATTACHMENT_ID SEC05_ALPHA_CONVERSATION_ID SEC05_BETA_CONVERSATION_ID
  export SEC05_ALPHA_MESSAGE_ID SEC05_BETA_MESSAGE_ID SEC05_ALPHA_SHADOW_CONVERSATION_ID SEC05_ALPHA_SHADOW_MESSAGE_ID
  export SEC05_ALPHA_NOTIFICATION_ID SEC05_BETA_NOTIFICATION_ID SEC05_ALPHA_ANNOUNCEMENT_ID SEC05_BETA_ANNOUNCEMENT_ID
}

sec05_write_evidence() {
  mkdir -p "$(dirname "$SEC05_EVIDENCE_PATH")"
  python3 - "$SEC05_JSONL" "$SEC05_ROUTE_INVENTORY" "$SEC05_EVIDENCE_PATH" "${GITHUB_SHA:-unknown}" <<'PY'
from __future__ import annotations

import datetime as dt
import json
import sys

jsonl_path, inventory_path, output_path, commit_sha = sys.argv[1:5]
with open(inventory_path, encoding="utf-8") as handle:
    inventory = json.load(handle)
with open(jsonl_path, encoding="utf-8") as handle:
    cases = [json.loads(line) for line in handle if line.strip()]
failed = [case for case in cases if not case["passed"]]
classes = sorted({case["negativeClass"] for case in cases})
families = sorted({case["resourceFamily"] for case in cases})
document = {
    "schemaVersion": 1,
    "program": "SEC-05",
    "generatedAtUtc": dt.datetime.now(dt.timezone.utc).isoformat(),
    "commitSha": commit_sha,
    "artifactPolicy": "metadata-only; protected response bodies and auth material are excluded",
    "routeInventory": inventory,
    "summary": {
        "totalCases": len(cases),
        "passedCases": len(cases) - len(failed),
        "failedCases": len(failed),
        "blockerCount": len(failed),
        "resourceFamilies": families,
        "negativeClasses": classes,
    },
    "cases": cases,
}
with open(output_path, "w", encoding="utf-8") as handle:
    json.dump(document, handle, indent=2, sort_keys=True)
    handle.write("\n")
PY
}

security_authorization_negative_matrix_run() {
  local task_probe cross_file_grant_probe cross_attachment_grant_probe conversation_cursor_probe
  local announcement_read_probe alpha_notification_probe beta_notification_probe role_task_probe
  local mass_assignment_probe csrf_missing_probe csrf_invalid_probe stale_role_probe

  sec05_require_runtime || return 1
  SEC05_FAILURES=0
  SEC05_CASES=0
  SEC05_JSONL="$(security_scan_host_path 'sec05-cases.jsonl')"
  SEC05_ROUTE_INVENTORY="$(security_scan_host_path 'sec05-route-inventory.json')"
  SEC05_EVIDENCE_PATH="${SEC05_EVIDENCE_PATH:-artifacts/security/sec05-authorization-negative-matrix.json}"
  : > "$SEC05_JSONL"

  if ! python3 scripts/security/sec05-route-inventory.py > "$SEC05_ROUTE_INVENTORY"; then
    sec05_fail "current P0 route inventory drifted; refusing to shrink authorization coverage"
    return 1
  fi
  sec05_resolve_fixture || return 1

  task_probe="SELECT COUNT(*) FROM task_items WHERE \"TenantId\"='$SEC05_ALPHA_TENANT_ID' AND \"Title\" LIKE 'SEC05 % MUST NOT CREATE';"
  cross_file_grant_probe="SELECT COUNT(*) FROM file_download_grants WHERE \"FileObjectId\"='$SEC05_BETA_FILE_ID';"
  cross_attachment_grant_probe="SELECT COUNT(*) FROM file_download_grants WHERE \"AttachmentId\"='$SEC05_BETA_ATTACHMENT_ID';"
  conversation_cursor_probe="SELECT COALESCE(\"LastReadMessageId\"::text,'<null>') FROM conversation_members WHERE \"TenantId\"='$SEC05_ALPHA_TENANT_ID' AND \"ConversationId\"='$SEC05_ALPHA_CONVERSATION_ID' AND \"UserId\"='$SEC05_ALPHA_OWNER_ID';"
  announcement_read_probe="SELECT COUNT(*) FROM announcement_reads WHERE \"TenantId\"='$SEC05_BETA_TENANT_ID' AND \"AnnouncementId\"='$SEC05_BETA_ANNOUNCEMENT_ID';"
  alpha_notification_probe="SELECT CASE WHEN \"IsRead\" THEN 'read' ELSE 'unread' END FROM notifications WHERE \"Id\"='$SEC05_ALPHA_NOTIFICATION_ID';"
  beta_notification_probe="SELECT CASE WHEN \"IsRead\" THEN 'read' ELSE 'unread' END FROM notifications WHERE \"Id\"='$SEC05_BETA_NOTIFICATION_ID';"
  role_task_probe="$task_probe"
  mass_assignment_probe="$task_probe"
  csrf_missing_probe="$task_probe"
  csrf_invalid_probe="$task_probe"
  stale_role_probe="$task_probe"

  # Anonymous boundary.
  sec05_case anon-workspace workspace anonymous anonymous \
    'none' 'security-alpha/workspace' GET 'GET /api/workspaces/{workspaceId}' "/api/workspaces/$SEC05_ALPHA_WORKSPACE_ID" \
    unauthenticated none __NO_BODY__ '' alpha

  # Tenant object substitution with an authenticated Alpha session.
  sec05_case alpha-switch-beta tenant cross-tenant-object-substitution alpha-owner \
    'security-alpha' 'security-beta' POST 'POST /api/tenants/switch' '/api/tenants/switch' \
    denied valid "{\"tenantId\":\"$SEC05_BETA_TENANT_ID\"}" '' beta

  # Cross-Tenant reads in both directions across the primary P0 object graph.
  sec05_case alpha-read-beta-workspace workspace cross-tenant-object-substitution alpha-owner \
    'security-alpha' 'security-beta/workspace' GET 'GET /api/workspaces/{workspaceId}' "/api/workspaces/$SEC05_BETA_WORKSPACE_ID" \
    denied none __NO_BODY__ '' beta
  sec05_case beta-read-alpha-workspace workspace cross-tenant-object-substitution beta-owner \
    'security-beta' 'security-alpha/workspace' GET 'GET /api/workspaces/{workspaceId}' "/api/workspaces/$SEC05_ALPHA_WORKSPACE_ID" \
    denied none __NO_BODY__ '' alpha
  sec05_case alpha-read-beta-workspace-management workspace-member-management cross-tenant-object-substitution alpha-owner \
    'security-alpha' 'security-beta/workspace-management' GET 'GET /api/workspaces/{workspaceId}/members/management' "/api/workspaces/$SEC05_BETA_WORKSPACE_ID/members/management" \
    denied none __NO_BODY__ '' beta

  sec05_case alpha-read-beta-project project cross-tenant-object-substitution alpha-owner \
    'security-alpha' 'security-beta/project' GET 'GET /api/projects/{projectId}' "/api/projects/$SEC05_BETA_PROJECT_ID" \
    denied none __NO_BODY__ '' beta
  sec05_case beta-read-alpha-project project cross-tenant-object-substitution beta-owner \
    'security-beta' 'security-alpha/project' GET 'GET /api/projects/{projectId}' "/api/projects/$SEC05_ALPHA_PROJECT_ID" \
    denied none __NO_BODY__ '' alpha

  sec05_case alpha-read-beta-task task cross-tenant-object-substitution alpha-owner \
    'security-alpha' 'security-beta/task' GET 'GET /api/tasks/{taskId}' "/api/tasks/$SEC05_BETA_TASK_ID" \
    denied none __NO_BODY__ '' beta
  sec05_case beta-read-alpha-task task cross-tenant-object-substitution beta-owner \
    'security-beta' 'security-alpha/task' GET 'GET /api/tasks/{taskId}' "/api/tasks/$SEC05_ALPHA_TASK_ID" \
    denied none __NO_BODY__ '' alpha
  sec05_case alpha-read-beta-task-scope task-subresource cross-tenant-object-substitution alpha-owner \
    'security-alpha' 'security-beta/task/execution-scope' GET 'GET /api/tasks/{taskId}/execution-scope' "/api/tasks/$SEC05_BETA_TASK_ID/execution-scope" \
    denied none __NO_BODY__ '' beta

  sec05_case alpha-read-beta-file file cross-tenant-object-substitution alpha-owner \
    'security-alpha' 'security-beta/file' GET 'GET /api/files/{fileObjectId}' "/api/files/$SEC05_BETA_FILE_ID" \
    denied none __NO_BODY__ '' beta
  sec05_case alpha-read-beta-file-sharing file-sharing cross-tenant-object-substitution alpha-owner \
    'security-alpha' 'security-beta/file/sharing' GET 'GET /api/files/{fileObjectId}/sharing' "/api/files/$SEC05_BETA_FILE_ID/sharing" \
    denied none __NO_BODY__ '' beta
  sec05_case alpha-mutate-beta-file-grant file cross-tenant-object-substitution alpha-owner \
    'security-alpha' 'security-beta/file/download-grant' POST 'POST /api/files/{fileObjectId}/download-grants' "/api/files/$SEC05_BETA_FILE_ID/download-grants" \
    denied valid '{}' "$cross_file_grant_probe" beta

  sec05_case alpha-read-beta-attachment attachment cross-tenant-object-substitution alpha-owner \
    'security-alpha' 'security-beta/attachment' GET 'GET /api/attachments/{attachmentId}' "/api/attachments/$SEC05_BETA_ATTACHMENT_ID" \
    denied none __NO_BODY__ '' beta
  sec05_case alpha-mutate-beta-attachment-grant attachment cross-tenant-object-substitution alpha-owner \
    'security-alpha' 'security-beta/attachment/download-grant' POST 'POST /api/attachments/{attachmentId}/download-grants' "/api/attachments/$SEC05_BETA_ATTACHMENT_ID/download-grants" \
    safe-not-found valid '{}' "$cross_attachment_grant_probe" beta

  sec05_case alpha-read-beta-conversation conversation cross-tenant-object-substitution alpha-owner \
    'security-alpha' 'security-beta/conversation' GET 'GET /api/conversations/{conversationId}' "/api/conversations/$SEC05_BETA_CONVERSATION_ID" \
    denied none __NO_BODY__ '' beta
  sec05_case beta-read-alpha-conversation conversation cross-tenant-object-substitution beta-owner \
    'security-beta' 'security-alpha/conversation' GET 'GET /api/conversations/{conversationId}' "/api/conversations/$SEC05_ALPHA_CONVERSATION_ID" \
    denied none __NO_BODY__ '' alpha
  sec05_case alpha-read-beta-message-thread message cross-tenant-object-substitution alpha-owner \
    'security-alpha' 'security-beta/message' GET 'GET /api/messages/{messageId}/thread' "/api/messages/$SEC05_BETA_MESSAGE_ID/thread" \
    denied none __NO_BODY__ '' beta

  # Same-Tenant wrong-scope substitution: a real message from another allowed
  # Alpha conversation must not become a read cursor for the primary conversation.
  sec05_case alpha-wrong-conversation-read conversation same-tenant-wrong-scope alpha-owner \
    'security-alpha/primary-conversation' 'security-alpha/shadow-conversation-message' POST 'POST /api/conversations/{conversationId}/read' "/api/conversations/$SEC05_ALPHA_CONVERSATION_ID/read" \
    denied valid "{\"lastReadMessageId\":\"$SEC05_ALPHA_SHADOW_MESSAGE_ID\"}" "$conversation_cursor_probe" alpha-shadow

  # Notification open is recipient-owned and reauthorizes the current target.
  sec05_case alpha-open-beta-notification notification-open cross-tenant-object-substitution alpha-owner \
    'security-alpha' 'security-beta/notification' POST 'POST /api/notifications/{notificationId}/open' "/api/notifications/$SEC05_BETA_NOTIFICATION_ID/open" \
    safe-not-found valid '{}' "$beta_notification_probe" beta
  sec05_case beta-open-alpha-notification notification-open cross-tenant-object-substitution beta-owner \
    'security-beta' 'security-alpha/notification' POST 'POST /api/notifications/{notificationId}/open' "/api/notifications/$SEC05_ALPHA_NOTIFICATION_ID/open" \
    safe-not-found valid '{}' "$alpha_notification_probe" alpha

  sec05_case alpha-read-beta-announcement announcement cross-tenant-object-substitution alpha-owner \
    'security-alpha' 'security-beta/announcement' GET 'GET /api/announcements/{announcementId}' "/api/announcements/$SEC05_BETA_ANNOUNCEMENT_ID" \
    denied none __NO_BODY__ '' beta
  sec05_case alpha-mark-beta-announcement-read announcement cross-tenant-object-substitution alpha-owner \
    'security-alpha' 'security-beta/announcement/read' POST 'POST /api/announcements/{announcementId}/read' "/api/announcements/$SEC05_BETA_ANNOUNCEMENT_ID/read" \
    denied valid '{}' "$announcement_read_probe" beta
  sec05_case alpha-announcement-audiences announcement cross-tenant-projection-nondisclosure alpha-owner \
    'security-alpha' 'security-beta/audience-canaries' GET 'GET /api/announcements/audiences' '/api/announcements/audiences' \
    allowed-no-disclosure none __NO_BODY__ '' beta

  # BFLA / role separation.
  # Tenant ownership is not PlatformAdmin authority; pagination never grants access.
  sec05_case restricted-admin-invites admin-invites bfla-role-downgrade alpha-restricted \
    'security-alpha/restricted' 'security-alpha/admin-invites' GET 'GET /api/admin/invites' '/api/admin/invites?page=1&pageSize=50' \
    forbidden none __NO_BODY__ '' alpha
  sec05_case beta-owner-admin-invites admin-invites bfla-role-downgrade beta-owner \
    'security-beta/owner' 'security-beta/admin-invites' GET 'GET /api/admin/invites' '/api/admin/invites?pageSize=50&page=1' \
    forbidden none __NO_BODY__ '' beta
  sec05_case restricted-workspace-management workspace-member-management bfla-role-downgrade alpha-restricted \
    'security-alpha/restricted' 'security-alpha/workspace-management' GET 'GET /api/workspaces/{workspaceId}/members/management' "/api/workspaces/$SEC05_ALPHA_WORKSPACE_ID/members/management" \
    denied none __NO_BODY__ '' none
  sec05_case member-admin-audit-grid audit bfla-role-downgrade alpha-member \
    'security-alpha/member' 'security-alpha/admin-audit' GET 'GET /api/admin/audit-grid' '/api/admin/audit-grid?page=1&pageSize=20' \
    denied none __NO_BODY__ '' none
  sec05_case restricted-task-create task-create-authority bfla-role-downgrade alpha-restricted \
    'security-alpha/restricted' 'security-alpha/project/task-create' POST 'POST /api/projects/{projectId}/tasks/create' "/api/projects/$SEC05_ALPHA_PROJECT_ID/tasks/create" \
    denied valid '{"title":"SEC05 RESTRICTED MUST NOT CREATE","sourceScopeMode":"Inherit"}' "$role_task_probe" none \
    'Idempotency-Key: sec05-restricted-task-create-000001'

  # Cookie-authenticated mutation CSRF boundary.
  sec05_case task-create-missing-csrf task-create-authority csrf-boundary alpha-owner \
    'security-alpha/owner' 'security-alpha/project/task-create' POST 'POST /api/projects/{projectId}/tasks/create' "/api/projects/$SEC05_ALPHA_PROJECT_ID/tasks/create" \
    csrf-rejected missing '{"title":"SEC05 CSRF MISSING MUST NOT CREATE","sourceScopeMode":"Inherit"}' "$csrf_missing_probe" none \
    'Idempotency-Key: sec05-csrf-missing-task-create-000001'
  sec05_case task-create-invalid-csrf task-create-authority csrf-boundary alpha-owner \
    'security-alpha/owner' 'security-alpha/project/task-create' POST 'POST /api/projects/{projectId}/tasks/create' "/api/projects/$SEC05_ALPHA_PROJECT_ID/tasks/create" \
    csrf-rejected invalid '{"title":"SEC05 CSRF INVALID MUST NOT CREATE","sourceScopeMode":"Inherit"}' "$csrf_invalid_probe" none \
    'Idempotency-Key: sec05-csrf-invalid-task-create-000001'

  # Strict DTO rejects server-owned scope injection before any Task is created.
  sec05_case task-create-mass-assignment task-create-authority mass-assignment alpha-owner \
    'security-alpha/owner' 'security-beta/server-owned-scope-fields' POST 'POST /api/projects/{projectId}/tasks/create' "/api/projects/$SEC05_ALPHA_PROJECT_ID/tasks/create" \
    mass-assignment-rejected valid "{\"title\":\"SEC05 MASS ASSIGNMENT MUST NOT CREATE\",\"sourceScopeMode\":\"Inherit\",\"tenantId\":\"$SEC05_BETA_TENANT_ID\",\"workspaceId\":\"$SEC05_BETA_WORKSPACE_ID\",\"ownerUserId\":\"$SEC05_BETA_OWNER_ID\"}" "$mass_assignment_probe" beta \
    'Idempotency-Key: sec05-mass-assignment-task-create-000001'

  # Stale authorization: keep the established cookie, mutate only current server
  # authorization state, then prove the same session loses access without login.
  sec05_db_exec "UPDATE workspace_members SET \"Status\"='Suspended' WHERE \"TenantId\"='$SEC05_ALPHA_TENANT_ID' AND \"WorkspaceId\"='$SEC05_ALPHA_WORKSPACE_ID' AND \"UserId\"='$SEC05_ALPHA_OWNER_ID';" || return 1
  sec05_case stale-membership-workspace workspace stale-authorization alpha-owner \
    'security-alpha/session-established' 'security-alpha/workspace-after-membership-revoke' GET 'GET /api/workspaces/{workspaceId}' "/api/workspaces/$SEC05_ALPHA_WORKSPACE_ID" \
    denied none __NO_BODY__ '' alpha
  sec05_db_exec "UPDATE workspace_members SET \"Status\"='Active' WHERE \"TenantId\"='$SEC05_ALPHA_TENANT_ID' AND \"WorkspaceId\"='$SEC05_ALPHA_WORKSPACE_ID' AND \"UserId\"='$SEC05_ALPHA_OWNER_ID';" || return 1

  sec05_db_exec "UPDATE workspace_members SET \"Role\"='ReadOnly' WHERE \"TenantId\"='$SEC05_ALPHA_TENANT_ID' AND \"WorkspaceId\"='$SEC05_ALPHA_WORKSPACE_ID' AND \"UserId\"='$SEC05_ALPHA_OWNER_ID';" || return 1
  sec05_db_exec "UPDATE project_members SET \"Role\"='Viewer' WHERE \"TenantId\"='$SEC05_ALPHA_TENANT_ID' AND \"ProjectId\"='$SEC05_ALPHA_PROJECT_ID' AND \"UserId\"='$SEC05_ALPHA_OWNER_ID';" || return 1
  sec05_case stale-role-task-create task-create-authority stale-authorization alpha-owner \
    'security-alpha/session-established' 'security-alpha/project-after-role-downgrade' POST 'POST /api/projects/{projectId}/tasks/create' "/api/projects/$SEC05_ALPHA_PROJECT_ID/tasks/create" \
    denied valid '{"title":"SEC05 STALE ROLE MUST NOT CREATE","sourceScopeMode":"Inherit"}' "$stale_role_probe" none \
    'Idempotency-Key: sec05-stale-role-task-create-000001'
  sec05_db_exec "UPDATE workspace_members SET \"Role\"='Owner' WHERE \"TenantId\"='$SEC05_ALPHA_TENANT_ID' AND \"WorkspaceId\"='$SEC05_ALPHA_WORKSPACE_ID' AND \"UserId\"='$SEC05_ALPHA_OWNER_ID';" || return 1
  sec05_db_exec "UPDATE project_members SET \"Role\"='Owner' WHERE \"TenantId\"='$SEC05_ALPHA_TENANT_ID' AND \"ProjectId\"='$SEC05_ALPHA_PROJECT_ID' AND \"UserId\"='$SEC05_ALPHA_OWNER_ID';" || return 1

  sec05_db_exec "UPDATE workspace_members SET \"Status\"='Suspended' WHERE \"TenantId\"='$SEC05_ALPHA_TENANT_ID' AND \"WorkspaceId\"='$SEC05_ALPHA_WORKSPACE_ID' AND \"UserId\"='$SEC05_ALPHA_OWNER_ID';" || return 1
  sec05_case stale-notification-open notification-open stale-authorization alpha-owner \
    'security-alpha/session-established' 'security-alpha/notification-target-after-auth-loss' POST 'POST /api/notifications/{notificationId}/open' "/api/notifications/$SEC05_ALPHA_NOTIFICATION_ID/open" \
    safe-not-found valid '{}' "$alpha_notification_probe" alpha
  sec05_db_exec "UPDATE workspace_members SET \"Status\"='Active' WHERE \"TenantId\"='$SEC05_ALPHA_TENANT_ID' AND \"WorkspaceId\"='$SEC05_ALPHA_WORKSPACE_ID' AND \"UserId\"='$SEC05_ALPHA_OWNER_ID';" || return 1

  sec05_write_evidence || return 1
  if (( SEC05_FAILURES != 0 )); then
    sec05_fail "$SEC05_FAILURES blocker case(s) failed out of $SEC05_CASES; no baseline escape is permitted"
    return 1
  fi

  printf 'SEC-05 authorization negative matrix passed: %s real-PostgreSQL HTTP cases, zero disclosures, zero denied mutations.\n' "$SEC05_CASES"
}
