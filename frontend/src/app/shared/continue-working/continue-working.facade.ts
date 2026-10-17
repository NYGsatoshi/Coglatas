import { I18nService, TranslationKey } from '../../core/i18n/i18n.service';
import { HttpClient, HttpErrorResponse, HttpResponse } from '@angular/common/http';
import { computed, effect, inject, Injectable, signal, untracked } from '@angular/core';
import { catchError, from, map, mergeMap, Observable, of, Subscription, toArray } from 'rxjs';

import { normalizeApiError } from '../../core/api/api-error.adapter';
import { AuthSessionFacade } from '../../core/auth/auth-session.facade';
import { ProtectedStateClearReason, RealtimeFacade } from '../../core/realtime/realtime.facade';
import { ActiveWorkspaceFacade } from '../../core/workspace/active-workspace.facade';
import { WorkStatus } from '../ui/work-status/work-status';
import {
  ContinueWorkingHistoryEntry,
  ContinueWorkingHistoryService,
  ContinueWorkingScope,
} from './continue-working-history.service';

export interface ContinueWorkingItemViewModel {
  readonly kind: 'project' | 'file';
  readonly resourceId: string;
  readonly title: string;
  readonly status: WorkStatus;
  readonly updatedAtUtc: string;
  readonly lastOpenedUtc: string;
  readonly route: string | null;
}

export type ContinueWorkingStatus =
  | 'idle'
  | 'loading'
  | 'ready'
  | 'empty'
  | 'error'
  | 'storageUnavailable'
  | 'permissionDenied';

export interface ContinueWorkingViewModel {
  readonly status: ContinueWorkingStatus;
  readonly workspaceId: string | null;
  readonly items: readonly ContinueWorkingItemViewModel[];
  readonly retryAvailable: boolean;
  readonly downloadingFileId: string | null;
  readonly message?: string;
  readonly messageKey?: TranslationKey;
  readonly downloadMessage?: string;
  readonly downloadMessageKey?: TranslationKey;
}

interface ProjectDetailDto {
  readonly id?: unknown;
  readonly workspaceId?: unknown;
  readonly title?: unknown;
  readonly status?: unknown;
  readonly createdAt?: unknown;
  readonly updatedAt?: unknown;
}

interface FileDetailDto {
  readonly id?: unknown;
  readonly workspaceId?: unknown;
  readonly originalFileName?: unknown;
  readonly status?: unknown;
  readonly createdAt?: unknown;
  readonly updatedAt?: unknown;
  readonly deletedAt?: unknown;
}

interface FileDownloadGrantDto {
  readonly fileDownloadGrantId?: unknown;
  readonly fileObjectId?: unknown;
  readonly expiresAt?: unknown;
  readonly token?: unknown;
}

type HydrationOutcome =
  | { readonly kind: 'hydrated'; readonly entry: ContinueWorkingHistoryEntry; readonly item: ContinueWorkingItemViewModel }
  | { readonly kind: 'prune'; readonly entry: ContinueWorkingHistoryEntry }
  | { readonly kind: 'transient'; readonly entry: ContinueWorkingHistoryEntry };

const maximumHydrationConcurrency = 3;
const maximumDisplayedItems = 6;

