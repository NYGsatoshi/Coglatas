import { computed, DestroyRef, effect, inject, Injectable } from '@angular/core';
import { ComponentStore } from '@ngrx/component-store';
import { defer, finalize, Subscription, take, throwIfEmpty } from 'rxjs';

import { AuthSessionFacade } from '../../core/auth/auth-session.facade';
import { RealtimeFacade } from '../../core/realtime/realtime.facade';
import { ActiveWorkspaceFacade } from '../../core/workspace/active-workspace.facade';
import { DEMO_LINK_TARGETS, LOCAL_DRAFT_REPOSITORY, REMOTE_DRAFT_ADAPTER } from './local-draft.adapters';
import { copyDraft, EMPTY_LOCAL_DRAFT, LocalDraftCheckpoint, LocalDraftContent, sameDraft } from './local-draft.models';

export type LocalDraftStatus = 'editing' | 'saving' | 'checkpoint' | 'error' | 'conflict' | 'unavailable' | 'denied';

export interface LocalDraftState {
  readonly scope: string | null;
  readonly content: LocalDraftContent;
  readonly checkpoint: LocalDraftCheckpoint;
  readonly status: LocalDraftStatus;
  readonly message: string;
  readonly linksPreviewEnabled: boolean;
  readonly linkQuery: string;
}

const initialState = (): LocalDraftState => ({
  scope: null, content: copyDraft(EMPTY_LOCAL_DRAFT),
  checkpoint: { version: 0, content: copyDraft(EMPTY_LOCAL_DRAFT) },
  status: 'editing', message: '', linksPreviewEnabled: false, linkQuery: ''
});

/** Root lifetime retains working edits across route navigation within one scope. */
@Injectable({ providedIn: 'root' })
export class LocalDraftStore extends ComponentStore<LocalDraftState> {
  private readonly auth = inject(AuthSessionFacade);
  private readonly workspace = inject(ActiveWorkspaceFacade);
  private readonly realtime = inject(RealtimeFacade);
  private readonly repository = inject(LOCAL_DRAFT_REPOSITORY);
  private readonly remote = inject(REMOTE_DRAFT_ADAPTER);
  private pending: Subscription | null = null;
  private generation = 0;
  private busy = false;
  private readonly stateView = this.selectSignal((state) => state);
  // Hide the previous scope synchronously, before the cleanup effect is flushed.
  readonly view = computed(() => {
    const state = this.stateView();
    return state.scope === null || state.scope === this.currentScope() ? state : initialState();
  });
  readonly changed = computed(() => !sameDraft(this.view().content, this.view().checkpoint.content));
  readonly hasContent = computed(() => {
    const content = this.view().content;
    return !!(content.title || content.body || content.links.length);
  });
  readonly targets = computed(() => {
    const state = this.view();
    if (!state.linksPreviewEnabled) { return []; }
    const query = state.linkQuery.normalize('NFKC').toLocaleLowerCase();
    return DEMO_LINK_TARGETS.filter((target) =>
      !state.content.links.some((link) => link.id === target.id) &&
      `${target.kind} ${target.label}`.normalize('NFKC').toLocaleLowerCase().includes(query));
  });

  constructor() {
    super(initialState());
    const destroy = inject(DestroyRef);
    const unregister = this.realtime.registerProtectedStateClearer('local-draft-workbench', () => this.clearProtectedState());
    const warnBeforeLeaving = (event: BeforeUnloadEvent): void => {
      if (this.hasContent()) { event.preventDefault(); event.returnValue = ''; }
    };
    globalThis.addEventListener?.('beforeunload', warnBeforeLeaving);
    destroy.onDestroy(() => {
      unregister();
      this.pending?.unsubscribe();
      globalThis.removeEventListener?.('beforeunload', warnBeforeLeaving);
    });
    effect(() => {
      const scope = this.currentScope();
      if (scope !== this.get().scope) {
        this.clearProtectedState();
        this.patchState({ scope });
      }
    });
  }

  edit(field: 'title' | 'body', value: string): void {
    if (!this.ensureCurrentScope()) { return; }
    const limit = field === 'title' ? 200 : 20000;
    // Local editor bounds only; no unapproved server limit is implied.
    if (value.length > limit) { return; }
    this.patchState((state) => ({ content: { ...state.content, [field]: value },
      ...(this.busy ? {} : { status: 'editing' as const, message: '' }) }));
  }

  setLinksPreview(enabled: boolean): void {
    if (!this.ensureCurrentScope() || this.busy) { return; }
    this.patchState({ linksPreviewEnabled: enabled, linkQuery: '' });
  }

  searchLinks(query: string): void {
    if (this.ensureCurrentScope()) { this.patchState({ linkQuery: query.slice(0, 200) }); }
  }

  addLink(id: string): void {
    if (!this.ensureCurrentScope() || this.busy) { return; }
    const state = this.get();
    const target = DEMO_LINK_TARGETS.find((item) => item.id === id);
    if (!state.scope || !state.linksPreviewEnabled || !target || state.content.links.some((item) => item.id === id)) { return; }
    this.patchState({ content: { ...state.content, links: [...state.content.links, { ...target }] }, status: 'editing', message: '' });
  }

