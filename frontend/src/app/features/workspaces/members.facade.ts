import { HttpClient } from '@angular/common/http';
import { inject, Injectable, InjectionToken, signal } from '@angular/core';
import { Subscription } from 'rxjs';
import { normalizeApiError } from '../../core/api/api-error.adapter';

import {
  ProtectedStateClearReason,
  RealtimeFacade,
} from '../../core/realtime/realtime.facade';

import {
  WORKSPACE_MEMBERS_DEFAULT_PAGE_SIZE,
  WORKSPACE_MEMBERS_MAXIMUM_PAGE_SIZE,
  WorkspaceMemberActionId,
  WorkspaceMemberAccountStatusLabel,
  WorkspaceMemberGridRow,
  WorkspaceMemberDetail,
  WorkspaceMemberMockRecord,
  WorkspaceMemberRowAction,
  WorkspaceMembersScenario,
  WorkspaceMembersViewModel,
} from './members.types';

export const COGLATAS_WORKSPACE_MEMBERS_MOCK = new InjectionToken<WorkspaceMembersScenario>(
  'COGLATAS_WORKSPACE_MEMBERS_MOCK',
);

interface WorkspaceMemberDto {
  readonly userId?: unknown;
  readonly displayName?: unknown;
  readonly role?: unknown;
  readonly status?: unknown;
  readonly joinedAt?: unknown;
}

const WORKSPACE_MEMBERS_REALTIME_OWNER = 'workspace-members';

@Injectable({
  providedIn: 'root',
})
export class WorkspaceMembersFacade {
  private readonly http = inject(HttpClient);
  private readonly realtime = inject(RealtimeFacade);
  private readonly scenario = inject(COGLATAS_WORKSPACE_MEMBERS_MOCK, { optional: true });
  private readonly livePages = signal<Record<string, WorkspaceMembersViewModel>>({});
  private readonly detailState = signal<WorkspaceMemberDetail | null>(null);
  readonly detail = this.detailState.asReadonly();
  private detailRequest: Subscription | null = null;
  private detailGeneration = 0;
  private request: Subscription | null = null;
  private requestGeneration = 0;
  private currentWorkspaceId: string | null = null;
  private realtimeCleanups: (() => void)[] = [];

  constructor() {
    if (!this.scenario) {
      this.realtime.registerProtectedStateClearer?.(
        WORKSPACE_MEMBERS_REALTIME_OWNER,
        (reason) => this.clearProtectedState(reason),
      );
    }
  }

  getPage(workspaceId: string): WorkspaceMembersViewModel {
    if (!this.scenario) {
      return this.livePages()[workspaceId] ?? this.emptyPage(workspaceId, 'loading');
    }

    const authorizedRows = this.scenario.members
      .filter((member) => member.workspaceId === workspaceId)
      .map((member) => this.toGridRow(member));

    return {
      status: this.scenario.status,
      workspaceId,
      title: this.scenario.title,
      subtitle: this.scenario.subtitle,
      rows: authorizedRows,
      columns: [],
      pageSize: {
        defaultPageSize: WORKSPACE_MEMBERS_DEFAULT_PAGE_SIZE,
        maximumPageSize: WORKSPACE_MEMBERS_MAXIMUM_PAGE_SIZE,
      },
      queryOwnership: {
        loadedRowsOwner: 'backendAuthorization',
        clientSearchOwner: 'alreadyLoadedAuthorizedRowsOnly',
        futureSortOwner: 'backendWhenLive',
        futureFilterOwner: 'backendWhenLive',
      },
      message: this.scenario.message,
    };
  }

  ensureLoaded(workspaceId: string): void {
    if (!this.scenario) {
      this.setRouteWorkspace(workspaceId);
      this.loadMembers(workspaceId);
    }
  }

  reload(workspaceId: string): void {
    if (!this.scenario) {
      this.setRouteWorkspace(workspaceId);
    }
    this.loadMembers(workspaceId, true);
  }

  openMemberDetail(workspaceId: string, userId: string): void {
    this.closeMemberDetail();
    const page = this.getPage(workspaceId);
    const row = page.rows.find((candidate) => candidate.id === userId);
    if (
      !userId || page.status !== 'ready' || !row ||
      !row.rowActions.some((action) => action.id === 'openMemberDetail' && !action.disabled) ||
      (!this.scenario && this.currentWorkspaceId !== workspaceId)
    ) {
      return;
    }

    if (this.scenario) {
      this.detailState.set({ workspaceId, userId, status: 'ready', member: { displayName: row.displayName, role: row.role } });
      return;
    }

    const generation = this.detailGeneration;
    this.detailState.set({ workspaceId, userId, status: 'loading' });
    this.detailRequest = this.http.get<WorkspaceMemberDto>(
      `/api/workspaces/${encodeURIComponent(workspaceId)}/members/${encodeURIComponent(userId)}`,
      { withCredentials: true },
    ).subscribe({
      next: (member) => {
        if (!this.isCurrentDetail(generation, workspaceId, userId)) return;
        const displayName = stringValue(member?.displayName);
        if (member?.userId !== userId || !displayName) {
          this.detailState.set({ workspaceId, userId, status: 'error' });
          return;
        }
        // Copy only display identity and role from the ordinary response.
        // Management-only fields and example capability decisions are not copied.
        this.detailState.set({ workspaceId, userId, status: 'ready', member: { displayName, role: memberRole(member.role) } });
      },
      error: (error: unknown) => {
        if (!this.isCurrentDetail(generation, workspaceId, userId)) return;
        this.detailState.set({ workspaceId, userId, status: [401, 403, 404].includes(normalizeApiError(error).httpStatus) ? 'permissionDenied' : 'error' });
      },
    });
  }

