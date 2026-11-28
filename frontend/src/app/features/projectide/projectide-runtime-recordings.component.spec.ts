import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { AuthSessionFacade } from '../../core/auth/auth-session.facade';
import { FrontendFeatureFlagsService } from '../../core/feature-flags/frontend-feature-flags.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { PROJECTIDE_ENGLISH_MESSAGES, PROJECTIDE_JAPANESE_MESSAGES } from '../../core/i18n/messages/projectide.messages';
import { RealtimeFacade } from '../../core/realtime/realtime.facade';
import { ActiveWorkspaceFacade } from '../../core/workspace/active-workspace.facade';
import { ProjectIdeRuntimeRecordingsComponent } from './projectide-runtime-recordings.component';

const projectId = '034a2ba8-5e35-4894-9095-a0ce716ec34c';
const runId = '7fc1d7e5-6cc8-4fd5-9e86-1cdc3dd0996a';
const endpoint = `/api/projectide/runtime/projects/${projectId}/compiler-recordings/${runId}`;
const receipt = {
  runId,
  receiptDigest: 'e'.repeat(64),
  hostStatus: 'Completed',
  captureMode: 'Manifest',
  captureState: 'ManifestOnly',
  result: {
    resultDigest: 'f'.repeat(64),
    execution: 'Completed',
    coverage: 'Partial',
    verdict: 'Inconclusive',
    evidenceComplete: false,
  },
};

function render(enabled = true) {
  const isAuthenticated = signal(true);
  const currentTenant = signal({ tenantId: 'tenant', isAvailable: true });
  const currentUser = signal({ userId: 'actor' });
  let clear!: () => void;

  TestBed.configureTestingModule({
    imports: [ProjectIdeRuntimeRecordingsComponent],
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      {
        provide: AuthSessionFacade,
        useValue: { isAuthenticated, currentTenant, currentUser },
      },
      {
        provide: RealtimeFacade,
        useValue: {
          registerProtectedStateClearer: (_owner: string, callback: () => void) => {
            clear = callback;
            return () => undefined;
          },
        },
      },
    ],
  });

  const flags = TestBed.inject(FrontendFeatureFlagsService);
  flags.setForTesting({ 'projectide.developmentRuntime': enabled });
  const i18n = TestBed.inject(I18nService);
  i18n.registerMessages(PROJECTIDE_ENGLISH_MESSAGES, PROJECTIDE_JAPANESE_MESSAGES);
  i18n.setLocale('en');
  const fixture = TestBed.createComponent(ProjectIdeRuntimeRecordingsComponent);
  fixture.componentRef.setInput('projectId', projectId);
  fixture.detectChanges();

  const root = fixture.nativeElement as HTMLElement;
  const submit = (selector: string): void => {
    const input = root.querySelector('input')!;
    input.value = selector;
    input.dispatchEvent(new Event('input'));
    root.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    fixture.detectChanges();
  };

  return {
    fixture, root, flags, isAuthenticated, currentTenant, currentUser, submit,
    clearProtectedState: () => clear(),
    workspace: TestBed.inject(ActiveWorkspaceFacade),
    http: TestBed.inject(HttpTestingController),
    status: () => root.querySelector('[role="status"]')?.textContent?.trim(),
  };
}

