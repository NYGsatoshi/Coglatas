import { DOCUMENT } from '@angular/common';
import { ChangeDetectionStrategy, Component, Injector, afterNextRender, computed, effect, inject, signal, untracked } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, ParamMap, Router } from '@angular/router';
import { distinctUntilChanged, map } from 'rxjs';

import {
  AppDataGridActionEvent,
  AppDataGridColumnDef,
  AppDataGridRowActivationEvent,
} from '../../../shared/grid/app-data-grid/app-data-grid.types';
import { AppDataGridComponent } from '../../../shared/grid/app-data-grid/app-data-grid.component';
import { AppEmptyStateComponent } from '../../../shared/empty-state/app-empty-state/app-empty-state.component';
import { AppSkeletonComponent } from '../../../shared/loading/app-skeleton/app-skeleton.component';
import { AppPermissionDeniedComponent } from '../../../shared/permission/app-permission-denied/app-permission-denied.component';
import { CoglatasFilterChipComponent } from '../../../shared/ui/coglatas-filter-chip/coglatas-filter-chip.component';
import { AdminFacade } from '../admin.facade';
import { AuditDetailDrawerComponent } from '../audit-detail-drawer/audit-detail-drawer.component';
import {
  AuditFilterSnapshot,
  AuditGridRow,
  AuditLogViewModel,
  AuditSavedView,
  AuditSeverityFilter,
  AuditStatusFilter,
  AuditTimeRange,
  EMPTY_AUDIT_FILTERS,
} from '../admin.types';
import {
  AuditViewPreferenceService,
  normalizeAuditFilterSnapshot,
} from '../audit-view-preference.service';

type AuditGridOptionalColumn = 'workspace' | 'requestId';
type AuditGridDensity = 'default' | 'dense';

interface DrawerScrollPosition {
  readonly host: HTMLElement;
  readonly left: number;
  readonly top: number;
}

interface DrawerReturnContext {
  readonly auditId: string;
  readonly target: HTMLElement;
  readonly positions: readonly DrawerScrollPosition[];
}

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-audit-log-page',
  standalone: true,
  imports: [
    AppDataGridComponent,
    AppEmptyStateComponent,
    AppSkeletonComponent,
    AppPermissionDeniedComponent,
    AuditDetailDrawerComponent,
    CoglatasFilterChipComponent,
    FormsModule,
  ],
  templateUrl: './audit-log-page.component.html',
  styleUrl: './audit-log-page.component.scss',
})
export class AuditLogPageComponent {
  private readonly facade = inject(AdminFacade);
  private readonly viewPreferences = inject(AuditViewPreferenceService);
  private readonly document = inject(DOCUMENT);
  private readonly injector = inject(Injector);
  private readonly route = inject(ActivatedRoute, { optional: true });
  private readonly router = inject(Router, { optional: true });
  private readonly routeEventId = this.route
    ? toSignal(
        this.route.queryParamMap.pipe(
          map((params) => params.get('event')),
          distinctUntilChanged(),
        ),
        { initialValue: this.route.snapshot.queryParamMap.get('event') },
      )
    : signal<string | null>(null);
  private readonly routeFilters = this.route
    ? toSignal(
        this.route.queryParamMap.pipe(map((params) => auditFiltersFromParams(params))),
        { initialValue: auditFiltersFromParams(this.route.snapshot.queryParamMap) },
      )
    : signal<AuditFilterSnapshot>(EMPTY_AUDIT_FILTERS);
  private routeSelectionInitialized = false;
  private routeFilterSignature = '';
  private readonly selectedAuditId = signal<string | null>(null);
  private retryFocusRestorePending = false;
  private readonly visibleOptionalColumns = signal<ReadonlySet<AuditGridOptionalColumn>>(new Set());
  // This is retained across a Back -> Forward round trip for the same event.
  // That lets an explicit Close after Forward return to the original row while
  // still refusing to focus an element that was virtualized away.
  private readonly drawerReturnContext = signal<DrawerReturnContext | null>(null);
  readonly density = signal<AuditGridDensity>('default');
  readonly savedViews = signal<readonly AuditSavedView[]>([]);
  readonly filterMessage = signal('');
  searchDraft = '';
  severityDraft: AuditSeverityFilter = '';
  typeDraft = '';
  actorDraft = '';
  sourceDraft = '';
  statusDraft: AuditStatusFilter = '';
  rangeDraft: AuditTimeRange = '';
  savedViewName = '';
  selectedSavedViewId = '';
  readonly optionalColumns = [
    { id: 'workspace' as const, label: 'Workspace' },
    { id: 'requestId' as const, label: 'Request ID' },
  ];

