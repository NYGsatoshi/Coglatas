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
  const data = readPreviewBundle(JSON.parse(readFileSync(resolve('public/assets/projectide/compiler-outputs-v1.json'), 'utf8')));
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




async function basisReady(locale: 'en' | 'ja', family = 'false') {
  const f = await render(); f.c.i18n.setLocale(locale); f.c.choose('tests-publication-'+family+'-base');
  f.c.loadResult(); f.c.inspectDiagnostic(f.c.store.result()!.result.diagnostics[0]); f.fixture.detectChanges(); return f;
}
describe('Recorded diagnostic basis component', () => {
  for (const locale of ['en','ja'] as const) {
    it('shows exact digest references and limitations in '+locale, async () => {
      const { c, fixture, root } = await basisReady(locale); const context = c.store.context()!;
      const panel = root.querySelector('[data-testid="ide-diagnostic-basis"]')!;
      expect(panel.textContent).toContain(c.t('ide.basisNotice'));
      expect(panel.textContent).toContain(context.inputDigest.value); expect(panel.textContent).toContain(context.resultDigest.value);
      expect(panel.textContent).toContain(context.result.diagnostics[0].diagnosticId);
      expect(Array.from(panel.querySelectorAll('[data-testid="ide-diagnostic-basis-premise"]')).map(r=>r.textContent))
        .toEqual(context.result.diagnostics[0].evidence.premises.map(p=>p.algorithm+':'+p.value));
      expect(c.completePass()).toBe(false); expect(context.result.verdict).toBe('coglatas.fail');
      c.choose('tests-publication-false-hypothesis'); fixture.detectChanges();
      expect(c.store.diagnosticBasis()).toBeNull(); expect(root.querySelector('[data-testid="ide-diagnostic-basis"]')).toBeNull();
    });
    it('shows recorded missing evidence without an invented premise in '+locale, async () => {
      const {c,root} = await basisReady(locale,'unknown'); const diagnostic=c.store.diagnostic()!;
      expect(diagnostic.evidence.kind).toBe('coglatas.missing'); expect(diagnostic.evidence.missingReason).not.toBeNull();
      expect(root.querySelector('[data-testid="ide-diagnostic-basis-missing"]')!.textContent).toBe(diagnostic.evidence.missingReason);
      expect(root.querySelectorAll('[data-testid="ide-diagnostic-basis-premise"]')).toHaveLength(0);
      expect(root.textContent).toContain(c.t('ide.basisNoReferences')); expect(c.completePass()).toBe(false);
    });
  }
  it('withdraws basis on actor access loss', async () => {
    const {c,fixture,root,authenticated}=await basisReady('en'); authenticated.set(false);
    fixture.detectChanges(); await fixture.whenStable(); fixture.detectChanges();
    expect(c.store.diagnosticBasis()).toBeNull(); expect(root.querySelector('[data-testid="ide-diagnostic-basis"]')).toBeNull();
  });
  it('rejects a foreign diagnostic copy and resets the page on selection change', async () => {
    const {c,fixture,root}=await basisReady('en'); const diagnostic=c.store.diagnostic()!;
    c.basisPage.set(1); expect(c.store.selectDiagnostic({...diagnostic})).toBe(false); fixture.detectChanges();
    expect(c.basisPage()).toBe(0); expect(root.querySelector('[data-testid="ide-diagnostic-basis"]')).toBeNull();
    c.inspectDiagnostic(diagnostic); fixture.detectChanges(); expect(c.store.diagnosticBasis()).not.toBeNull();
    c.store.result.set({...c.store.result()!}); fixture.detectChanges();
    expect(c.store.diagnosticBasis()).toBeNull(); expect(root.querySelector('[data-testid="ide-diagnostic-basis"]')).toBeNull();
  });
  it('withdraws a mutable package that no longer passes admission', async () => {
    const {c,fixture,root}=await basisReady('en');
    (c.store.diagnostic()!.evidence as {missingReason:string|null}).missingReason='coglatas.invalid-mutated-basis';
    fixture.changeDetectorRef.markForCheck(); fixture.detectChanges(); expect(c.store.diagnosticBasis()).toBeNull();
    expect(root.querySelector('[data-testid="ide-diagnostic-basis"]')).toBeNull();
  });
});