@Injectable({ providedIn: 'root' })
export class ContinueWorkingFacade {
  private readonly i18n = inject(I18nService);
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthSessionFacade);
  private readonly activeWorkspace = inject(ActiveWorkspaceFacade);
  private readonly realtime = inject(RealtimeFacade);
  private readonly history = inject(ContinueWorkingHistoryService);
  private readonly requestedWorkspaceId = signal<string | null>(null);
  private readonly state = signal<ContinueWorkingViewModel>(emptyView('idle'));
  private currentScope: ContinueWorkingScope | null = null;
  private observedBoundaryKey = '';
  private generation = 0;
  private hydrationRequest: Subscription | null = null;
  private downloadOperation: Subscription | null = null;

  readonly view = computed(() => {
    const state = this.state();
    return { ...state,
      message: state.messageKey ? this.i18n.translate(state.messageKey) : state.message,
      downloadMessage: state.downloadMessageKey ? this.i18n.translate(state.downloadMessageKey) : state.downloadMessage,
    };
  });

  constructor() {
    this.realtime.registerProtectedStateClearer?.(
      'continue-working',
      (reason) => this.clearProtectedState(reason),
    );
    this.realtime.registerCatchUp?.('continue-working', () => {
      this.applyRequestedScope(this.requestedWorkspaceId());
    });
    effect(() => {
      const requestedWorkspaceId = this.requestedWorkspaceId();
      const session = this.auth.session();
      const activeWorkspaceId = this.activeWorkspace.activeWorkspace()?.id ?? null;
      const boundaryKey = [
        requestedWorkspaceId ?? '',
        session.status,
        session.isAuthenticated ? '1' : '0',
        session.currentTenant?.tenantId ?? '',
        session.currentTenant?.isAvailable ? '1' : '0',
        session.currentTenant?.isPlatformScope ? '1' : '0',
        session.currentUser?.userId ?? '',
        activeWorkspaceId ?? '',
      ].join('\u0000');
      if (boundaryKey === this.observedBoundaryKey) {
        return;
      }
      this.observedBoundaryKey = boundaryKey;
      untracked(() => this.applyRequestedScope(requestedWorkspaceId));
    });
  }

  activate(workspaceId: string | null | undefined): void {
    const next = workspaceId?.trim() || null;
    if (this.requestedWorkspaceId() === next) {
      this.applyRequestedScope(next);
      return;
    }
    this.requestedWorkspaceId.set(next);
  }

  release(): void {
    this.requestedWorkspaceId.set(null);
    this.observedBoundaryKey = '';
    this.clearRequestsAndProjection('idle');
  }

  retry(): void {
    if (this.currentScope) {
      this.hydrate(this.currentScope);
    }
  }

  downloadFile(resourceId: string): void {
    const scope = this.currentScope;
    const item = this.state().items.find((candidate) => candidate.kind === 'file' && candidate.resourceId === resourceId);
    if (!scope || !item || this.downloadOperation) {
      return;
    }

    const generation = this.generation;
    this.state.update((current) => ({
      ...current,
      downloadingFileId: resourceId,
      downloadMessage: 'Authorizing download.',
      downloadMessageKey: 'recent.authorizing',
    }));
    const operation = new Subscription();
    this.downloadOperation = operation;
    operation.add(() => {
      if (this.downloadOperation === operation) {
        this.downloadOperation = null;
      }
    });

    const grantRequest = this.http.post<FileDownloadGrantDto>(
      `/api/files/${resourceId}/download-grants`,
      { purpose: 'continue-working-download' },
      { withCredentials: true },
    ).subscribe({
      next: (grant) => {
        if (!this.operationIsCurrent(scope, generation, resourceId)) {
          return;
        }
        const grantId = requiredUuid(grant.fileDownloadGrantId);
        const grantedFileId = requiredUuid(grant.fileObjectId);
        const expiresAt = requiredTimestamp(grant.expiresAt);
        const token = boundedString(grant.token, 4_096);
        if (!grantId || grantedFileId !== resourceId || !expiresAt || !token) {
          this.setDownloadFailure('The download grant response could not be verified.', 'recent.grantUnverified');
          operation.unsubscribe();
          return;
        }
        this.downloadWithGrant(scope, generation, item, grantId, token, operation);
      },
      error: (error: unknown) => {
        if (!this.operationIsCurrent(scope, generation, resourceId)) {
          return;
        }
        this.handleDownloadAuthorizationError(scope, generation, item, error, operation);
      },
    });
    operation.add(grantRequest);
  }

  private applyRequestedScope(workspaceId: string | null): void {
    const scope = this.history.resolveCurrentScope(workspaceId);
    if (!scope) {
      this.clearRequestsAndProjection('idle');
      return;
    }
    const sameScope = scopeKey(scope) === scopeKey(this.currentScope);
    this.currentScope = scope;
    if (!sameScope || this.state().status === 'idle' || this.state().status === 'permissionDenied') {
      this.hydrate(scope);
    }
  }

  private hydrate(scope: ContinueWorkingScope): void {
    this.cancelRequests();
    this.currentScope = scope;
    const generation = ++this.generation;
    const history = this.history.read(scope);
    if (history.status === 'storageUnavailable') {
      this.state.set({
        ...emptyView('storageUnavailable', scope.workspaceId),
        message: 'Recent work is unavailable because browser storage could not be accessed.',
        messageKey: 'recent.storageUnavailable',
      });
      return;
    }
    if (history.entries.length === 0) {
      this.state.set({
        ...emptyView('empty', scope.workspaceId),
        messageKey: history.status === 'discarded'
          ? 'recent.discarded' : 'recent.startHistory',
        message: history.status === 'discarded'
          ? 'Invalid recent-work data was discarded safely.'
          : 'Open a Research or download a File to see it here.',
      });
      return;
    }

    this.state.set(emptyView('loading', scope.workspaceId));
    this.hydrationRequest = from(history.entries).pipe(
      mergeMap((entry) => this.hydrateEntry(entry, scope), maximumHydrationConcurrency),
      toArray(),
    ).subscribe({
      next: (outcomes) => {
        if (!this.scopeIsCurrent(scope, generation)) {
          return;
        }
        const pruned = outcomes.filter((outcome) => outcome.kind === 'prune').map((outcome) => outcome.entry);
        const storageUpdated = this.history.removeEntries(scope, pruned);
        const items = outcomes
          .filter((outcome): outcome is Extract<HydrationOutcome, { readonly kind: 'hydrated' }> => outcome.kind === 'hydrated')
          .map((outcome) => outcome.item)
          .sort((left, right) => right.lastOpenedUtc.localeCompare(left.lastOpenedUtc))
          .slice(0, maximumDisplayedItems);
        const transientCount = outcomes.filter((outcome) => outcome.kind === 'transient').length;
        if (items.length > 0) {
          this.state.set({
            status: 'ready',
            workspaceId: scope.workspaceId,
            items,
            retryAvailable: transientCount > 0 || !storageUpdated,
            downloadingFileId: null,
            messageKey: transientCount > 0 ? 'recent.someUnavailable' : !storageUpdated ? 'recent.historyWriteFailed' : undefined,
            message: transientCount > 0
              ? 'Some recent work could not be reauthorized. Retry to check it again.'
              : !storageUpdated
                ? 'Revoked entries were hidden, but browser history could not be updated.'
                : undefined,
          });
          return;
        }
        if (transientCount > 0 || !storageUpdated) {
          this.state.set({
            ...emptyView('error', scope.workspaceId),
            retryAvailable: true,
            message: 'Recent work could not be reauthorized. No cached labels are shown.',
            messageKey: 'recent.reauthorizeError',
          });
          return;
        }
        this.state.set({
          ...emptyView('empty', scope.workspaceId),
          message: 'No currently authorized recent work is available.',
          messageKey: 'recent.noAuthorized',
        });
      },
      error: () => {
        if (this.scopeIsCurrent(scope, generation)) {
          this.state.set({
            ...emptyView('error', scope.workspaceId),
            retryAvailable: true,
            message: 'Recent work could not be reauthorized. No cached labels are shown.',
            messageKey: 'recent.reauthorizeError',
          });
        }
      },
    });
  }

  private hydrateEntry(entry: ContinueWorkingHistoryEntry, scope: ContinueWorkingScope): Observable<HydrationOutcome> {
    return entry.kind === 'project'
      ? this.http.get<ProjectDetailDto>(`/api/projects/${entry.resourceId}`, { withCredentials: true }).pipe(
          map((response) => mapProjectOutcome(entry, scope, response)),
          catchError((error: unknown) => this.handleHydrationError(entry, error)),
        )
      : this.hydrateFile(entry, scope);
  }

  private hydrateFile(entry: ContinueWorkingHistoryEntry, scope: ContinueWorkingScope): Observable<HydrationOutcome> {
    return this.http.get<FileDetailDto>(`/api/files/${entry.resourceId}`, { withCredentials: true }).pipe(
      map((response) => mapFileOutcome(entry, scope, response)),
      catchError((error: unknown) => this.handleHydrationError(entry, error)),
    );
  }

  private handleHydrationError(entry: ContinueWorkingHistoryEntry, error: unknown): Observable<HydrationOutcome> {
    const status = httpStatus(error);
    if (status === 401) {
      this.clearRequestsAndProjection('permissionDenied');
      return of({ kind: 'transient', entry });
    }
    if (entry.kind === 'file' && (status === 400 || status === 403 || status === 404)) {
      return of({ kind: 'prune', entry });
    }
    if (entry.kind === 'project' && (status === 403 || status === 404)) {
      return of({ kind: 'prune', entry });
    }
    return of({ kind: 'transient', entry });
  }

  private downloadWithGrant(
    scope: ContinueWorkingScope,
    generation: number,
    item: ContinueWorkingItemViewModel,
    grantId: string,
    token: string,
    operation: Subscription,
  ): void {
    const request = this.http.post(
      `/api/file-download-grants/${grantId}/download`,
      { token },
      { observe: 'response', responseType: 'blob', withCredentials: true },
    ).subscribe({
      next: (response) => {
        if (!this.operationIsCurrent(scope, generation, item.resourceId)) {
          return;
        }
        const downloaded = saveBlob(response, item.title === 'File' ? 'download' : item.title);
        if (!downloaded) {
          this.setDownloadFailure('The File was received, but the browser could not start the download.', 'recent.browserDownloadFailed');
          operation.unsubscribe();
          return;
        }
        const touched = this.history.touchFile(item.resourceId, scope.workspaceId);
        if (touched) {
          this.state.update((current) => ({
            ...current,
            items: current.items
              .map((candidate) => candidate.resourceId === item.resourceId
                ? { ...candidate, lastOpenedUtc: touched.lastOpenedUtc }
                : candidate)
              .sort((left, right) => right.lastOpenedUtc.localeCompare(left.lastOpenedUtc)),
            downloadingFileId: null,
            downloadMessage: 'Download started.',
            downloadMessageKey: 'recent.downloadStarted',
          }));
        } else {
          this.state.update((current) => ({
            ...current,
            downloadingFileId: null,
            downloadMessage: 'Download started. Browser recency could not be saved.',
            downloadMessageKey: 'recent.downloadHistoryFailed',
          }));
        }
        operation.unsubscribe();
      },
      error: (error: unknown) => {
        if (!this.operationIsCurrent(scope, generation, item.resourceId)) {
          return;
        }
        this.handleDownloadAuthorizationError(scope, generation, item, error, operation);
      },
    });
    operation.add(request);
  }

  private handleDownloadAuthorizationError(
    scope: ContinueWorkingScope,
    generation: number,
    item: ContinueWorkingItemViewModel,
    error: unknown,
    operation: Subscription,
  ): void {
    const status = httpStatus(error);
    if (status === 401) {
      this.clearRequestsAndProjection('permissionDenied');
      operation.unsubscribe();
      return;
    }
    if (status !== 400 && status !== 403 && status !== 404) {
      this.setDownloadFailure(normalizeApiError(error).message || 'The download could not be completed.');
      operation.unsubscribe();
      return;
    }

    // File grant routes use 400 for both policy/scan denial and revoked
    // resources. Re-read exact metadata before deciding whether to prune.
    const entry: ContinueWorkingHistoryEntry = {
      kind: 'file',
      resourceId: item.resourceId,
      lastOpenedUtc: item.lastOpenedUtc,
    };
    const metadataRequest = this.hydrateFile(entry, scope).subscribe((outcome) => {
      if (!this.operationIsCurrent(scope, generation, item.resourceId)) {
        return;
      }
      if (outcome.kind === 'prune') {
        this.history.removeEntries(scope, [entry]);
        this.state.update((current) => {
          const items = current.items.filter((candidate) => candidate.resourceId !== item.resourceId);
          return {
            ...current,
            status: items.length > 0 ? 'ready' : 'empty',
            items,
            retryAvailable: false,
            downloadingFileId: null,
            messageKey: items.length > 0 ? current.messageKey : 'recent.noAuthorized',
            message: items.length > 0 ? current.message : 'No currently authorized recent work is available.',
            downloadMessage: 'The File is no longer available.',
            downloadMessageKey: 'recent.fileUnavailable',
          };
        });
      } else if (outcome.kind === 'hydrated') {
        this.state.update((current) => ({
          ...current,
          items: current.items.map((candidate) => candidate.resourceId === item.resourceId ? outcome.item : candidate),
          downloadingFileId: null,
          downloadMessage: 'Download is not currently allowed by server policy.',
          downloadMessageKey: 'recent.downloadDenied',
        }));
      } else {
        this.state.update((current) => ({
          ...current,
          status: current.items.length > 1 ? 'ready' : 'error',
          items: current.items.filter((candidate) => candidate.resourceId !== item.resourceId),
          retryAvailable: true,
          downloadingFileId: null,
          message: 'The File could not be reauthorized. No cached metadata is shown.',
          messageKey: 'recent.fileReauthorizeError',
          downloadMessage: undefined,
          downloadMessageKey: undefined,
        }));
      }
      operation.unsubscribe();
    });
    operation.add(metadataRequest);
  }

  private setDownloadFailure(message: string, messageKey?: TranslationKey): void {
    this.state.update((current) => ({
      ...current,
      downloadingFileId: null,
      downloadMessage: message,
      downloadMessageKey: messageKey,
    }));
  }

  private scopeIsCurrent(scope: ContinueWorkingScope, generation: number): boolean {
    return generation === this.generation && scopeKey(scope) === scopeKey(this.currentScope);
  }

  private operationIsCurrent(scope: ContinueWorkingScope, generation: number, resourceId: string): boolean {
    return this.scopeIsCurrent(scope, generation) &&
      this.downloadOperation !== null &&
      this.state().downloadingFileId === resourceId;
  }

  private clearRequestsAndProjection(status: 'idle' | 'permissionDenied'): void {
    this.cancelRequests();
    this.currentScope = null;
    this.generation += 1;
    this.state.set({
      ...emptyView(status),
      messageKey: status === 'permissionDenied' ? 'recent.sessionDenied' : undefined,
      message: status === 'permissionDenied'
        ? 'Recent work was cleared because the session is no longer authorized.'
        : undefined,
    });
  }

  private clearProtectedState(reason: ProtectedStateClearReason): void {
    this.cancelRequests();
    this.currentScope = null;
    this.generation += 1;
    this.observedBoundaryKey = '';
    this.state.set({
      ...emptyView(reason === 'authorization' ? 'loading' : 'idle'),
      workspaceId: this.requestedWorkspaceId(),
      messageKey: reason === 'authorization' ? 'recent.reauthorizing' : undefined,
      message: reason === 'authorization'
        ? 'Recent work is being reauthorized.'
        : undefined,
    });
  }

  private cancelRequests(): void {
    this.hydrationRequest?.unsubscribe();
    this.hydrationRequest = null;
    this.downloadOperation?.unsubscribe();
    this.downloadOperation = null;
  }
}

