import { Injectable, InjectionToken } from '@angular/core';
import { defer, Observable, of } from 'rxjs';

import {
  copyDraft, DraftLinkPreview, EMPTY_LOCAL_DRAFT, LocalCheckpointResult,
  LocalDraftCheckpoint, LocalDraftContent, RemoteDraftAttempt
} from './local-draft.models';

export interface LocalDraftRepository {
  read(scope: string): LocalDraftCheckpoint;
  save(scope: string, expectedVersion: number, content: LocalDraftContent): Observable<LocalCheckpointResult>;
  clear(): void;
}

/** Memory only: no browser storage, network, endpoint, or production persistence. */
@Injectable({ providedIn: 'root' })
export class InMemoryLocalDraftRepository implements LocalDraftRepository {
  private readonly checkpoints = new Map<string, LocalDraftCheckpoint>();

  read(scope: string): LocalDraftCheckpoint {
    const value = this.checkpoints.get(scope) ?? { version: 0, content: EMPTY_LOCAL_DRAFT };
    return { ...value, content: copyDraft(value.content) };
  }

  save(scope: string, expectedVersion: number, content: LocalDraftContent): Observable<LocalCheckpointResult> {
    const snapshot = copyDraft(content);
    return defer(() => {
      const current = this.read(scope);
      if (current.version !== expectedVersion) {
        return of({ status: 'conflict' as const, checkpoint: current });
      }
      const checkpoint = { version: current.version + 1, content: snapshot };
      this.checkpoints.set(scope, checkpoint);
      return of({ status: 'saved' as const, checkpoint: this.read(scope) });
    });
  }

  clear(): void { this.checkpoints.clear(); }
}

export const LOCAL_DRAFT_REPOSITORY = new InjectionToken<LocalDraftRepository>('LOCAL_DRAFT_REPOSITORY', {
  providedIn: 'root', factory: () => new InMemoryLocalDraftRepository()
});

/** Future adapter seam only. A successful remote save is deliberately not representable. */
export interface RemoteDraftAdapter {
  attemptSave(content: LocalDraftContent): Observable<RemoteDraftAttempt>;
}

export const REMOTE_DRAFT_ADAPTER = new InjectionToken<RemoteDraftAdapter>('REMOTE_DRAFT_ADAPTER', {
  providedIn: 'root', factory: () => ({ attemptSave: () => of('unavailable') })
});

/** Synthetic targets are opt-in UI fixtures, never authorized product resources. */
export const DEMO_LINK_TARGETS: readonly DraftLinkPreview[] = [
  { id: 'demo-project', kind: 'Project', label: 'Demo Project — Research' },
  { id: 'demo-task', kind: 'Task', label: 'Demo Task — Review notes' },
  { id: 'demo-file', kind: 'File', label: 'Demo File — Notes.txt' }
];
