import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap } from '@angular/router';

import { I18nService } from '../../../core/i18n/i18n.service';
import { ReportReaderPageComponent } from './report-reader-page.component';

const PROJECT_ID = '11111111-1111-1111-1111-111111111111';
const TASK_ID = '22222222-2222-2222-2222-222222222222';
const VERSION_ID = '33333333-3333-3333-3333-333333333333';
const CLAIM_ID = '44444444-4444-4444-4444-444444444444';
const LOGICAL_CLAIM_ID = '55555555-5555-5555-5555-555555555555';
const SECTION_ID = '66666666-6666-6666-6666-666666666666';
const LOGICAL_SECTION_ID = '77777777-7777-7777-7777-777777777777';
const PLAN_REVISION_ID = '88888888-8888-8888-8888-888888888888';

function reportResponse() {
  return {
    projectId: PROJECT_ID,
    taskId: TASK_ID,
    artifactId: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
    artifactVersionId: VERSION_ID,
    versionNumber: 4,
    canRefine: true,
    title: 'Evidence report',
    sections: [{
      id: SECTION_ID,
      logicalSectionId: LOGICAL_SECTION_ID,
      ordinal: 1,
      heading: 'Findings',
      runs: [{
        kind: 'citation',
        text: 'Supported claim',
        citation: {
          id: 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',
          ordinal: 1,
          claimId: CLAIM_ID,
          logicalClaimId: LOGICAL_CLAIM_ID,
          evidence: []
        }
      }]
    }]
  };
}

function preflightResponse() {
  return {
    projectId: PROJECT_ID,
    taskItemId: TASK_ID,
    artifactId: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
    baseArtifactVersionId: VERSION_ID,
    baseVersionNumber: 4,
    targetKind: 'Claim',
    targetLogicalId: LOGICAL_CLAIM_ID,
    targetLabel: 'Supported claim',
    scope: {
      origin: 'TaskOverride',
      projectScopeVersion: 8,
      taskOverrideVersion: 3,
      webEnabled: false,
      projectFilesEnabled: true,
      sourcePolicySchemaVersion: 2,
      researchPlanRevisionId: PLAN_REVISION_ID,
      researchPlanRevisionNo: 5,
      provider: 'LocalizedProjectFilesEvidenceRefineV1'
    },
    canRefine: true,
    restrictionCode: null,
    changesApplyTo: 'Only evidence for the selected Claim is refreshed.'
  };
}

function taskScopeResponse() {
  return {
    effectivePolicy: {
      webEnabled: false,
      projectFilesEnabled: true,
      policyV2: {
        schemaVersion: 2,
        web: 'Exclude',
        webSite: 'Exclude',
        projectFile: 'Allow',
        connectedApp: 'Exclude',
        items: [
          { kind: 'ProjectFile', sourceId: 'file:11111111111111111111111111111111', state: 'Prioritize' },
          { kind: 'ProjectFile', sourceId: 'file:22222222222222222222222222222222', state: 'Exclude' }
        ]
      }
    },
    origin: 'TaskOverride',
    projectDefaultVersion: 8,
    taskOverrideVersion: 3,
    taskOverridePolicy: null,
    canManage: true,
    latestRun: null,
    changesApplyTo: 'nextRun',
    sourceInventory: [
      { kind: 'ProjectFile', sourceId: 'file:11111111111111111111111111111111', label: 'preferred.md' },
      { kind: 'ProjectFile', sourceId: 'file:22222222222222222222222222222222', label: 'excluded.md' }
    ]
  };
}