  readonly vm = computed(() => this.withColumns(this.facade.getAuditLog()));
  readonly auditDetail = computed(() => this.facade.getAuditDetail());
  readonly auditCapabilities = computed(() => this.facade.getAuditCapabilities());
  readonly sensitiveMetadata = computed(() => this.facade.getAuditSensitiveMetadata());
  readonly selectedAudit = computed(() => this.auditDetail().row);
  readonly drawerOpen = computed(() => this.selectedAuditId() !== null);
  readonly activeFilterChips = computed(() => describeAuditFilterChips(this.vm().appliedFilters));
  readonly hasActiveFilters = computed(() => this.activeFilterChips().length > 0);
  readonly accessibilityStatus = computed(() =>
    this.describeAuditStatus(this.vm(), this.density(), this.visibleOptionalColumns()),
  );

  constructor() {
    const initialFilters = this.routeFilters();
    this.routeFilterSignature = JSON.stringify(initialFilters);
    this.setDraftFilters(initialFilters);
    this.facade.initializeAuditLog(initialFilters);

    effect(() => {
      const eventId = this.routeEventId();
      untracked(() => this.syncSelectionFromRoute(eventId));
    });

    effect(() => {
      const filters = this.routeFilters();
      const signature = JSON.stringify(filters);
      untracked(() => {
        if (signature === this.routeFilterSignature) {return;}
        this.routeFilterSignature = signature;
        this.setDraftFilters(filters);
        this.facade.applyAuditFilters(filters);
      });
    });

    effect(() => {
      this.viewPreferences.identityKey();
      untracked(() => this.reloadSavedViews());
    });

    effect(() => {
      const page = this.vm();
      if (page.status !== 'permissionDenied' || describeAuditFilterChips(page.appliedFilters).length > 0) {return;}
      untracked(() => {
        this.setDraftFilters(EMPTY_AUDIT_FILTERS);
        this.routeFilterSignature = JSON.stringify(EMPTY_AUDIT_FILTERS);
        void this.updateRouteFilters(EMPTY_AUDIT_FILTERS);
      });
    });

    effect(() => {
      const page = this.vm();
      if (!this.retryFocusRestorePending || page.loadPhase === 'retry') {
        return;
      }

      this.retryFocusRestorePending = false;
      afterNextRender(
        {
          write: () => {
            queueMicrotask(() => {
              const retry = this.document.querySelector<HTMLElement>('[data-testid="audit-log-retry"]');
              if (retry === this.document.activeElement) {
                return;
              }

              this.focusPageTitle();
            });
          },
        },
        { injector: this.injector },
      );
    });
  }

  handleGridAction(event: AppDataGridActionEvent<AuditGridRow>): void {
    if (event.actionId === 'openAuditDetail') {
      this.openAuditDetail(event.row.id, event.trigger ?? null);
    }
  }

  applyFilters(): void {
    const snapshot = normalizeAuditFilterSnapshot(this.draftFilters());
    if (!snapshot) {
      this.filterMessage.set('Check the filter lengths and try again.');
      return;
    }
    this.setDraftFilters(snapshot);
    this.filterMessage.set('Filters applied. Results were reauthorized by the server.');
    this.routeFilterSignature = JSON.stringify(snapshot);
    this.facade.applyAuditFilters(snapshot);
    void this.updateRouteFilters(snapshot);
  }

  removeFilter(key: keyof AuditFilterSnapshot): void {
    switch (key) {
      case 'q': this.searchDraft = ''; break;
      case 'severity': this.severityDraft = ''; break;
      case 'type': this.typeDraft = ''; break;
      case 'actor': this.actorDraft = ''; break;
      case 'source': this.sourceDraft = ''; break;
      case 'status': this.statusDraft = ''; break;
      case 'range': this.rangeDraft = ''; break;
    }
    this.applyFilters();
  }

  clearAllFilters(): void {
    this.setDraftFilters(EMPTY_AUDIT_FILTERS);
    this.applyFilters();
  }

  saveCurrentView(): void {
    const snapshot = normalizeAuditFilterSnapshot(this.vm().appliedFilters);
    if (!snapshot) {
      this.filterMessage.set('Check the filter lengths before saving this view.');
      return;
    }
    const result = this.viewPreferences.save(this.savedViewName, snapshot);
    this.savedViews.set(result.views);
    if (result.status === 'ready') {
      this.savedViewName = '';
      this.filterMessage.set('Saved view updated. Applying it will reauthorize the query.');
      return;
    }
    this.filterMessage.set(savedViewStatusMessage(result.status));
  }

