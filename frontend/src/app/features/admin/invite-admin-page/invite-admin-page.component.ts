import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { finalize } from 'rxjs';

import { FrontendApiError } from '../../../core/api/api-error.model';
import { AppEmptyStateComponent } from '../../../shared/empty-state/app-empty-state/app-empty-state.component';
import { AppInlineLoadingComponent } from '../../../shared/loading/app-inline-loading/app-inline-loading.component';
import { AppPermissionDeniedComponent } from '../../../shared/permission/app-permission-denied/app-permission-denied.component';
import { AppDataGridComponent } from '../../../shared/grid/app-data-grid/app-data-grid.component';
import { AppDataGridColumnDef } from '../../../shared/grid/app-data-grid/app-data-grid.types';
import { CoglatasDateTimePickerComponent } from '../../../shared/ui/coglatas-date-time-picker/coglatas-date-time-picker.component';
import { CoglatasDialogComponent } from '../../../shared/ui/coglatas-dialog/coglatas-dialog.component';

type InvitePageStatus = 'loading' | 'ready' | 'empty' | 'permissionDenied' | 'error';
type WorkspaceRoleName = 'Owner' | 'Admin' | 'Adviser' | 'Member' | 'ReadOnly';
type WorkspaceRoleValue = 0 | 1 | 2 | 3 | 4;

interface WorkspaceRoleOption {
  readonly label: WorkspaceRoleName;
  readonly value: WorkspaceRoleValue;
}

interface WorkspaceDto {
  readonly id?: unknown;
  readonly name?: unknown;
}

interface InviteDto {
  readonly id?: unknown;
  readonly workspaceId?: unknown;
  readonly email?: unknown;
  readonly role?: unknown;
  readonly inviteUrl?: unknown;
  readonly expiresAt?: unknown;
  readonly acceptedAt?: unknown;
  readonly revokedAt?: unknown;
  readonly createdAt?: unknown;
}

interface PagedResponseDto<T> {
  readonly items?: readonly T[];
}

interface WorkspaceOption {
  readonly id: string;
  readonly name: string;
}

interface InviteRow {
  readonly id: string;
  readonly workspaceId: string;
  readonly email: string;
  readonly role: string;
  readonly status: string;
  readonly expiresAt: string;
}

interface CreatedInviteDetails {
  readonly email: string;
  readonly role: string;
  readonly inviteUrl: string;
  readonly expiresAt: string;
}

const WORKSPACE_ROLE: Record<WorkspaceRoleName, WorkspaceRoleValue> = {
  Owner: 0,
  Admin: 1,
  Adviser: 2,
  Member: 3,
  ReadOnly: 4
};

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-invite-admin-page',
  standalone: true,
  imports: [FormsModule, CoglatasDateTimePickerComponent, CoglatasDialogComponent, AppDataGridComponent, AppEmptyStateComponent, AppInlineLoadingComponent, AppPermissionDeniedComponent],
  templateUrl: './invite-admin-page.component.html',
  styleUrl: './invite-admin-page.component.scss',
})
export class InviteAdminPageComponent {
  private readonly http = inject(HttpClient);

  readonly status = signal<InvitePageStatus>('loading');
  readonly message = signal<string | null>(null);
  readonly workspaces = signal<readonly WorkspaceOption[]>([]);
  readonly invites = signal<readonly InviteRow[]>([]);
  readonly selectedWorkspaceId = signal('');
  readonly email = signal('');
  readonly role = signal<WorkspaceRoleValue>(WORKSPACE_ROLE.Member);
  readonly expiresAt = signal('');
  readonly submitting = signal(false);
  readonly createdNotice = signal<string | null>(null);
  readonly createdInvite = signal<CreatedInviteDetails | null>(null);
  readonly createDialogOpen = signal(false);
  readonly revokeDialogOpen = signal(false);
  readonly revokeTarget = signal<InviteRow | null>(null);
  readonly bulkEmails = signal('');
  readonly revoking = signal(false);
  readonly inviteColumns: readonly AppDataGridColumnDef<InviteRow>[] = [
    { field: 'email', headerName: 'Email', flex: 2 },
    { field: 'role', headerName: 'Role' },
    { field: 'status', headerName: 'Status' },
    { field: 'expiresAt', headerName: 'Expires', flex: 1 },
    { headerName: 'Actions', actions: (row) => [{ id: 'revoke', label: 'Revoke', row, destructive: true, disabled: row.status !== 'Pending', disabledReason: row.status === 'Pending' ? undefined : 'Only pending invites can be revoked.' }] },
  ];

