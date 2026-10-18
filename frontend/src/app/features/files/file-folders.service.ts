import { HttpClient, HttpParams } from '@angular/common/http';
import { DestroyRef, Injectable, computed, inject, signal, untracked } from '@angular/core';
import { Observable, Subject, Subscription, catchError, defer, finalize, map, of, switchMap, takeUntil, throwError } from 'rxjs';

import { RealtimeFacade } from '../../core/realtime/realtime.facade';

export interface FileFolderViewModel {
  readonly id: string;
  readonly workspaceId: string;
  readonly parentFolderId: string | null;
  readonly name: string;
  readonly sortOrder: number;
  readonly version: number;
}

export interface FileFolderTreeNode {
  readonly id: string;
  readonly name: string;
  readonly children: readonly FileFolderTreeNode[];
}

interface FileFolderNavigationViewModel {
  readonly rootVersion: number;
  readonly folders: readonly FileFolderViewModel[];
}

@Injectable({ providedIn: 'root' })
export class FileFolderStore {
  private readonly http = inject(HttpClient);
  private readonly realtime = inject(RealtimeFacade);
  private readonly folderState = signal<readonly FileFolderViewModel[]>([]);
  private readonly rootVersionState = signal(0);
  private readonly loadingState = signal(false);
  private readonly errorState = signal(false);
  private generation = 0;
  private workspaceId: string | null = null;
  private loadedWorkspaceId: string | null = null;
  private readonly contextChanges = new Subject<void>();
  private contextEpoch = 0;
  private folderRequest: Subscription | null = null;

  readonly folders = this.folderState.asReadonly();
  readonly rootVersion = this.rootVersionState.asReadonly();
  readonly loading = this.loadingState.asReadonly();
  readonly failed = this.errorState.asReadonly();
  readonly tree = computed<readonly FileFolderTreeNode[]>(() => buildFolderTree(this.folderState()));

  constructor() {
    const unregister = this.realtime.registerProtectedStateClearer?.('file-folders', () => this.load(null));
    inject(DestroyRef).onDestroy(() => {
      unregister?.();
      this.load(null);
      this.contextChanges.complete();
    });
  }

  load(workspaceId: string | null | undefined, force = false): void {
    const normalizedWorkspaceId = workspaceId || null;
    if (
      !force &&
      normalizedWorkspaceId !== null &&
      normalizedWorkspaceId === this.workspaceId &&
      (
        untracked(() => this.loadingState() || this.errorState()) ||
        this.loadedWorkspaceId === normalizedWorkspaceId
      )
    ) {
      return;
    }

    const generation = ++this.generation;
    if (normalizedWorkspaceId !== this.workspaceId) {
      this.loadedWorkspaceId = null;
      this.workspaceId = normalizedWorkspaceId;
      this.contextEpoch++;
      this.contextChanges.next();
      this.folderState.set([]);
      this.rootVersionState.set(0);
    }
    this.folderRequest?.unsubscribe();
    this.folderRequest = null;
    this.errorState.set(false);
    if (!workspaceId) {
      this.loadedWorkspaceId = null;
      this.folderState.set([]);
      this.rootVersionState.set(0);
      this.loadingState.set(false);
      return;
    }

    this.loadingState.set(true);
    const params = new HttpParams().set('workspaceId', workspaceId);
    this.folderRequest = this.http.get<unknown>('/api/file-folders', { params, withCredentials: true }).pipe(
      map((response) => mapNavigation(response, workspaceId)),
      catchError(() => of(null)),
      finalize(() => {
        if (generation === this.generation) {
          this.loadingState.set(false);
          this.folderRequest = null;
        }
      }),
    ).subscribe((navigation) => {
      if (generation !== this.generation || workspaceId !== this.workspaceId) {
        return;
      }
      if (!navigation) {
        this.loadedWorkspaceId = null;
        this.folderState.set([]);
        this.rootVersionState.set(0);
        this.errorState.set(true);
        return;
      }
      this.folderState.set(navigation.folders);
      this.rootVersionState.set(navigation.rootVersion);
      this.loadedWorkspaceId = workspaceId;
    });
  }

  moveFile(fileObjectId: string, destinationFolderId: string | null): Observable<void> {
    const workspaceId = this.workspaceId;
    const contextEpoch = this.contextEpoch;
    const expectedDestinationVersion = this.destinationVersion(destinationFolderId);
    if (!workspaceId || !fileObjectId || expectedDestinationVersion === undefined || !this.canMove(workspaceId, contextEpoch)) {
      return throwError(() => new Error('File move context is unavailable.'));
    }

    return defer(() => this.canMove(workspaceId, contextEpoch) ? this.http.get<unknown>(
      `/api/files/${encodeURIComponent(fileObjectId)}/location`,
      { withCredentials: true },
    ) : throwError(() => new Error('File move context is unavailable.'))).pipe(
      map((response) => mapLocation(response, fileObjectId, workspaceId)),
      switchMap((location) => this.canMove(workspaceId, contextEpoch) ? this.http.post<unknown>(
        `/api/files/${encodeURIComponent(location.fileObjectId)}/move`,
        {
          destinationFolderId,
          expectedVersion: location.version,
          expectedDestinationVersion,
        },
        { withCredentials: true },
      ) : throwError(() => new Error('File move context is unavailable.'))),
      map(() => undefined),
      takeUntil(this.contextChanges),
      finalize(() => this.refreshCurrentWorkspace(workspaceId, contextEpoch)),
    );
  }

