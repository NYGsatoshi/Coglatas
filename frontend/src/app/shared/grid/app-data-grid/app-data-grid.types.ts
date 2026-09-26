export const APP_DATA_GRID_DEFAULT_PAGE_SIZE = 50;
export const APP_DATA_GRID_MAXIMUM_PAGE_SIZE = 100;

export interface AppDataGridActionEvent<TData> {
  readonly actionId: string;
  readonly row: TData;
  readonly trigger?: HTMLElement;
}

/**
 * A row activation is separate from selection. Adapters provide the focusable
 * trigger when available so a non-modal inspector can return focus safely.
 */
export interface AppDataGridRowActivationEvent<TData> {
  readonly row: TData;
  readonly trigger?: HTMLElement;
}

export type AppDataGridSelectionMode = 'none' | 'single' | 'multiple';
export type AppDataGridMigrationTarget = 'workspace-members' | 'admin-audit-log' | 'files' | 'admin-invites';

export interface AppDataGridPageChange {
  readonly page: number;
  readonly pageSize: number;
}

export interface AppDataGridSortChange {
  readonly columnId: string;
  readonly direction: 'ascending' | 'descending' | null;
}

export interface AppDataGridFilterChange {
  readonly columnId: string;
  readonly value: string | null;
}

export interface AppDataGridSelectionChange<TData> {
  readonly rows: readonly TData[];
}

export interface AppDataGridRowAction<TData> {
  readonly id: string;
  readonly label: string;
  readonly disabled?: boolean;
  readonly disabledReason?: string;
  readonly destructive?: boolean;
  readonly row: TData;
}

/**
 * Coglatas-owned input supplied to column formatters and value readers.  This
 * deliberately has no vendor event or row-node shape: adapters map it to
 * their own callback model at the boundary.
 */
export interface AppDataGridCellValueContext<TData> {
  readonly data?: TData;
  readonly value?: unknown;
}

export interface AppDataGridColumnDef<TData> {
  readonly colId?: string;
  readonly field?: keyof TData & string;
  readonly headerName: string;
  readonly minWidth?: number;
  readonly maxWidth?: number;
  readonly flex?: number;
  readonly sortable?: boolean;
  readonly filter?: boolean | 'text';
  readonly wrapText?: boolean;
  readonly autoHeight?: boolean;
  readonly cellClass?: string | string[];
  readonly valueGetter?: (params: AppDataGridCellValueContext<TData>) => unknown;
  readonly valueFormatter?: (params: AppDataGridCellValueContext<TData>) => string;
  /**
   * Coglatas-owned action description. The Syncfusion adapter maps this to its
   * own template while the retained AG Grid fallback continues to support the
   * legacy cellRenderer callback during the rollback window.
   */
  readonly actions?: (row: TData) => readonly AppDataGridRowAction<TData>[];
  /** Adapter-owned renderer token or callback. Feature code never imports a vendor renderer type. */
  readonly cellRenderer?: unknown;
}

export const clampAppDataGridPageSize = (
  requestedPageSize: number,
  maximumPageSize = APP_DATA_GRID_MAXIMUM_PAGE_SIZE
): number => {
  const boundedMaximum = Math.max(1, Math.min(maximumPageSize, APP_DATA_GRID_MAXIMUM_PAGE_SIZE));
  return Math.max(1, Math.min(requestedPageSize, boundedMaximum));
};
