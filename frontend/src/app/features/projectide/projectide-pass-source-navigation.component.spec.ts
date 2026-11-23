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
  const data = readPreviewBundle(JSON.parse(readFileSync(resolve('public/assets/projectide/pass-recordings-v1.json'), 'utf8')));
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




async function readyPass(key = 'fail') {
  const view = await render(); view.c.choose(key); view.c.loadResult(); view.c.open('source');
  await view.c.loadRootSource(); view.fixture.detectChanges(); return view;
}

describe('Original pass map navigation controls',()=>{
  it.each(['en','ja'] as const)('requires explicit original map/page reads and opens the exact Source target: %s',async locale=>{
    const {c,fixture,root,adapter}=await readyPass('pass');const initialLoads=adapter.read.mock.calls.length;c.i18n.setLocale(locale);await c.loadPassRecording('diagnostic');
    await c.loadRecordedPassBoundary(5);fixture.detectChanges();expect(root.querySelector('[data-testid="ide-pass-source-read"]')).toBeNull();
    await c.loadRecordedPassBoundary(5,'available');fixture.detectChanges();expect(root.querySelector('[data-testid="ide-pass-source-page"]')).toBeNull();
    const read=root.querySelector<HTMLButtonElement>('[data-testid="ide-pass-source-read"]')!;expect(read.textContent).toContain(c.t('ide.passSourceRead'));read.click();expect(c.store.passBoundarySourcePage()).not.toBeNull();
    fixture.detectChanges();await fixture.whenStable();fixture.detectChanges();
    expect(adapter.read).toHaveBeenCalledTimes(initialLoads);const page=c.store.passBoundarySourcePage()!;expect(root.querySelector('[data-testid="ide-pass-source-page"]')!.textContent).toContain(page.originalMapDigest.value);
    expect(root.querySelectorAll('[data-testid="ide-pass-source-target"]')).toHaveLength(page.targets.length);
    await new Promise<void>(resolve=>setTimeout(resolve,0));expect(document.activeElement).toBe(root.querySelector('[data-testid="ide-pass-boundary"]'));
    root.querySelector<HTMLButtonElement>('[data-testid="ide-pass-source-target"]')!.click();fixture.detectChanges();await fixture.whenStable();fixture.detectChanges();
    expect(c.store.sourceLocation()).toBe(page.targets[0].location);expect(c.store.sourceTarget()).not.toBeNull();
    await new Promise<void>(resolve=>setTimeout(resolve,0));expect(document.activeElement).toBe(root.querySelector('[data-testid="ide-source-target"]'));
    expect(root.querySelector('[data-testid="ide-pass-source-page"]')!.textContent).toContain(c.t('ide.passSourceTitle'));
  });
  it.each(['permission','callback','input','source','boundary','run','whole'] as const)('removes owned target and page after %s',async action=>{
    const {c,fixture,root,authenticated,clear}=await readyPass('pass');await c.loadPassRecording('diagnostic');await c.loadRecordedPassBoundary(5,'available');
    c.loadPassBoundarySourcePage();const page=c.store.passBoundarySourcePage()!;c.navigatePassBoundarySource(page,page.targets[0]);fixture.detectChanges();
    if(action==='permission'){authenticated.set(false);await fixture.whenStable();}if(action==='callback')clear();if(action==='input')c.choose('unknown');
    if(action==='source')await c.loadRootSource();if(action==='boundary')await c.loadRecordedPassBoundary(0,'available');
    if(action==='run')await c.loadPassRecording('retry');if(action==='whole')await c.loadRecordedPasses();
    fixture.detectChanges();await fixture.whenStable();fixture.detectChanges();expect(c.store.passBoundarySourcePage()).toBeNull();expect(c.store.sourceTarget()).toBeNull();
    expect(root.querySelector('[data-testid="ide-pass-source-page"]')).toBeNull();expect(root.querySelector('[data-testid="ide-source-target"]')).toBeNull();
    c.navigatePassBoundarySource(page,page.targets[0]);expect(c.store.sourceTarget()).toBeNull();expect(page.targets.length).toBeGreaterThan(0);
  });
  it('does not expose map controls for Source-only, budget or missing-IR boundaries',async()=>{
    const {c,fixture,root}=await readyPass('test-assertion-navigation');await c.loadPassRecording('diagnostic');
    for(const ordinal of [0,5]){await c.loadRecordedPassBoundary(ordinal,'available');fixture.detectChanges();expect(root.querySelector('[data-testid="ide-pass-source-read"]')).toBeNull();}
    c.choose('pass');c.loadResult();await c.loadRootSource();await c.loadPassRecording('diagnostic');await c.loadRecordedPassBoundary(5,'budget');fixture.detectChanges();
    expect(root.querySelector('[data-testid="ide-pass-source-read"]')).toBeNull();expect(root.querySelector('[data-testid="ide-pass-source-page"]')).toBeNull();
  });
  it('rejects a copied candidate and prevents an old scheduled focus after Source replacement',async()=>{
    const {c,fixture,root}=await readyPass('pass');await c.loadPassRecording('diagnostic');await c.loadRecordedPassBoundary(5,'available');
    c.loadPassBoundarySourcePage();const page=c.store.passBoundarySourcePage()!;c.navigatePassBoundarySource(page,{...page.targets[0]});expect(c.store.sourceTarget()).toBeNull();
    c.navigatePassBoundarySource(page,page.targets[0]);await c.loadRootSource();fixture.detectChanges();await fixture.whenStable();fixture.detectChanges();
    expect(c.store.sourceTarget()).toBeNull();expect(root.querySelector('[data-testid="ide-source-target"]')).toBeNull();
    expect(document.activeElement).not.toBe(root.querySelector('[data-testid="ide-pass-boundary"]'));expect(c.store.passBoundarySourcePage()).toBeNull();
  });
});
