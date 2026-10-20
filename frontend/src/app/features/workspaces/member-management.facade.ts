import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Subscription } from 'rxjs';
import { normalizeApiError } from '../../core/api/api-error.adapter';
import { ProtectedStateClearReason, RealtimeFacade } from '../../core/realtime/realtime.facade';
import { COGLATAS_WORKSPACE_MEMBERS_MOCK, WorkspaceMembersFacade } from './members.facade';
import {
  isMemberSearchQuery, isWorkspaceMemberRole, MemberManagementStatus,
  WorkspaceMemberCandidate, WorkspaceMemberEditor, WorkspaceMemberMutationOptions, WorkspaceMemberRoleValue,
} from './member-management.types';

const OWNER = 'workspace-member-management';
const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/iu;

@Injectable({ providedIn: 'root' })
export class WorkspaceMemberManagementFacade {
  private readonly http = inject(HttpClient);
  private readonly realtime = inject(RealtimeFacade);
  private readonly directory = inject(WorkspaceMembersFacade);
  private readonly scenario = inject(COGLATAS_WORKSPACE_MEMBERS_MOCK, { optional: true });
  private readonly accessState = signal<{ workspaceId: string | null; status: MemberManagementStatus; canAddMember: boolean }>({ workspaceId: null, status: 'idle', canAddMember: false });
  private readonly editorState = signal<WorkspaceMemberEditor | null>(null);
  readonly access = this.accessState.asReadonly();
  readonly editor = this.editorState.asReadonly();
  private readonly savedState = signal(false);
  readonly saved = this.savedState.asReadonly();
  private workspaceId: string | null = null;
  private generation = 0;
  private editorGeneration = 0;
  private searchGeneration = 0;
  private readonly requests = new Map<string, Subscription>();
  private cleanups: (() => void)[] = [];

  constructor() {
    if (!this.scenario) this.realtime.registerProtectedStateClearer(OWNER, (reason) => this.clear(reason));
  }

  setWorkspace(workspaceId: string | null): void {
    if (this.scenario || workspaceId === this.workspaceId) return;
    this.clear('workspace');
    if (!workspaceId || !UUID.test(workspaceId)) return;
    this.workspaceId = workspaceId;
    this.cleanups = [
      this.realtime.registerSubscription(OWNER, { subscriptionType: 'workspace', resourceId: workspaceId }),
      this.realtime.registerCatchUp(OWNER, () => {
        if (this.workspaceId === workspaceId) { this.closeEditor(); this.loadAccess(); }
      }),
    ];
    this.loadAccess();
  }

  recheck(): void { this.closeEditor(); this.loadAccess(); }

  open(mode: WorkspaceMemberEditor['mode'], identity: { userId: string; displayName: string } | null = null): void {
    const access = this.access();
    if (!this.workspaceId || access.workspaceId !== this.workspaceId || access.status !== 'ready' || !access.canAddMember) return;
    if (mode !== 'add' && (!identity || !UUID.test(identity.userId) || !identity.displayName)) return;
    this.closeEditor();
    this.savedState.set(false);
    const workspaceId = this.workspaceId;
    const generation = this.generation;
    const editorGeneration = this.editorGeneration;
    this.editorState.set({ workspaceId, mode, status: 'loading', identity: mode === 'add' ? null : identity,
      allowedRoles: [], role: null, identityConfirmed: false, query: '', searchStatus: 'idle', candidates: [], hasMore: false, nextAfterUserId: null });
    const params = identity && mode !== 'add' ? new HttpParams().set('userId', identity.userId) : undefined;
    this.replace('editor', this.http.get<unknown>(this.base(workspaceId) + '/management/options', { params, withCredentials: true }).subscribe({
      next: (body) => {
        if (!this.isCurrentEditor(generation, editorGeneration)) return;
        const options = parseOptions(body, workspaceId, mode === 'add' ? null : identity!.userId);
        if (!options || (mode === 'add' ? !options.canAddMember : mode === 'change' ? !options.target?.canChangeRole : !options.target?.canRemove)) {
          this.editorState.set({ ...this.editor()!, status: options ? 'denied' : 'error', identity: null });
          return;
        }
        this.editorState.set({ ...this.editor()!, status: 'ready', allowedRoles: options.target?.allowedRoles ?? [] });
      },
      error: (error: unknown) => {
        if (this.isCurrentEditor(generation, editorGeneration)) this.failEditor(error);
      },
    }));
  }

  closeEditor(): void {
    this.editorGeneration++;
    this.searchGeneration++;
    for (const key of ['editor', 'search', 'mutation']) this.cancel(key);
    this.editorState.set(null);
  }

  updateQuery(query: string): void {
    const editor = this.editor();
    if (!editor || editor.mode !== 'add' || editor.status !== 'ready') return;
    this.searchGeneration++;
    this.cancel('search');
    this.editorState.set({ ...editor, query, identity: null, role: null, allowedRoles: [], identityConfirmed: false,
      candidates: [], searchStatus: 'idle', hasMore: false, nextAfterUserId: null });
  }

