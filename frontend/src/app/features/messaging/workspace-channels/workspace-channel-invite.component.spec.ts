import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { I18nService } from '../../../core/i18n/i18n.service';
import { WorkspaceChannelInviteComponent } from './workspace-channel-invite.component';
import { WorkspaceChannelItem } from './workspace-channels.api';
import { WorkspaceChannelsFacade } from './workspace-channels.facade';

const workspaceId = '10000000-0000-4000-8000-000000000001';
const channelId = '20000000-0000-4000-8000-000000000001';
const recipientId = '30000000-0000-4000-8000-000000000001';
const endpoint = `/api/workspaces/${workspaceId}/chat-channels/${channelId}/members`;
const recipient = { userId: recipientId, displayName: 'Synthetic council member' };
let activeHttp: HttpTestingController | undefined;

function setup() {
  const item: WorkspaceChannelItem = { id: channelId, workspaceId, name: 'Council events',
    visibility: 'Private', isMember: true, canJoin: false, canManage: true, isDefault: false };
  const scope = signal<{ key: string } | null>({ key: 'synthetic-tenant:actor:workspace' });
  const status = signal('ready');
  const pending = signal<string | null>(null);
  const directory = signal<{ items: WorkspaceChannelItem[] } | null>({ items: [item] });
  const notice = signal<string | null>(null);
  const refresh = vi.fn().mockResolvedValue(undefined);
  const locale = signal('en');
  TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(),
    { provide: WorkspaceChannelsFacade, useValue: { scope, status, pending, directory, notice, refresh } },
    { provide: I18nService, useValue: { locale } }
  ] });
  const component = TestBed.runInInjectionContext(() => new WorkspaceChannelInviteComponent());
  component.channel = item;
  activeHttp = TestBed.inject(HttpTestingController);
  return { component, http: activeHttp, scope, status, pending, directory, notice, refresh, locale, item };
}