  closeMemberDetail(): void {
    this.detailGeneration++;
    this.detailRequest?.unsubscribe();
    this.detailRequest = null;
    this.detailState.set(null);
  }

  private isCurrentDetail(generation: number, workspaceId: string, userId: string): boolean {
    return generation === this.detailGeneration && this.currentWorkspaceId === workspaceId &&
      this.detailState()?.userId === userId && this.getPage(workspaceId).status === 'ready';
  }

  private loadMembers(workspaceId: string, force = false): Promise<void> {
    if (!force && this.livePages()[workspaceId]?.status === 'loading') {
      return Promise.resolve();
    }

    this.closeMemberDetail();
    const generation = ++this.requestGeneration;
    this.request?.unsubscribe();
    this.setLivePage(workspaceId, this.emptyPage(workspaceId, 'loading'));
    return new Promise<void>((resolve) => {
      let settled = false;
      const settle = (): void => {
        if (!settled) {
          settled = true;
          resolve();
        }
      };
      const request = this.http
        .get<
          readonly WorkspaceMemberDto[]
        >(`/api/workspaces/${workspaceId}/members`, { withCredentials: true })
        .subscribe({
          next: (members) => {
            if (!this.isCurrentRequest(generation, workspaceId)) {
              return;
            }
            const rows = members.map((member) =>
              this.toGridRow(this.toMockRecord(member, workspaceId)),
            );
            this.setLivePage(workspaceId, {
              ...this.emptyPage(workspaceId, rows.length === 0 ? 'empty' : 'ready'),
              rows,
              message: rows.length === 0 ? 'No members were returned by the API.' : undefined,
            });
          },
          error: (error: unknown) => {
            if (this.isCurrentRequest(generation, workspaceId)) {
              const denied = [401, 403, 404].includes(normalizeApiError(error).httpStatus);
              this.setLivePage(workspaceId, {
                ...this.emptyPage(
                  workspaceId,
                  denied ? 'permissionDenied' : 'error',
                ),
                message:
                  denied
                    ? 'Authentication or member permission is required.'
                    : 'Workspace member API request failed.',
              });
            }
            settle();
          },
          complete: settle,
        });
      this.request = request;
      request.add(() => {
        if (this.request === request) {
          this.request = null;
        }
        settle();
      });
    });
  }

  private setRouteWorkspace(workspaceId: string): void {
    if (this.currentWorkspaceId === workspaceId && this.realtimeCleanups.length > 0) {
      return;
    }

    this.closeMemberDetail();
    this.releaseRealtime();
    this.currentWorkspaceId = workspaceId;
    this.realtimeCleanups = [
      this.realtime.registerSubscription(WORKSPACE_MEMBERS_REALTIME_OWNER, {
        subscriptionType: 'workspace',
        resourceId: workspaceId,
      }),
      this.realtime.registerCatchUp(WORKSPACE_MEMBERS_REALTIME_OWNER, () => {
        if (this.currentWorkspaceId === workspaceId) {
          return this.loadMembers(workspaceId, true);
        }
        return undefined;
      }),
    ];
  }

  private clearProtectedState(reason: ProtectedStateClearReason): void {
    this.closeMemberDetail();
    this.requestGeneration++;
    this.request?.unsubscribe();
    this.request = null;
    this.livePages.set({});
    if (reason !== 'authorization') {
      this.releaseRealtime();
      this.currentWorkspaceId = null;
    }
  }

  private releaseRealtime(): void {
    for (const cleanup of this.realtimeCleanups.splice(0)) {
      cleanup();
    }
  }

  private isCurrentRequest(generation: number, workspaceId: string): boolean {
    return generation === this.requestGeneration && this.currentWorkspaceId === workspaceId;
  }

  private setLivePage(workspaceId: string, page: WorkspaceMembersViewModel): void {
    this.livePages.update((pages) => ({ ...pages, [workspaceId]: page }));
  }

