// Investigative Angular lifecycle control; mocked HTTP, not backend/RLS acceptance.
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Subject } from 'rxjs';
import { COGLATAS_AUTH_SESSION_MOCK, DEFAULT_AUTH_SESSION } from '../../../core/auth/auth-session.facade';
import { I18nService } from '../../../core/i18n/i18n.service';
import { RealtimeFacade } from '../../../core/realtime/realtime.facade';
import { MessagingFacade } from '../messaging.facade';
import { MessagesPageComponent } from './messages-page.component';

async function setup() {
  let catchUp: (() => Promise<void> | void) | undefined;
  await TestBed.configureTestingModule({
    imports: [MessagesPageComponent],
    providers: [
      provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
      { provide: COGLATAS_AUTH_SESSION_MOCK, useValue: DEFAULT_AUTH_SESSION },
      { provide: RealtimeFacade, useValue: {
        durableEvents$: new Subject(),
        registerProtectedStateClearer: () => () => {},
        registerSubscription: () => () => {},
        registerCatchUp: (_owner: string, callback: () => Promise<void> | void) => {
          catchUp = callback; return () => {};
        },
      } },
    ],
  }).compileComponents();
  TestBed.inject(I18nService).setLocale('en');
  const fixture = TestBed.createComponent(MessagesPageComponent);
  document.body.appendChild(fixture.nativeElement);
  const http = TestBed.inject(HttpTestingController);
  http.expectOne('/api/conversations').flush({ items: [] });
  fixture.detectChanges();
  const facade = TestBed.inject(MessagingFacade);
  const identity = facade.createDialogScopeIdentity();
  expect(identity).not.toBeNull();
  const root = fixture.nativeElement as HTMLElement;
  const dialog = () => root.querySelector('[data-testid="new-message-dialog"]') !== null;
  const search = () => root.querySelector('[data-testid="recipient-search"]') !== null;
  root.querySelector<HTMLButtonElement>('[data-testid="new-message-button"]')!.click();
  fixture.detectChanges();
  expect(dialog()).toBe(true);
  expect(search()).toBe(true);
  return { fixture, http, facade, identity, dialog, search,
    catchUp: () => catchUp!(),
    finish: () => { fixture.destroy(); fixture.nativeElement.remove(); http.verify(); },
  };
}

describe('Investigative supported direct-dialog lifecycle during current-scope inbox catch-up', () => {
  it('keeps a healthy authenticated direct dialog without a refresh', async () => {
    const context = await setup();
    context.fixture.detectChanges();
    const state = { dialog: context.dialog(), search: context.search(),
      sameIdentity: context.facade.createDialogScopeIdentity() === context.identity };
    context.finish();
    expect(state).toEqual({ dialog: true, search: true, sameIdentity: true });
  });

  it('retains the direct dialog through a held same-scope authorized inbox refresh', async () => {
    const context = await setup();
    const pending = context.catchUp();
    context.fixture.detectChanges();
    const during = { dialog: context.dialog(), search: context.search(),
      status: context.facade.page().status,
      sameIdentity: context.facade.createDialogScopeIdentity() === context.identity };
    context.http.expectOne('/api/conversations').flush({ items: [] });
    await pending;
    context.fixture.detectChanges();
    const after = { dialog: context.dialog(), search: context.search(),
      status: context.facade.page().status,
      sameIdentity: context.facade.createDialogScopeIdentity() === context.identity };
    context.finish();
    console.info('INVESTIGATION_HELD_AUTHORIZED_REFRESH', JSON.stringify({ during, after }));
    expect({ during, after }).toEqual({
      during: { dialog: true, search: true, status: 'loading', sameIdentity: true },
      after: { dialog: true, search: true, status: 'empty', sameIdentity: true },
    });
  });

  it('retains the direct dialog when same-scope loading and success coalesce before rendering', async () => {
    const context = await setup();
    const pending = context.catchUp();
    context.http.expectOne('/api/conversations').flush({ items: [] });
    await pending;
    context.fixture.detectChanges();
    const state = { dialog: context.dialog(), search: context.search(),
      sameIdentity: context.facade.createDialogScopeIdentity() === context.identity };
    context.finish();
    expect(state).toEqual({ dialog: true, search: true, sameIdentity: true });
  });

  it('discards the direct dialog after a real facade forbidden inbox response', async () => {
    const context = await setup();
    const pending = context.catchUp();
    context.http.expectOne('/api/conversations').flush({ error: 'Unavailable' }, {
      status: 403, statusText: 'Forbidden',
    });
    await pending;
    context.fixture.detectChanges();
    const state = { dialog: context.dialog(), search: context.search(),
      status: context.facade.page().status };
    context.finish();
    expect(state).toEqual({ dialog: false, search: false, status: 'permissionDenied' });
  });
});