describe('Workspace channel membership command lifecycle', () => {
  afterEach(() => {
    activeHttp?.verify({ ignoreCancelled: true });
    activeHttp = undefined;
    TestBed.resetTestingModule();
  });

  it('submits one credentialed command and requires a confirmed server response', () => {
    const { component, http } = setup();
    component.open();
    component.select([recipient]);
    component.submit();
    component.submit();
    const request = http.expectOne(endpoint);
    expect(request.request.method).toBe('POST');
    expect(request.request.withCredentials).toBe(true);
    expect(request.request.body).toEqual({ userId: recipientId });
    expect(component.pending()).toBe(true);
    component.close();
    expect(component.opened()).toBe(true);
    request.flush({ status: 'OK' });
    expect(component.pending()).toBe(false);
    expect(component.selected()).toBeNull();
    expect(component.result()).toBe('done');
    component.submit();
    http.expectNone(endpoint);
    component.close();
    expect(component.opened()).toBe(false);
  });

  it('rejects empty, multiple and invalid recipient selections without a command', () => {
    const { component, http } = setup();
    component.open();
    for (const people of [[], [recipient, recipient], [{ ...recipient, userId: '../another-user' }]]) {
      component.select(people);
      component.submit();
      expect(component.selected()).toBeNull();
      http.expectNone(endpoint);
    }
  });

  it('requires current scope, ready directory and an exact server management capability', () => {
    const { component, http, scope, status, directory, item } = setup();
    component.select([recipient]);
    for (const changed of [null, { items: [] }, { items: [{ ...item, canManage: false }] },
      { items: [{ ...item, workspaceId: '10000000-0000-4000-8000-000000000002' }] }]) {
      directory.set(changed);
      expect(component.current()).toBe(false);
      component.open(); component.submit();
      expect(component.opened()).toBe(false);
      http.expectNone(endpoint);
    }
    directory.set({ items: [item] });
    status.set('loading');
    component.submit(); http.expectNone(endpoint);
    status.set('ready');
    scope.set({ key: 'synthetic-other-actor' });
    component.submit(); http.expectNone(endpoint);
    scope.set(null);
    component.submit(); http.expectNone(endpoint);
  });

  it('does not open another command while the directory owner is pending', () => {
    const { component, pending, http } = setup();
    pending.set('create');
    component.open();
    expect(component.opened()).toBe(false);
    http.expectNone(endpoint);
  });

  it.each([400, 401, 403, 404])('clears protected dialog state and refreshes authority after HTTP %i', (status) => {
    const { component, http, notice, refresh } = setup();
    component.open(); component.select([recipient]); component.submit();
    http.expectOne(endpoint).flush({ error: 'Synthetic denied response' }, { status, statusText: 'Denied' });
    expect(component.pending()).toBe(false);
    expect(component.opened()).toBe(false);
    expect(component.selected()).toBeNull();
    expect(component.result()).toBeNull();
    expect(notice()).toBe('denied');
    expect(refresh).toHaveBeenCalledExactlyOnceWith();
  });

  it.each([503, 0])('retains the selection and allows an explicit retry after failure %i', (status) => {
    const { component, http, refresh } = setup();
    component.open(); component.select([recipient]); component.submit();
    const request = http.expectOne(endpoint);
    if (status === 0) request.error(new ProgressEvent('error'));
    else request.flush({}, { status, statusText: 'Unavailable' });
    expect(component.pending()).toBe(false);
    expect(component.opened()).toBe(true);
    expect(component.selected()).toBe(recipientId);
    expect(component.result()).toBe('error');
    expect(refresh).not.toHaveBeenCalled();
    component.submit();
    http.expectOne(endpoint).flush({ status: 'OK' });
    expect(component.result()).toBe('done');
  });

  it('treats an unconfirmed success envelope as an error', () => {
    const { component, http } = setup();
    component.open(); component.select([recipient]); component.submit();
    http.expectOne(endpoint).flush({ status: 'NOT_CONFIRMED' });
    expect(component.result()).toBe('error');
    expect(component.pending()).toBe(false);
    expect(component.selected()).toBe(recipientId);
  });

  it.each(['success', 'denial'])('ignores a late %s response after the actor scope changes', (outcome) => {
    const { component, http, scope, notice, refresh } = setup();
    component.open(); component.select([recipient]); component.submit();
    const request = http.expectOne(endpoint);
    scope.set({ key: 'synthetic-new-actor' });
    if (outcome === 'success') request.flush({ status: 'OK' });
    else request.flush({}, { status: 403, statusText: 'Denied' });
    expect(component.current()).toBe(false);
    expect(component.result()).toBeNull();
    expect(notice()).toBeNull();
    expect(refresh).not.toHaveBeenCalled();
  });

  it('cancels the active HTTP subscription when its owner is destroyed', () => {
    const { component, http } = setup();
    component.open(); component.select([recipient]); component.submit();
    const request = http.expectOne(endpoint);
    TestBed.resetTestingModule();
    expect(request.cancelled).toBe(true);
    expect(component.current()).toBe(false);
  });

  it('preserves an IME composition and closes only after an ordinary Escape', () => {
    const { component } = setup();
    component.open();
    component.escape(new KeyboardEvent('keydown', { key: 'Escape', isComposing: true }));
    expect(component.opened()).toBe(true);
    component.escape(new KeyboardEvent('keydown', { key: 'Escape', keyCode: 229 }));
    expect(component.opened()).toBe(true);
    component.escape(new KeyboardEvent('keydown', { key: 'Escape' }));
    expect(component.opened()).toBe(false);
  });

  it('reopens with a fresh picker state and follows the current locale', () => {
    const { component, http, locale } = setup();
    component.open(); component.select([recipient]); component.submit();
    http.expectOne(endpoint).flush({}, { status: 503, statusText: 'Unavailable' });
    component.close(); component.open();
    expect(component.selected()).toBeNull();
    expect(component.result()).toBeNull();
    expect(component.resetKey()).toBe(2);
    const english = component.text();
    locale.set('ja');
    expect(component.text().submit).not.toBe(english.submit);
  });
});
