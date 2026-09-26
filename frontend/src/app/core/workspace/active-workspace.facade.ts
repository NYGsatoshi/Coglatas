import { InjectionToken, inject, Injectable, signal } from '@angular/core';

export interface WorkspaceSummary {
  readonly id: string;
  readonly label: string;
  readonly description?: string | null;
}

export const COGLATAS_ACTIVE_WORKSPACE_MOCK = new InjectionToken<WorkspaceSummary | null>(
  'COGLATAS_ACTIVE_WORKSPACE_MOCK',
);

@Injectable({ providedIn: 'root' })
export class ActiveWorkspaceFacade {
  private readonly initialWorkspace = inject(COGLATAS_ACTIVE_WORKSPACE_MOCK, { optional: true }) ?? null;
  private readonly activeWorkspaceState = signal<WorkspaceSummary | null>(this.initialWorkspace);

  readonly activeWorkspace = this.activeWorkspaceState.asReadonly();

  clearWorkspace(): void {
    this.activeWorkspaceState.set(null);
  }

  setActiveWorkspace(workspace: WorkspaceSummary | null): void {
    this.activeWorkspaceState.set(workspace);
  }

  setMockWorkspace(workspace: WorkspaceSummary | null): void {
    this.setActiveWorkspace(workspace);
  }
}
