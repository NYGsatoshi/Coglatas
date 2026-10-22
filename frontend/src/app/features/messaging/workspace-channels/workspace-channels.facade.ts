import { computed, DestroyRef, effect, inject, Injectable, signal, untracked } from '@angular/core';
import { finalize, Subscription } from 'rxjs';
import { AuthSessionFacade } from '../../../core/auth/auth-session.facade';
import { RealtimeFacade } from '../../../core/realtime/realtime.facade';
import { ActiveWorkspaceFacade } from '../../../core/workspace/active-workspace.facade';
import {
  channelHttpStatus, channelScopeId, validChannelName, WorkspaceChannelDirectory,
  WorkspaceChannelItem, WorkspaceChannelsApi, WorkspaceChannelVisibility
} from './workspace-channels.api';

type DirectoryStatus = 'idle' | 'loading' | 'ready' | 'unavailable' | 'error';
type ActionNotice = 'uncertain' | 'denied' | 'invalid' | 'committed' | null;
let nextOwner = 0;

@Injectable()
export class WorkspaceChannelsFacade {
  private readonly api = inject(WorkspaceChannelsApi);
  private readonly auth = inject(AuthSessionFacade);
  private readonly workspace = inject(ActiveWorkspaceFacade);
  private readonly realtime = inject(RealtimeFacade);
  private readonly owner = `workspace-channel-directory-${++nextOwner}`;
  private boundScope: string | null = null;
  private boundary = 0;
  private listGeneration = 0;
  private listRequest?: Subscription;
  private commandRequest?: Subscription;
  private createIdentity: { readonly fingerprint: string; readonly key: string } | null = null;
  private destroyed = false;

  readonly scope = computed(() => {
    const tenant = this.auth.currentTenant();
    const userId = channelScopeId(this.auth.currentUser()?.userId);
    const workspace = this.workspace.activeWorkspace();
    const workspaceId = channelScopeId(workspace?.id);
    const tenantId = channelScopeId(tenant?.tenantId);
    return this.auth.isAuthenticated() && tenant?.isAvailable && !tenant.isPlatformScope && tenantId && userId && workspaceId
      ? { key: `${tenantId}:${userId}:${workspaceId}`, workspaceId, label: workspace!.label } : null;
  });
  readonly directory = signal<WorkspaceChannelDirectory | null>(null);
  readonly status = signal<DirectoryStatus>('idle');
  readonly notice = signal<ActionNotice>(null);
  readonly pending = signal<string | null>(null);
  readonly search = signal('');
  readonly name = signal('');
  readonly visibility = signal<WorkspaceChannelVisibility>('PublicWithinScope');
  readonly dialogOpen = signal(false);
  readonly confirmedId = signal<string | null>(null);
  readonly totalPages = computed(() => Math.max(1, Math.ceil((this.directory()?.totalCount ?? 0) / 50)));
  readonly canCreate = computed(() => this.status() === 'ready' && this.directory()?.canCreate === true &&
    !!this.scope() && this.scope()!.key === this.boundScope && !this.pending());
  readonly canSubmitCreate = computed(() => this.dialogOpen() && this.canCreate() && validChannelName(this.name()) &&
    ['PublicWithinScope', 'Private'].includes(this.visibility()));

  constructor() {
    const clear = this.realtime.registerProtectedStateClearer(this.owner, () => {
      this.reset();
      this.status.set('unavailable');
    });
    const catchUp = this.realtime.registerCatchUp(this.owner, () => this.refresh());
    effect(() => {
      const scope = this.scope();
      untracked(() => {
        if (scope?.key === this.boundScope || (!scope && !this.boundScope)) return;
        this.reset();
        this.boundScope = scope?.key ?? null;
        if (scope) void this.refresh();
      });
    });
    inject(DestroyRef).onDestroy(() => {
      this.destroyed = true;
      clear(); catchUp(); this.reset();
    });
  }

