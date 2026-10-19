import { I18nService } from '../../../core/i18n/i18n.service';
import { ChangeDetectionStrategy, Component, computed, effect, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { distinctUntilChanged, map } from 'rxjs';

import {
  AppDataGridActionEvent,
  AppDataGridColumnDef
} from '../../../shared/grid/app-data-grid/app-data-grid.types';
import { AppDataGridComponent } from '../../../shared/grid/app-data-grid/app-data-grid.component';
import { AppAuditReasonDialogComponent } from '../../../shared/dialog/app-audit-reason-dialog/app-audit-reason-dialog.component';
import { AppConfirmDialogComponent } from '../../../shared/dialog/app-confirm-dialog/app-confirm-dialog.component';
import { AppEmptyStateComponent } from '../../../shared/empty-state/app-empty-state/app-empty-state.component';
import { AppInlineLoadingComponent } from '../../../shared/loading/app-inline-loading/app-inline-loading.component';
import { AppPermissionDeniedComponent } from '../../../shared/permission/app-permission-denied/app-permission-denied.component';
import { MemberRoleBadgeComponent } from '../member-role-badge/member-role-badge.component';
import { WorkspaceMembersFacade } from '../members.facade';
import { ActiveWorkspaceFacade } from '../../../core/workspace/active-workspace.facade';
import { WorkspaceMemberGridRow, WorkspaceMemberRowAction } from '../members.types';

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-workspace-members-page',
  standalone: true,
  imports: [
    FormsModule,
    AppAuditReasonDialogComponent,
    AppConfirmDialogComponent,
    AppDataGridComponent,
    AppEmptyStateComponent,
    AppInlineLoadingComponent,
    AppPermissionDeniedComponent
  ],
  templateUrl: './workspace-members-page.component.html',
  styleUrl: './workspace-members-page.component.scss',
})
export class WorkspaceMembersPageComponent {
  readonly i18n = inject(I18nService);
  private readonly facade = inject(WorkspaceMembersFacade);
  private readonly route = inject(ActivatedRoute);
  private readonly activeWorkspace = inject(ActiveWorkspaceFacade);

  readonly searchValue = signal('');
  readonly confirmAction = signal<AppDataGridActionEvent<WorkspaceMemberGridRow> | null>(null);
  readonly auditAction = signal<AppDataGridActionEvent<WorkspaceMemberGridRow> | null>(null);
  readonly lastAuditReason = signal<string | null>(null);
  private readonly workspaceIdState = signal(
    this.route.snapshot.paramMap.get('workspaceId') ?? 'workspace-alpha',
  );
  private hasObservedActiveWorkspace = false;
  private lastLoadedWorkspaceId: string | null = null;
  readonly vm = computed(() => this.withColumns(this.facade.getPage(this.workspaceIdState())));
  readonly filteredRows = computed(() => this.filterRows(this.vm().rows, this.searchValue()));

  get workspaceId(): string {
    return this.workspaceIdState();
  }

  constructor() {
    this.route.paramMap
      .pipe(
        map((paramMap) => paramMap.get('workspaceId') ?? 'workspace-alpha'),
        distinctUntilChanged(),
        takeUntilDestroyed(),
      )
      .subscribe((workspaceId) => {
        if (workspaceId !== this.workspaceIdState()) {
          this.searchValue.set('');
          this.confirmAction.set(null);
          this.auditAction.set(null);
          this.lastAuditReason.set(null);
          this.workspaceIdState.set(workspaceId);
        }
        this.loadForCommittedScope();
      });

    effect(() => {
      this.workspaceIdState();
      this.activeWorkspace.activeWorkspace();
      this.loadForCommittedScope();
    });
  }

  private loadForCommittedScope(): void {
    const workspaceId = this.workspaceIdState();
    const activeWorkspaceId = this.activeWorkspace.activeWorkspace()?.id ?? null;
    if (activeWorkspaceId) {
      this.hasObservedActiveWorkspace = true;
    }
    if (
      (activeWorkspaceId && activeWorkspaceId !== workspaceId) ||
      (!activeWorkspaceId && this.hasObservedActiveWorkspace) ||
      this.lastLoadedWorkspaceId === workspaceId
    ) {
      return;
    }

    this.lastLoadedWorkspaceId = workspaceId;
    this.facade.ensureLoaded(workspaceId);
  }

  updateSearch(value: string): void {
    this.searchValue.set(value);
  }

  handleGridAction(event: AppDataGridActionEvent<WorkspaceMemberGridRow>): void {
    if (this.facade.isDestructiveAction(event.actionId)) {
      this.confirmAction.set(event);
      return;
    }

    this.lastAuditReason.set(null);
  }

  handleMobileAction(actionId: string, row: WorkspaceMemberGridRow, trigger: HTMLElement): void {
    this.handleGridAction({ actionId, row, trigger });
  }

  retry(): void {
    this.facade.reload(this.workspaceId);
  }