  applySelectedView(): void {
    const view = this.savedViews().find((candidate) => candidate.id === this.selectedSavedViewId);
    if (!view) {
      this.filterMessage.set('Choose a saved view first.');
      return;
    }
    this.setDraftFilters(view.snapshot);
    this.filterMessage.set(`Applied saved view ${view.name}. Results were reauthorized by the server.`);
    this.routeFilterSignature = JSON.stringify(view.snapshot);
    this.facade.applyAuditFilters(view.snapshot);
    void this.updateRouteFilters(view.snapshot);
  }

  deleteSelectedView(): void {
    if (!this.selectedSavedViewId) {return;}
    const result = this.viewPreferences.delete(this.selectedSavedViewId);
    this.savedViews.set(result.views);
    if (result.status === 'ready') {
      this.selectedSavedViewId = '';
      this.filterMessage.set('Saved view deleted.');
      return;
    }
    this.filterMessage.set(savedViewStatusMessage(result.status));
  }

  private draftFilters(): AuditFilterSnapshot {
    return {
      q: this.searchDraft,
      severity: this.severityDraft,
      type: this.typeDraft,
      actor: this.actorDraft,
      source: this.sourceDraft,
      status: this.statusDraft,
      range: this.rangeDraft,
    };
  }

  private setDraftFilters(filters: AuditFilterSnapshot): void {
    this.searchDraft = filters.q;
    this.severityDraft = filters.severity;
    this.typeDraft = filters.type;
    this.actorDraft = filters.actor;
    this.sourceDraft = filters.source;
    this.statusDraft = filters.status;
    this.rangeDraft = filters.range;
  }

  private reloadSavedViews(): void {
    const result = this.viewPreferences.load();
    this.savedViews.set(result.views);
    if (result.status === 'discarded') {
      this.filterMessage.set('An invalid saved-view record was discarded.');
    }
  }

  private async updateRouteFilters(filters: AuditFilterSnapshot): Promise<void> {
    if (!this.router || !this.route) {return;}
    await this.router.navigate([], {
      relativeTo: this.route,
      queryParams: {
        q: filters.q || null,
        severity: filters.severity || null,
        type: filters.type || null,
        actor: filters.actor || null,
        source: filters.source || null,
        status: filters.status || null,
        range: filters.range || null,
      },
      queryParamsHandling: 'merge',
    });
  }

  handleGridRowActivation(event: AppDataGridRowActivationEvent<AuditGridRow>): void {
    this.openAuditDetail(event.row.id, event.trigger ?? null);
  }

  openMobileDetail(row: AuditGridRow, trigger: HTMLElement): void {
    this.openAuditDetail(row.id, trigger);
  }

  retry(): void {
    const page = this.vm();
    if (!page.canRetry || page.loadPhase === 'retry') {
      return;
    }

    this.retryFocusRestorePending = this.document.activeElement
      === this.document.querySelector<HTMLElement>('[data-testid="audit-log-retry"]');
    this.facade.reloadAuditLog();
    if (this.vm().loadPhase !== 'retry') {
      this.retryFocusRestorePending = false;
    }
  }

  closeDrawer(): void {
    const hadSelection = this.selectedAuditId() !== null;
    this.selectedAuditId.set(null);
    this.facade.clearAuditDetail();
    if (hadSelection) {
      this.restoreDrawerFocus();
    }
    void this.updateRouteSelection(null, true);
  }

  toggleSensitiveMetadata(): void {
    const auditId = this.selectedAuditId();
    if (!auditId) {
      return;
    }

    const metadata = this.sensitiveMetadata();
    if (metadata.status !== 'hidden') {
      this.facade.hideAuditSensitiveMetadata();
      return;
    }

    this.facade.revealAuditSensitiveMetadata(auditId);
  }

  isColumnVisible(column: AuditGridOptionalColumn): boolean {
    return this.visibleOptionalColumns().has(column);
  }

  toggleColumn(column: AuditGridOptionalColumn, visible: boolean): void {
    this.visibleOptionalColumns.update((current) => {
      const next = new Set(current);
      if (visible) {
        next.add(column);
      } else {
        next.delete(column);
      }
      return next;
    });
  }

  setDensity(density: AuditGridDensity): void {
    this.density.set(density);
  }

