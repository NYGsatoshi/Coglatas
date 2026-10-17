import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { ProjectSummaryViewModel } from '../projects.types';
import { ProjectSummaryPanelComponent } from './project-summary-panel.component';

const project = (status: ProjectSummaryViewModel['status']): ProjectSummaryViewModel => ({
  id: `project-${status}`,
  name: `Project ${status}`,
  status,
  statusLabel: 'legacy status label',
  startDate: '2026-08-01',
  dueDate: '2026-08-31',
  updatedAt: '2026-08-22T09:30:00+09:00',
  group: 'Group not shown by API',
  taskCounts: { total: 5, done: 2, blocked: 1 },
  canCreateTask: false
});

beforeEach(() => localStorage.setItem('coglatas.locale', 'en'));

describe('ProjectSummaryPanelComponent', () => {
  afterEach(() => TestBed.resetTestingModule());

  async function render(status: ProjectSummaryViewModel['status'] = 'active') {
    await TestBed.configureTestingModule({
      imports: [ProjectSummaryPanelComponent],
      providers: [provideRouter([])]
    }).compileComponents();

    const fixture = TestBed.createComponent(ProjectSummaryPanelComponent);
    fixture.componentRef.setInput('projects', [project(status)]);
    fixture.detectChanges();
    return fixture;
  }

  it('renders canonical status as icon plus text and keeps secondary metrics collapsed by default', async () => {
    const fixture = await render('active');
    const root = fixture.nativeElement as HTMLElement;
    const badge = root.querySelector<HTMLElement>('app-work-status-badge .work-status');
    const details = root.querySelector<HTMLDetailsElement>('.project-summary-panel__secondary');

    expect(badge?.textContent).toContain('Running');
    expect(badge?.getAttribute('data-work-status')).toBe('running');
    expect(badge?.querySelector('svg')).not.toBeNull();
    expect(details?.open).toBe(false);
    expect(root.textContent).not.toContain('legacy status label');
    expect(root.textContent).not.toContain('Group not shown by API');
  });

  it('keeps authoritative update time visible while auxiliary dates and task counts stay collapsed', async () => {
    const fixture = await render();
    const root = fixture.nativeElement as HTMLElement;
    const update = root.querySelector<HTMLElement>('[data-testid="project-updated-at"]');
    const time = update?.querySelector<HTMLTimeElement>('time');

    expect(update?.textContent).toContain('Updated');
    expect(time?.getAttribute('datetime')).toBe('2026-08-22T09:30:00+09:00');
    expect(root.querySelector<HTMLDetailsElement>('.project-summary-panel__secondary')?.open).toBe(false);
  });

  it('updates the canonical status and timestamp when the project state changes', async () => {
    const fixture = await render('active');
    fixture.componentRef.setInput('projects', [
      {
        ...project('review'),
        id: 'project-active',
        updatedAt: '2026-08-22T12:45:00+09:00'
      }
    ]);
    fixture.detectChanges();

    const root = fixture.nativeElement as HTMLElement;
    const badge = root.querySelector<HTMLElement>('app-work-status-badge .work-status');
    const time = root.querySelector<HTMLTimeElement>('[data-testid="project-updated-at"] time');

    expect(badge?.textContent).toContain('Needs review');
    expect(badge?.getAttribute('data-work-status')).toBe('needsReview');
    expect(time?.getAttribute('datetime')).toBe('2026-08-22T12:45:00+09:00');
  });
});
