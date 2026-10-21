vi.mock('@syncfusion/ej2-angular-grids', async () => {
  const { Directive, Injectable, NgModule } = await import('@angular/core');
  class AggregateColumnDirective {}
  class AggregateColumnsDirective {}
  class AggregateDirective {}
  class AggregatesDirective {}
  class ColumnDirective {}
  class ColumnsDirective {}
  class FilterService {}
  class GridComponent {}
  class GridModule {}
  class PageService {}
  class SelectionService {}
  class StackedColumnDirective {}
  class StackedColumnsDirective {}
  class SortService {}
  Directive({ selector: 'e-aggregate-column' })(AggregateColumnDirective);
  Directive({ selector: 'e-aggregate-columns' })(AggregateColumnsDirective);
  Directive({ selector: 'e-aggregate' })(AggregateDirective);
  Directive({ selector: 'e-aggregates' })(AggregatesDirective);
  Directive({ selector: 'e-column' })(ColumnDirective);
  Directive({ selector: 'e-columns' })(ColumnsDirective);
  Directive({ selector: 'e-stacked-column' })(StackedColumnDirective);
  Directive({ selector: 'e-stacked-columns' })(StackedColumnsDirective);
  Injectable()(FilterService);
  Injectable()(PageService);
  Injectable()(SelectionService);
  Injectable()(SortService);
  NgModule({
    declarations: [
      AggregateColumnDirective,
      AggregateColumnsDirective,
      AggregateDirective,
      AggregatesDirective,
      ColumnDirective,
      ColumnsDirective,
      StackedColumnDirective,
      StackedColumnsDirective,
    ],
  })(GridModule);
  return {
    AggregateColumnDirective,
    AggregateColumnsDirective,
    AggregateDirective,
    AggregatesDirective,
    ColumnDirective,
    ColumnsDirective,
    FilterService,
    GridComponent,
    GridModule,
    PageService,
    SelectionService,
    StackedColumnDirective,
    StackedColumnsDirective,
    SortService,
  };
});

import { SyncfusionDataGridComponent } from './syncfusion-data-grid.component';
import { SimpleChange, type SimpleChanges } from '@angular/core';

interface TestRow {
  readonly id: string;
  readonly label: string;
}

describe('SyncfusionDataGridComponent adapter options', () => {
  it('accepts the adapter-neutral sticky option and emits a row activation with its focus trigger', () => {
    const component = new SyncfusionDataGridComponent<TestRow>();
    component.stickyHeader = true;
    expect(component.pagination).toBe(true);
    const activations: unknown[] = [];
    component.rowActivated.subscribe((event) => activations.push(event));
    const cell = document.createElement('td');
    cell.className = 'e-rowcell';
    document.body.append(cell);

    component.handleRecordClick({
      rowData: { id: 'syncfusion-row', label: 'Syncfusion row' },
      target: cell,
    });

    expect(component.stickyHeader).toBe(true);
    expect(activations).toEqual([{
      row: { id: 'syncfusion-row', label: 'Syncfusion row' },
      trigger: cell,
    }]);
    cell.remove();
  });

  it('keeps Enter activation on a rendered action local to the adapter', () => {
    const component = new SyncfusionDataGridComponent<TestRow>();
    const actions: unknown[] = [];
    component.actionInvoked.subscribe((event) => actions.push(event));
    const button = document.createElement('button');
    document.body.append(button);
    button.addEventListener('keydown', (event) => {
      component.handleActionKeydown('open', { id: 'syncfusion-row', label: 'Open detail' }, event);
    });

    const event = new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true });
    button.dispatchEvent(event);

    expect(event.defaultPrevented).toBe(true);
    expect(actions).toEqual([{
      actionId: 'open',
      row: { id: 'syncfusion-row', label: 'Open detail' },
      trigger: button,
    }]);
    button.remove();
  });

  it('keeps vendor settings stable until the relevant adapter input changes', () => {
    const component = new SyncfusionDataGridComponent<TestRow>();
    const initialPageSettings = component.pageSettings;
    const initialSelectionSettings = component.selectionSettings;

    component.ngOnChanges({ rows: new SimpleChange([], [], false) } as SimpleChanges);
    expect(component.pageSettings).toBe(initialPageSettings);
    expect(component.selectionSettings).toBe(initialSelectionSettings);

    component.page = 2;
    component.ngOnChanges({ page: new SimpleChange(1, 2, false) } as SimpleChanges);
    expect(component.pageSettings).not.toBe(initialPageSettings);
    expect(component.pageSettings.currentPage).toBe(2);
    expect(component.selectionSettings).toBe(initialSelectionSettings);

    component.selectionMode = 'multiple';
    component.ngOnChanges({ selectionMode: new SimpleChange('none', 'multiple', false) } as SimpleChanges);
    expect(component.selectionSettings).not.toBe(initialSelectionSettings);
    expect(component.selectionSettings.type).toBe('Multiple');
  });
});
