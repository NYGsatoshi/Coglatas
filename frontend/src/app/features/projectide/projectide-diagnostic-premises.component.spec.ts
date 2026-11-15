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
  const data = readPreviewBundle(JSON.parse(readFileSync(resolve('public/assets/projectide/diagnostic-premises-v1.json'), 'utf8')));
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





async function readyPremises(locale: 'en'|'ja'='en', family='false') {
  const f=await render();f.c.i18n.setLocale(locale);f.c.choose('tests-publication-'+family+'-base');f.c.loadResult();
  f.c.open('source');await f.c.loadScopedSources();f.c.inspectDiagnostic(f.c.store.result()!.result.diagnostics[0]);f.fixture.detectChanges();return f;
}
describe('Captured diagnostic premise Inspector',()=>{
  for(const locale of ['en','ja'] as const){
    it('shows exact native Manifest without bodies in '+locale,async()=>{
      const {c,root,fixture}=await readyPremises(locale);const result=c.store.result();await c.loadDiagnosticPremises();fixture.detectChanges();await fixture.whenStable();
      const panel=root.querySelector('[data-testid="ide-diagnostic-premises"]')!;expect(panel.textContent).toContain(c.t('ide.premisesManifestOnly'));
      expect(panel.textContent).toContain(locale==='en'?'Body bytes: 0 / 1048576':'本文バイト数: 0 / 1048576');
      expect(panel.querySelectorAll('[data-testid="ide-diagnostic-premise-reference"]')).toHaveLength(3);
      expect(panel.querySelectorAll('[data-testid="ide-diagnostic-premise-body"]')).toHaveLength(0);expect(c.store.result()).toBe(result);
      await new Promise<void>(r=>setTimeout(r,0));expect(document.activeElement).toBe(panel);
    });
    it('renders all original captured texts without altering the failure in '+locale,async()=>{
      const {c,root,fixture}=await readyPremises(locale);const result=c.store.result()!;await c.loadDiagnosticPremises('available');fixture.detectChanges();
      const panel=root.querySelector('[data-testid="ide-diagnostic-premises"]')!;const actual=c.store.diagnosticPremises()!;
      const bodies=panel.querySelectorAll('[data-testid="ide-diagnostic-premise-body"]');expect(bodies).toHaveLength(3);
      actual.bodies.forEach((body,i)=>{expect(bodies[i].querySelector('pre')!.textContent).toBe(body.text);expect(bodies[i].textContent).toContain(body.digest.value);});
      expect(actual.bodies.find(b=>b.kind==='RootInput')!.text).toBe(result.scopedSources!.inputText);
      expect(panel.textContent).toContain(c.t('ide.premisesNoticeCoverage'));expect(c.store.result()).toBe(result);expect(result.result.verdict).toBe('coglatas.fail');expect(c.completePass()).toBe(false);
    });
    it('shows an atomic budget gap with unchanged references in '+locale,async()=>{
      const {c,root,fixture}=await readyPremises(locale);const result=c.store.result();await c.loadDiagnosticPremises('budget');fixture.detectChanges();
      const panel=root.querySelector('[data-testid="ide-diagnostic-premises"]')!;expect(panel.textContent).toContain(c.t('ide.premisesBudgetExceeded'));
      expect(panel.textContent).toContain(locale==='en'?'Body bytes: 0 / 1':'本文バイト数: 0 / 1');
      expect(panel.querySelectorAll('[data-testid="ide-diagnostic-premise-reference"]')).toHaveLength(3);expect(panel.querySelectorAll('[data-testid="ide-diagnostic-premise-body"]')).toHaveLength(0);expect(c.store.result()).toBe(result);
    });
    it('preserves missing basis without inventing bodies in '+locale,async()=>{
      const {c,root,fixture}=await readyPremises(locale,'unknown');const reason=c.store.diagnostic()!.evidence.missingReason!;
      await c.loadDiagnosticPremises('available');fixture.detectChanges();expect(root.textContent).toContain(reason);
      expect(root.querySelector('[data-testid="ide-diagnostic-premises-state"]')!.textContent).toContain(c.t('ide.premisesNone'));
      expect(root.querySelectorAll('[data-testid="ide-diagnostic-premise-body"]')).toHaveLength(0);expect(c.completePass()).toBe(false);
    });
  }
  it('requires Source admission and keeps the existing explicit selection workflow',async()=>{
    const {c,fixture,root}=await render();c.choose('tests-publication-false-base');c.loadResult();c.inspectDiagnostic(c.store.result()!.result.diagnostics[0]);fixture.detectChanges();
    expect(root.textContent).toContain(c.t('ide.premisesRequireSources'));
    expect(Array.from(root.querySelectorAll('button')).some(b=>b.textContent===c.t('ide.premisesReadBodies'))).toBe(false);
    expect(await c.store.loadDiagnosticPremises()).toBe(false);expect(c.store.diagnosticPremises()).toBeNull();
  });
  it('withdraws data on current actor access loss',async()=>{
    const {c,fixture,root,authenticated}=await readyPremises();await c.loadDiagnosticPremises('available');fixture.detectChanges();const retained=c.store.diagnosticPremises()!;
    authenticated.set(false);fixture.detectChanges();await fixture.whenStable();fixture.detectChanges();
    expect(c.store.diagnosticPremises()).toBeNull();expect(root.querySelector('[data-testid="ide-diagnostic-premises"]')).toBeNull();expect(retained.bodies).toHaveLength(3);
  });
  it('withdraws a body read after another input is chosen',async()=>{
    const {c,fixture,root}=await readyPremises();const pending=c.loadDiagnosticPremises('available');c.choose('tests-publication-false-hypothesis');await pending;fixture.detectChanges();
    expect(c.store.diagnosticPremises()).toBeNull();expect(root.querySelector('[data-testid="ide-diagnostic-premises"]')).toBeNull();
  });
  it('keeps only the newest read when a second choice starts',async()=>{
    const {c,fixture,root}=await readyPremises();const older=c.loadDiagnosticPremises('available');const newest=c.loadDiagnosticPremises('manifest');await Promise.all([older,newest]);fixture.detectChanges();
    expect(c.store.diagnosticPremises()!.state).toBe('ManifestOnly');expect(root.querySelectorAll('[data-testid="ide-diagnostic-premise-body"]')).toHaveLength(0);
  });
  it('withdraws a read on Source readmission and same-ID foreign selection',async()=>{
    const {c,fixture,root}=await readyPremises();const diagnostic=c.store.diagnostic()!;await c.loadDiagnosticPremises('available');
    await c.loadScopedSources();fixture.detectChanges();expect(c.store.diagnosticPremises()).toBeNull();expect(c.store.diagnostic()).toBeNull();
    c.inspectDiagnostic(diagnostic);await c.loadDiagnosticPremises('available');expect(c.store.diagnosticPremises()).not.toBeNull();
    expect(c.store.selectDiagnostic({...diagnostic})).toBe(false);fixture.detectChanges();expect(c.store.diagnosticPremises()).toBeNull();
    expect(root.querySelector('[data-testid="ide-diagnostic-premises"]')).toBeNull();
  });
  it('reports invalid native metadata without exposing a partial body set',async()=>{
    const {c,fixture,root}=await render();c.choose('tests-publication-false-base');c.loadResult();
    Object.assign(c.store.context()!.diagnosticPremises!.diagnostics[0].available,{digest:{algorithm:'sha-256',value:'f'.repeat(64)}});
    await c.loadScopedSources();c.inspectDiagnostic(c.store.result()!.result.diagnostics[0]);await c.loadDiagnosticPremises();fixture.detectChanges();
    expect(c.store.diagnosticPremiseReadState()).toBe('unavailable');expect(c.store.diagnosticPremises()).toBeNull();
    expect(root.querySelector('[role="alert"]')!.textContent).toContain(c.t('ide.premisesUnavailable'));expect(root.querySelector('[data-testid="ide-diagnostic-premises"]')).toBeNull();
  });
  it('loads the original diagnostic premise example through the existing adapter',async()=>{
    const {c,adapter}=await render();await c.reload('diagnostic-premises');expect(adapter.read.mock.calls.at(-1)![0]).toContain('assets/projectide/diagnostic-premises-v1.json');
  });
});
