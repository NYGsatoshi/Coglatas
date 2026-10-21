import {
  AfterViewInit,
  ChangeDetectionStrategy,
  Component,
  ComponentRef,
  EventEmitter,
  Input,
  OnChanges,
  OnDestroy,
  OnInit,
  Output,
  SimpleChanges,
  ViewChild,
  ViewContainerRef,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { AgGridAngular } from 'ag-grid-angular';
import {
  AllCommunityModule,
  CellClickedEvent,
  ColDef,
  GridOptions,
  Module,
  ModuleRegistry
} from 'ag-grid-community';

import { FrontendFeatureFlagsService } from '../../../core/feature-flags/frontend-feature-flags.service';
import { I18nService } from '../../../core/i18n/i18n.service';
import {
  APP_DATA_GRID_DEFAULT_PAGE_SIZE,
  APP_DATA_GRID_MAXIMUM_PAGE_SIZE,
  AppDataGridActionEvent,
  AppDataGridColumnDef,
  AppDataGridFilterChange,
  AppDataGridMigrationTarget,
  AppDataGridPageChange,
  AppDataGridRowActivationEvent,
  AppDataGridSelectionChange,
  AppDataGridSelectionMode,
  AppDataGridSortChange,
  clampAppDataGridPageSize
} from './app-data-grid.types';
import type { SyncfusionDataGridComponent } from '../../ui/adapters/syncfusion/syncfusion-data-grid.component';

ModuleRegistry.registerModules([AllCommunityModule]);

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-data-grid',
  standalone: true,
  imports: [AgGridAngular],
  templateUrl: './app-data-grid.component.html',
  styleUrl: './app-data-grid.component.scss',
})
export class AppDataGridComponent<TData extends object> implements OnInit, AfterViewInit, OnChanges, OnDestroy {
  private readonly flags = inject(FrontendFeatureFlagsService);
  protected readonly i18n = inject(I18nService);
  @ViewChild('syncfusionHost', { read: ViewContainerRef }) private syncfusionHost?: ViewContainerRef;
  @ViewChild(AgGridAngular) private agGrid?: AgGridAngular<TData>;

  @Input() rows: readonly TData[] = [];
  @Input() columns: readonly AppDataGridColumnDef<TData>[] = [];
  @Input() loading = false;
  @Input() defaultPageSize = APP_DATA_GRID_DEFAULT_PAGE_SIZE;
  @Input() maximumPageSize = APP_DATA_GRID_MAXIMUM_PAGE_SIZE;
  /** Disable local paging when a feature already supplies one server page. */
  @Input() pagination = true;
  @Input() rowIdField: keyof TData & string = 'id' as keyof TData & string;
  @Input({ required: true }) ariaLabel = '';
  /** Target-limited internal migration switch; feature code remains vendor-neutral. */
  @Input() migrationTarget?: AppDataGridMigrationTarget;
  @Input() selectionMode: AppDataGridSelectionMode = 'none';
  @Input() rowHeight?: number;
  /** Adapter-neutral request for a sticky header in long, in-page grids. */
  @Input() stickyHeader = false;
  @Input() page = 1;
  @Input() error: string | null = null;
  @Input() emptyState: string | null = null;
  @Input() permissionDenied = false;
  @Output() actionInvoked = new EventEmitter<AppDataGridActionEvent<TData>>();
  @Output() rowActivated = new EventEmitter<AppDataGridRowActivationEvent<TData>>();
  @Output() selectionChanged = new EventEmitter<AppDataGridSelectionChange<TData>>();
  @Output() pageChanged = new EventEmitter<AppDataGridPageChange>();
  @Output() sortChanged = new EventEmitter<AppDataGridSortChange>();
  @Output() filterChanged = new EventEmitter<AppDataGridFilterChange>();

  readonly syncfusionRequested = signal(false);
  readonly syncfusionLoadError = signal(false);
  readonly agGridLocaleText = computed(() => this.i18n.agGridLocaleText());
  private syncfusionComponent?: ComponentRef<SyncfusionDataGridComponent<TData>>;
  private viewReady = false;
  private readonly localizedAdapterInputs = effect(() => {
    this.i18n.locale();
    this.updateSyncfusionInputs();
    this.refreshAgGridLocale();
  });

