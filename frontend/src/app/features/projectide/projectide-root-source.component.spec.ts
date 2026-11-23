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
  const data = readPreviewBundle(JSON.parse(readFileSync(resolve('public/assets/projectide/root-diagnostic-premises-v1.json'), 'utf8')));
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





async function readyRoot(locale:'en'|'ja'='en',key='fail'){
  const f=await render();f.c.i18n.setLocale(locale);f.c.choose(key);f.c.loadResult();f.c.open('source');
  await f.c.loadRootSource();f.c.inspectDiagnostic(f.c.store.result()!.result.diagnostics[0]);f.fixture.detectChanges();return f;
}
describe('Root Source Inspector without regional IR',()=>{
  for(const locale of ['en','ja'] as const){
    it('explicitly reads original Source before root premise bodies in '+locale,async()=>{
      const{c,root,fixture}=await render();c.i18n.setLocale(locale);c.choose('fail');c.loadResult();const result=c.store.result()!;
      c.inspectDiagnostic(result.result.diagnostics[0]);fixture.detectChanges();expect(c.store.diagnosticSourceReady()).toBe(false);expect(await c.store.loadDiagnosticPremises()).toBe(false);
      c.open('source');await c.loadRootSource();fixture.detectChanges();await fixture.whenStable();fixture.detectChanges();
      expect(c.store.scopedSources()).toHaveLength(0);expect(c.store.diagnostic()).toBeNull();expect(c.store.rootSource()!.sourceText).toBe(result.sourceText);
      expect(root.querySelector('[data-testid="ide-root-source-body"] pre')!.textContent).toBe(result.sourceText);
      c.inspectDiagnostic(result.result.diagnostics[0]);await c.loadDiagnosticPremises();fixture.detectChanges();await fixture.whenStable();
      expect(root.querySelectorAll('[data-testid="ide-diagnostic-premise-body"]')).toHaveLength(0);
      expect(root.querySelector('[data-testid="ide-diagnostic-premises"]')!.textContent).toContain(c.t('ide.rootSourceOnly'));
      await c.loadDiagnosticPremises('available');fixture.detectChanges();
      expect(root.querySelectorAll('[data-testid="ide-diagnostic-premise-body"]')).toHaveLength(2);
      expect(c.store.diagnosticPremises()!.bodies.find(b=>b.kind==='RootSource')!.text).toBe(result.sourceText);
      expect(c.store.diagnosticPremises()!.references.filter(r=>r.resolution==='Unavailable')).toHaveLength(1);
      expect(c.store.result()).toBe(result);expect(result.result.verdict).toBe('coglatas.fail');expect(c.completePass()).toBe(false);
    });
    it('keeps one-byte budget and missing basis separate from original result in '+locale,async()=>{
      const{c,root,fixture}=await readyRoot(locale);await c.loadDiagnosticPremises('budget');fixture.detectChanges();
      expect(root.textContent).toContain(c.t('ide.premisesBudgetExceeded'));expect(root.querySelectorAll('[data-testid="ide-diagnostic-premise-body"]')).toHaveLength(0);
      c.choose('unknown');c.loadResult();c.open('source');await c.loadRootSource();c.inspectDiagnostic(c.store.result()!.result.diagnostics[0]);await c.loadDiagnosticPremises('available');fixture.detectChanges();
      expect(root.textContent).toContain(c.t('ide.premisesNone'));expect(c.store.diagnosticPremises()!.references).toHaveLength(0);expect(c.completePass()).toBe(false);
    });
  }
  it('withdraws root and premise views on explicit Source readmission or failed regional admission',async()=>{
    const{c,fixture}=await readyRoot();await c.loadDiagnosticPremises('available');const retained=c.store.diagnosticPremises()!;
    await c.loadRootSource();fixture.detectChanges();expect(c.store.diagnostic()).toBeNull();expect(c.store.diagnosticPremises()).toBeNull();expect(retained.bodies).toHaveLength(2);
    await c.loadScopedSources();expect(c.store.rootSource()).toBeNull();expect(c.store.diagnosticPremises()).toBeNull();
  });
  it('withdraws protected root data on actor loss without calling retained objects physically deleted',async()=>{
    const{c,fixture,root,authenticated}=await readyRoot();await c.loadDiagnosticPremises('available');fixture.detectChanges();const retained=c.store.rootSource()!;
    authenticated.set(false);fixture.detectChanges();await fixture.whenStable();fixture.detectChanges();
    expect(c.store.rootSource()).toBeNull();expect(c.store.diagnosticPremises()).toBeNull();expect(root.querySelector('[data-testid="ide-root-source-body"]')).toBeNull();expect(retained.sourceText).not.toBe('');
  });
  it('rejects a late Source read after selection of another input',async()=>{
    const{c,fixture}=await render();c.choose('fail');c.loadResult();const pending=c.loadRootSource();c.choose('pass');await pending;fixture.detectChanges();expect(c.store.rootSource()).toBeNull();
  });
  it('keeps only the newest root read and only the newest premise read',async()=>{
    const{c,fixture}=await readyRoot();const old=c.loadRootSource();const fresh=c.loadRootSource();await Promise.all([old,fresh]);
    expect(c.store.rootSource()).not.toBeNull();c.inspectDiagnostic(c.store.result()!.result.diagnostics[0]);
    const bodies=c.loadDiagnosticPremises('available');const manifest=c.loadDiagnosticPremises('manifest');await Promise.all([bodies,manifest]);fixture.detectChanges();expect(c.store.diagnosticPremises()!.state).toBe('ManifestOnly');
  });
  it('withdraws a pending body read after root Source readmission',async()=>{
    const{c,fixture}=await readyRoot();const pending=c.loadDiagnosticPremises('available');await c.loadRootSource();await pending;fixture.detectChanges();expect(c.store.diagnosticPremises()).toBeNull();
  });
  it('rejects corrupted root frames and never displays a partial root body',async()=>{
    const{c,fixture,root}=await render();c.choose('fail');c.loadResult();Object.assign(c.store.context()!.rootSource!.result,{digest:{algorithm:'sha-256',value:'f'.repeat(64)}});
    c.open('source');await c.loadRootSource();fixture.detectChanges();expect(c.store.rootSourceReadState()).toBe('unavailable');expect(c.store.rootSource()).toBeNull();expect(root.querySelector('[data-testid="ide-root-source-body"]')).toBeNull();
    expect(root.textContent).toContain(c.t('ide.rootSourceUnavailable'));
  });
  it('loads the distinct root example without replacing the regional example',async()=>{
    const{c,adapter}=await render();await c.reload('root-diagnostic-premises');expect(adapter.read.mock.calls.at(-1)![0]).toContain('assets/projectide/root-diagnostic-premises-v1.json');
  });
});
