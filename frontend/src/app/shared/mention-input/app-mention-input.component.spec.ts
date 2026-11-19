import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { vi } from 'vitest';

import { AppMentionInputComponent } from './app-mention-input.component';

const taskId = '11111111-1111-4111-8111-111111111111';
const anotherTaskId = '22222222-2222-4222-8222-222222222222';
const userId = '33333333-3333-4333-8333-333333333333';

describe('AppMentionInputComponent canonical mention boundaries', () => {
  let fixture: ComponentFixture<AppMentionInputComponent>;
  let component: AppMentionInputComponent;
  let http: HttpTestingController;

  beforeEach(async () => {
    localStorage.setItem('coglatas.locale', 'en');
    await TestBed.configureTestingModule({
      imports: [AppMentionInputComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()]
    }).compileComponents();
    fixture = TestBed.createComponent(AppMentionInputComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('taskId', taskId);
    fixture.detectChanges();
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
    fixture.destroy();
    TestBed.resetTestingModule();
    localStorage.removeItem('coglatas.locale');
  });

  function editor(): HTMLTextAreaElement {
    const textarea = (fixture.nativeElement as HTMLElement).querySelector<HTMLTextAreaElement>('textarea');
    expect(textarea).not.toBeNull();
    return textarea!;
  }

  function edit(next: string): void {
    const textarea = editor();
    textarea.value = next;
    textarea.setSelectionRange(next.length, next.length);
    textarea.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();
  }

  it('renders canonical mention identities using known display names and stable ARIA targets', () => {
    fixture.componentRef.setInput('knownMentions', [{ userId, displayName: 'Alex' }]);
    fixture.componentRef.setInput('value', 'Hi @{' + userId + '}!');
    fixture.componentRef.setInput('inputId', 'comment-editor');
    fixture.detectChanges();
    expect(editor().value).toBe('Hi @Alex!');
    expect(editor().id).toBe('comment-editor');
    expect(component.listboxId).toBe('comment-editor-candidates');
    expect(component.statusId).toBe('comment-editor-status');
    expect(component.activeOptionId()).toBeNull();
    expect((fixture.nativeElement as HTMLElement).querySelector('[role="listbox"]')).toBeNull();
  });

  it('preserves an unchanged token while extending adjacent plaintext', () => {
    fixture.componentRef.setInput('knownMentions', [{ userId, displayName: 'Alex' }]);
    fixture.componentRef.setInput('value', 'Hi @{' + userId + '}!');
    fixture.detectChanges();
    const emitted: string[] = [];
    component.valueChange.subscribe(value => emitted.push(value));
    edit('Hi @Alex!!');
    expect(component.displayValue()).toBe('Hi @Alex!!');
    expect(emitted).toEqual(['Hi @{' + userId + '}!!']);
  });

  it('drops canonical identity when the displayed mention itself is modified', () => {
    fixture.componentRef.setInput('knownMentions', [{ userId, displayName: 'Alex' }]);
    fixture.componentRef.setInput('value', 'Hi @{' + userId + '}!');
    fixture.detectChanges();
    const emitted: string[] = [];
    component.valueChange.subscribe(value => emitted.push(value));
    edit('Hi @Al!');
    expect(emitted).toEqual(['Hi @Al!']);
  });

  it('does not treat a missing display-name lookup as a trusted identity label', () => {
    fixture.componentRef.setInput('value', 'Hi @{' + userId + '}!');
    fixture.detectChanges();
    expect(editor().value).toBe('Hi @Mention!');
    expect(component.displayValue()).toBe('Hi @Mention!');
  });

  it('bounds keyboard navigation and clears locally displayed candidates', () => {
    component.candidates.set([{ userId, displayName: 'Alex' }, {
      userId: anotherTaskId, displayName: 'Jordan'
    }]);
    fixture.detectChanges();
    const down = new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true, cancelable: true });
    editor().dispatchEvent(down);
    expect(down.defaultPrevented).toBe(true);
    expect(component.selectedIndex()).toBe(1);
    expect(component.activeOptionId()).toBe(component.optionId(1));

    editor().dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true, cancelable: true }));
    expect(component.selectedIndex()).toBe(1);
    editor().dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowUp', bubbles: true, cancelable: true }));
    expect(component.selectedIndex()).toBe(0);
    editor().dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }));
    fixture.detectChanges();
    expect(component.candidates()).toEqual([]);
    expect(component.activeOptionId()).toBeNull();
  });

  it('uses a credentialed, scoped mention query and emits canonical tokens on selection', async () => {
    const emitted: string[] = [];
    component.valueChange.subscribe(value => emitted.push(value));
    edit('Hi @Al');
    const request = await vi.waitFor(
      () => http.expectOne('/api/tasks/' + taskId + '/mention-candidates?query=Al&limit=10'),
      { timeout: 5000 }
    );
    expect(request.request.withCredentials).toBe(true);
    expect(request.request.method).toBe('GET');
    request.flush([{ userId, displayName: 'Alex' }]);
    fixture.detectChanges();
    expect(component.candidates()).toEqual([{ userId, displayName: 'Alex' }]);

    const button = (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('[role="option"] button');
    expect(button).not.toBeNull();
    button!.click();
    fixture.detectChanges();
    await Promise.resolve();
    expect(emitted.at(-1)).toBe('Hi @{' + userId + '}');
    expect(component.displayValue()).toBe('Hi @Alex');
    expect(component.candidates()).toEqual([]);
  });

  it.each([
    { code: 403, key: 'mention.denied' },
    { code: 429, key: 'mention.rateLimited' },
    { code: 500, key: 'mention.error' }
  ] as const)('shows authorization, rate-limit and server failures without stale suggestions: $code', async ({ code, key }) => {
    edit('Hi @Al');
    const request = await vi.waitFor(
      () => http.expectOne('/api/tasks/' + taskId + '/mention-candidates?query=Al&limit=10'),
      { timeout: 5000 }
    );
    expect(request.request.withCredentials).toBe(true);
    request.flush({ message: 'Controlled test error' }, { status: code, statusText: 'Denied' });
    fixture.detectChanges();
    expect(component.candidates()).toEqual([]);
    expect(component.activeOptionId()).toBeNull();
    expect(component.status()).toBe(component.i18n.translate(key));
    expect((fixture.nativeElement as HTMLElement).querySelector('[role="listbox"]')).toBeNull();
  });

  it('clears a completed suggestion list after a real caret movement', async () => {
    edit('Hi @Al');
    const request = await vi.waitFor(
      () => http.expectOne('/api/tasks/' + taskId + '/mention-candidates?query=Al&limit=10'),
      { timeout: 5000 }
    );
    request.flush([{ userId, displayName: 'Alex' }]);
    fixture.detectChanges();
    expect(component.candidates()).toHaveLength(1);

    const textarea = editor();
    textarea.setSelectionRange(0, 0);
    textarea.dispatchEvent(new Event('click', { bubbles: true }));
    fixture.detectChanges();

    expect(component.candidates()).toEqual([]);
    expect(component.activeOptionId()).toBeNull();
    expect(component.status()).toBe(component.i18n.translate('mention.caretMoved'));
  });

  it('ignores a late suggestion after the authoritative task scope changes', async () => {
    edit('Hi @Al');
    const request = await vi.waitFor(
      () => http.expectOne('/api/tasks/' + taskId + '/mention-candidates?query=Al&limit=10'),
      { timeout: 5000 }
    );
    fixture.componentRef.setInput('taskId', anotherTaskId);
    fixture.detectChanges();
    request.flush([{ userId, displayName: 'Alex' }]);
    fixture.detectChanges();
    expect(component.candidates()).toEqual([]);
    expect(component.activeOptionId()).toBeNull();
  });
});