  readonly modules: Module[] = [AllCommunityModule];
  readonly defaultColDef: ColDef<TData> = {
    resizable: true,
    sortable: true,
    filter: false,
    suppressHeaderMenuButton: true,
    wrapHeaderText: true,
    autoHeaderHeight: true
  };
  // Keep these references stable. Recreating GridOptions on every Angular
  // change-detection pass causes AG Grid to reprocess its configuration while
  // a feature updates unrelated local state such as density or columns.
  private readonly autoHeightGridOptions: GridOptions<TData> = {
    theme: 'legacy',
    rowModelType: 'clientSide',
    domLayout: 'autoHeight',
    suppressContextMenu: true,
    suppressCellFocus: false,
    enableCellTextSelection: true,
    ensureDomOrder: true,
    suppressCsvExport: true
  };
  private readonly stickyHeaderGridOptions: GridOptions<TData> = {
    ...this.autoHeightGridOptions,
    // A sticky audit header needs a bounded adapter scroll surface. With
    // autoHeight, an overflow-x ancestor prevents CSS sticky from following
    // document scroll; normal layout keeps the native AG header fixed while
    // its rows scroll beneath it instead.
    domLayout: 'normal',
  };

  get gridOptions(): GridOptions<TData> {
    return this.stickyHeader ? this.stickyHeaderGridOptions : this.autoHeightGridOptions;
  }

  get columnDefs(): ColDef<TData>[] {
    return this.columns.map((column) => ({
      ...column,
      valueGetter: column.valueGetter
        ? (params) => column.valueGetter?.({ data: params.data })
        : undefined,
      valueFormatter: column.valueFormatter
        ? (params) => column.valueFormatter?.({ data: params.data, value: params.value })
        : undefined,
      // Existing feature-owned renderers remain authoritative. The generic
      // vendor-neutral action renderer is only the fallback for columns that
      // declare action metadata without their own renderer (Files #337).
      cellRenderer: column.cellRenderer ?? (column.actions
        ? (params: { data?: TData }) => this.renderActions(column, params.data)
        : undefined)
    })) as unknown as ColDef<TData>[];
  }

  get rowSelection(): GridOptions<TData>['rowSelection'] {
    if (this.selectionMode === 'none') {
      return undefined;
    }

    return this.selectionMode === 'multiple'
      ? { mode: 'multiRow', checkboxes: true, headerCheckbox: true, enableClickSelection: false }
      : { mode: 'singleRow', checkboxes: true, enableClickSelection: false };
  }

  get rowData(): TData[] {
    return [...this.rows];
  }

  get boundedPageSize(): number {
    return clampAppDataGridPageSize(this.defaultPageSize, this.maximumPageSize);
  }

  get pageSizeOptions(): number[] {
    return [this.boundedPageSize, this.maximumPageSize]
      .map((size) => clampAppDataGridPageSize(size, this.maximumPageSize))
      .filter((size, index, sizes) => sizes.indexOf(size) === index);
  }

  get maximumPageSizeLabel(): string {
    return this.i18n.formatNumber(Math.min(this.maximumPageSize, APP_DATA_GRID_MAXIMUM_PAGE_SIZE));
  }

  getRowId = (params: { data: TData }): string => String(params.data[this.rowIdField]);

  ngOnInit(): void {
    this.syncfusionRequested.set(
      this.migrationTarget !== undefined && this.flags.syncfusionGridEnabled()
    );
  }

  ngAfterViewInit(): void {
    this.viewReady = true;
    this.refreshAgGridLocale();
    if (this.syncfusionRequested()) {
      void this.loadSyncfusionAdapter();
    }
  }

  ngOnChanges(_: SimpleChanges): void {
    this.updateSyncfusionInputs();
  }

  ngOnDestroy(): void {
    this.syncfusionComponent?.destroy();
  }

  handleCellClicked(event: CellClickedEvent<TData>): void {
    const target = event.event?.target;
    if (!(target instanceof HTMLElement) || !event.data) {
      return;
    }

    const actionTarget = target.closest<HTMLElement>('[data-grid-action]');
    const actionId = actionTarget?.dataset['gridAction'];
    if (actionTarget) {
      if (actionId && actionTarget.getAttribute('aria-disabled') !== 'true') {
        this.actionInvoked.emit({ actionId, row: event.data, trigger: actionTarget });
      }
      return;
    }

    const trigger = target.closest<HTMLElement>('.ag-cell') ?? target;
    this.rowActivated.emit({ row: event.data, trigger });
  }

  handleSelectionChanged(event: { api: { getSelectedRows(): TData[] } }): void {
    if (this.selectionMode === 'none') {
      return;
    }
    this.selectionChanged.emit({ rows: event.api.getSelectedRows() });
  }

  /** Adapter-neutral imperative reset for feature-owned contextual toolbars. */
  clearSelection(): void {
    this.agGrid?.api.deselectAll();
    this.syncfusionComponent?.instance.clearSelection();
  }