  private withColumns(vm: AuditLogViewModel): AuditLogViewModel {
    return {
      ...vm,
      columns: this.columns
    };
  }

  private get columns(): readonly AppDataGridColumnDef<AuditGridRow>[] {
    const visible = this.visibleOptionalColumns();
    const columns: AppDataGridColumnDef<AuditGridRow>[] = [
    { field: 'createdAt', headerName: 'Created', minWidth: 150, flex: 0.8, sortable: true },
    { field: 'action', headerName: 'Action', minWidth: 190, flex: 1.1, sortable: true, wrapText: true, autoHeight: true },
    { field: 'actorDisplay', headerName: 'Actor', minWidth: 160, flex: 0.9, sortable: true },
    { field: 'targetType', headerName: 'Target', minWidth: 140, flex: 0.8, sortable: true },
    {
      field: 'severity',
      headerName: 'Severity',
      minWidth: 120,
      flex: 0.7,
      sortable: true,
      valueFormatter: (params) => params.data?.severityLabel ?? '',
      cellRenderer: (params: { data?: AuditGridRow }) => this.renderBadge(params.data?.severityLabel ?? '')
    },
    {
      field: 'result',
      headerName: 'Result',
      minWidth: 120,
      flex: 0.7,
      sortable: true,
      valueFormatter: (params) => params.data?.resultLabel ?? '',
      cellRenderer: (params: { data?: AuditGridRow }) => this.renderBadge(params.data?.resultLabel ?? '')
    },
    {
      field: 'summary',
      headerName: 'Summary',
      minWidth: 260,
      flex: 1.5,
      sortable: false,
      wrapText: true,
      autoHeight: true,
      actions: (row) => [{ id: 'openAuditDetail', label: row.summary, row }],
      cellRenderer: (params: { data?: AuditGridRow }) => this.renderDetailButton(params.data)
    },
    ];

    if (visible.has('workspace')) {
      columns.splice(4, 0, { field: 'workspace', headerName: 'Workspace', minWidth: 180, flex: 1, sortable: true, wrapText: true });
    }
    if (visible.has('requestId')) {
      columns.push({ field: 'requestId', headerName: 'Request ID', minWidth: 160, flex: 0.9, sortable: true, wrapText: true });
    }

    return columns;
  }

  private openAuditDetail(auditId: string, trigger: HTMLElement | null): void {
    if (trigger) {
      this.drawerReturnContext.set({
        auditId,
        target: trigger,
        positions: this.captureScrollPositions(trigger),
      });
    } else if (this.drawerReturnContext()?.auditId !== auditId) {
      this.drawerReturnContext.set(null);
    }
    this.selectedAuditId.set(auditId);
    this.facade.selectAuditDetail(auditId);
    void this.updateRouteSelection(auditId, false);
  }

  private syncSelectionFromRoute(auditId: string | null): void {
    if (!this.routeSelectionInitialized) {
      this.routeSelectionInitialized = true;
      if (auditId) {
        this.selectedAuditId.set(auditId);
        this.facade.selectAuditDetail(auditId);
      }
      return;
    }

    const previousAuditId = this.selectedAuditId();
    this.selectedAuditId.set(auditId);
    if (auditId) {
      if (this.drawerReturnContext()?.auditId !== auditId) {
        this.drawerReturnContext.set(null);
      }
      this.facade.selectAuditDetail(auditId);
      return;
    }

    this.facade.clearAuditDetail();
    if (previousAuditId) {
      this.restoreDrawerFocus({ preserveForHistory: true });
    }
  }