  readonly canSubmit = computed(
    () =>
      !this.submitting() &&
      this.selectedWorkspaceId().length > 0 &&
      this.email().trim().length > 0 &&
      Number.isInteger(this.role())
  );

  readonly roleOptions: readonly WorkspaceRoleOption[] = [
    { label: 'Member', value: WORKSPACE_ROLE.Member },
    { label: 'ReadOnly', value: WORKSPACE_ROLE.ReadOnly },
    { label: 'Adviser', value: WORKSPACE_ROLE.Adviser },
    { label: 'Admin', value: WORKSPACE_ROLE.Admin },
    { label: 'Owner', value: WORKSPACE_ROLE.Owner }
  ];

  constructor() {
    this.load();
  }

  load(): void {
    this.status.set('loading');
    this.message.set(null);
    this.http.get<readonly WorkspaceDto[]>('/api/workspaces', { withCredentials: true }).subscribe({
      next: (response) => {
        const workspaces = response.map(toWorkspaceOption).filter((workspace) => workspace.id.length > 0);
        this.workspaces.set(workspaces);
        this.selectedWorkspaceId.set(workspaces[0]?.id ?? '');
        this.status.set(workspaces.length > 0 ? 'ready' : 'empty');
        this.loadInvites();
      },
      error: (error: { status?: number }) => {
        this.status.set(error.status === 401 || error.status === 403 ? 'permissionDenied' : 'error');
        this.message.set(
          error.status === 401 || error.status === 403
            ? 'Admin access is required.'
            : 'Workspace API request failed.'
        );
      }
    });
  }

  createInvite(): void {
    if (!this.canSubmit()) {
      return;
    }

    this.submitting.set(true);
    this.createdNotice.set(null);
    this.createdInvite.set(null);
    const body = {
      workspaceId: this.selectedWorkspaceId(),
      email: this.email().trim(),
      role: this.role(),
      expiresAt: this.toExpiresAtIso()
    };

    this.http
      .post<InviteDto>('/api/admin/invites', body)
      .pipe(finalize(() => this.submitting.set(false)))
      .subscribe({
        next: (invite) => {
          const created = toCreatedInviteDetails(invite, body.email);
          this.email.set('');
          this.createdInvite.set(created);
          this.createdNotice.set(`Invite created for ${created.email}. Copy or send the URL below.`);
          this.createDialogOpen.set(false);
          this.loadInvites();
        },
        error: (error: unknown) => {
          this.message.set(this.formatInviteError(error));
          if (isHttpStatus(error, 401) || isHttpStatus(error, 403)) {
            this.status.set('permissionDenied');
          }
        }
      });
  }

  createBulkInvites(): void {
    const emails = this.bulkEmails().split(/[\n,;]/u).map((email) => email.trim()).filter(Boolean);
    if (!this.canSubmit() || emails.length === 0) { this.message.set('Enter at least one email address.'); return; }
    this.submitting.set(true);
    this.http.post<readonly InviteDto[]>('/api/admin/invites/bulk', { workspaceId: this.selectedWorkspaceId(), emails, role: this.role(), expiresAt: this.toExpiresAtIso() }, { withCredentials: true })
      .pipe(finalize(() => this.submitting.set(false))).subscribe({
        next: (invites) => { this.bulkEmails.set(''); this.createdNotice.set(`${invites.length} invites created. Each URL is available only in the authorized one-time response.`); this.createDialogOpen.set(false); this.loadInvites(); },
        error: (error: unknown) => this.message.set(this.formatInviteError(error)),
      });
  }

  requestRevoke(row: InviteRow): void { this.revokeTarget.set(row); this.revokeDialogOpen.set(true); }
  revokeInvite(): void {
    const target = this.revokeTarget();
    if (!target?.id || this.revoking()) { return; }
    this.revoking.set(true);
    this.http.post(`/api/admin/invites/${target.id}/revoke`, {}, { withCredentials: true }).pipe(finalize(() => this.revoking.set(false))).subscribe({
      next: () => { this.revokeDialogOpen.set(false); this.revokeTarget.set(null); this.createdNotice.set('Invite revoked.'); this.loadInvites(); },
      error: (error: unknown) => this.message.set(this.formatInviteError(error)),
    });
  }

  handleGridAction(event: { actionId: string; row: InviteRow }): void { if (event.actionId === 'revoke') { this.requestRevoke(event.row); } }

