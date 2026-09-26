#!/usr/bin/env python3
"""Verify and emit the implemented P0 authorization surfaces covered by SEC-05."""

from __future__ import annotations

import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
CONTROLLERS = ROOT / "src" / "Coglatas.Web" / "Controllers"

# These are current implemented P0 families from Issue #576. Tokens are checked
# against controller source so route renames/removals fail the security gate rather
# than silently shrinking the negative matrix. Unimplemented resource families are
# intentionally not invented here.
SURFACES = [
    {
        "family": "tenant",
        "controller": "TenantsController.cs",
        "tokens": ['[Route("api/tenants")]', '[HttpGet("current")]', '[HttpPost("switch")]'],
        "routeClasses": ["GET /api/tenants/current", "POST /api/tenants/switch"],
    },
    {
        "family": "workspace",
        "controller": "WorkspacesController.cs",
        "tokens": ['[HttpGet("api/workspaces/{workspaceId:guid}")]', '[HttpPatch("api/workspaces/{workspaceId:guid}")]'],
        "routeClasses": ["GET /api/workspaces/{workspaceId}", "PATCH /api/workspaces/{workspaceId}"],
    },
    {
        "family": "workspace-member-management",
        "controller": "WorkspacesController.cs",
        "tokens": ['[HttpGet("api/workspaces/{workspaceId:guid}/members/management")]', '[HttpPatch("api/workspaces/{workspaceId:guid}/members/{userId:guid}")]'],
        "routeClasses": ["GET /api/workspaces/{workspaceId}/members/management", "PATCH /api/workspaces/{workspaceId}/members/{userId}"],
    },
    {
        "family": "project",
        "controller": "ProjectsController.cs",
        "tokens": ['api/projects/{projectId:guid}'],
        "routeClasses": ["GET /api/projects/{projectId}"],
    },
    {
        "family": "task",
        "controller": "ProjectsController.cs",
        "tokens": ['api/tasks/{taskItemId:guid}'],
        "routeClasses": ["GET /api/tasks/{taskId}"],
    },
    {
        "family": "task-subresource",
        "controller": "TaskExecutionController.cs",
        "tokens": ['[HttpGet("api/tasks/{taskItemId:guid}/execution-scope")]'],
        "routeClasses": ["GET /api/tasks/{taskId}/execution-scope"],
    },
    {
        "family": "task-create-authority",
        "controller": "ProjectTaskCreateController.cs",
        "tokens": ['[HttpPost("api/projects/{projectId:guid}/tasks/create")]'],
        "routeClasses": ["POST /api/projects/{projectId}/tasks/create"],
    },
    {
        "family": "file",
        "controller": "FilesController.cs",
        "tokens": ['[HttpGet("api/files/{fileObjectId:guid}")]', '[HttpPost("api/files/{fileObjectId:guid}/download-grants")]'],
        "routeClasses": ["GET /api/files/{fileObjectId}", "POST /api/files/{fileObjectId}/download-grants"],
    },
    {
        "family": "file-sharing",
        "controller": "FilesController.cs",
        "tokens": ['[HttpGet("api/files/{fileObjectId:guid}/sharing")]', '[HttpPut("api/files/{fileObjectId:guid}/sharing")]'],
        "routeClasses": ["GET /api/files/{fileObjectId}/sharing", "PUT /api/files/{fileObjectId}/sharing"],
    },
    {
        "family": "attachment",
        "controller": "AttachmentsController.cs",
        "tokens": ['[HttpGet("api/attachments/{attachmentId:guid}")]', '[HttpPost("api/attachments/{attachmentId:guid}/download-grants")]'],
        "routeClasses": ["GET /api/attachments/{attachmentId}", "POST /api/attachments/{attachmentId}/download-grants"],
    },
    {
        "family": "conversation",
        "controller": "ConversationsController.cs",
        "tokens": ['[HttpGet("api/conversations/{conversationId:guid}")]', '[HttpPost("api/conversations/{conversationId:guid}/read")]'],
        "routeClasses": ["GET /api/conversations/{conversationId}", "POST /api/conversations/{conversationId}/read"],
    },
    {
        "family": "message",
        "controller": "ConversationsController.cs",
        "tokens": ['[HttpGet("api/messages/{messageId:guid}/thread")]', '[HttpPatch("api/messages/{messageId:guid}")]'],
        "routeClasses": ["GET /api/messages/{messageId}/thread", "PATCH /api/messages/{messageId}"],
    },
    {
        "family": "notification-open",
        "controller": "NotificationsController.cs",
        "tokens": ['[HttpPost("api/notifications/{notificationId:guid}/open")]'],
        "routeClasses": ["POST /api/notifications/{notificationId}/open"],
    },
    {
        "family": "announcement",
        "controller": "AnnouncementsController.cs",
        "tokens": ['[HttpGet("api/announcements/audiences")]', '[HttpGet("api/announcements/{announcementId:guid}")]', '[HttpPost("api/announcements/{announcementId:guid}/read")]'],
        "routeClasses": ["GET /api/announcements/audiences", "GET /api/announcements/{announcementId}", "POST /api/announcements/{announcementId}/read"],
    },
    {
        "family": "audit",
        "controller": "AuditController.cs",
        "tokens": ['[HttpGet("api/audit-logs")]', '[HttpGet("api/admin/audit-grid")]'],
        "routeClasses": ["GET /api/audit-logs", "GET /api/admin/audit-grid"],
    },
    {
        "family": "audit-claims-evidence",
        "controller": "AuditClaimsEvidenceController.cs",
        "tokens": ['[HttpGet("api/admin/audit/claims-evidence")]'],
        "routeClasses": ["GET /api/admin/audit/claims-evidence"],
    },
    {
        "family": "audit-finding",
        "controller": "AuditFindingsController.cs",
        "tokens": ['[HttpGet("api/admin/audit/findings")]', '[HttpPatch("api/admin/audit/findings/{findingId:guid}/triage")]'],
        "routeClasses": ["GET /api/admin/audit/findings", "PATCH /api/admin/audit/findings/{findingId}/triage"],
    },
]


def main() -> int:
    failures: list[str] = []
    emitted: list[dict[str, object]] = []

    for surface in SURFACES:
        path = CONTROLLERS / str(surface["controller"])
        if not path.is_file():
            failures.append(f"{surface['family']}: missing {path.relative_to(ROOT)}")
            continue
        source = path.read_text(encoding="utf-8")
        missing = [token for token in surface["tokens"] if token not in source]
        emitted.append(
            {
                "family": surface["family"],
                "controller": surface["controller"],
                "routeClasses": surface["routeClasses"],
                "implemented": not missing,
            }
        )
        if missing:
            failures.append(
                f"{surface['family']}: expected implemented route token(s) disappeared from {surface['controller']}"
            )

    document = {
        "schemaVersion": 1,
        "program": "SEC-05",
        "source": "current implemented controller route set",
        "families": emitted,
    }
    json.dump(document, sys.stdout, indent=2, sort_keys=True)
    sys.stdout.write("\n")

    if failures:
        for failure in failures:
            print(f"SEC-05 route inventory failed: {failure}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
