import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { ContinueWorkingPanelComponent } from './continue-working-panel.component';
import { ContinueWorkingFacade, ContinueWorkingViewModel } from './continue-working.facade';

const WORKSPACE_ID = '33333333-3333-4333-8333-333333333333';
const PROJECT_ID = '44444444-4444-4444-8444-444444444444';
const FILE_ID = '55555555-5555-4555-8555-555555555555';

describe('ContinueWorkingPanelComponent', () => {
  beforeEach(() => window.localStorage.setItem('coglatas.locale', 'en'));
  afterEach(() => window.localStorage.removeItem('coglatas.locale'));
  const view = signal<ContinueWorkingViewModel>({
    status: 'ready',
    workspaceId: WORKSPACE_ID,
    items: [
      {
        kind: 'project', resourceId: PROJECT_ID, title: 'Research evidence', status: 'running',
        updatedAtUtc: '2026-08-27T00:00:00.000Z', lastOpenedUtc: '2026-08-28T00:00:00.000Z', route: `/projects/${PROJECT_ID}`,
      },
      {
        kind: 'file', resourceId: FILE_ID, title: 'File', status: 'ready',
        updatedAtUtc: '2026-08-26T00:00:00.000Z', lastOpenedUtc: '2026-08-27T00:00:00.000Z', route: null,
      },
    ],
    retryAvailable: false,
    downloadingFileId: null,
  });
  const facade = {
    view,
    activate: vi.fn(),
    release: vi.fn(),
    retry: vi.fn(),
    downloadFile: vi.fn(),
  };
  let fixture: ComponentFixture<ContinueWorkingPanelComponent>;

  beforeEach(async () => {
    vi.clearAllMocks();
    view.set({
      status: 'ready', workspaceId: WORKSPACE_ID, retryAvailable: false, downloadingFileId: null,
      items: [
        { kind: 'project', resourceId: PROJECT_ID, title: 'Research evidence', status: 'running', updatedAtUtc: '2026-08-27T00:00:00.000Z', lastOpenedUtc: '2026-08-28T00:00:00.000Z', route: `/projects/${PROJECT_ID}` },
        { kind: 'file', resourceId: FILE_ID, title: 'File', status: 'ready', updatedAtUtc: '2026-08-26T00:00:00.000Z', lastOpenedUtc: '2026-08-27T00:00:00.000Z', route: null },
      ],
    });
    await TestBed.configureTestingModule({
      imports: [ContinueWorkingPanelComponent],
      providers: [provideRouter([]), { provide: ContinueWorkingFacade, useValue: facade }],
    }).compileComponents();
    fixture = TestBed.createComponent(ContinueWorkingPanelComponent);
    fixture.componentRef.setInput('workspaceId', WORKSPACE_ID);
    fixture.detectChanges();
  });

  afterEach(() => TestBed.resetTestingModule());

  it('distinguishes Research and File with icon plus text, server status, timestamps, and bounded actions', () => {
    const host = fixture.nativeElement as HTMLElement;

    expect(facade.activate).toHaveBeenCalledWith(WORKSPACE_ID);
    expect(host.querySelectorAll('[data-testid="continue-working-item"]')).toHaveLength(2);
    expect(host.textContent).toContain('Research');
    expect(host.textContent).toContain('File');
    expect(host.textContent).toContain('Running');
    expect(host.textContent).toContain('Ready');
    expect(host.textContent).toContain('Updated');
    expect(host.textContent).toContain('Last opened');
    expect(host.querySelector<HTMLAnchorElement>('[data-testid="continue-working-project-link"]')?.getAttribute('href')).toBe(`/projects/${PROJECT_ID}`);
    expect(host.querySelector('[data-testid="continue-working-download"]')?.textContent).toContain('Download');
    expect(host.textContent).not.toMatch(/collaborator|permission|workspace id/iu);
  });

  it('dispatches File download without treating a File as a navigable preview', () => {
    const host = fixture.nativeElement as HTMLElement;
    const button = host.querySelector<HTMLButtonElement>('[data-testid="continue-working-download"]')!;

    button.click();

    expect(facade.downloadFile).toHaveBeenCalledWith(FILE_ID);
    expect(host.querySelector(`a[href*="${FILE_ID}"]`)).toBeNull();
  });

  it('shows only capability-backed empty actions and keeps the empty state usable', () => {
    view.set({
      status: 'empty', workspaceId: WORKSPACE_ID, items: [], retryAvailable: false, downloadingFileId: null,
      message: 'Open a Research or download a File to see it here.',
    });
    fixture.componentRef.setInput('canCreateResearch', true);
    fixture.componentRef.setInput('canBrowseFiles', false);
    fixture.detectChanges();
    const host = fixture.nativeElement as HTMLElement;

    expect(host.textContent).toContain('Open a Research or download a File');
    expect(host.querySelector<HTMLAnchorElement>(`a[href="/workspaces/${WORKSPACE_ID}/research/new"]`)).not.toBeNull();
    expect(host.textContent).not.toContain('Browse Files');

    fixture.componentRef.setInput('canBrowseFiles', true);
    fixture.detectChanges();
    expect(host.querySelector<HTMLAnchorElement>(`a[href="/workspaces/${WORKSPACE_ID}/files"]`)).not.toBeNull();
  });

  it('keeps a visible Retry control for transient reauthorization failure', () => {
    view.set({
      status: 'error', workspaceId: WORKSPACE_ID, items: [], retryAvailable: true, downloadingFileId: null,
      message: 'Recent work could not be reauthorized. No cached labels are shown.',
    });
    fixture.detectChanges();
    const retry = (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('[data-testid="continue-working-retry"]')!;

    retry.click();

    expect(facade.retry).toHaveBeenCalledOnce();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('No cached labels');
  });
});
