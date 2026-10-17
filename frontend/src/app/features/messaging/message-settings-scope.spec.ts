import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { ConversationListComponent } from './conversation-list/conversation-list.component';
import { ConversationSettingsPanelComponent } from './conversation-settings-panel/conversation-settings-panel.component';
import { MessageGlobalSettingsService } from './message-global-settings.service';
import { MessageSettingsPageComponent } from './message-settings-page/message-settings-page.component';

// These existing contract assertions intentionally select the English UI.
beforeEach(() => { localStorage.setItem('coglatas.locale', 'en'); });
afterEach(() => { localStorage.removeItem('coglatas.locale'); });

describe('Message settings scope separation', () => {
  afterEach(() => {
    TestBed.inject(HttpTestingController, null)?.verify();
    globalThis.localStorage?.clear();
    localStorage.setItem('coglatas.locale', 'en');
    TestBed.resetTestingModule();
  });

  it('updates mute only through the currently opened conversation state endpoint', async () => {
    await TestBed.configureTestingModule({
      imports: [ConversationSettingsPanelComponent],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()]
    }).compileComponents();

    const httpMock = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(ConversationSettingsPanelComponent);
    fixture.componentRef.setInput('conversationId', 'conversation-a');
    fixture.componentRef.setInput('conversationTitle', 'General');
    fixture.detectChanges();

    const root = fixture.nativeElement as HTMLElement;
    root.querySelector<HTMLButtonElement>('[data-testid="conversation-settings-trigger"]')?.click();

    const stateRequest = httpMock.expectOne('/api/conversations/conversation-a/state');
    expect(stateRequest.request.method).toBe('GET');
    expect(stateRequest.request.withCredentials).toBe(true);
    stateRequest.flush({ conversationId: 'conversation-a', isMuted: false });
    fixture.detectChanges();

    const select = root.querySelector<HTMLSelectElement>('[data-testid="conversation-notification-level"]');
    expect(select?.value).toBe('all');
    select!.value = 'muted';
    select!.dispatchEvent(new Event('change'));

    const saveRequest = httpMock.expectOne('/api/conversations/conversation-a/state');
    expect(saveRequest.request.method).toBe('PATCH');
    expect(saveRequest.request.body).toEqual({ isMuted: true });
    expect(saveRequest.request.withCredentials).toBe(true);
    saveRequest.flush({ conversationId: 'conversation-a', isMuted: true });
    fixture.detectChanges();

    expect(root.querySelector('[data-testid="conversation-settings-panel"]')?.textContent).toContain(
      'this conversation only'
    );
    expect(root.textContent).toContain('Muted');
  });

  it('moves focus into conversation settings and restores it when Escape closes the dialog', async () => {
    await TestBed.configureTestingModule({
      imports: [ConversationSettingsPanelComponent],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()]
    }).compileComponents();

    const httpMock = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(ConversationSettingsPanelComponent);
    fixture.componentRef.setInput('conversationId', 'conversation-a');
    fixture.componentRef.setInput('conversationTitle', 'General');
    document.body.appendChild(fixture.nativeElement);
    fixture.detectChanges();

    const root = fixture.nativeElement as HTMLElement;
    const trigger = root.querySelector<HTMLButtonElement>('[data-testid="conversation-settings-trigger"]')!;
    trigger.focus();
    trigger.click();
    fixture.detectChanges();
    httpMock.expectOne('/api/conversations/conversation-a/state').flush({
      conversationId: 'conversation-a',
      isMuted: false
    });
    fixture.detectChanges();
    await Promise.resolve();

    expect(document.activeElement).toBe(
      root.querySelector<HTMLButtonElement>('[data-testid="conversation-settings-close"]')
    );

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }));
    fixture.detectChanges();
    await Promise.resolve();

    expect(root.querySelector('[data-testid="conversation-settings-panel"]')).toBeNull();
    expect(document.activeElement).toBe(trigger);
    fixture.destroy();
  });

  it('focuses the existing close control when settings reopen before the closed view renders', async () => {
    await TestBed.configureTestingModule({
      imports: [ConversationSettingsPanelComponent],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()]
    }).compileComponents();

    const httpMock = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(ConversationSettingsPanelComponent);
    fixture.componentRef.setInput('conversationId', 'conversation-a');
    fixture.componentRef.setInput('conversationTitle', 'General');
    document.body.appendChild(fixture.nativeElement);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    const trigger = root.querySelector<HTMLButtonElement>('[data-testid="conversation-settings-trigger"]')!;
    trigger.click();
    fixture.detectChanges();
    httpMock.expectOne('/api/conversations/conversation-a/state').flush({
      conversationId: 'conversation-a', isMuted: false
    });
    fixture.detectChanges();
    await Promise.resolve();
    const close = root.querySelector<HTMLButtonElement>('[data-testid="conversation-settings-close"]')!;
    expect(document.activeElement).toBe(close);

    close.click();
    trigger.focus();
    trigger.click();
    fixture.detectChanges();
    await Promise.resolve();

    expect(root.querySelector('[data-testid="conversation-settings-close"]')).toBe(close);
    expect(document.activeElement).toBe(close);
    expect(fixture.componentInstance.open()).toBe(true);
    httpMock.expectNone('/api/conversations/conversation-a/state');
    fixture.destroy();
  });

  it('rejects a participant-state response for a different conversation', async () => {
    await TestBed.configureTestingModule({
      imports: [ConversationSettingsPanelComponent],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()]
    }).compileComponents();

    const httpMock = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(ConversationSettingsPanelComponent);
    fixture.componentRef.setInput('conversationId', 'conversation-a');
    fixture.componentRef.setInput('conversationTitle', 'General');
    fixture.detectChanges();

    (fixture.nativeElement as HTMLElement)
      .querySelector<HTMLButtonElement>('[data-testid="conversation-settings-trigger"]')
      ?.click();
    httpMock.expectOne('/api/conversations/conversation-a/state').flush({
      conversationId: 'conversation-b',
      isMuted: true
    });
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('response was incomplete');
    expect(fixture.componentInstance.isMuted()).toBe(false);
  });

  it('confirms and persists the tenant-wide Message notification setting before local display changes', async () => {
    globalThis.localStorage?.clear();
    localStorage.setItem('coglatas.locale', 'en');
    await TestBed.configureTestingModule({
      imports: [MessageSettingsPageComponent],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()]
    }).compileComponents();

    const httpMock = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(MessageSettingsPageComponent);
    const settings = TestBed.inject(MessageGlobalSettingsService);
    httpMock.expectOne('/api/me/message-notification-preferences').flush({ messageNotificationsEnabled: true });
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;

    const notificationCheckbox = root.querySelector<HTMLInputElement>('[data-testid="global-message-notifications"]');
    const badgeCheckbox = root.querySelector<HTMLInputElement>('[data-testid="global-show-unread-badges"]');
    notificationCheckbox!.checked = false;
    notificationCheckbox!.dispatchEvent(new Event('change'));
    badgeCheckbox!.checked = false;
    badgeCheckbox!.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    root.querySelector<HTMLButtonElement>('[data-testid="save-global-message-settings"]')?.click();
    fixture.detectChanges();
    expect(settings.showUnreadBadges()).toBe(true);
    expect(root.querySelector('[data-testid="global-settings-confirmation"]')).not.toBeNull();
    expect(root.textContent).toContain('Individual conversation mute settings will not be changed');
    httpMock.expectNone('/api/me/message-notification-preferences');

    const applyButton = Array.from(
      root.querySelectorAll<HTMLButtonElement>('[data-testid="global-settings-confirmation"] button')
    ).find((button) => button.textContent?.includes('Apply global change'));
    applyButton?.click();

    const saveRequest = httpMock.expectOne('/api/me/message-notification-preferences');
    expect(saveRequest.request.method).toBe('PATCH');
    expect(saveRequest.request.body).toEqual({ messageNotificationsEnabled: false });
    expect(saveRequest.request.withCredentials).toBe(true);
    expect(settings.showUnreadBadges()).toBe(true);
    saveRequest.flush({ messageNotificationsEnabled: false });
    fixture.detectChanges();

    expect(settings.showUnreadBadges()).toBe(false);
    expect(root.textContent).toContain('Global Message settings were updated');
    expect(root.textContent).toContain('Current saved value: Off');
  });

  it('uses the shared accessible confirm dialog and Escape cancels without saving', async () => {
    await TestBed.configureTestingModule({
      imports: [MessageSettingsPageComponent],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()]
    }).compileComponents();

    const httpMock = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(MessageSettingsPageComponent);
    httpMock.expectOne('/api/me/message-notification-preferences').flush({ messageNotificationsEnabled: true });
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;

    const checkbox = root.querySelector<HTMLInputElement>('[data-testid="global-message-notifications"]');
    checkbox!.checked = false;
    checkbox!.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    root.querySelector<HTMLButtonElement>('[data-testid="save-global-message-settings"]')?.click();
    fixture.detectChanges();

    const dialog = root.querySelector('[role="dialog"][aria-modal="true"]');
    expect(dialog).not.toBeNull();
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }));
    await Promise.resolve();
    fixture.detectChanges();

    expect(root.querySelector('[data-testid="global-settings-confirmation"]')).toBeNull();
    httpMock.expectNone('/api/me/message-notification-preferences');
  });

  it('applies the global unread-badge preference without changing conversation data', async () => {
    await TestBed.configureTestingModule({
      imports: [ConversationListComponent],
      providers: [provideRouter([])]
    }).compileComponents();

    const fixture = TestBed.createComponent(ConversationListComponent);
    fixture.componentRef.setInput('conversations', [
      {
        id: 'conversation-a',
        kind: 'channel',
        title: 'General',
        route: '/conversations/conversation-a',
        lastActivityLabel: 'Now',
        safePreviewLabel: 'Preview',
        viewerIsParticipant: true,
        unreadCount: 4
      }
    ]);
    fixture.componentRef.setInput('showUnreadBadges', false);
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).querySelector('[data-testid="conversation-unread-badge"]')).toBeNull();
  });
});