function mapProjectOutcome(
  entry: ContinueWorkingHistoryEntry,
  scope: ContinueWorkingScope,
  response: ProjectDetailDto,
): HydrationOutcome {
  const id = requiredUuid(response.id);
  const workspaceId = requiredUuid(response.workspaceId);
  if (id !== entry.resourceId || workspaceId !== scope.workspaceId) {
    return { kind: 'prune', entry };
  }
  const title = boundedString(response.title, 1_000);
  const updatedAtUtc = requiredTimestamp(response.updatedAt) ?? requiredTimestamp(response.createdAt);
  if (!title || !updatedAtUtc) {
    return { kind: 'transient', entry };
  }
  return {
    kind: 'hydrated',
    entry,
    item: {
      kind: 'project',
      resourceId: entry.resourceId,
      title,
      status: projectStatus(response.status),
      updatedAtUtc,
      lastOpenedUtc: entry.lastOpenedUtc,
      route: `/projects/${entry.resourceId}`,
    },
  };
}

function mapFileOutcome(
  entry: ContinueWorkingHistoryEntry,
  scope: ContinueWorkingScope,
  response: FileDetailDto,
): HydrationOutcome {
  const id = requiredUuid(response.id);
  const workspaceId = requiredUuid(response.workspaceId);
  if (id !== entry.resourceId || workspaceId !== scope.workspaceId) {
    return { kind: 'prune', entry };
  }
  if (response.deletedAt !== null && response.deletedAt !== undefined) {
    return typeof response.deletedAt === 'string'
      ? { kind: 'prune', entry }
      : { kind: 'transient', entry };
  }
  const lifecycle = String(response.status ?? '').trim().toLowerCase();
  if (lifecycle === 'deleted') {
    return { kind: 'prune', entry };
  }
  const status = fileStatus(lifecycle);
  const updatedAtUtc = requiredTimestamp(response.updatedAt) ?? requiredTimestamp(response.createdAt);
  if (!status || !updatedAtUtc) {
    return { kind: 'transient', entry };
  }
  const originalFileName = boundedString(response.originalFileName, 1_000);
  if (!originalFileName) {
    return { kind: 'transient', entry };
  }
  return {
    kind: 'hydrated',
    entry,
    item: {
      kind: 'file',
      resourceId: entry.resourceId,
      title: originalFileName === '[redacted:file]' ? 'File' : originalFileName,
      status,
      updatedAtUtc,
      lastOpenedUtc: entry.lastOpenedUtc,
      route: null,
    },
  };
}

