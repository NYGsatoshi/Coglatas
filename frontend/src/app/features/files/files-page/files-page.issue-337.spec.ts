import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';

import { AppDataGridComponent } from '../../../shared/grid/app-data-grid/app-data-grid.component';
import { COGLATAS_FILES_PAGE_MOCK } from '../files.facade';
import { DEFAULT_FILES, FILES_PAGE_SCENARIOS } from '../files.mock';
import { FilesPageViewModel, FileViewModel } from '../files.types';
import { FilesPageComponent } from './files-page.component';

const renderFilesPage = async (
  page: FilesPageViewModel = FILES_PAGE_SCENARIOS.default,
): Promise<ComponentFixture<FilesPageComponent>> => {
  await TestBed.configureTestingModule({
    imports: [FilesPageComponent],
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: COGLATAS_FILES_PAGE_MOCK, useValue: page },
    ],
  }).compileComponents();

  const fixture = TestBed.createComponent(FilesPageComponent);
  fixture.detectChanges();
  return fixture;
};

describe('FilesPageComponent issue #337', () => {
  beforeEach(() => window.localStorage.setItem('coglatas.locale', 'en'));

  afterEach(() => {
    window.localStorage.removeItem('coglatas.locale');
    TestBed.resetTestingModule();
  });

  it('keeps the default file list focused on the primary columns', async () => {
    const fixture = await renderFilesPage();

    expect(fixture.componentInstance.columns().map((column) => column.headerName)).toEqual([
      'Name',
      'Modified',
      'Owner',
      'Access',
      'Status',
    ]);
    expect(fixture.componentInstance.isColumnVisible('type')).toBe(false);
    expect(fixture.componentInstance.isColumnVisible('size')).toBe(false);
    expect(fixture.componentInstance.isColumnVisible('scan')).toBe(false);
  }, 15_000);

  it('adds and removes secondary columns without changing the primary set', async () => {
    const fixture = await renderFilesPage();
    const component = fixture.componentInstance;

    component.toggleColumn('type', true);
    component.toggleColumn('size', true);
    fixture.detectChanges();

    expect(component.columns().map((column) => column.headerName)).toEqual([
      'Name',
      'Modified',
      'Owner',
      'Access',
      'Status',
      'Type',
      'Size',
    ]);

    component.toggleColumn('type', false);
    fixture.detectChanges();

    expect(component.columns().map((column) => column.headerName)).toEqual([
      'Name',
      'Modified',
      'Owner',
      'Access',
      'Status',
      'Size',
    ]);
  }, 15_000);

  it('separates opening a file from checkbox selection and keyboard focus', async () => {
    const fixture = await renderFilesPage();
    const file = DEFAULT_FILES[0];
    if (!file) {
      throw new Error('Expected the default file fixture.');
    }

    const grid = fixture.debugElement.query(By.directive(AppDataGridComponent))
      .componentInstance as AppDataGridComponent<FileViewModel>;
    const nameColumn = fixture.componentInstance.columns()[0];
    const actions = nameColumn?.actions?.(file) ?? [];
    const fallbackRenderer = grid.columnDefs[0]?.cellRenderer;

    expect(actions.map((action) => action.id)).toEqual(['open']);
    expect(typeof fallbackRenderer).toBe('function');
    if (typeof fallbackRenderer !== 'function') {
      throw new Error('Expected the AG Grid fallback action renderer.');
    }
    const renderedAction = fallbackRenderer({ data: file }) as HTMLElement;
    expect(renderedAction.querySelector('[data-grid-action="open"]')?.textContent).toBe(file.originalFileName);
    expect(grid.selectionMode).toBe('multiple');
    expect(grid.rowSelection).toMatchObject({
      checkboxes: true,
      headerCheckbox: true,
      enableClickSelection: false,
    });
    expect(grid.gridOptions.suppressCellFocus).toBe(false);
  }, 15_000);

  it('preserves feature-owned action renderers outside Files', async () => {
    const fixture = await renderFilesPage();
    const grid = fixture.debugElement.query(By.directive(AppDataGridComponent))
      .componentInstance as AppDataGridComponent<FileViewModel>;
    const file = DEFAULT_FILES[0];
    if (!file) {
      throw new Error('Expected the default file fixture.');
    }

    const customRenderer = (): HTMLElement => {
      const element = document.createElement('span');
      element.textContent = 'Feature renderer';
      return element;
    };
    grid.columns = [{
      colId: 'custom-action',
      headerName: 'Custom action',
      actions: (row) => [{ id: 'open', label: 'Open', row }],
      cellRenderer: customRenderer,
    }];
    grid.rows = [file];

    expect(grid.columnDefs[0]?.cellRenderer).toBe(customRenderer);
  });

  it('changes row density without changing selection state', async () => {
    const fixture = await renderFilesPage();
    const file = DEFAULT_FILES[0];
    if (!file) {
      throw new Error('Expected the default file fixture.');
    }

    const component = fixture.componentInstance;
    component.handleSelectionChanged({ rows: [file] });
    component.setDensity('compact');
    fixture.detectChanges();

    const grid = fixture.debugElement.query(By.directive(AppDataGridComponent))
      .componentInstance as AppDataGridComponent<FileViewModel>;
    expect(component.selectedCount()).toBe(1);
    expect(component.density()).toBe('compact');
    expect(grid.rowHeight).toBe(36);

    component.clearSelection();
    fixture.detectChanges();
    const compactButton = (fixture.nativeElement as HTMLElement)
      .querySelector('.files-page__density button:nth-child(2)');
    expect(compactButton?.getAttribute('aria-pressed')).toBe('true');
  });

  it('renders one bounded server page for a workspace with more than 1,000 files', async () => {
    const seed = DEFAULT_FILES[0];
    if (!seed) {
      throw new Error('Expected the default file fixture.');
    }
    const fixture = await renderFilesPage({
      ...FILES_PAGE_SCENARIOS.default,
      recentFiles: [seed],
      pickerFiles: [seed],
      page: 21,
      pageSize: 50,
      totalCount: 1_001,
      hasMore: false,
    });
    const grid = fixture.debugElement.query(By.directive(AppDataGridComponent))
      .componentInstance as AppDataGridComponent<FileViewModel>;
    const status = (fixture.nativeElement as HTMLElement)
      .querySelector('[data-testid="files-page-pagination-status"]');

    expect(grid.rowData).toHaveLength(1);
    expect(grid.boundedPageSize).toBe(50);
    expect(grid.maximumPageSize).toBe(50);
    expect(status?.textContent).toContain('Page 21 of 21');
    expect(status?.textContent).toContain('1001 files');
  });
});