  search(more = false): void {
    const editor = this.editor();
    if (!editor || editor.mode !== 'add' || editor.status !== 'ready' || !isMemberSearchQuery(editor.query) || editor.searchStatus === 'loading') return;
    if (more && (!editor.hasMore || !editor.nextAfterUserId)) return;
    const generation = this.generation, editorGeneration = this.editorGeneration, searchGeneration = ++this.searchGeneration;
    const query = editor.query.trim();
    let params = new HttpParams().set('q', query).set('take', 20);
    if (more) params = params.set('afterUserId', editor.nextAfterUserId!);
    this.editorState.set({ ...editor, searchStatus: 'loading', identity: null, role: null, allowedRoles: [], identityConfirmed: false,
      candidates: more ? editor.candidates : [] });
    this.replace('search', this.http.get<unknown>(this.base(editor.workspaceId) + '/management/candidates', { params, withCredentials: true }).subscribe({
      next: (body) => {
        if (!this.isCurrentEditor(generation, editorGeneration) || searchGeneration !== this.searchGeneration) return;
        const page = parseCandidates(body, editor.workspaceId);
        const previous = more ? editor.candidates : [];
        if (!page || page.items.some((item) => previous.some((old) => old.userId === item.userId))) {
          this.editorState.set({ ...this.editor()!, candidates: [], searchStatus: 'error', hasMore: false, nextAfterUserId: null });
          return;
        }
        this.editorState.set({ ...this.editor()!, searchStatus: 'ready', candidates: [...previous, ...page.items], hasMore: page.hasMore, nextAfterUserId: page.nextAfterUserId });
      },
      error: (error: unknown) => {
        if (!this.isCurrentEditor(generation, editorGeneration) || searchGeneration !== this.searchGeneration) return;
        if (isDenied(error)) this.failEditor(error);
        else this.editorState.set({ ...this.editor()!, searchStatus: 'error', candidates: [], hasMore: false, nextAfterUserId: null });
      },
    }));
  }

  select(userId: string): void {
    const editor = this.editor();
    const candidate = editor?.candidates.find((item) => item.userId === userId);
    if (!editor || editor.status !== 'ready' || editor.searchStatus !== 'ready' || !candidate) return;
    this.editorState.set({ ...editor, identity: { userId: candidate.userId, displayName: candidate.displayName }, allowedRoles: candidate.allowedRoles, role: null, identityConfirmed: false });
  }

  selectRole(value: unknown): void {
    const editor = this.editor();
    if (editor?.status !== 'ready') return;
    this.editorState.set({ ...editor, role: isWorkspaceMemberRole(value) && editor.allowedRoles.includes(value) ? value : null, identityConfirmed: false });
  }

  confirmIdentity(confirmed: boolean): void {
    const editor = this.editor();
    if (editor?.status === 'ready') this.editorState.set({ ...editor, identityConfirmed: confirmed });
  }

  canSubmit(): boolean {
    const editor = this.editor();
    return !!editor && editor.workspaceId === this.workspaceId && editor.status === 'ready' && !!editor.identity && editor.identityConfirmed &&
      (editor.mode === 'remove' || (editor.role !== null && editor.allowedRoles.includes(editor.role)));
  }

  submit(): void {
    if (!this.canSubmit()) return;
    const editor = this.editor()!;
    const generation = this.generation, editorGeneration = this.editorGeneration;
    const url = this.base(editor.workspaceId);
    const write = editor.mode === 'add'
      ? this.http.post<unknown>(url, { userId: editor.identity!.userId, role: editor.role }, { withCredentials: true })
      : editor.mode === 'change'
        ? this.http.patch<unknown>(url + '/' + encodeURIComponent(editor.identity!.userId), { role: editor.role }, { withCredentials: true })
        : this.http.delete<unknown>(url + '/' + encodeURIComponent(editor.identity!.userId), { withCredentials: true });
    this.editorState.set({ ...editor, status: 'submitting', candidates: [] });
    this.replace('mutation', write.subscribe({
      next: () => {
        if (!this.isCurrentEditor(generation, editorGeneration)) return;
        this.closeEditor();
        this.savedState.set(true);
        this.directory.reload(editor.workspaceId);
        this.loadAccess();
      },
      error: (error: unknown) => {
        if (!this.isCurrentEditor(generation, editorGeneration)) return;
        const status = normalizeApiError(error).httpStatus;
        this.editorState.set({ ...editor, status: isDenied(error) ? 'denied' : status === 0 || status >= 500 ? 'uncertain' : 'error',
          identity: null, candidates: [], allowedRoles: [], role: null, identityConfirmed: false });
        this.accessState.set({ workspaceId: editor.workspaceId, status: 'idle', canAddMember: false });
        // A failed/uncertain command is never automatically repeated. Recheck is GET-only.
        this.directory.reload(editor.workspaceId);
      },
    }));
  }

