import { HttpClient } from '@angular/common/http';
import { DestroyRef, Injectable, computed, effect, inject, signal, untracked } from '@angular/core';
import { Subscription, catchError, from, map, mergeMap, of, toArray } from 'rxjs';

import { normalizeApiError } from '../../core/api/api-error.adapter';
import { RealtimeFacade } from '../../core/realtime/realtime.facade';
import { ActiveWorkspaceFacade } from '../../core/workspace/active-workspace.facade';
import { injectFilesI18n } from '../../core/i18n/providers/files-i18n';
import { ContinueWorkingHistoryService, ContinueWorkingScope } from '../../shared/continue-working/continue-working-history.service';
import { FileListItemDto, mapFileListItem } from './files.api';
import { canonicalFileStarId, fileStarsScopeKey, LOCAL_FILE_STARS_LIMIT, LocalFileStarsRepository } from './file-stars.repository';
import { FileViewModel } from './files.types';

type StarStatus = 'idle' | 'loading' | 'ready' | 'empty' | 'error' | 'storageUnavailable';
type StarNotice = 'none' | 'saved' | 'removed' | 'limit' | 'storageUnavailable' | 'discarded';
interface StarView {
  readonly status: StarStatus;
  readonly files: readonly FileViewModel[];
  readonly retryAvailable: boolean;
}
type ReadOutcome = { readonly id: string; readonly file?: FileViewModel; readonly prune?: boolean; readonly denied?: boolean };

/** Local IDs never authorize a read. Every Starred visit replaces metadata through existing File GETs. */
@Injectable({ providedIn: 'root' })
export class LocalFileStarsService {
  private readonly http = inject(HttpClient);
  private readonly repository = inject(LocalFileStarsRepository);
  private readonly scopeResolver = inject(ContinueWorkingHistoryService);
  private readonly activeWorkspace = inject(ActiveWorkspaceFacade);
  private readonly realtime = inject(RealtimeFacade);
  private readonly i18n = injectFilesI18n();
  private readonly visible = signal(false);
  private readonly currentScope = computed(() =>
    this.scopeResolver.resolveCurrentScope(this.activeWorkspace.activeWorkspace()?.id));
  private readonly state = signal<StarView>(emptyView());
  private readonly ids = signal<readonly string[]>([]);
  private readonly noticeState = signal<StarNotice>('none');
  private readonly observedScopeKey = signal('');
  private generation = 0;
  private request: Subscription | null = null;

  readonly available = computed(() => this.currentScope() !== null);
  readonly notice = this.noticeState.asReadonly();
  readonly view = computed(() => this.visible() && fileStarsScopeKey(this.currentScope()) === this.observedScopeKey()
    ? this.state() : emptyView());

  constructor() {
    effect(() => {
      const key = fileStarsScopeKey(this.currentScope());
      untracked(() => {
        if (key === this.observedScopeKey()) { return; }
        this.reset();
        this.observedScopeKey.set(key);
        this.readIds();
        if (this.visible()) { this.refresh(); }
      });
    });
    const clear = this.realtime.registerProtectedStateClearer?.('local-file-stars', () => {
      this.reset();
      this.observedScopeKey.set('');
    });
    const catchUp = this.realtime.registerCatchUp?.('local-file-stars', () => {
      this.readIds();
      if (this.visible()) { this.refresh(); }
    });
    const events = this.realtime.durableEvents$?.subscribe((event) => {
      if (event.eventType === 'Files.FileChanged.v1' && this.visible()) { this.refresh(); }
    });
    inject(DestroyRef).onDestroy(() => { clear?.(); catchUp?.(); events?.unsubscribe(); this.reset(); });
  }

  isStarred(file: FileViewModel): boolean {
    return fileStarsScopeKey(this.currentScope()) === this.observedScopeKey() &&
      !!file.canonicalFileId && this.ids().includes(file.canonicalFileId.toLowerCase());
  }

  toggle(file: FileViewModel): void {
    const scope = this.currentScope();
    const id = canonicalFileStarId(file.canonicalFileId);
    if (!scope || !id) { return; }
    const current = this.repository.read(scope);
    if (current.status === 'unavailable') { this.noticeState.set('storageUnavailable'); return; }
    const removing = current.ids.includes(id);
    if (!removing && current.ids.length >= LOCAL_FILE_STARS_LIMIT) { this.noticeState.set('limit'); return; }
    const next = removing ? current.ids.filter((candidate) => candidate !== id) : [...current.ids, id];
    if (!this.repository.write(scope, next)) { this.noticeState.set('storageUnavailable'); return; }
    this.observedScopeKey.set(fileStarsScopeKey(scope));
    this.ids.set(next);
    this.noticeState.set(removing ? 'removed' : 'saved');
    if (this.visible()) { this.refresh(); }
  }

