import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { of } from 'rxjs';
import { PublicSurvey } from './aq-api';
import { AqPublicSurvey } from './aq-public-survey';

const survey: PublicSurvey = {
  title: '発表アンケート',
  versionRef: 'published-version',
  schemaDigest: '0'.repeat(64),
  fields: [{ fieldKey: 'feedback', label: '感想', required: true, type: 'text' }],
  nameMode: 'anonymous',
  accountOptInAvailable: false,
  receptionState: 'open',
  localMarkerKey: 'fixture-marker',
  notice: '公開アンケートです。',
};

describe('AqPublicSurvey', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [AqPublicSurvey],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ActivatedRoute, useValue: { paramMap: of(convertToParamMap({ code: 'survey-code' })) } },
      ],
    });
  });

  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
    localStorage.removeItem('coglatas.aq.submitted.fixture-marker');
    TestBed.resetTestingModule();
  });

  it('submits an anonymous answer with antiforgery and records a marker after the receipt', () => {
    const fixture = TestBed.createComponent(AqPublicSurvey);
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/v1/public/surveys/survey-code').flush({ data: survey });
    fixture.detectChanges();
    const form = fixture.nativeElement.querySelector('form') as HTMLFormElement;
    const answer = form.querySelector('textarea')!;
    answer.value = '合成データ';
    answer.dispatchEvent(new Event('input'));
    form.dispatchEvent(new Event('submit', { cancelable: true }));

    http.expectOne('/api/security/csrf-token').flush({ token: 'test-token', headerName: 'X-CSRF-Token' });
    const request = http.expectOne('/api/v1/public/surveys/survey-code/submissions');
    expect(request.request.body).toEqual({
      versionRef: survey.versionRef, schemaDigest: survey.schemaDigest,
      answers: [{ fieldKey: 'feedback', value: '合成データ' }],
    });
    expect(request.request.headers.get('X-CSRF-Token')).toBe('test-token');
    expect(request.request.headers.get('Idempotency-Key')).toMatch(/^[0-9a-f-]{36}$/);
    expect(localStorage.getItem('coglatas.aq.submitted.fixture-marker')).toBeNull();
    request.flush({ data: { accepted: true } }, { status: 201, statusText: 'Created' });
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('視聴者向けフォームの回答を受け付けました');
    expect(localStorage.getItem('coglatas.aq.submitted.fixture-marker')).toBe('1');
  });

  it('counts entered questions without treating false, zero or cleared choices as the same value', () => {
    const fixture = TestBed.createComponent(AqPublicSurvey);
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/v1/public/surveys/survey-code').flush({
      data: {
        ...survey,
        fields: [
          ...survey.fields,
          { fieldKey: 'boolean', label: 'Boolean', type: 'boolean', required: false },
          { fieldKey: 'number', label: 'Number', type: 'number', required: false },
          { fieldKey: 'choices', label: 'Choices', type: 'multipleChoice', required: false,
            options: [{ optionKey: 'first', label: 'First' }] },
        ],
      },
    });
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    const progress = root.querySelector('progress')!;
    const answer = root.querySelector('textarea')!;
    const number = root.querySelector<HTMLInputElement>('input[type="number"]')!;
    const choice = root.querySelector<HTMLInputElement>('input[type="checkbox"]')!;
    expect(progress.max).toBe(4);
    expect(progress.value).toBe(0);
    answer.value = '  ';
    answer.dispatchEvent(new Event('input'));
    root.querySelectorAll<HTMLInputElement>('input[type="radio"]')[1].click();
    number.value = '0';
    number.dispatchEvent(new Event('input'));
    choice.click();
    fixture.detectChanges();
    expect(progress.value).toBe(3);
    choice.click();
    number.value = '';
    number.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    expect(progress.value).toBe(1);
    http.expectNone(request => request.method === 'POST');
  });

  it('retains the same intent after a connection failure', () => {
    const fixture = TestBed.createComponent(AqPublicSurvey);
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/v1/public/surveys/survey-code').flush({ data: survey });
    fixture.detectChanges();
    const form = fixture.nativeElement.querySelector('form') as HTMLFormElement;
    const answer = form.querySelector('textarea')!;
    answer.value = '再試行するデータ';
    answer.dispatchEvent(new Event('input'));
    form.dispatchEvent(new Event('submit', { cancelable: true }));
    http.expectOne('/api/security/csrf-token').flush({ token: 'test-token' });
    const first = http.expectOne('/api/v1/public/surveys/survey-code/submissions');
    first.error(new ProgressEvent('error'));
    fixture.detectChanges();
    expect(localStorage.getItem('coglatas.aq.submitted.fixture-marker')).toBeNull();
    const retry = Array.from(fixture.nativeElement.querySelectorAll('button') as NodeListOf<HTMLButtonElement>)
      .find(button => button.textContent?.includes('同じ回答を再試行'));
    expect(retry).toBeDefined();
    retry!.click();
    const second = http.expectOne('/api/v1/public/surveys/survey-code/submissions');
    expect(second.request.headers.get('Idempotency-Key')).toBe(first.request.headers.get('Idempotency-Key'));
    expect(second.request.body).toEqual(first.request.body);
    second.flush({ data: { accepted: true } }, { status: 201, statusText: 'Created' });
  });
});