  copyInviteUrl(): void {
    const inviteUrl = this.createdInvite()?.inviteUrl;
    if (!inviteUrl || typeof navigator === 'undefined' || !navigator.clipboard) {
      this.message.set('Copy is not available in this browser. Select and copy the invite URL.');
      return;
    }

    void navigator.clipboard.writeText(inviteUrl).then(
      () => this.createdNotice.set('Invite URL copied. Send it manually to the invitee.'),
      () => this.message.set('Copy failed. Select and copy the invite URL.')
    );
  }

  private loadInvites(): void {
    this.http.get<PagedResponseDto<InviteDto>>('/api/admin/invites', { withCredentials: true }).subscribe({
      next: (response) => this.invites.set((response.items ?? []).map(toInviteRow)),
      error: (error: { status?: number }) => {
        if (error.status === 401 || error.status === 403) {
          this.status.set('permissionDenied');
          this.message.set('Admin access is required.');
        }
      }
    });
  }

  private toExpiresAtIso(): string | null {
    const value = this.expiresAt();
    return value.length > 0 ? new Date(value).toISOString() : null;
  }

  private formatInviteError(error: unknown): string {
    if (isFrontendApiError(error)) {
      const details = error.details
        .map((detail) => (detail.target ? `${detail.target}: ${detail.message}` : detail.message))
        .join('\n');
      return details || error.message || 'Invite failed.';
    }

    const httpError = error as {
      error?: {
        detail?: unknown;
        title?: unknown;
        errors?: Record<string, unknown>;
        error?: unknown;
      };
      status?: number;
      message?: string;
    };

    if (httpError.error?.detail) {
      return String(httpError.error.detail);
    }

    if (httpError.error?.title) {
      return String(httpError.error.title);
    }

    if (httpError.error?.errors) {
      return Object.entries(httpError.error.errors)
        .map(([field, messages]) => `${field}: ${normalizeErrorMessages(messages).join(', ')}`)
        .join('\n');
    }

    if (httpError.error?.error) {
      return String(httpError.error.error);
    }

    return httpError.message ?? 'Invite failed.';
  }
}

function toWorkspaceOption(workspace: WorkspaceDto): WorkspaceOption {
  const id = stringValue(workspace.id);
  return {
    id,
    name: stringValue(workspace.name) || id
  };
}

function toInviteRow(invite: InviteDto): InviteRow {
  const acceptedAt = stringValue(invite.acceptedAt);
  const revokedAt = stringValue(invite.revokedAt);
  return {
    id: stringValue(invite.id),
    workspaceId: stringValue(invite.workspaceId),
    email: stringValue(invite.email),
    role: workspaceRoleLabel(invite.role),
    status: revokedAt ? 'Revoked' : acceptedAt ? 'Accepted' : 'Pending',
    expiresAt: formatDate(invite.expiresAt)
  };
}

function toCreatedInviteDetails(invite: InviteDto, fallbackEmail: string): CreatedInviteDetails {
  return {
    email: stringValue(invite.email) || fallbackEmail,
    role: workspaceRoleLabel(invite.role),
    inviteUrl: stringValue(invite.inviteUrl),
    expiresAt: formatDate(invite.expiresAt)
  };
}

function stringValue(value: unknown): string {
  return typeof value === 'string' ? value : '';
}

function formatDate(value: unknown): string {
  const raw = stringValue(value);
  return raw ? new Date(raw).toLocaleString() : '';
}

function workspaceRoleLabel(value: unknown): string {
  if (typeof value === 'number') {
    return Object.entries(WORKSPACE_ROLE).find(([, roleValue]) => roleValue === value)?.[0] ?? value.toString();
  }

  return stringValue(value);
}

function isFrontendApiError(error: unknown): error is FrontendApiError {
  const candidate = error as Partial<FrontendApiError>;
  return (
    !!candidate &&
    typeof candidate === 'object' &&
    typeof candidate.message === 'string' &&
    Array.isArray(candidate.details) &&
    typeof candidate.httpStatus === 'number'
  );
}

function isHttpStatus(error: unknown, status: number): boolean {
  const candidate = error as { status?: number; httpStatus?: number };
  return candidate?.status === status || candidate?.httpStatus === status;
}

function normalizeErrorMessages(messages: unknown): readonly string[] {
  if (Array.isArray(messages)) {
    return messages.map((message) => String(message));
  }

  return [String(messages)];
}
