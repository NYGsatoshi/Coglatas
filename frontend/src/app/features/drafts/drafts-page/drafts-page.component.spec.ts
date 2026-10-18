import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { AuthSessionFacade } from '../../../core/auth/auth-session.facade';
import { RealtimeFacade } from '../../../core/realtime/realtime.facade';
import { ActiveWorkspaceFacade } from '../../../core/workspace/active-workspace.facade';
import { LocalDraftStore } from '../local-draft.store';
import { DraftsPageComponent } from './drafts-page.component';

describe('DraftsPageComponent local-only workbench', () => {
  let workspace = signal({ id: 'workspace-a' });
  let clear: () => void;

  beforeEach(async () => {
    workspace = signal({ id: 'workspace-a' });
    await TestBed.configureTestingModule({ imports: [DraftsPageComponent], providers: [
      provideRouter([]),
      { provide: AuthSessionFacade, useValue: { currentUser: signal({ userId: 'user-a' }), currentTenant: signal({ tenantId: 'tenant-a', isAvailable: true }), isAuthenticated: signal(true) } },
      { provide: ActiveWorkspaceFacade, useValue: { activeWorkspace: workspace } },
      { provide: RealtimeFacade, useValue: { registerProtectedStateClearer: (_owner: string, callback: () => void) => { clear = callback; return () => undefined; } } }
    ] }).compileComponents();
  });
  afterEach(() => TestBed.resetTestingModule());

  async function render() {
    const fixture = TestBed.createComponent(DraftsPageComponent);
    fixture.detectChanges(); await fixture.whenStable(); fixture.detectChanges();
    return fixture;
  }

  it('discloses storage limits and uses no production resource links or forms', async () => {
    const fixture = await render();
    const host: HTMLElement = fixture.nativeElement;
    expect(host.textContent).toContain('Refreshing or closing the tab loses them');
    expect(host.textContent).toContain('Server save and cross-device sync are unavailable');
    expect(host.querySelectorAll('[aria-label="Demo target search results"] li')).toHaveLength(0);
    expect(host.querySelector('form')).toBeNull();
    expect(host.querySelector('a[href*="projects/"]')).toBeNull();
    [...host.querySelectorAll<HTMLButtonElement>('button')].find((button) => button.textContent?.includes('Show server save status'))!.click();
    fixture.detectChanges();
    expect(host.querySelector('[data-testid="draft-status"]')?.textContent).toContain('Server save and cross-device sync are unavailable');
  });

  it('preserves live edits across page recreation and renders HTML as text', async () => {
    const first = await render();
    first.componentInstance.store.edit('body', '<img src=x onerror=alert(1)>');
    first.destroy();
    const second = await render();
    const host: HTMLElement = second.nativeElement;
    expect(host.querySelector<HTMLTextAreaElement>('textarea')!.value).toBe('<img src=x onerror=alert(1)>');
    expect(host.querySelector('img')).toBeNull();
  });

  it('never submits or discards text on an IME composition Enter', async () => {
    const fixture = await render();
    const textarea = (fixture.nativeElement as HTMLElement).querySelector<HTMLTextAreaElement>('textarea')!;
    textarea.value = '変換確定'; textarea.dispatchEvent(new Event('input'));
    textarea.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', isComposing: true, bubbles: true }));
    await fixture.whenStable();
    expect(fixture.componentInstance.store.view().content.body).toBe('変換確定');
    expect(fixture.componentInstance.store.view().checkpoint.version).toBe(0);
  });

  it('supports opt-in search, add and remove with focus returning to the search input', async () => {
    const fixture = await render();
    const host: HTMLElement = fixture.nativeElement;
    fixture.componentInstance.store.setLinksPreview(true); fixture.detectChanges(); await fixture.whenStable();
    host.querySelector<HTMLButtonElement>('[aria-label="Add preview link Demo File — Notes.txt"]')!.click();
    fixture.detectChanges();
    expect(fixture.componentInstance.store.view().content.links).toHaveLength(1);
    expect(document.activeElement?.id).toBe('draft-link-query');
    host.querySelector<HTMLButtonElement>('[aria-label="Remove preview link Demo File — Notes.txt"]')!.click();
    fixture.detectChanges();
    expect(fixture.componentInstance.store.view().content.links).toHaveLength(0);
    expect(document.activeElement?.id).toBe('draft-link-query');
  });

  it('requires an explicit confirmation before discarding and supports cancellation', async () => {
    const fixture = await render();
    fixture.componentInstance.store.edit('body', 'keep me'); fixture.detectChanges();
    const buttons = () => [...(fixture.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>('button')];
    buttons().find((button) => button.textContent?.includes('Discard local draft'))!.click(); fixture.detectChanges();
    expect(fixture.componentInstance.store.hasContent()).toBe(true);
    buttons().find((button) => button.textContent?.includes('Keep editing'))!.click(); fixture.detectChanges();
    expect(fixture.componentInstance.store.hasContent()).toBe(true);
    fixture.componentInstance.confirmDiscard.set(true); fixture.detectChanges();
    buttons().find((button) => button.textContent?.includes('Confirm discard'))!.click(); fixture.detectChanges();
    expect(fixture.componentInstance.store.hasContent()).toBe(false);
    expect(document.activeElement?.id).toBe('local-draft-title');
  });

  it('redacts the editor immediately after protected state clearing', async () => {
    const fixture = await render();
    const store = TestBed.inject(LocalDraftStore);
    store.edit('body', 'private'); store.setLinksPreview(true); store.addLink('demo-task'); fixture.detectChanges();
    clear(); fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).querySelector('textarea')).toBeNull();
    expect((fixture.nativeElement as HTMLElement).textContent).not.toContain('Demo Task');
    expect(store.hasContent()).toBe(false);
  });
});
