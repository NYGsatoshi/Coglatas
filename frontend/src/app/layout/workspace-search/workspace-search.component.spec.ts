import { I18nService } from '../../core/i18n/i18n.service';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { WorkspaceSearchComponent } from './workspace-search.component';

const WORKSPACE_A = '11111111-1111-4111-8111-111111111111';
const WORKSPACE_B = '22222222-2222-4222-8222-222222222222';
const PROJECT_ID = '33333333-3333-4333-8333-333333333333';
const FILE_ID = '44444444-4444-4444-8444-444444444444';

describe('WorkspaceSearchComponent', () => {
  beforeEach(() => window.localStorage.setItem('coglatas.locale', 'en'));
  afterEach(() => window.localStorage.removeItem('coglatas.locale'));
  afterEach(() => {
    TestBed.inject(HttpTestingController, null)?.verify();
    TestBed.resetTestingModule();
  });

  async function createFixture(workspaceId: string | null = WORKSPACE_A) {
    await TestBed.configureTestingModule({
      imports: [WorkspaceSearchComponent],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();

    const fixture = TestBed.createComponent(WorkspaceSearchComponent);
    fixture.componentRef.setInput('workspaceId', workspaceId);
    fixture.componentRef.setInput('workspaceLabel', workspaceId ? 'Workspace A' : '');
    fixture.detectChanges();
    return fixture;
  }

  it('keeps the search entry disabled until a Workspace is selected', async () => {
    const fixture = await createFixture(null);
    const root = fixture.nativeElement as HTMLElement;
    const input = root.querySelector<HTMLInputElement>('[data-testid="workspace-search-input"]');

    expect(input?.disabled).toBe(true);
    expect(input?.getAttribute('aria-controls')).toBeNull();
    expect(root.textContent).toContain('Select a Workspace to search');
  });

  it('queries only Project and File in the active Workspace and rejects mismatched result scopes', async () => {
    const fixture = await createFixture();
    const httpMock = TestBed.inject(HttpTestingController);
    const component = fixture.componentInstance;

    component.query.set('needle');
    component.submitSearch(new Event('submit', { cancelable: true }));

    const requests = httpMock.match((request) => request.url === '/api/search');
    expect(requests.length).toBe(2);
    const projectRequest = requests.find((request) => request.request.params.get('type') === 'Project');
    const fileRequest = requests.find((request) => request.request.params.get('type') === 'File');
    expect(projectRequest).toBeDefined();
    expect(fileRequest).toBeDefined();

    for (const request of requests) {
      expect(request.request.method).toBe('GET');
      expect(request.request.withCredentials).toBe(true);
      expect(request.request.params.get('q')).toBe('needle');
      expect(request.request.params.get('workspaceId')).toBe(WORKSPACE_A);
      expect(request.request.params.get('page')).toBe('1');
      expect(request.request.params.get('pageSize')).toBe('8');
    }

    projectRequest!.flush({
      items: [
        {
          type: 7,
          id: PROJECT_ID,
          title: 'Authorized Research',
          workspaceId: WORKSPACE_A,
          createdAt: '2026-08-28T00:00:00Z',
        },
        {
          type: 7,
          id: '55555555-5555-4555-8555-555555555555',
          title: 'Wrong Workspace',
          workspaceId: WORKSPACE_B,
          createdAt: '2026-08-28T00:01:00Z',
        },
      ],
    });
    fileRequest!.flush({
      items: [
        {
          type: 'File',
          id: FILE_ID,
          title: 'authorized.pdf',
          workspaceId: WORKSPACE_A,
          createdAt: '2026-08-28T00:02:00Z',
        },
      ],
    });
    fixture.detectChanges();

    const root = fixture.nativeElement as HTMLElement;
    const text = root.textContent ?? '';
    expect(text).toContain('Authorized Research');
    expect(text).toContain('authorized.pdf');
    expect(text).toContain('Research / Project');
    expect(text).toContain('File');
    expect(text).not.toContain('Wrong Workspace');
    expect(text).not.toContain('snippet');
    expect(component.results().length).toBe(2);
    expect(root.querySelector('[data-testid="workspace-search-results"]')).not.toBeNull();
    expect(root.querySelector('[data-testid="workspace-search-input"]')?.getAttribute('aria-controls')).toBe(
      'workspace-search-results',
    );
  });

  it('cancels stale reads and clears protected results when the Workspace changes', async () => {
    const fixture = await createFixture();
    const httpMock = TestBed.inject(HttpTestingController);
    const component = fixture.componentInstance;

    component.query.set('needle');
    component.submitSearch(new Event('submit', { cancelable: true }));
    const requests = httpMock.match((request) => request.url === '/api/search');
    expect(requests.length).toBe(2);

    fixture.componentRef.setInput('workspaceId', WORKSPACE_B);
    fixture.componentRef.setInput('workspaceLabel', 'Workspace B');
    fixture.detectChanges();

    expect(requests.every((request) => request.cancelled)).toBe(true);
    expect(component.query()).toBe('');
    expect(component.results()).toEqual([]);
    expect(component.status()).toBe('idle');
    expect(
      (fixture.nativeElement as HTMLElement)
        .querySelector('[data-testid="workspace-search-input"]')
        ?.getAttribute('aria-controls'),
    ).toBeNull();
  });

  it('renders a fixed retry-safe error without exposing a response body', async () => {
    const fixture = await createFixture();
    const httpMock = TestBed.inject(HttpTestingController);
    const component = fixture.componentInstance;

    component.query.set('needle');
    component.submitSearch(new Event('submit', { cancelable: true }));
    const requests = httpMock.match((request) => request.url === '/api/search');
    requests[0]!.flush(
      { detail: 'secret stack and internal filename' },
      { status: 500, statusText: 'Server Error' },
    );
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Search is unavailable. Try again.');
    expect(text).not.toContain('secret stack');
    expect(text).not.toContain('internal filename');
    expect(requests[1]!.cancelled).toBe(true);
  });

  it('focuses the visible Workspace search field with Ctrl+K', async () => {
    const fixture = await createFixture();
    document.body.appendChild(fixture.nativeElement);
    fixture.detectChanges();

    document.dispatchEvent(
      new KeyboardEvent('keydown', {
        key: 'k',
        ctrlKey: true,
        bubbles: true,
        cancelable: true,
      }),
    );

    expect(document.activeElement).toBe(
      (fixture.nativeElement as HTMLElement).querySelector('[data-testid="workspace-search-input"]'),
    );
    fixture.destroy();
  });

  it('switches Japanese search labels without replacing the input or restarting pending reads', async () => {
    window.localStorage.removeItem('coglatas.locale');
    const fixture = await createFixture();
    const host = fixture.nativeElement as HTMLElement;
    const input = host.querySelector<HTMLInputElement>('[data-testid="workspace-search-input"]')!;
    input.value = 'retained query';
    input.dispatchEvent(new Event('input'));
    fixture.componentInstance.submitSearch(new Event('submit', { cancelable: true }));
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    const requests = http.match((request) => request.url === '/api/search');
    expect(requests).toHaveLength(2);
    expect(host.textContent).toContain('現在のワークスペースを検索しています。');

    TestBed.inject(I18nService).setLocale('en');
    fixture.detectChanges();

    expect(host.querySelector('[data-testid="workspace-search-input"]')).toBe(input);
    expect(input.value).toBe('retained query');
    expect(fixture.componentInstance.status()).toBe('loading');
    expect(host.textContent).toContain('Searching the current Workspace.');
    expect(requests.every((request) => !request.cancelled)).toBe(true);
    http.expectNone((request) => request.url === '/api/search');
    for (const request of requests) { request.flush({ items: [] }); }
    fixture.detectChanges();
    expect(host.textContent).toContain('No matching Files or Research / Projects were found.');
  });

});