  removeLink(id: string): void {
    if (!this.ensureCurrentScope() || this.busy) { return; }
    const state = this.get();
    if (!state.scope) { return; }
    this.patchState({ content: { ...state.content, links: state.content.links.filter((item) => item.id !== id) }, status: 'editing', message: '' });
  }

  saveCheckpoint(retry = false): void {
    if (!this.ensureCurrentScope()) { return; }
    const state = this.get();
    if (!state.scope || this.busy || (!retry && state.status === 'conflict')) { return; }
    let version = state.checkpoint.version;
    try { if (retry) { version = this.repository.read(state.scope).version; } }
    catch { this.checkpointReadFailed(); return; }
    const generation = this.begin();
    const content = copyDraft(state.content);
    this.pending = defer(() => this.repository.save(state.scope!, version, content)).pipe(
      take(1),
      throwIfEmpty(),
      finalize(() => { if (generation === this.generation) { this.busy = false; } })
    ).subscribe({
      next: (result) => {
        if (!this.acceptResult(generation, state.scope!)) { return; }
        if (result.status === 'conflict') {
          this.patchState({ status: 'conflict', message: 'The local checkpoint changed. Your edits are retained. Retry replaces that checkpoint with your current edits; restore loads the checkpoint.' });
          return;
        }
        this.patchState({ checkpoint: result.checkpoint,
          status: sameDraft(this.get().content, result.checkpoint.content) ? 'checkpoint' : 'editing',
          message: 'Checkpoint kept in this tab memory only. It is not saved to the server.' });
      },
      error: () => {
        if (this.acceptResult(generation, state.scope!)) {
          this.patchState({ status: 'error', message: 'The local checkpoint could not be kept. Your working edits remain in this tab. Retry when ready.' });
        }
      }
    });
  }

  checkServerSave(): void {
    if (!this.ensureCurrentScope() || this.busy) { return; }
    const scope = this.get().scope!;
    const generation = this.begin();
    const content = copyDraft(this.get().content);
    this.pending = defer(() => this.remote.attemptSave(content)).pipe(
      take(1),
      throwIfEmpty(),
      finalize(() => { if (generation === this.generation) { this.busy = false; } })
    ).subscribe({
      next: (result) => {
        if (!this.acceptResult(generation, scope)) { return; }
        if (result === 'denied') {
          this.clearProtectedState();
          this.patchState({ status: 'denied', message: 'Access is unavailable. Local draft content and preview links were cleared.' });
          return;
        }
        this.patchState({ status: result === 'failed' ? 'error' : result,
          message: result === 'conflict' ? 'The adapter reported a conflict. Your edits are retained; no server save was confirmed.' :
            result === 'failed' ? 'The adapter failed. Your edits are retained; no server save was confirmed.' :
              'Server save and cross-device sync are unavailable. Your edits remain in this tab memory only.' });
      },
      error: () => {
        if (this.acceptResult(generation, scope)) {
          this.patchState({ status: 'error', message: 'The adapter could not be reached. Your edits are retained; no server save was confirmed.' });
        }
      }
    });
  }

  restoreCheckpoint(): void {
    if (!this.ensureCurrentScope() || this.busy) { return; }
    let checkpoint: LocalDraftCheckpoint;
    try { checkpoint = this.repository.read(this.get().scope!); }
    catch { this.checkpointReadFailed(); return; }
    this.patchState({ checkpoint, content: copyDraft(checkpoint.content), status: 'editing', message: 'Local checkpoint restored. No server data was read.' });
  }

  discard(): void {
    if (!this.ensureCurrentScope() || this.busy) { return; }
    const scope = this.get().scope;
    this.clearProtectedState();
    this.patchState({ scope, message: 'Local draft and preview links discarded.' });
  }

  private begin(): number {
    this.busy = true;
    const generation = ++this.generation;
    this.patchState({ status: 'saving', message: '' });
    return generation;
  }

  private currentScope(): string | null {
    const user = this.auth.currentUser();
    const tenant = this.auth.currentTenant();
    const workspace = this.workspace.activeWorkspace();
    return this.auth.isAuthenticated() && user && tenant?.isAvailable && workspace
      ? JSON.stringify([tenant.tenantId, user.userId, workspace.id]) : null;
  }

  private ensureCurrentScope(): boolean {
    const scope = this.currentScope();
    if (scope !== this.get().scope) {
      this.clearProtectedState();
      return false;
    }
    return scope !== null;
  }

  private acceptResult(generation: number, scope: string): boolean {
    if (generation !== this.generation) { return false; }
    return this.ensureCurrentScope() && this.get().scope === scope;
  }

  private checkpointReadFailed(): void {
    this.patchState({ status: 'error', message: 'The local checkpoint could not be read. Your working edits are retained. No server data was read.' });
  }

  private clearProtectedState(): void {
    this.generation++;
    this.pending?.unsubscribe();
    this.pending = null;
    this.busy = false;
    this.repository.clear();
    this.setState(initialState());
  }
}