function projectStatus(value: unknown): WorkStatus {
  switch (String(value ?? '').trim().toLowerCase()) {
    case '0':
    case 'planning':
      return 'draft';
    case '1':
    case 'active':
      return 'running';
    case '2':
    case 'review':
      return 'needsReview';
    case '3':
    case 'complete':
    case 'completed':
      return 'completed';
    case '4':
    case 'suspended':
      return 'paused';
    case '5':
    case '6':
    case 'archived':
    case 'deleted':
      return 'archived';
    default:
      return 'needsAttention';
  }
}

function fileStatus(value: string): WorkStatus | null {
  switch (value) {
    case 'active':
      return 'ready';
    case 'quarantined':
      return 'needsAttention';
    case 'archived':
      return 'archived';
    default:
      return null;
  }
}

function emptyView(status: ContinueWorkingStatus, workspaceId: string | null = null): ContinueWorkingViewModel {
  return {
    status,
    workspaceId,
    items: [],
    retryAvailable: false,
    downloadingFileId: null,
  };
}

function scopeKey(scope: ContinueWorkingScope | null): string {
  return scope ? `${scope.tenantId}\u0000${scope.userId}\u0000${scope.workspaceId}` : '';
}

function requiredUuid(value: unknown): string | null {
  return typeof value === 'string' && uuidPattern.test(value.trim())
    ? value.trim().toLowerCase()
    : null;
}

