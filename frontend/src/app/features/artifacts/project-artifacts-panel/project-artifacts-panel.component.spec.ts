import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { I18nService } from '../../../core/i18n/i18n.service';
import { ProjectArtifactsPanelComponent } from './project-artifacts-panel.component';

const PROJECT_A = '11111111-1111-4111-8111-111111111111';
const PROJECT_B = '22222222-2222-4222-8222-222222222222';
const ARTIFACT = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';

async function render(projectId = PROJECT_A) {
  await TestBed.configureTestingModule({
    imports: [ProjectArtifactsPanelComponent],
    providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
  }).compileComponents();
  const fixture = TestBed.createComponent(ProjectArtifactsPanelComponent);
  fixture.componentRef.setInput('projectId', projectId);
  fixture.detectChanges();
  return { fixture, http: TestBed.inject(HttpTestingController), i18n: TestBed.inject(I18nService) };
}

const scopedArtifact = (projectId: string) => ({
  id: ARTIFACT, projectId, title: 'Original source title', description: '作成者が入力した説明',
  status: 'Draft', createdAt: '2026-10-17T01:00:00Z', updatedAt: '2026-10-17T02:00:00Z'
});

describe('ProjectArtifactsPanelComponent', () => {
  beforeEach(() => window.localStorage.setItem('coglatas.locale', 'en'));
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify({ ignoreCancelled: true });
    window.localStorage.removeItem('coglatas.locale');
    TestBed.resetTestingModule();
  });

  it('lists only matching authorized Project artifacts with links and preserved source copy', async () => {
    const { fixture, http } = await render();
    const request = http.expectOne(`/api/projects/${PROJECT_A}/artifacts`);
    expect(request.request.method).toBe('GET');
    expect(request.request.withCredentials).toBe(true);
    request.flush([scopedArtifact(PROJECT_A)]);
    fixture.detectChanges();
    const host = fixture.nativeElement as HTMLElement;
    expect(host.querySelector('[data-testid="project-artifacts-list"]')?.textContent).toContain('Original source title');
    expect(host.querySelector('a')?.getAttribute('href')).toBe(`/artifacts/${ARTIFACT}`);
    expect(host.textContent).toContain('Draft');
    expect(host.textContent).toContain('作成者が入力した説明');

    TestBed.inject(I18nService).setLocale('ja');
    fixture.detectChanges();
    expect(host.textContent).toContain('下書き');
    expect(host.textContent).toContain('Original source title');
    expect(http.match(() => true)).toHaveLength(0);
  });

  it('fails closed on a mismatched Project row without disclosing its title', async () => {
    const { fixture, http } = await render();
    http.expectOne(`/api/projects/${PROJECT_A}/artifacts`)
      .flush([scopedArtifact(PROJECT_A), { ...scopedArtifact(PROJECT_B), title: 'Secret from another Project' }]);
    fixture.detectChanges();
    const host = fixture.nativeElement as HTMLElement;
    expect(host.querySelector('[data-testid="project-artifacts-unavailable"]')).not.toBeNull();
    expect(host.textContent).not.toContain('Secret from another Project');
    expect(host.textContent).not.toContain('Original source title');
    expect(host.querySelector('[data-testid="project-artifacts-list"]')).toBeNull();
  });

  it('cancels an old Project request before showing another Project', async () => {
    const { fixture, http } = await render();
    const prior = http.expectOne(`/api/projects/${PROJECT_A}/artifacts`);
    fixture.componentRef.setInput('projectId', PROJECT_B);
    fixture.detectChanges();
    expect(prior.cancelled).toBe(true);
    const next = http.expectOne(`/api/projects/${PROJECT_B}/artifacts`);
    next.flush([{ ...scopedArtifact(PROJECT_B), title: 'Project B title' }]);
    fixture.detectChanges();
    const host = fixture.nativeElement as HTMLElement;
    expect(host.textContent).toContain('Project B title');
    expect(host.textContent).not.toContain('Original source title');
  });

  it('never shows protected metadata on an authorization failure', async () => {
    const { fixture, http } = await render();
    http.expectOne(`/api/projects/${PROJECT_A}/artifacts`).flush({}, { status: 403, statusText: 'Forbidden' });
    fixture.detectChanges();
    const host = fixture.nativeElement as HTMLElement;
    expect(host.querySelector('[data-testid="project-artifacts-unavailable"]')).not.toBeNull();
    expect(host.querySelector('[data-testid="project-artifacts-list"]')).toBeNull();
  });

  it('keeps an unpaged server response bounded in the UI without claiming a global repository', async () => {
    const { fixture, http } = await render();
    const items = Array.from({ length: 21 }, (_, index) => ({
      ...scopedArtifact(PROJECT_A), id: `artifact-${index}`, title: `Entry ${index}`
    }));
    http.expectOne(`/api/projects/${PROJECT_A}/artifacts`).flush(items);
    fixture.detectChanges();
    const host = fixture.nativeElement as HTMLElement;
    expect(host.querySelectorAll('[data-testid="project-artifacts-list"] li')).toHaveLength(20);
    expect(host.querySelector('[data-testid="project-artifacts-bounded"]')?.textContent).toContain('first 20 of 21');
  });
});