  /** Select every row currently rendered by the adapter. */
  selectAllRows(): void {
    if (this.selectionMode !== 'multiple') {
      return;
    }

    this.agGrid?.api.selectAll();
    this.syncfusionComponent?.instance.selectAllRows();
  }

  private renderActions(column: AppDataGridColumnDef<TData>, row: TData | undefined): HTMLElement | string {
    if (!row || !column.actions) {
      return '';
    }

    const actions = column.actions(row);
    const container = document.createElement('div');
    container.className = 'app-grid-actions';

    if (actions.length === 0) {
      const empty = document.createElement('span');
      empty.className = 'app-grid-actions__empty';
      empty.textContent = this.i18n.translate('grid.noActions');
      container.append(empty);
      return container;
    }

    for (const action of actions) {
      const button = document.createElement('button');
      button.type = 'button';
      button.className = action.destructive
        ? 'app-grid-actions__button app-grid-actions__button--danger'
        : 'app-grid-actions__button';
      button.textContent = action.label;
      button.dataset['gridAction'] = action.id;
      button.dataset['gridActionRowId'] = String(row[this.rowIdField]);
      button.setAttribute('aria-label', action.label);
      button.setAttribute('aria-disabled', action.disabled ? 'true' : 'false');
      // The grid owns cell keys; action buttons own their activation keys.
      button.addEventListener('keydown', (event) => {
        if (event.key !== 'Enter' && event.key !== ' ') { return; }
        event.preventDefault();
        event.stopPropagation();
        if (!event.repeat && !event.isComposing && event.keyCode !== 229 && button.getAttribute('aria-disabled') !== 'true') {
          this.actionInvoked.emit({ actionId: action.id, row, trigger: button });
        }
      });
      if (action.disabledReason) {
        button.title = action.disabledReason;
      }
      container.append(button);
    }

    return container;
  }

  private async loadSyncfusionAdapter(): Promise<void> {
    if (!this.viewReady || this.syncfusionComponent || !this.syncfusionHost) {
      return;
    }

    try {
      const { SyncfusionDataGridComponent } = await import('../../ui/adapters/syncfusion/syncfusion-data-grid.component');
      if (!this.syncfusionHost) {
        return;
      }

      const component = this.syncfusionHost.createComponent(SyncfusionDataGridComponent) as unknown as ComponentRef<SyncfusionDataGridComponent<TData>>;
      this.syncfusionComponent = component;
      this.syncfusionComponent.instance.actionInvoked.subscribe((event) => this.actionInvoked.emit(event));
      this.syncfusionComponent.instance.rowActivated.subscribe((event) => this.rowActivated.emit(event));
      this.syncfusionComponent.instance.selectionChanged.subscribe((event) => this.selectionChanged.emit(event));
      this.syncfusionComponent.instance.pageChanged.subscribe((event) => this.pageChanged.emit(event));
      this.syncfusionComponent.instance.sortChanged.subscribe((event) => this.sortChanged.emit(event));
      this.syncfusionComponent.instance.filterChanged.subscribe((event) => this.filterChanged.emit(event));
      this.updateSyncfusionInputs();
    } catch {
      // A flagged vendor adapter must never silently become the AG Grid fallback.
      this.syncfusionLoadError.set(true);
    }
  }

  private updateSyncfusionInputs(): void {
    const component = this.syncfusionComponent;
    if (!component) {
      return;
    }

    component.setInput('rows', this.rows);
    component.setInput('columns', this.columns);
    component.setInput('loading', this.loading);
    component.setInput('defaultPageSize', this.defaultPageSize);
    component.setInput('maximumPageSize', this.maximumPageSize);
    component.setInput('pagination', this.pagination);
    component.setInput('rowIdField', this.rowIdField);
    component.setInput('ariaLabel', this.ariaLabel);
    component.setInput('selectionMode', this.selectionMode);
    component.setInput('rowHeight', this.rowHeight);
    component.setInput('stickyHeader', this.stickyHeader);
    component.setInput('page', this.page);
    component.setInput('error', this.error);
    component.setInput('emptyState', this.emptyState);
    component.setInput('permissionDenied', this.permissionDenied);
    component.setInput('locale', this.i18n.syncfusionLocale());
    component.setInput('loadingLabel', this.i18n.translate('grid.loading', { label: this.ariaLabel }));
    component.setInput('permissionDeniedLabel', this.i18n.translate('grid.permissionDenied'));
    component.setInput('emptyLabel', this.i18n.translate('grid.empty'));
  }

  private refreshAgGridLocale(): void {
    this.agGrid?.api.refreshHeader();
    this.agGrid?.api.refreshCells({ force: true });
  }
}