  refresh(page = 1): Promise<void> {
    const scope = this.scope();
    if (this.destroyed || !scope || scope.key !== this.boundScope || this.pending()) return Promise.resolve();
    this.listRequest?.unsubscribe();
    const generation = ++this.listGeneration;
    const boundary = this.boundary;
    this.directory.set(null);
    this.status.set('loading');
    const search = this.search().trim();
    if (search.length > 200 || !Number.isSafeInteger(page) || page < 1) {
      this.status.set('error');
      return Promise.resolve();
    }
    return new Promise<void>((resolve) => {
      this.listRequest = this.api.list(scope.workspaceId, page, search).pipe(finalize(resolve)).subscribe({
        next: (directory) => {
          if (!this.current(scope.key, boundary) || generation !== this.listGeneration) return;
          this.directory.set(directory);
          this.status.set('ready');
          if (!directory.canCreate) { this.clearCreate(); this.dialogOpen.set(false); }
        },
        error: (error: unknown) => {
          if (!this.current(scope.key, boundary) || generation !== this.listGeneration) return;
          if (this.isDenial(error)) {
            this.reset(); this.status.set('unavailable'); this.notice.set('denied');
          } else {
            this.directory.set(null); this.status.set('error');
          }
        }
      });
    });
  }

  openCreate(): void {
    if (!this.canCreate()) return;
    this.notice.set(null);
    this.dialogOpen.set(true);
  }

  closeCreate(): void {
    if (!this.pending()) this.dialogOpen.set(false);
  }

  create(): void {
    const scope = this.scope();
    if (!scope || !this.canSubmitCreate()) return;
    const name = this.name().trim();
    const visibility = this.visibility();
    const fingerprint = JSON.stringify([scope.key, name, visibility]);
    try {
      if (this.createIdentity?.fingerprint !== fingerprint) {
        this.createIdentity = { fingerprint, key: crypto.randomUUID() };
      }
    } catch {
      this.notice.set('invalid');
      return;
    }
    const boundary = this.boundary;
    this.notice.set(null);
    this.pending.set('create');
    this.commandRequest = this.api.create(scope.workspaceId, name, visibility, this.createIdentity.key).subscribe({
      next: (item) => {
        if (!this.current(scope.key, boundary)) return;
        if (!item.isMember) { this.pending.set(null); this.notice.set('uncertain'); return; }
        this.confirmedId.set(item.id);
        this.pending.set(null);
        this.dialogOpen.set(false);
        this.clearCreate();
        this.notice.set('committed');
        this.search.set('');
        void this.refresh();
      },
      error: (error: unknown) => this.commandFailed(error, scope.key, boundary)
    });
  }

  join(id: string): void {
    const scope = this.scope();
    const item = this.directory()?.items.find((candidate) => candidate.id === id);
    if (!scope || scope.key !== this.boundScope || this.status() !== 'ready' || this.pending() || !item?.canJoin) return;
    const boundary = this.boundary;
    this.pending.set(id);
    this.notice.set(null);
    this.commandRequest = this.api.join(scope.workspaceId, item.id).subscribe({
      next: (joined) => {
        if (!this.current(scope.key, boundary)) return;
        this.pending.set(null);
        this.confirmedId.set(joined.id);
        this.notice.set('committed');
        void this.refresh();
      },
      error: (error: unknown) => this.commandFailed(error, scope.key, boundary)
    });
  }

  route(item: Pick<WorkspaceChannelItem, 'id' | 'workspaceId'>): readonly string[] {
    return ['/workspaces', item.workspaceId, 'channels', item.id];
  }

  private commandFailed(error: unknown, scope: string, boundary: number): void {
    if (!this.current(scope, boundary)) return;
    if (this.isDenial(error)) {
      this.reset(); this.status.set('unavailable'); this.notice.set('denied');
    } else {
      this.pending.set(null);
      this.notice.set('uncertain');
    }
  }

  private isDenial(error: unknown): boolean {
    return [400, 401, 403, 404].includes(channelHttpStatus(error));
  }

  private current(scope: string, boundary: number): boolean {
    return !this.destroyed && this.boundary === boundary && this.boundScope === scope && this.scope()?.key === scope;
  }

  private clearCreate(): void {
    this.name.set('');
    this.visibility.set('PublicWithinScope');
    this.createIdentity = null;
  }

  private reset(): void {
    this.boundary++;
    this.listGeneration++;
    this.listRequest?.unsubscribe();
    this.commandRequest?.unsubscribe();
    this.directory.set(null);
    this.pending.set(null);
    this.confirmedId.set(null);
    this.notice.set(null);
    this.search.set('');
    this.dialogOpen.set(false);
    this.clearCreate();
    this.status.set('idle');
  }
}