  private loadAccess(): void {
    const workspaceId = this.workspaceId;
    if (!workspaceId || this.scenario) return;
    const generation = this.generation;
    this.cancel('access');
    this.accessState.set({ workspaceId, status: 'loading', canAddMember: false });
    this.replace('access', this.http.get<unknown>(this.base(workspaceId) + '/management/options', { withCredentials: true }).subscribe({
      next: (body) => {
        if (generation !== this.generation || workspaceId !== this.workspaceId) return;
        const options = parseOptions(body, workspaceId, null);
        this.accessState.set({ workspaceId, status: options ? 'ready' : 'error', canAddMember: options?.canAddMember ?? false });
      },
      error: (error: unknown) => {
        if (generation === this.generation && workspaceId === this.workspaceId) this.accessState.set({ workspaceId, status: isDenied(error) ? 'denied' : 'error', canAddMember: false });
      },
    }));
  }

  private failEditor(error: unknown): void {
    const editor = this.editor();
    if (!editor) return;
    this.editorState.set({ ...editor, status: isDenied(error) ? 'denied' : 'error', identity: null, candidates: [], allowedRoles: [], role: null, identityConfirmed: false });
    this.accessState.set({ workspaceId: editor.workspaceId, status: 'idle', canAddMember: false });
  }

  private clear(reason: ProtectedStateClearReason): void {
    this.generation++;
    this.closeEditor();
    for (const key of this.requests.keys()) this.cancel(key);
    this.accessState.set({ workspaceId: null, status: 'idle', canAddMember: false });
    this.savedState.set(false);
    if (reason !== 'authorization') {
      for (const cleanup of this.cleanups.splice(0)) cleanup();
      this.workspaceId = null;
    }
  }

  private isCurrentEditor(generation: number, editorGeneration: number): boolean {
    return generation === this.generation && editorGeneration === this.editorGeneration && this.editor()?.workspaceId === this.workspaceId;
  }
  private base(workspaceId: string): string { return `/api/workspaces/${encodeURIComponent(workspaceId)}/members`; }
  private cancel(key: string): void { this.requests.get(key)?.unsubscribe(); this.requests.delete(key); }
  private replace(key: string, request: Subscription): void { this.requests.set(key, request); }
}

function isDenied(error: unknown): boolean { return [401, 403, 404].includes(normalizeApiError(error).httpStatus); }
function record(value: unknown): value is Record<string, unknown> { return !!value && typeof value === 'object' && !Array.isArray(value); }
function roles(value: unknown): value is readonly WorkspaceMemberRoleValue[] { return Array.isArray(value) && value.every(isWorkspaceMemberRole) && new Set(value).size === value.length; }

function parseOptions(body: unknown, workspaceId: string, userId: string | null): WorkspaceMemberMutationOptions | null {
  if (!record(body) || body['workspaceId'] !== workspaceId || typeof body['canAddMember'] !== 'boolean') return null;
  const target = body['target'];
  if (userId === null) return target === null ? { workspaceId, canAddMember: body['canAddMember'], target: null } : null;
  if (!record(target) || target['userId'] !== userId || !isWorkspaceMemberRole(target['currentRole']) || ![0, 1, 2].includes(target['membershipStatus'] as number) ||
    typeof target['canChangeRole'] !== 'boolean' || typeof target['canRemove'] !== 'boolean' || !roles(target['allowedRoles'])) return null;
  return { workspaceId, canAddMember: body['canAddMember'], target: { userId, currentRole: target['currentRole'], membershipStatus: target['membershipStatus'] as 0 | 1 | 2,
    canChangeRole: target['canChangeRole'], canRemove: target['canRemove'], allowedRoles: [...target['allowedRoles']] } };
}

function parseCandidates(body: unknown, workspaceId: string): { items: readonly WorkspaceMemberCandidate[]; hasMore: boolean; nextAfterUserId: string | null } | null {
  if (!record(body) || body['workspaceId'] !== workspaceId || !Array.isArray(body['items']) || body['items'].length > 20 || typeof body['hasMore'] !== 'boolean') return null;
  const items: WorkspaceMemberCandidate[] = [];
  for (const row of body['items']) {
    if (!record(row) || typeof row['userId'] !== 'string' || !UUID.test(row['userId']) || typeof row['displayName'] !== 'string' || !row['displayName'].trim() || !roles(row['allowedRoles'])) return null;
    items.push({ userId: row['userId'], displayName: row['displayName'], allowedRoles: [...row['allowedRoles']] });
  }
  if (new Set(items.map((item) => item.userId)).size !== items.length) return null;
  const cursor = body['nextAfterUserId'];
  if (body['hasMore'] ? !items.length || cursor !== items.at(-1)?.userId : cursor !== null) return null;
  return { items, hasMore: body['hasMore'], nextAfterUserId: cursor as string | null };
}
