import { I18nService } from '../../../core/i18n/i18n.service';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { MessageComposerComponent } from './message-composer.component';

beforeEach(() => { localStorage.setItem('coglatas.locale', 'ja'); });
afterEach(() => { localStorage.removeItem('coglatas.locale'); });

describe('MessageComposerComponent', () => {
  afterEach(() => {
    TestBed.resetTestingModule();
  });

  async function createComposer(draft = 'Hello'): Promise<ComponentFixture<MessageComposerComponent>> {
    await TestBed.configureTestingModule({
      imports: [MessageComposerComponent]
    }).compileComponents();

    const fixture = TestBed.createComponent(MessageComposerComponent);
    fixture.componentRef.setInput('draft', draft);
    fixture.detectChanges();
    return fixture;
  }

  it('switches mounted composer labels without changing its draft, mention IDs, focus, or emitting a send', async () => {
    const fixture = await createComposer('メモ / Keep this draft');
    fixture.componentRef.setInput('mentionCandidates', [{ userId: 'alice-id', displayName: 'Alice' }]);
    fixture.detectChanges();
    const i18n = TestBed.inject(I18nService);
    const root = fixture.nativeElement as HTMLElement;
    const textarea = root.querySelector<HTMLTextAreaElement>('[data-testid="message-draft"]')!;
    const send = vi.spyOn(fixture.componentInstance.send, 'emit');
    const draft = vi.spyOn(fixture.componentInstance.draftChange, 'emit');
    textarea.focus();
    expect(root.querySelector('[data-testid="send-message"]')?.textContent?.trim()).toBe('送信');
    expect(textarea.placeholder).toBe('本文を入力');

    i18n.setLocale('en');
    fixture.detectChanges();
    expect(root.querySelector('[data-testid="send-message"]')?.textContent?.trim()).toBe('Send');
    expect(textarea.placeholder).toBe('Enter a message');
    expect(root.querySelector('[data-testid="mention-candidate"]')?.textContent).toContain('Add @Alice');
    expect(root.querySelector('[data-testid="message-draft"]')).toBe(textarea);
    expect(textarea.value).toBe('メモ / Keep this draft');
    expect(document.activeElement).toBe(textarea);
    expect(send).not.toHaveBeenCalled();
    expect(draft).not.toHaveBeenCalled();

    i18n.setLocale('ja');
    fixture.detectChanges();
    expect(root.querySelector('[data-testid="send-message"]')?.textContent?.trim()).toBe('送信');
    expect(textarea.value).toBe('メモ / Keep this draft');
    expect(send).not.toHaveBeenCalled();
    expect(draft).not.toHaveBeenCalled();
  });

  it('renders Send as the primary action and keeps helper tools secondary', async () => {
    const fixture = await createComposer();
    const root = fixture.nativeElement as HTMLElement;
    const sendButton = root.querySelector<HTMLButtonElement>('[data-testid="send-message"]');

    expect(sendButton).not.toBeNull();
    expect(sendButton?.classList.contains('composer__send')).toBe(true);
    expect(sendButton?.getAttribute('type')).toBe('submit');
    expect(root.querySelector('[data-testid="composer-secondary-tools"]')).not.toBeNull();
    expect(root.textContent).toContain('Enterで送信');
    expect(root.textContent).toContain('Shift+Enterで改行');
  });

  it('sends on Enter and leaves Shift+Enter available for a newline', async () => {
    const fixture = await createComposer();
    const sendSpy = vi.spyOn(fixture.componentInstance.send, 'emit');

    const newlineEvent = new KeyboardEvent('keydown', {
      key: 'Enter',
      shiftKey: true,
      cancelable: true
    });
    fixture.componentInstance.onDraftKeydown(newlineEvent);

    expect(newlineEvent.defaultPrevented).toBe(false);
    expect(sendSpy).not.toHaveBeenCalled();

    const sendEvent = new KeyboardEvent('keydown', {
      key: 'Enter',
      cancelable: true
    });
    fixture.componentInstance.onDraftKeydown(sendEvent);

    expect(sendEvent.defaultPrevented).toBe(true);
    expect(sendSpy).toHaveBeenCalledTimes(1);
  });

  it('does not send Enter while IME composition is active', async () => {
    const fixture = await createComposer();
    const sendSpy = vi.spyOn(fixture.componentInstance.send, 'emit');

    fixture.componentInstance.onCompositionStart();
    fixture.componentInstance.onDraftKeydown(
      new KeyboardEvent('keydown', { key: 'Enter', cancelable: true })
    );
    expect(sendSpy).not.toHaveBeenCalled();

    fixture.componentInstance.onCompositionEnd();
    fixture.componentInstance.onDraftKeydown(
      new KeyboardEvent('keydown', { key: 'Enter', cancelable: true })
    );
    expect(sendSpy).toHaveBeenCalledTimes(1);
  });

  it('shows sending state and prevents another submit while sending', async () => {
    const fixture = await createComposer();
    const sendSpy = vi.spyOn(fixture.componentInstance.send, 'emit');

    fixture.componentRef.setInput('sending', true);
    fixture.componentRef.setInput('sendState', { status: 'sending', clientRequestId: 'request-a' });
    fixture.detectChanges();

    const root = fixture.nativeElement as HTMLElement;
    const sendButton = root.querySelector<HTMLButtonElement>('[data-testid="send-message"]');

    expect(root.querySelector('[data-testid="composer-send-status"]')?.textContent).toContain(
      '送信しています'
    );
    expect(sendButton?.disabled).toBe(true);

    fixture.componentInstance.submit(new Event('submit', { cancelable: true }));
    expect(sendSpy).not.toHaveBeenCalled();
  });

  it('shows send failure beside the draft with retry guidance and keeps the draft', async () => {
    const fixture = await createComposer('Do not clear me');
    fixture.componentRef.setInput('sendState', {
      status: 'failed',
      message: 'Message API request failed.'
    });
    fixture.detectChanges();

    const root = fixture.nativeElement as HTMLElement;
    const error = root.querySelector('[data-testid="composer-send-error"]');
    const textarea = root.querySelector<HTMLTextAreaElement>('[data-testid="message-draft"]');

    expect(error?.textContent).toContain('Message API request failed.');
    expect(error?.textContent).toContain('再試行');
    expect(textarea?.value).toBe('Do not clear me');
  });

  it('adds an explicit mention token and emits the canonical mentioned user id on send', async () => {
    const fixture = await createComposer('Please review');
    fixture.componentRef.setInput('mentionCandidates', [
      { userId: 'user-a', displayName: 'Alice' }
    ]);
    fixture.detectChanges();

    const draftSpy = vi.spyOn(fixture.componentInstance.draftChange, 'emit');
    const sendSpy = vi.spyOn(fixture.componentInstance.send, 'emit');
    const root = fixture.nativeElement as HTMLElement;
    const mentionButton = root.querySelector<HTMLButtonElement>('[data-testid="mention-candidate"]');

    expect(mentionButton?.textContent).toContain('@Alice');
    mentionButton?.click();
    fixture.detectChanges();

    expect(draftSpy).toHaveBeenCalledWith('Please review @Alice ');
    expect(mentionButton?.getAttribute('aria-pressed')).toBe('true');

    fixture.componentInstance.submit(new Event('submit', { cancelable: true }));
    expect(sendSpy).toHaveBeenCalledWith(['user-a']);
  });

  it('drops canonical mention state when the visible mention token is removed from the draft', async () => {
    const fixture = await createComposer('Please review');
    fixture.componentRef.setInput('mentionCandidates', [
      { userId: 'user-a', displayName: 'Alice' }
    ]);
    fixture.detectChanges();

    const root = fixture.nativeElement as HTMLElement;
    root.querySelector<HTMLButtonElement>('[data-testid="mention-candidate"]')?.click();
    fixture.detectChanges();

    fixture.componentInstance.onDraftInput({
      target: { value: 'Please review' }
    } as unknown as Event);

    const sendSpy = vi.spyOn(fixture.componentInstance.send, 'emit');
    fixture.componentInstance.submit(new Event('submit', { cancelable: true }));
    expect(sendSpy).toHaveBeenCalledWith([]);
  });
});