  moveFolder(folderId: string, destinationParentFolderId: string | null): Observable<void> {
    const workspaceId = this.workspaceId;
    const contextEpoch = this.contextEpoch;
    const source = this.folderState().find((folder) => folder.id === folderId);
    const expectedDestinationVersion = this.destinationVersion(destinationParentFolderId);
    if (!workspaceId || !source || expectedDestinationVersion === undefined || !this.canMove(workspaceId, contextEpoch)) {
      return throwError(() => new Error('Folder move context is unavailable.'));
    }
    if (destinationParentFolderId === folderId) {
      return throwError(() => new Error('A folder cannot be moved into itself.'));
    }

    return defer(() => this.canMove(workspaceId, contextEpoch) ? this.http.post<unknown>(
      `/api/file-folders/${encodeURIComponent(folderId)}/move`,
      {
        destinationParentFolderId,
        expectedVersion: source.version,
        expectedDestinationVersion,
      },
      { withCredentials: true },
    ) : throwError(() => new Error('Folder move context is unavailable.'))).pipe(
      map(() => undefined),
      takeUntil(this.contextChanges),
      finalize(() => this.refreshCurrentWorkspace(workspaceId, contextEpoch)),
    );
  }

  private canMove(workspaceId: string, contextEpoch: number): boolean {
    return workspaceId === this.workspaceId && contextEpoch === this.contextEpoch &&
      workspaceId === this.loadedWorkspaceId && !this.loadingState() && !this.errorState();
  }

  private refreshCurrentWorkspace(workspaceId: string, contextEpoch: number): void {
    if (workspaceId === this.workspaceId && contextEpoch === this.contextEpoch) {
      this.load(workspaceId, true);
    }
  }

  private destinationVersion(folderId: string | null): number | undefined {
    if (!folderId) {
      return this.rootVersionState();
    }
    return this.folderState().find((folder) => folder.id === folderId)?.version;
  }
}

function mapNavigation(response: unknown, workspaceId: string): FileFolderNavigationViewModel | null {
  if (!isObject(response) || stringValue(response['workspaceId']) !== workspaceId) {
    return null;
  }
  const rootVersion = nonNegativeInteger(response['rootVersion']);
  const rawFolders = response['folders'];
  if (rootVersion === undefined || !Array.isArray(rawFolders)) {
    return null;
  }

  const folders: FileFolderViewModel[] = [];
  for (const item of rawFolders) {
    const folder = mapFolder(item, workspaceId);
    if (!folder) {
      return null;
    }
    folders.push(folder);
  }
  folders.sort((left, right) =>
    left.sortOrder - right.sortOrder || left.name.localeCompare(right.name) || left.id.localeCompare(right.id));
  return { rootVersion, folders };
}

function mapFolder(value: unknown, workspaceId: string): FileFolderViewModel | null {
  if (!isObject(value)) {
    return null;
  }
  const id = stringValue(value['id']);
  const responseWorkspaceId = stringValue(value['workspaceId']);
  const parentFolderId = nullableStringValue(value['parentFolderId']);
  const name = stringValue(value['name']);
  const sortOrder = nonNegativeInteger(value['sortOrder']);
  const version = positiveInteger(value['version']);
  if (!id || responseWorkspaceId !== workspaceId || parentFolderId === undefined || !name || sortOrder === undefined || version === undefined) {
    return null;
  }
  return { id, workspaceId, parentFolderId, name, sortOrder, version };
}

function mapLocation(response: unknown, fileObjectId: string, workspaceId: string): {
  readonly fileObjectId: string;
  readonly version: number;
} {
  if (!isObject(response) || stringValue(response['fileObjectId']) !== fileObjectId ||
    stringValue(response['workspaceId']) !== workspaceId) {
    throw new Error('Invalid file location response.');
  }
  const version = nonNegativeInteger(response['version']);
  if (version === undefined) {
    throw new Error('Invalid file location version.');
  }
  return { fileObjectId, version };
}

function buildFolderTree(folders: readonly FileFolderViewModel[]): readonly FileFolderTreeNode[] {
  const byParent = new Map<string | null, FileFolderViewModel[]>();
  for (const folder of folders) {
    const siblings = byParent.get(folder.parentFolderId) ?? [];
    siblings.push(folder);
    byParent.set(folder.parentFolderId, siblings);
  }
  const visited = new Set<string>();
  const build = (parentFolderId: string | null): readonly FileFolderTreeNode[] => {
    const result: FileFolderTreeNode[] = [];
    for (const folder of byParent.get(parentFolderId) ?? []) {
      if (visited.has(folder.id)) {
        continue;
      }
      visited.add(folder.id);
      result.push({ id: folder.id, name: folder.name, children: build(folder.id) });
    }
    return result;
  };
  return build(null);
}

function isObject(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function stringValue(value: unknown): string | undefined {
  return typeof value === 'string' && value.length > 0 ? value : undefined;
}

function nullableStringValue(value: unknown): string | null | undefined {
  return value === null ? null : stringValue(value);
}

function nonNegativeInteger(value: unknown): number | undefined {
  return typeof value === 'number' && Number.isSafeInteger(value) && value >= 0 ? value : undefined;
}

function positiveInteger(value: unknown): number | undefined {
  return typeof value === 'number' && Number.isSafeInteger(value) && value > 0 ? value : undefined;
}
