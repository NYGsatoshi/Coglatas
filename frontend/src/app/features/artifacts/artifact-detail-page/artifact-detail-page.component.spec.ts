import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { of } from 'rxjs';

import { I18nService } from '../../../core/i18n/i18n.service';
import { ArtifactDetailPageComponent } from './artifact-detail-page.component';

const ARTIFACT_ID = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';

async function render() {
  await TestBed.configureTestingModule({
    imports: [ArtifactDetailPageComponent],
    providers: [provideHttpClient(), provideHttpClientTesting(), {
      provide: ActivatedRoute,
      useValue: { paramMap: of(convertToParamMap({ artifactId: ARTIFACT_ID })) },
    }],
  }).compileComponents();
  const fixture = TestBed.createComponent(ArtifactDetailPageComponent);
  fixture.detectChanges();
  return { fixture, http: TestBed.inject(HttpTestingController), i18n: TestBed.inject(I18nService) };
}

describe('Artifact detail locale presentation', () => {
  beforeEach(() => window.localStorage.setItem('coglatas.locale', 'ja'));
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
    window.localStorage.removeItem('coglatas.locale');
    TestBed.resetTestingModule();
  });

  it('switches mounted loading and detail copy without refetching or translating authored fields', async () => {
    const { fixture, http, i18n } = await render();
    const request = http.expectOne(`/api/artifacts/${ARTIFACT_ID}`);
    expect(fixture.nativeElement.textContent).toContain('成果物を読み込んでいます');
    i18n.setLocale('en');
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Loading artifact');
    expect(request.cancelled).toBe(false);
    request.flush({ id: ARTIFACT_ID, title: 'Artifact', description: '成果物という原文', artifactType: 'Document',
      status: 'Draft', updatedAt: '2026-10-17T01:23:00Z' });
    fixture.detectChanges();
    const viewModel = fixture.componentInstance.viewModel();
    i18n.setLocale('ja');
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('h1').textContent).toBe('Artifact');
    expect(fixture.nativeElement.textContent).toContain('成果物という原文');
    expect(fixture.nativeElement.textContent).toContain('文書');
    expect(fixture.nativeElement.textContent).toContain('下書き');
    expect(fixture.nativeElement.textContent).toContain(i18n.formatDateTime(viewModel.updatedAt, { dateStyle: 'medium', timeStyle: 'short' }));
    i18n.setLocale('en');
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Updated');
    expect(fixture.componentInstance.viewModel()).toBe(viewModel);
    expect(viewModel.artifactType).toBe('Document');
    expect(viewModel.artifactStatus).toBe('Draft');
    expect(http.match(() => true)).toHaveLength(0);
  });

  it.each([403, 500])('switches a held %s failure without another request', async (status) => {
    const { fixture, http, i18n } = await render();
    http.expectOne(`/api/artifacts/${ARTIFACT_ID}`).flush({}, { status, statusText: 'Failure' });
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('h1').textContent).toBe('成果物を表示できません');
    i18n.setLocale('en');
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('h1').textContent).toBe('Artifact unavailable');
    expect(fixture.nativeElement.textContent).toContain(status === 403
      ? 'This artifact is no longer available to the current user.' : 'The artifact could not be loaded.');
    expect(http.match(() => true)).toHaveLength(0);
  });
});
