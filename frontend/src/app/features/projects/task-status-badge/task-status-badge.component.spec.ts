import { TestBed } from '@angular/core/testing';

import { I18nService } from '../../../core/i18n/i18n.service';
import { TaskDependenciesReadonlyComponent } from '../task-dependencies-readonly/task-dependencies-readonly.component';
import { TaskProgressFieldComponent } from '../task-progress-field/task-progress-field.component';
import { TaskStatusBadgeComponent } from './task-status-badge.component';

describe('Task presentation locale boundaries', () => {
  beforeEach(() => localStorage.setItem('coglatas.locale', 'en'));
  afterEach(() => {
    TestBed.resetTestingModule();
    localStorage.removeItem('coglatas.locale');
  });

  it('localizes explicitly canonical dependency states while retaining supplied Stage names and raw progress', async () => {
    await TestBed.configureTestingModule({
      imports: [TaskDependenciesReadonlyComponent, TaskStatusBadgeComponent, TaskProgressFieldComponent],
    }).compileComponents();
    const dependencies = TestBed.createComponent(TaskDependenciesReadonlyComponent);
    dependencies.componentRef.setInput('dependencies', [{ id: 'dependency-a', title: 'Done', status: 'done' }]);
    const stage = TestBed.createComponent(TaskStatusBadgeComponent);
    stage.componentRef.setInput('status', 'inProgress');
    stage.componentRef.setInput('label', 'In progress');
    const progress = TestBed.createComponent(TaskProgressFieldComponent);
    progress.componentRef.setInput('value', 37.5);
    dependencies.detectChanges();
    stage.detectChanges();
    progress.detectChanges();
    const i18n = TestBed.inject(I18nService);
    const badge = (dependencies.nativeElement as HTMLElement).querySelector('[data-status="done"]')!;
    const meter = (progress.nativeElement as HTMLElement).querySelector('meter')!;

    i18n.setLocale('ja');
    dependencies.detectChanges();
    stage.detectChanges();
    progress.detectChanges();
    expect((dependencies.nativeElement as HTMLElement).querySelector('h2')?.textContent).toBe('依存関係');
    expect(badge.textContent).toBe('完了');
    expect((dependencies.nativeElement as HTMLElement).querySelector('li > span')?.textContent).toBe('Done');
    expect((stage.nativeElement as HTMLElement).textContent).toBe('In progress');
    expect(meter.value).toBe(37.5);
    expect(meter.getAttribute('aria-label')).toBe('タスクの進捗');
    expect((progress.nativeElement as HTMLElement).textContent).toContain(i18n.formatNumber(0.375, { style: 'percent', maximumFractionDigits: 2 }));
    expect(dependencies.componentInstance.dependencies[0].status).toBe('done');

    i18n.setLocale('en');
    dependencies.detectChanges();
    stage.detectChanges();
    progress.detectChanges();
    expect(badge.textContent).toBe('Done');
    expect((stage.nativeElement as HTMLElement).textContent).toBe('In progress');
    expect(meter.getAttribute('aria-label')).toBe('Task progress');
    expect(meter.value).toBe(37.5);
  });
});
