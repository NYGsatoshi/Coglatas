/// <reference types="node" />
import { webcrypto } from 'node:crypto';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { vi } from 'vitest';
import { AuthSessionFacade } from '../../core/auth/auth-session.facade';
import { FrontendFeatureFlagsService } from '../../core/feature-flags/frontend-feature-flags.service';
import { RealtimeFacade } from '../../core/realtime/realtime.facade';
import { ActiveWorkspaceFacade } from '../../core/workspace/active-workspace.facade';
import { readPreviewBundle, ProjectIdePreviewAdapter } from './projectide-preview.adapter';
import { ProjectIdeWorkbenchComponent } from './projectide-workbench.component';
import { ProjectIdeWorkbenchStore } from './projectide-workbench.store';

async function render(enabled = true, allowed = true, fail = false) {
  localStorage.setItem('coglatas.locale', 'en');
  const authenticated = signal(allowed);
  let clear!: () => void;
  const data = readPreviewBundle(JSON.parse(readFileSync(resolve('public/assets/projectide/preview-v1.json'), 'utf8')));
  const adapter = {
    read: vi.fn(async (_url?: string, _signal?: AbortSignal) => {
      if (fail) {
        throw new Error('private');
      }
      return data;
    }),
  };
  TestBed.configureTestingModule({
    imports: [ProjectIdeWorkbenchComponent],
    providers: [
      provideRouter([]),
      {
        provide: AuthSessionFacade,
        useValue: {
          isAuthenticated: authenticated,
          currentTenant: signal({ tenantId: 'synthetic-auth-tenant', isAvailable: true }),
          currentUser: signal({ userId: 'synthetic-actor' }),
          session: signal({ capabilities: ['projects:view'] }),
        },
      },
      {
        provide: RealtimeFacade,
        useValue: {
          registerProtectedStateClearer: (_: string, fn: () => void) => {
            clear = fn;
            return () => undefined;
          },
        },
      },
      ActiveWorkspaceFacade,
    ],
  });
  TestBed.overrideComponent(ProjectIdeWorkbenchComponent, {
    set: {
      providers: [
        ProjectIdeWorkbenchStore,
        { provide: ProjectIdePreviewAdapter, useValue: adapter },
      ],
    },
  });
  TestBed.inject(FrontendFeatureFlagsService).setForTesting({
    'projectide.syntheticPreview': enabled,
  });
  const fixture = TestBed.createComponent(ProjectIdeWorkbenchComponent);
  fixture.detectChanges();
  await fixture.whenStable();
  fixture.detectChanges();
  return {
    fixture,
    c: fixture.componentInstance,
    root: fixture.nativeElement as HTMLElement,
    adapter,
    authenticated,
    clear: () => clear(),
  };
}

vi.stubGlobal('crypto', webcrypto);