function requiredTimestamp(value: unknown): string | null {
  if (typeof value !== 'string' || value.length > 40) {
    return null;
  }
  const milliseconds = Date.parse(value);
  return Number.isFinite(milliseconds) ? new Date(milliseconds).toISOString() : null;
}

function boundedString(value: unknown, maximumLength: number): string | null {
  return typeof value === 'string' && value.trim().length > 0 && value.length <= maximumLength
    ? value.trim()
    : null;
}

function httpStatus(error: unknown): number | undefined {
  return error instanceof HttpErrorResponse ? error.status : normalizeApiError(error).httpStatus;
}

function saveBlob(response: HttpResponse<Blob>, fallbackName: string): boolean {
  const blob = response.body;
  if (!blob || typeof document === 'undefined' || typeof URL === 'undefined' || typeof URL.createObjectURL !== 'function') {
    return false;
  }
  let objectUrl: string | null = null;
  try {
    objectUrl = URL.createObjectURL(blob);
    const anchor = document.createElement('a');
    anchor.href = objectUrl;
    anchor.download = safeFileName(response.headers.get('content-disposition'), fallbackName);
    anchor.rel = 'noopener';
    document.body.append(anchor);
    anchor.click();
    anchor.remove();
    return true;
  } catch {
    return false;
  } finally {
    if (objectUrl) {
      URL.revokeObjectURL(objectUrl);
    }
  }
}

function safeFileName(contentDisposition: string | null, fallbackName: string): string {
  const utf8 = contentDisposition ? /filename\*=UTF-8''([^;]+)/iu.exec(contentDisposition)?.[1] : undefined;
  const plain = contentDisposition ? /filename="?([^";]+)"?/iu.exec(contentDisposition)?.[1] : undefined;
  let candidate = fallbackName;
  try {
    candidate = utf8 ? decodeURIComponent(utf8) : plain ?? fallbackName;
  } catch {
    candidate = fallbackName;
  }
  const leaf = candidate.replaceAll('\\', '/').split('/').pop()?.replace(/[\u0000-\u001f\u007f]/gu, '').trim();
  return leaf || 'download';
}

const uuidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/iu;