  setVisible(visible: boolean): void {
    this.visible.set(visible);
    this.resetProjection();
    if (visible) { this.refresh(); }
  }

  refresh(): void {
    this.resetProjection();
    const scope = this.currentScope();
    if (!scope || !this.visible()) { return; }
    this.observedScopeKey.set(fileStarsScopeKey(scope));
    const record = this.readIds();
    if (!record || record.status === 'unavailable') {
      this.state.set({ status: 'storageUnavailable', files: [], retryAvailable: true });
      return;
    }
    if (record.ids.length === 0) { this.state.set({ status: 'empty', files: [], retryAvailable: false }); return; }
    const generation = this.generation;
    this.state.set({ status: 'loading', files: [], retryAvailable: false });
    this.request = from(record.ids).pipe(
      mergeMap((id) => this.http.get<unknown>(`/api/files/${id}`, { withCredentials: true }).pipe(
        map((response): ReadOutcome => ({ id, file: this.mapDetail(response, id, scope) ?? undefined,
          prune: !this.validDetail(response, id, scope) })),
        catchError((error: unknown) => {
          const status = normalizeApiError(error).httpStatus;
          if (status === 401 && generation === this.generation) {
            this.resetProjection();
            this.ids.set([]);
            this.state.set({ status: 'error', files: [], retryAvailable: true });
          }
          return of<ReadOutcome>({ id, prune: [400, 403, 404].includes(status), denied: status === 401 });
        }),
      ), 3),
      toArray(),
    ).subscribe((outcomes) => {
      if (generation !== this.generation || !this.visible() ||
        fileStarsScopeKey(scope) !== fileStarsScopeKey(this.currentScope())) { return; }
      this.request = null;
      // A terminal session response invalidates the entire protected projection.
      if (outcomes.some((outcome) => outcome.denied)) {
        this.ids.set([]);
        this.state.set({ status: 'error', files: [], retryAvailable: true });
        return;
      }
      const pruned = new Set(outcomes.filter((outcome) => outcome.prune).map((outcome) => outcome.id));
      // Re-read before pruning so another browser tab's newer preference is retained.
      const latest = this.repository.read(scope);
      const retained = latest.ids.filter((id) => !pruned.has(id));
      const written = latest.status !== 'unavailable' &&
        (pruned.size === 0 || this.repository.write(scope, retained));
      if (written) { this.ids.set(retained); }
      if (!written) { this.noticeState.set('storageUnavailable'); }
      const byId = new Map(outcomes.filter((outcome) => outcome.file).map((outcome) => [outcome.id, outcome.file!]));
      const files = retained.flatMap((id) => byId.has(id) ? [byId.get(id)!] : []);
      const transient = outcomes.some((outcome) => !outcome.file && !outcome.prune);
      this.state.set({ status: transient ? 'error' : files.length ? 'ready' : 'empty', files,
        retryAvailable: transient || !written });
    });
  }

  private readIds() {
    const scope = this.currentScope();
    if (!scope) { this.ids.set([]); return null; }
    this.observedScopeKey.set(fileStarsScopeKey(scope));
    const result = this.repository.read(scope);
    this.ids.set(result.ids);
    if (result.status === 'discarded') { this.noticeState.set('discarded'); }
    return result;
  }

  private validDetail(response: unknown, id: string, scope: ContinueWorkingScope): response is FileListItemDto {
    if (!response || typeof response !== 'object' || Array.isArray(response)) { return false; }
    const dto = response as FileListItemDto;
    return canonicalFileStarId(dto.id) === id && canonicalFileStarId(dto.workspaceId) === scope.workspaceId &&
      typeof dto.originalFileName === 'string' && dto.originalFileName.length > 0 && dto.originalFileName.length <= 4_096 &&
      typeof dto.contentType === 'string' && Number.isSafeInteger(dto.sizeBytes) && Number(dto.sizeBytes) >= 0 &&
      dto.status === 'Active' && !dto.deletedAt;
  }

  private mapDetail(response: unknown, id: string, scope: ContinueWorkingScope): FileViewModel | null {
    if (!this.validDetail(response, id, scope)) { return null; }
    // FileObject detail has no scan/owner/delete projection. Do not manufacture those capabilities.
    return mapFileListItem({ ...response, id, fileObjectId: id, scanStatus: 'unavailable', canDelete: false }, {
      untitledFile: this.i18n.translate('files.untitledFile'),
      unknownUser: this.i18n.translate('files.unknownUser'),
      formatDate: () => '',
    });
  }

  private resetProjection(): void {
    this.generation += 1;
    this.request?.unsubscribe();
    this.request = null;
    this.state.set(emptyView());
  }

  private reset(): void {
    this.resetProjection();
    this.ids.set([]);
    this.noticeState.set('none');
  }
}

function emptyView(): StarView { return { status: 'idle', files: [], retryAvailable: false }; }