  closeConfirm(): void {
    this.confirmAction.set(null);
  }

  continueToAuditReason(): void {
    const pendingAction = this.confirmAction();
    this.confirmAction.set(null);
    this.auditAction.set(pendingAction);
  }

  cancelAuditReason(): void {
    this.auditAction.set(null);
  }

  submitAuditReason(reason: string): void {
    this.lastAuditReason.set(reason);
    this.auditAction.set(null);
  }

  private withColumns(vm: ReturnType<WorkspaceMembersFacade['getPage']>): ReturnType<WorkspaceMembersFacade['getPage']> {
    return {
      ...vm,
      columns: this.columns
    };
  }

  private get columns(): readonly AppDataGridColumnDef<WorkspaceMemberGridRow>[] { return [
    {
      field: 'displayName',
      headerName: this.i18n.translate('members.name'),
      minWidth: 190,
      flex: 1.3,
      sortable: true,
      filter: false,
      wrapText: true,
      autoHeight: true,
      cellClass: 'workspace-members__display-name'
    },
    {
      field: 'roleLabel',
      headerName: this.i18n.translate('account.role'),
      minWidth: 130,
      flex: 0.8,
      sortable: true,
      filter: false,
      cellRenderer: MemberRoleBadgeComponent
    },
    {
      field: 'groupProjectLabel',
      headerName: this.i18n.translate('members.groupProject'),
      minWidth: 210,
      flex: 1.2,
      sortable: true,
      filter: false,
      wrapText: true,
      autoHeight: true
    },
    {
      field: 'accountStatusLabel',
      valueFormatter: (params: { data?: WorkspaceMemberGridRow }) => params.data ? this.statusLabel(params.data.accountStatus) : '',
      headerName: this.i18n.translate('account.status'),
      minWidth: 120,
      flex: 0.7,
      sortable: true,
      filter: false
    },
    {
      field: 'joinedAtLabel',
      valueFormatter: (params: { data?: WorkspaceMemberGridRow }) => params.data ? this.joinedAtLabel(params.data) : '',
      headerName: this.i18n.translate('members.joined'),
      minWidth: 130,
      flex: 0.7,
      sortable: true,
      filter: false
    },
    {
      colId: 'rowActions',
      headerName: this.i18n.translate('members.actions'),
      minWidth: 250,
      flex: 1.2,
      sortable: false,
      filter: false,
      actions: (row) => row.rowActions.map((action) => ({ ...action, row })),
      cellRenderer: (params: { data?: WorkspaceMemberGridRow }) => this.renderActions(params.data?.rowActions ?? [])
    }
  ]; }

  joinedAtLabel(row: WorkspaceMemberGridRow): string { return row.joinedAtUtc ? this.i18n.formatDateTime(row.joinedAtUtc, { dateStyle: 'medium' }) : row.joinedAtLabel; }

  roleLabel(role: WorkspaceMemberGridRow['role']): string { return this.i18n.translate(`members.role.${role}`); }

  statusLabel(status: WorkspaceMemberGridRow['accountStatus']): string { return this.i18n.translate(`members.status.${status}`); }

  actionLabel(action: WorkspaceMemberRowAction): string { return this.i18n.translate(`members.action.${action.id}`); }

  actionDisabledReason(action: WorkspaceMemberRowAction): string | null {
    return action.disabledReasonKey
      ? this.i18n.translate(action.disabledReasonKey)
      : action.disabledReason ?? null;
  }

  private filterRows(rows: readonly WorkspaceMemberGridRow[], searchValue: string): readonly WorkspaceMemberGridRow[] {
    const query = searchValue.trim().toLocaleLowerCase('ja-JP');
    if (!query) {
      return rows;
    }

    return rows.filter((row) =>
      [row.displayName, row.role, this.roleLabel(row.role), row.groupProjectLabel, row.accountStatusLabel, row.joinedAtLabel]
        .join(' ')
        .toLocaleLowerCase('ja-JP')
        .includes(query)
    );
  }

  private renderActions(actions: readonly WorkspaceMemberRowAction[]): HTMLElement {
    const container = document.createElement('div');
    container.className = 'app-grid-actions';

    if (actions.length === 0) {
      const empty = document.createElement('span');
      empty.className = 'app-grid-actions__empty';
      empty.textContent = this.i18n.translate('members.noActions');
      container.append(empty);
      return container;
    }

    actions.forEach((action) => {
      const button = document.createElement('button');
      button.type = 'button';
      button.className = action.destructive
        ? 'app-grid-actions__button app-grid-actions__button--danger'
        : 'app-grid-actions__button';
      button.dataset['gridAction'] = action.id;
      button.textContent = this.actionLabel(action);
      button.setAttribute('aria-disabled', String(action.disabled));
      const disabledReason = this.actionDisabledReason(action);
      if (disabledReason) {
        button.title = disabledReason;
      }
      container.append(button);
    });

    return container;
  }
}