  private emptyPage(
    workspaceId: string,
    status: WorkspaceMembersViewModel['status'],
  ): WorkspaceMembersViewModel {
    return {
      status,
      workspaceId,
      title: 'Workspace members',
      subtitle: 'Live API data',
      rows: [],
      columns: [],
      pageSize: {
        defaultPageSize: WORKSPACE_MEMBERS_DEFAULT_PAGE_SIZE,
        maximumPageSize: WORKSPACE_MEMBERS_MAXIMUM_PAGE_SIZE,
      },
      queryOwnership: {
        loadedRowsOwner: 'backendAuthorization',
        clientSearchOwner: 'alreadyLoadedAuthorizedRowsOnly',
        futureSortOwner: 'backendWhenLive',
        futureFilterOwner: 'backendWhenLive',
      },
    };
  }

  private toGridRow(member: WorkspaceMemberMockRecord): WorkspaceMemberGridRow {
    return {
      id: member.id,
      workspaceId: member.workspaceId,
      displayName: member.displayName,
      role: member.role,
      roleLabel: member.role,
      groupProjectLabel: member.groupProjectLabel,
      accountStatus: member.accountStatus,
      accountStatusLabel: member.accountStatusLabel,
      joinedAtLabel: member.joinedAtLabel,
      joinedAtUtc: member.joinedAtUtc,
      rowActions: this.buildActions(member),
    };
  }

  private buildActions(member: WorkspaceMemberMockRecord): readonly WorkspaceMemberRowAction[] {
    const actions: WorkspaceMemberRowAction[] = [];

    if (member.capabilities.includes('openMemberDetail')) {
      actions.push({
        id: 'openMemberDetail',
        label: '詳細',
        destructive: false,
        disabled: !member.mockDetailSupported,
        disabledReason: member.mockDetailSupported ? undefined : '削除済み',
        disabledReasonKey: member.mockDetailSupported ? undefined : 'members.status.removed',
      });
    }

    if (member.capabilities.includes('changeRole') && member.accountStatus !== 'removed') {
      actions.push({
        id: 'changeRole',
        label: '権限変更',
        destructive: false,
        disabled: false,
      });
    }

    if (member.capabilities.includes('disableMember') && member.accountStatus !== 'removed') {
      actions.push({
        id: 'disableMember',
        label: member.accountStatus === 'disabled' ? '解除確認' : '利用停止',
        destructive: true,
        disabled: false,
      });
    }

    return actions.filter((action) => action.id === 'openMemberDetail') satisfies readonly WorkspaceMemberRowAction[];
  }

  private toMockRecord(member: WorkspaceMemberDto, workspaceId: string): WorkspaceMemberMockRecord {
    const accountStatus = memberStatus(member.status);
    const role = memberRole(member.role);

    return {
      id: stringValue(member.userId) ?? '',
      workspaceId,
      displayName: stringValue(member.displayName) ?? 'Member',
      role,
      roleLabel: role,
      groupProjectLabel: 'Workspace',
      accountStatus,
      accountStatusLabel: accountStatusLabel(accountStatus),
      joinedAtLabel: formatDate(member.joinedAt),
      joinedAtUtc: typeof member.joinedAt === 'string' && Number.isFinite(Date.parse(member.joinedAt)) ? member.joinedAt : null,
      capabilities: ['openMemberDetail'],
      mockDetailSupported: !!stringValue(member.userId),
    };
  }

  isDestructiveAction(actionId: string): actionId is WorkspaceMemberActionId {
    return actionId === 'disableMember';
  }
}

function stringValue(value: unknown): string | undefined {
  return typeof value === 'string' && value.length > 0 ? value : undefined;
}

function formatDate(value: unknown): string {
  const raw = stringValue(value);
  return raw ? new Date(raw).toLocaleDateString() : '';
}

function memberRole(value: unknown): WorkspaceMemberGridRow['role'] {
  const normalized = typeof value === 'string' ? value.toLowerCase() : value;
  switch (normalized) {
    case 0: case '0': case 'owner': return 'Owner';
    case 1: case '1': case 'admin': return 'Admin';
    case 2: case '2': case 'adviser': return 'Adviser';
    case 3: case '3': case 'member': return 'Member';
    case 4: case '4': case 'readonly': return 'ReadOnly';
    default: return 'Unknown';
  }
}

function memberStatus(value: unknown): WorkspaceMemberGridRow['accountStatus'] {
  const normalized = String(value ?? '').toLowerCase();
  if (normalized === '2' || normalized === 'suspended') {
    return 'disabled';
  }
  return 'active';
}

function accountStatusLabel(
  status: WorkspaceMemberGridRow['accountStatus'],
): WorkspaceMemberAccountStatusLabel {
  if (status === 'disabled') {
    return '蛻ｩ逕ｨ蛛懈ｭ｢' as WorkspaceMemberAccountStatusLabel;
  }
  if (status === 'removed') {
    return '蜑企勁貂医∩' as WorkspaceMemberAccountStatusLabel;
  }
  return '蜿ょ刈荳ｭ' as WorkspaceMemberAccountStatusLabel;
}