  private async updateRouteSelection(auditId: string | null, replaceUrl: boolean): Promise<void> {
    if (!this.router || !this.route) {
      return;
    }

    await this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { event: auditId },
      queryParamsHandling: 'merge',
      replaceUrl,
    });
  }

  private restoreDrawerFocus({ preserveForHistory = false }: { preserveForHistory?: boolean } = {}): void {
    const context = this.drawerReturnContext();
    if (!preserveForHistory) {
      this.drawerReturnContext.set(null);
    }
    // The drawer is conditionally removed by the same state change. Restore
    // only after that render so a direct deep link or disconnected virtual row
    // has a stable in-page fallback instead of retaining focus on Close.
    afterNextRender(
      {
        write: () => {
          this.restoreScrollPositions(context?.positions ?? []);
          queueMicrotask(() => this.finishDrawerFocusRestore(context, preserveForHistory));
        },
      },
      { injector: this.injector },
    );
  }

  private captureScrollPositions(trigger: HTMLElement): readonly DrawerScrollPosition[] {
    const positions: DrawerScrollPosition[] = [];
    const seen = new Set<HTMLElement>();
    const remember = (host: HTMLElement | null): void => {
      if (!host || seen.has(host) || !this.isScrollable(host)) {
        return;
      }

      seen.add(host);
      positions.push({ host, left: host.scrollLeft, top: host.scrollTop });
    };

    for (let current = trigger.parentElement; current; current = current.parentElement) {
      remember(current);
    }
    remember(this.document.getElementById('app-shell-main-content'));
    remember(this.document.scrollingElement instanceof HTMLElement ? this.document.scrollingElement : null);

    return positions;
  }

  private restoreScrollPositions(positions: readonly DrawerScrollPosition[]): void {
    // Restore outer surfaces first, then the grid's own viewport. This keeps a
    // virtualized row in the same visual context before we validate its ID.
    for (const position of [...positions].reverse()) {
      if (!position.host.isConnected || typeof position.host.scrollTo !== 'function') {
        continue;
      }

      try {
        position.host.scrollTo({ left: position.left, top: position.top, behavior: 'auto' });
      } catch {
        // JSDOM and embedded webviews may expose a non-implemented scrollTo.
        // Focus restoration remains valid without a programmatic scroll.
      }
    }
  }

  private isScrollable(host: HTMLElement): boolean {
    return host.scrollHeight > host.clientHeight + 1 || host.scrollWidth > host.clientWidth + 1;
  }

  private finishDrawerFocusRestore(
    context: DrawerReturnContext | null,
    preserveForHistory: boolean,
  ): void {
    const target = context ? this.resolveReturnFocusTarget(context) : null;
    if (target) {
      target.focus({ preventScroll: true });
      return;
    }

    if (preserveForHistory && context && this.drawerReturnContext() === context) {
      this.drawerReturnContext.set(null);
    }
    this.focusPageTitle();
  }

  private resolveReturnFocusTarget(context: DrawerReturnContext): HTMLElement | null {
    if (this.isFocusableReturnTarget(context.target) && this.isReturnTargetForAudit(context.target, context.auditId)) {
      return context.target;
    }

    return Array.from(
      this.document.querySelectorAll<HTMLElement>(
        '[data-testid="audit-log-page"] button[data-grid-row-id], ' +
        '[data-testid="audit-log-page"] [tabindex][data-grid-row-id]',
      ),
    ).find((candidate) =>
      candidate.dataset['gridRowId'] === context.auditId &&
      this.isFocusableReturnTarget(candidate) &&
      this.isReturnTargetForAudit(candidate, context.auditId),
    ) ?? null;
  }

  private isFocusableReturnTarget(target: HTMLElement): boolean {
    if (target.matches(':disabled, [aria-disabled="true"]')) {
      return false;
    }

    // AG and Syncfusion grid cells are programmatic focus targets even when
    // they use tabindex="-1". Do not select arbitrary rendered text nodes:
    // calling focus on those would silently leave focus on the removed drawer.
    return target.matches('button, [href], input, select, textarea, [tabindex], .ag-cell, .e-rowcell');
  }

  private isReturnTargetForAudit(target: HTMLElement, auditId: string): boolean {
    if (!target.isConnected) {
      return false;
    }

    const agRow = target.closest<HTMLElement>('.ag-row');
    if (agRow) {
      return agRow.getAttribute('row-id') === auditId;
    }

    const syncfusionRow = target.closest<HTMLElement>('.e-row');
    if (syncfusionRow) {
      return Array.from(syncfusionRow.querySelectorAll<HTMLElement>('[data-grid-row-id]'))
        .some((element) => element.dataset['gridRowId'] === auditId);
    }

    return target.closest<HTMLElement>('[data-grid-row-id]')?.dataset['gridRowId'] === auditId;
  }

  private focusPageTitle(): void {
    this.document
      .querySelector<HTMLElement>('[data-testid="audit-log-title"]')
      ?.focus({ preventScroll: true });
  }

  private renderBadge(label: string): HTMLElement {
    const badge = document.createElement('span');
    badge.className = 'admin-grid-badge';
    badge.textContent = label.trim() || 'Unrecognized audit classification';
    return badge;
  }

  private describeAuditStatus(
    page: AuditLogViewModel,
    density: AuditGridDensity,
    visibleColumns: ReadonlySet<AuditGridOptionalColumn>,
  ): string {
    if (page.status === 'loading') {
      return page.loadPhase === 'retry' ? 'Retrying audit log.' : 'Loading audit log.';
    }
    if (page.status === 'permissionDenied') {
      return 'Audit log access is unavailable.';
    }
    if (page.status === 'error') {
      return 'Audit log could not be loaded.';
    }
    if (page.status === 'empty' || page.rows.length === 0) {
      return this.hasActiveFilters()
        ? 'No authorized audit entries match the applied filters. Clear or change a filter to recover.'
        : 'No audit entries are available for the current authorized scope.';
    }

    const count = page.totalCount;
    const optionalColumnStatus = this.optionalColumns
      .map((column) => `${column.label} ${visibleColumns.has(column.id) ? 'shown' : 'hidden'}`)
      .join('; ');
    const shown = page.rows.length === count ? '' : ` Showing the first ${page.rows.length}.`;
    return `Showing ${count} audit ${count === 1 ? 'entry' : 'entries'}. Current authorized scope.${shown} ${density === 'dense' ? 'Dense' : 'Default'} density. Optional columns: ${optionalColumnStatus}.`;
  }

  private renderDetailButton(row: AuditGridRow | undefined): HTMLElement {
    const button = document.createElement('button');
    button.type = 'button';
    button.className = 'admin-grid-link';
    button.dataset['gridAction'] = 'openAuditDetail';
    if (row) {
      button.dataset['gridRowId'] = row.id;
    }
    button.textContent = row?.summary ?? 'Open detail';
    return button;
  }
}