async function readyPage(locale: 'en' | 'ja' = 'en') {
  const f = await render(); f.c.i18n.setLocale(locale); f.c.choose('reference-pages'); f.c.loadResult();
  f.c.open('source'); await f.c.loadRootSource();
  const result = f.c.store.result()!; const symbol = result.semanticInspection!.symbols.find(item => item.kind === 'coglatas.project')!;
  f.c.inspectRecord(result.declarations.find(item => item.identity === symbol.location.entityId)!);
  f.c.findReferences(symbol); f.fixture.detectChanges(); await f.fixture.whenStable(); await new Promise(resolve => setTimeout(resolve, 0)); f.fixture.detectChanges();
  return {...f, result, symbol};
}
describe('Reference paging in the Angular Workbench', () => {
  for (const locale of ['en', 'ja'] as const) {
    it('shows 32 plus 3 actual origins and moves keyboard focus to each current page in ' + locale, async () => {
      const {c, fixture, root, result} = await readyPage(locale);
      const first = c.store.referencePage()!; expect(first.references).toHaveLength(32);
      expect(root.querySelector('[data-testid="ide-reference-page"]')!.textContent).toContain(c.t('ide.referencePage', {page: 1, pages: 2, count: 32}));
      expect(document.activeElement).toBe(root.querySelector('[data-testid="ide-reference-page"]'));
      const next = root.querySelector<HTMLButtonElement>('[data-testid="ide-reference-next"]')!; next.click();
      fixture.detectChanges(); await fixture.whenStable(); await new Promise(resolve => setTimeout(resolve, 0)); fixture.detectChanges();
      const second = c.store.referencePage()!; expect(second.references).toHaveLength(3);
      expect(document.activeElement).toBe(root.querySelector('[data-testid="ide-reference-page"]'));
      expect(root.querySelector<HTMLButtonElement>('[data-testid="ide-reference-next"]')!.disabled).toBe(true);
      expect(root.querySelector('[data-testid="ide-reference-page"]')!.textContent).toContain(c.t('ide.referenceScanComplete'));
      expect(c.completePass()).toBe(false); expect(result.result.evidenceComplete).toBe(false);
      c.inspectReference(second.references[2]); c.navigateReference('origin'); fixture.detectChanges(); await fixture.whenStable(); await new Promise(resolve => setTimeout(resolve, 0)); fixture.detectChanges();
      expect(c.store.sourceLocation()).toBe(second.references[2].origin); expect(c.panel()).toBe('source');
      expect((document.activeElement as HTMLElement).getAttribute('data-testid')).toBe('ide-source-target');
      c.open('inspector'); c.moveReferencePage(second, 0); fixture.detectChanges(); await fixture.whenStable(); await new Promise(resolve => setTimeout(resolve, 0)); fixture.detectChanges();
      expect(c.store.sourceTarget()).toBeNull(); expect(c.store.referencePage()!.references).toEqual(first.references);
    });
  }
  it('does not move focus to a stale page after clearing or selecting an ordinary source target', async () => {
    const {c, fixture, root} = await readyPage(); const page = c.store.referencePage()!;
    c.moveReferencePage(page, 1); c.clearReferenceSearch(); c.navigate();
    fixture.detectChanges(); await fixture.whenStable(); await new Promise(resolve => setTimeout(resolve, 0)); fixture.detectChanges();
    expect(c.store.referencePage()).toBeNull(); expect(root.querySelector('[data-testid="ide-reference-page"]')).toBeNull();
    expect(c.panel()).toBe('source'); expect((document.activeElement as HTMLElement).getAttribute('data-testid')).toBe('ide-source-target');
  });
  it('withdraws protected pages and source locations on permission loss', async () => {
    const {c, fixture, root, authenticated} = await readyPage(); const page = c.store.referencePage()!;
    c.inspectReference(page.references[0]); c.navigateReference('origin'); authenticated.set(false);
    fixture.detectChanges(); await fixture.whenStable(); await new Promise(resolve => setTimeout(resolve, 0)); fixture.detectChanges();
    expect(c.store.referencePage()).toBeNull(); expect(c.store.rootSource()).toBeNull(); expect(c.store.sourceTarget()).toBeNull();
    expect(root.querySelector('[data-testid="ide-reference-page"]')).toBeNull();
    c.moveReferencePage(page, 1); expect(c.store.state()).toBe('denied');
  });
  it('withdraws page detail on explicit Source re-admission', async () => {
    const {c, fixture} = await readyPage(); const page = c.store.referencePage()!;
    c.inspectReference(page.references[0]); c.navigateReference('origin'); await c.loadRootSource();
    fixture.detectChanges(); await fixture.whenStable(); await new Promise(resolve => setTimeout(resolve, 0)); fixture.detectChanges();
    expect(c.store.referencePage()).toBeNull(); expect(c.store.selectedReference()).toBeNull(); expect(c.store.sourceTarget()).toBeNull();
    expect(c.store.moveReferencePage(page, 1)).toBe(false);
  });
});