describe('ReportReaderPageComponent localized refinement', () => {
  let fixture: ComponentFixture<ReportReaderPageComponent>;
  let http: HttpTestingController;

  beforeEach(async () => {
    window.localStorage.setItem('coglatas.locale', 'en');
    await TestBed.configureTestingModule({
      imports: [ReportReaderPageComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              paramMap: convertToParamMap({
                projectId: PROJECT_ID,
                taskId: TASK_ID,
                artifactVersionId: VERSION_ID
              })
            }
          }
        }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(ReportReaderPageComponent);
    http = TestBed.inject(HttpTestingController);
    http.expectOne(
      `/api/projects/${PROJECT_ID}/artifact-versions/${VERSION_ID}/report?taskId=${TASK_ID}`
    ).flush(reportResponse());
    fixture.detectChanges();
  });

  afterEach(() => {
    try {
      http.verify();
    } finally {
      window.localStorage.removeItem('coglatas.locale');
      TestBed.resetTestingModule();
    }
  });

  it('renders an asynchronous report response without a manual change-detection pass', async () => {
    fixture.destroy();
    fixture = TestBed.createComponent(ReportReaderPageComponent);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('.state[role="status"]')?.textContent).toContain('Loading report');

    http.expectOne(`/api/projects/${PROJECT_ID}/artifact-versions/${VERSION_ID}/report?taskId=${TASK_ID}`)
      .flush(reportResponse());
    await fixture.whenStable();

    expect(element.querySelector('.reader-header h1')?.textContent).toBe('Evidence report');
    expect(getComputedStyle(element.querySelector('.state[role="status"]') as HTMLElement).display).toBe('none');
    expect(getComputedStyle(element.querySelector('.dialog-backdrop') as HTMLElement).display).toBe('none');
    expect(element.querySelector('article.document')).not.toBeNull();
    expect(element.querySelector('main')).toBeNull();
  });

  it('renders an asynchronous report denial without waiting for another user action', async () => {
    fixture.destroy();
    fixture = TestBed.createComponent(ReportReaderPageComponent);
    fixture.detectChanges();
    http.expectOne(`/api/projects/${PROJECT_ID}/artifact-versions/${VERSION_ID}/report?taskId=${TASK_ID}`)
      .flush({}, { status: 403, statusText: 'Forbidden' });
    await fixture.whenStable();

    const element = fixture.nativeElement as HTMLElement;
    expect(getComputedStyle(element.querySelector('.state[role="status"]') as HTMLElement).display).toBe('none');
    expect(getComputedStyle(element.querySelector('.state[role="alert"]') as HTMLElement).display).not.toBe('none');
    expect(getComputedStyle(element.querySelector('.dialog-backdrop') as HTMLElement).display).toBe('none');
  });

  it('restores citation focus and collapsed semantics after rendering the closed inspector', async () => {
    const element = fixture.nativeElement as HTMLElement;
    const citation = element.querySelector('.citation') as HTMLButtonElement;
    citation.click();
    await fixture.whenStable();
    const selected = fixture.componentInstance.selected;
    const inspector = element.querySelector('#evidence-inspector') as HTMLElement;
    expect(citation.getAttribute('aria-expanded')).toBe('true');
    expect(document.activeElement).toBe(inspector);

    (inspector.querySelector('.close') as HTMLButtonElement).click();
    await fixture.whenStable();

    expect(getComputedStyle(inspector).display).toBe('none');
    expect(citation.getAttribute('aria-expanded')).toBe('false');
    expect(document.activeElement).toBe(citation);
    expect(fixture.componentInstance.selected).toBe(selected);
  });

  it('confirms the current itemized source scope before starting a Claim refinement', async () => {
    const citationButton = fixture.nativeElement.querySelector('.citation') as HTMLButtonElement;
    citationButton.click();
    fixture.detectChanges();
    const claimButton = fixture.nativeElement.querySelector('.claim-refine') as HTMLButtonElement;
    claimButton.click();

    const preflightRequest = http.expectOne(
      `/api/projects/${PROJECT_ID}/artifact-versions/${VERSION_ID}/report/refinement-preflight?targetKind=Claim&targetLogicalId=${LOGICAL_CLAIM_ID}`
    );
    expect(preflightRequest.request.method).toBe('GET');
    preflightRequest.flush(preflightResponse());
    http.expectOne(`/api/tasks/${TASK_ID}/execution-scope`).flush(taskScopeResponse());
    await fixture.whenStable();

    const backdrop = fixture.nativeElement.querySelector('.dialog-backdrop') as HTMLElement;
    const dialog = fixture.nativeElement.querySelector('#report-refinement-dialog') as HTMLElement;
    expect(getComputedStyle(backdrop).display).toBe('grid');
    expect(document.activeElement).toBe(dialog);
    expect(dialog.textContent).toContain('Supported claim');
    expect(dialog.textContent).toContain('Revision 5');
    expect(dialog.textContent).toContain('Project 8 · Task override 3');
    expect(dialog.textContent).toContain('Project Files default');
    expect(dialog.textContent).toContain('preferred.md');
    expect(dialog.textContent).toContain('Prioritize');
    expect(dialog.textContent).toContain('excluded.md');
    expect(dialog.textContent).toContain('Exclude');

    fixture.componentInstance.feedback = 'Use the preferred file.';
    fixture.componentInstance.confirmRefinement();
    const request = http.expectOne(
      `/api/projects/${PROJECT_ID}/artifact-versions/${VERSION_ID}/report/refinements`
    );
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({
      confirmedProjectScopeVersion: 8,
      confirmedResearchPlanRevisionId: PLAN_REVISION_ID,
      confirmedResearchPlanRevisionNo: 5,
      confirmedTaskOverrideVersion: 3,
      feedback: 'Use the preferred file.',
      targetKind: 'Claim',
      targetLogicalId: LOGICAL_CLAIM_ID
    });
    request.flush(
      { code: 'ReportRefinementScopeChanged' },
      { status: 409, statusText: 'Conflict' }
    );
    await fixture.whenStable();
    const error = fixture.nativeElement.querySelector('.refinement-toast') as HTMLElement;
    expect(getComputedStyle(error).display).not.toBe('none');
    expect(error.textContent).toContain('The report could not be refined.');
  });

  it('fails closed when the source scope changes between preflight and confirmation', () => {
    const sectionButton = fixture.nativeElement.querySelector('.refine-action') as HTMLButtonElement;
    sectionButton.click();

    const preflight = preflightResponse();
    preflight.targetKind = 'Section';
    preflight.targetLogicalId = LOGICAL_SECTION_ID;
    const preflightRequest = http.expectOne(
      `/api/projects/${PROJECT_ID}/artifact-versions/${VERSION_ID}/report/refinement-preflight?targetKind=Section&targetLogicalId=${LOGICAL_SECTION_ID}`
    );
    expect(preflightRequest.request.method).toBe('GET');
    preflightRequest.flush(preflight);
    const scope = taskScopeResponse();
    scope.projectDefaultVersion = 9;
    http.expectOne(`/api/tasks/${TASK_ID}/execution-scope`).flush(scope);
    fixture.detectChanges();

    const backdrop = fixture.nativeElement.querySelector('.dialog-backdrop') as HTMLElement;
    expect(backdrop.hidden).toBe(true);
    expect(fixture.nativeElement.textContent).toContain('The source scope changed during confirmation.');
    http.expectNone(`/api/projects/${PROJECT_ID}/artifact-versions/${VERSION_ID}/report/refinements`);
  });
  it('hot-switches the mounted report and refinement while retaining authored content and feedback', () => {
    const i18n = TestBed.inject(I18nService);
    i18n.setLocale('ja');
    fixture.detectChanges();
    const report = fixture.componentInstance.report;
    (fixture.nativeElement.querySelector('.citation') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('#evidence-inspector').getAttribute('aria-label')).toBe('根拠インスペクター');
    (fixture.nativeElement.querySelector('.claim-refine') as HTMLButtonElement).click();
    http.expectOne((request) => request.url.includes('/refinement-preflight?')).flush(preflightResponse());
    http.expectOne(`/api/tasks/${TASK_ID}/execution-scope`).flush(taskScopeResponse());
    fixture.detectChanges();
    const input = fixture.nativeElement.querySelector('textarea') as HTMLTextAreaElement;
    input.value = 'English と日本語の入力';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    const selected = fixture.componentInstance.selected;
    const sources = fixture.componentInstance.refinementSources;
    const dialog = fixture.nativeElement.querySelector('#report-refinement-dialog') as HTMLElement;
    expect(dialog.getAttribute('aria-label')).toBe('部分再調査を確認');
    expect(dialog.textContent).toContain('プロジェクトファイルの既定');
    expect(dialog.textContent).toContain('優先');
    i18n.setLocale('en');
    fixture.detectChanges();
    expect(dialog.getAttribute('aria-label')).toBe('Confirm localized refinement');
    expect(dialog.textContent).toContain('Project Files default');
    expect(dialog.textContent).toContain('Revision 5');
    expect(dialog.textContent).toContain('Prioritize');
    expect(input.value).toBe('English と日本語の入力');
    expect(input.placeholder).toContain('For example');
    expect(fixture.componentInstance.report).toBe(report);
    expect(fixture.componentInstance.selected).toBe(selected);
    expect(fixture.componentInstance.refinementSources).toBe(sources);
    expect(report.title).toBe('Evidence report');
    expect(report.sections[0].runs[0].text).toBe('Supported claim');
    expect(dialog.textContent).toContain('preferred.md');
    expect(fixture.componentInstance.inspectorOpen).toBe(true);
    expect(fixture.componentInstance.refinementDialogOpen).toBe(true);
    expect(http.match(() => true)).toHaveLength(0);
    fixture.componentInstance.confirmRefinement();
    const post = http.expectOne((request) => request.method === 'POST');
    expect(post.request.body.targetKind).toBe('Claim');
    expect(post.request.body.feedback).toBe('English と日本語の入力');
    post.flush({}, { status: 409, statusText: 'Conflict' });
    i18n.setLocale('ja');
    fixture.detectChanges();
    expect(fixture.componentInstance.refinementError).toContain('再調査できませんでした');
    i18n.setLocale('en');
    fixture.detectChanges();
    expect(fixture.componentInstance.refinementError).toContain('The report could not be refined.');
    expect(http.match(() => true)).toHaveLength(0);
  });

});