interface AuditFilterChipDescription {
  readonly key: keyof AuditFilterSnapshot;
  readonly label: string;
  readonly value: string;
}

function describeAuditFilterChips(filters: AuditFilterSnapshot): readonly AuditFilterChipDescription[] {
  const chips: AuditFilterChipDescription[] = [];
  if (filters.q) {chips.push({ key: 'q', label: 'Search', value: filters.q });}
  if (filters.severity) {chips.push({ key: 'severity', label: 'Severity', value: capitalize(filters.severity) });}
  if (filters.type) {chips.push({ key: 'type', label: 'Type', value: filters.type });}
  if (filters.actor) {chips.push({ key: 'actor', label: 'Actor', value: filters.actor });}
  if (filters.source) {chips.push({ key: 'source', label: 'Source', value: filters.source });}
  if (filters.status) {chips.push({ key: 'status', label: 'Status', value: capitalize(filters.status) });}
  if (filters.range) {chips.push({ key: 'range', label: 'Time range', value: timeRangeLabel(filters.range) });}
  return chips;
}

function auditFiltersFromParams(params: ParamMap): AuditFilterSnapshot {
  const candidate: AuditFilterSnapshot = {
    q: boundedParam(params.get('q'), 200),
    severity: toSeverityFilter(params.get('severity')),
    type: boundedParam(params.get('type'), 160),
    actor: boundedParam(params.get('actor'), 200),
    source: boundedParam(params.get('source'), 80),
    status: toStatusFilter(params.get('status')),
    range: toTimeRange(params.get('range')),
  };
  return normalizeAuditFilterSnapshot(candidate) ?? EMPTY_AUDIT_FILTERS;
}

function boundedParam(value: string | null, maximum: number): string {
  const normalized = value?.trim() ?? '';
  return normalized.length <= maximum ? normalized : '';
}

function toSeverityFilter(value: string | null): AuditSeverityFilter {
  return value === 'info' || value === 'warning' || value === 'critical' ? value : '';
}

function toStatusFilter(value: string | null): AuditStatusFilter {
  return value === 'success' || value === 'denied' || value === 'failed' ? value : '';
}

function toTimeRange(value: string | null): AuditTimeRange {
  return value === '24h' || value === '7d' || value === '30d' ? value : '';
}

function timeRangeLabel(value: AuditTimeRange): string {
  return value === '24h' ? 'Last 24 hours' : value === '7d' ? 'Last 7 days' : 'Last 30 days';
}

function capitalize(value: string): string {
  return value.length > 0 ? `${value[0]!.toUpperCase()}${value.slice(1)}` : value;
}

function savedViewStatusMessage(status: string): string {
  return status === 'identityUnavailable'
    ? 'Sign in to an active Tenant or platform scope to use saved views.'
    : status === 'storageUnavailable'
      ? 'Saved-view storage is unavailable in this browser.'
      : 'Enter a unique name of 1 to 80 characters. Up to 20 views are supported.';
}