describe('ProjectIDE development Runtime recording lookup', () => {
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('renders only the admitted server response for the scoped request', () => {
    const page = render();
    page.submit(runId.toUpperCase());
    expect(page.status()).toBe('Looking up Compiler recording…');
    const request = page.http.expectOne(endpoint);
    expect(request.request.method).toBe('GET');
    request.flush(receipt);
    page.fixture.detectChanges();

    expect(page.status()).toBe('Compiler recording loaded.');
    expect(Array.from(page.root.querySelectorAll('code'), (item) => item.textContent))
      .toEqual([receipt.receiptDigest, receipt.result.resultDigest]);
    expect(page.root.textContent).toContain('Inconclusive');
    expect(page.root.textContent).toContain('Partial');
  });

  it('preserves the absence of a capture or Compiler result', () => {
    const page = render();
    page.submit(runId);
    page.http.expectOne(endpoint).flush({
      ...receipt, hostStatus: 'Cancelled', captureState: null, result: null,
    });
    page.fixture.detectChanges();

    expect(page.root.textContent).toContain('No Compiler result was recorded.');
    expect(page.root.textContent).toContain('Not recorded');
    expect(page.root.querySelectorAll('code')).toHaveLength(1);
  });

  it('does not load a recording without the server presentation flag', () => {
    const page = render(false);
    page.fixture.componentInstance.setRunId(runId);
    page.fixture.componentInstance.findRecording();

    page.http.expectNone(endpoint);
    expect(page.root.querySelector('input')).toBeNull();
    expect(page.status()).toBe('The development Runtime is unavailable.');
  });

  it('treats run selection as an opaque UUID path segment', () => {
    const page = render();
    page.submit('not-a-run-id');

    page.http.expectNone(() => true);
    expect(page.status()).toBe('Enter a valid Compiler run ID.');
    expect(page.root.querySelector('input')?.getAttribute('aria-invalid')).toBe('true');
  });

  it.each([401, 403, 404, 503])('uses the same unavailable state for HTTP %s and clears an earlier receipt', (status) => {
    const page = render();
    page.submit(runId);
    page.http.expectOne(endpoint).flush(receipt);
    page.fixture.detectChanges();

    page.submit(runId);
    expect(page.root.querySelector('code')).toBeNull();
    page.http.expectOne(endpoint).flush({ detail: 'Private backend message' }, { status, statusText: 'Unavailable' });
    page.fixture.detectChanges();

    expect(page.status()).toBe('The Compiler recording is unavailable.');
    expect(page.root.textContent).not.toContain('Private backend message');
    expect(page.root.querySelector('code')).toBeNull();
  });

  it.each([
    { ...receipt, runId: projectId },
    { ...receipt, captureState: 'Unknown' },
    { ...receipt, source: 'Private Source body' },
    { ...receipt, result: { ...receipt.result, evidenceComplete: 'false' } },
  ])('refuses a malformed or mismatched 200 response', (body) => {
    const page = render();
    page.submit(runId);
    page.http.expectOne(endpoint).flush(body);
    page.fixture.detectChanges();

    expect(page.status()).toBe('The Compiler recording is unavailable.');
    expect(page.root.querySelector('code')).toBeNull();
  });

  it('cancels pending work on a newer selector or same-session authorization invalidation', () => {
    const page = render();
    page.submit(runId);
    const superseded = page.http.expectOne(endpoint);
    page.submit(runId);
    const revoked = page.http.expectOne(endpoint);
    expect(superseded.cancelled).toBe(true);

    page.clearProtectedState();
    page.fixture.detectChanges();

    expect(revoked.cancelled).toBe(true);
    expect(page.root.querySelector('input')?.value).toBe('');
    expect(page.root.querySelector('code')).toBeNull();
  });

  it.each(['project', 'tenant', 'actor', 'workspace', 'session', 'flag'] as const)(
    'removes the receipt and run selector at the %s boundary',
    (boundary) => {
      const page = render();
      page.submit(runId);
      page.http.expectOne(endpoint).flush(receipt);
      page.fixture.detectChanges();
      expect(page.root.querySelector('code')).not.toBeNull();

      switch (boundary) {
        case 'project': page.fixture.componentRef.setInput('projectId', runId); break;
        case 'tenant': page.currentTenant.set({ tenantId: 'another-tenant', isAvailable: true }); break;
        case 'actor': page.currentUser.set({ userId: 'another-actor' }); break;
        case 'workspace': page.workspace.setActiveWorkspace({ id: 'workspace', label: 'Workspace' }); break;
        case 'session': page.isAuthenticated.set(false); break;
        case 'flag': page.flags.setForTesting({}); break;
      }
      page.fixture.detectChanges();

      expect(page.root.querySelector('code')).toBeNull();
      expect(page.root.querySelector('input')?.value ?? '').toBe('');
    },
  );
});
