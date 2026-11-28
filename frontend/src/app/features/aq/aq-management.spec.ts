import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { of, Subject } from 'rxjs';
import { AqApi, SurveyField } from './aq-api';
import { AqManagement } from './aq-management';

describe('AqManagement workspace navigation', () => {
  const fields: SurveyField[] = [
    { fieldKey: 'first', label: 'First', type: 'text', required: true },
    { fieldKey: 'second', label: 'Second', type: 'singleChoice', required: false,
      options: [{ optionKey: 'a', label: 'Option A' }, { optionKey: 'b', label: 'Option B' }] },
  ];
  const command = vi.fn();
  let shareCode = 'a';
  const surveyUrl = () => 'https://example.test/app/aq/surveys/' + shareCode.repeat(64);
  const read = vi.fn((path: string) => {
    switch (path) {
      case 'events/event/feedback-settings':
        return of({ versionNo: 1, programCodeState: 'unissued', settings: {
          nameMode: 'anonymous', publicFields: [], programTargets: [], programPublished: false,
          receptionStopped: false, resultsStopped: false, accountBindingAllowed: false, timeZone: 'Asia/Tokyo',
        } });
      case 'events/event/presentations':
        return of({ items: [{ presentation: { id: 'presentation', title: '発表 A' }, versionNo: 1 }] });
      case 'events/event/survey-campaigns':
        return of({ items: [{ id: 'campaign', formDefinitionId: 'form', enabled: true, published: true, versionNo: 1 }] });
      case 'survey-campaigns/campaign/targets':
        return of({ items: [{ id: 'target', presentationId: 'presentation', versionNo: 1, settings: { enabled: true, resultsStopped: false } }] });
      case 'survey-campaigns/campaign/qr-share':
        return of({ title: '発表 A', surveyUrl: surveyUrl(),
          pngBase64: 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aW5kAAAAASUVORK5CYII=' });
      case 'forms':
        return of({ items: [{ id: 'form', title: 'Form', versionNo: 1 }] });
      case 'forms/form/versions':
        return of({ items: [{ id: 'version', versionNo: 1, state: 'published' }] });
      case 'forms/form/versions/version':
        return of({ fields });
      default:
        return of({ items: [] });
    }
  });

  beforeEach(() => {
    command.mockReset();
    shareCode = 'a';
    read.mockClear();
    TestBed.configureTestingModule({
      providers: [
        provideRouter([{ path: 'manage/:eventId', component: AqManagement }]),
        { provide: AqApi, useValue: { read, command, eventScope: () => of({ projectId: 'project' }) } },
      ],
    });
  });

  it('provides five keyboard-operable section shortcuts without hiding reception controls', async () => {
    const harness = await RouterTestingHarness.create('/manage/event');
    const root = harness.routeNativeElement as HTMLElement;
    const navigation = root.querySelector('nav.aq-management-nav')!;
    const buttons = navigation.querySelectorAll<HTMLButtonElement>('button');
    expect(buttons).toHaveLength(5);
    buttons[2].click();
    expect(document.activeElement?.id).toBe('aq-management-questions');
    expect(root.querySelector('#aq-management-reception')).not.toBeNull();
    expect(root.querySelector('#aq-management-reception')?.closest('section')?.hidden).toBe(false);
    expect(command).not.toHaveBeenCalled();
  });

  it('reorders the local question draft while retaining labels, options and stable field identities until publication', async () => {
    const harness = await RouterTestingHarness.create('/manage/event');
    const root = harness.routeNativeElement as HTMLElement;
    function select(testId: string, value: string) {
      const control = root.querySelector<HTMLSelectElement>('[data-testid="' + testId + '"]')!;
      control.value = value;
      control.dispatchEvent(new Event('change'));
      harness.detectChanges();
    }
    select('aq-common-form', 'form');
    select('aq-form-version', 'version');
    const getUp = () => Array.from(root.querySelectorAll<HTMLButtonElement>('[data-testid="aq-question-move-up"]'));
    const getDown = () => Array.from(root.querySelectorAll<HTMLButtonElement>('[data-testid="aq-question-move-down"]'));
    expect(getUp()[0].disabled).toBe(true);
    expect(getDown()[1].disabled).toBe(true);
    expect(getUp()[1].getAttribute('aria-label')).toContain('Second');
    getUp()[1].click();
    harness.detectChanges();
    expect(getUp()[0].getAttribute('aria-label')).toContain('Second');
    expect(getUp()[1].getAttribute('aria-label')).toContain('First');
    expect(root.textContent).toContain('Option A');
    expect(root.textContent).toContain('Option B');
    expect(fields.map(field => field.fieldKey)).toEqual(['first', 'second']);
    expect(command).not.toHaveBeenCalled();
  });
  it('shares only the selected survey target and replaces the old QR after a successful rotation', async () => {
    const harness = await RouterTestingHarness.create('/manage/event');
    const root = harness.routeNativeElement as HTMLElement;
    const distribution = () => root.querySelector('#aq-management-distribution')!.closest('section')!;
    const findButton = (text: string) => Array.from(distribution().querySelectorAll('button'))
      .find(button => button.textContent?.includes(text))!;
    function choose(value: string): void {
      const control = Array.from(distribution().querySelectorAll('select'))
        .find(select => Array.from(select.options).some(option => option.value === value))!;
      control.value = value;
      control.dispatchEvent(new Event('change'));
      harness.detectChanges();
    }
    expect(root.querySelector('app-aq-qr-share')).toBeNull();
    choose('campaign');
    expect(findButton('QR画像 PNG').disabled).toBe(true);
    expect(root.querySelector('app-aq-qr-share')).toBeNull();
    choose('target');
    await harness.fixture.whenStable();
    harness.detectChanges();
    expect(findButton('QR画像 PNG').disabled).toBe(false);
    expect(root.querySelector<HTMLInputElement>('app-aq-qr-share input[type=url]')?.value).toBe(surveyUrl());
    expect(read).toHaveBeenCalledWith('survey-campaigns/campaign/qr-share', { targetId: 'target' });

    const rotation = new Subject<unknown>();
    command.mockReturnValueOnce(rotation);
    findButton('回答リンクを発行または更新').click();
    harness.detectChanges();
    expect(command).toHaveBeenCalledWith('POST', 'survey-targets/target/public-code/rotate', { expectedVersionNo: 1 });
    expect(findButton('QR画像 PNG').disabled).toBe(true);
    expect(root.querySelector('app-aq-qr-share input[type=url]')).toBeNull();
    shareCode = 'b';
    rotation.next({ entryRef: 'b'.repeat(64) });
    rotation.complete();
    await harness.fixture.whenStable();
    harness.detectChanges();
    expect(root.querySelector<HTMLInputElement>('app-aq-qr-share input[type=url]')?.value).toBe(surveyUrl());
    expect(root.querySelector('app-aq-qr-share input[type=url]')?.getAttribute('value')).not.toContain('a'.repeat(64));
  });

});
