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
  const data = readPreviewBundle(JSON.parse(readFileSync(resolve('public/assets/projectide/semantic-index-v1.json'), 'utf8')));
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





async function settle(f: Awaited<ReturnType<typeof render>>) {
  f.fixture.detectChanges(); await f.fixture.whenStable(); await new Promise(resolve => setTimeout(resolve, 0)); f.fixture.detectChanges();
}
async function readyIndex(locale: 'en' | 'ja' = 'en') {
  const f = await render(); f.c.i18n.setLocale(locale); f.c.choose('tests-index-base'); f.c.loadResult(); f.c.open('source');
  await f.c.loadSemanticIndex(); await settle(f); return f;
}
describe('Full semantic index in the Angular Workbench', () => {
  for (const [locale, message] of [['en', 'Loading project symbols…'], ['ja', 'プロジェクトのシンボルを読み込んでいます…']] as const)
    it('translates the pending index status in ' + locale, async () => {
      const f = await render(); f.c.i18n.setLocale(locale); f.c.choose('tests-index-base'); f.c.loadResult(); f.c.open('source');
      expect(await f.c.store.loadScopedSources()).toBe(true);
      const pending = f.c.store.loadSemanticIndex(); f.fixture.detectChanges();
      expect(f.c.store.semanticIndexReadState()).toBe('loading');
      expect(f.root.querySelector('[data-testid="ide-semantic-index"] [role="status"]')?.textContent?.trim()).toBe(message);
      await pending; await settle(f);
      expect(f.c.store.semanticIndexReadState()).toBe('ready');
      expect(f.root.querySelector('[data-testid="ide-semantic-index"] [role="status"]')).toBeNull();
    });
  for (const locale of ['en', 'ja'] as const) it('shows real 32 plus 4 references and focuses the current index in ' + locale, async () => {
    const f = await readyIndex(locale); const { c, root } = f; const index = c.store.semanticIndex()!;
    expect(index).not.toBeNull(); expect(root.querySelectorAll('[data-testid="ide-index-example"]')).toHaveLength(1); expect(root.querySelector('[data-testid="ide-semantic-index"]')).not.toBeNull();
    expect(document.activeElement).toBe(root.querySelector('[data-testid="ide-semantic-index"]'));
    const group = index.projection.referenceSearches.find(g => g.pages.length > 1)!;
    const symbol = index.symbols.find(s => s.key === group.targetKey)!;
    c.indexedAction('select', symbol); c.indexedAction('references', symbol); await settle(f);
    expect(root.querySelectorAll('[data-testid="ide-index-reference"]')).toHaveLength(32);
    const first = c.store.indexedReferenceQuery()!;
    root.querySelector<HTMLButtonElement>('[data-testid="ide-index-references-next"]')!.click(); await settle(f);
    expect(root.querySelectorAll('[data-testid="ide-index-reference"]')).toHaveLength(4);
    expect(document.activeElement).toBe(root.querySelector('[data-testid="ide-semantic-index"]'));
    expect(root.querySelector<HTMLButtonElement>('[data-testid="ide-index-references-next"]')!.disabled).toBe(true);
    const second = c.store.indexedReferenceQuery()!; const reference = c.store.indexedReferenceRows(second)[0];
    c.openIndexedReference(second, reference, true); await settle(f);
    expect(c.store.scopedSourceTarget()!.location).toEqual(symbol.location);
    expect(root.querySelector('[data-testid="ide-scoped-source-path"]')!.textContent).toBe(symbol.location.propertyPath);
    c.moveIndexedReferences(first, 1); expect(c.store.indexedReferenceQuery()).toBe(second);
    c.store.selectIndexedSymbol({ ...symbol }); await settle(f); expect(c.store.indexedReferenceQuery()).toBeNull();
  });
  it('withdraws protected index, queries and Source navigation on permission loss', async () => {
    const f = await readyIndex(); const index = f.c.store.semanticIndex()!; const symbol = index.symbols[0];
    f.c.indexedAction('select', symbol); f.c.indexedAction('references', symbol); await settle(f);
    f.authenticated.set(false); await settle(f);
    expect(f.c.store.semanticIndex()).toBeNull(); expect(f.root.querySelector('[data-testid="ide-semantic-index"]')).toBeNull();
    f.c.indexedAction('source', symbol); expect(f.c.store.state()).toBe('denied'); expect(f.c.store.scopedSourceTarget()).toBeNull();
  });
  it('keeps partial closure explicitly unavailable and fences scheduled focus after input switch', async () => {
    const f = await readyIndex(); const symbol = f.c.store.semanticIndex()!.symbols[0];
    f.c.indexedAction('select', symbol); f.c.choose('tests-index-partial-base'); f.c.loadResult(); await f.c.loadSemanticIndex(); await settle(f);
    expect(f.c.store.semanticIndex()).toBeNull(); expect(f.c.store.semanticIndexReadState()).toBe('unavailable');
    expect(f.root.querySelectorAll('[data-testid="ide-index-symbol"]')).toHaveLength(0);
  });
});
