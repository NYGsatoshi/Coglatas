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
  const data = readPreviewBundle(JSON.parse(readFileSync(resolve('public/assets/projectide/predicate-recordings-v1.json'), 'utf8')));
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




async function readyPredicate(key = 'fail') {
  const view = await render(); view.c.choose(key); view.c.loadResult(); view.c.open('source');
  await view.c.loadRootSource(); view.fixture.detectChanges(); return view;
}
describe('Recorded predicate Workbench panels', () => {
  it.each(['en', 'ja'] as const)('keeps original Manifest metadata body-free and focuses its verified run: %s', async locale => {
    const { c, fixture, root } = await readyPredicate(); c.i18n.setLocale(locale); const result = c.store.result();
    await c.loadPredicateRecording(); fixture.detectChanges(); await fixture.whenStable(); fixture.detectChanges();
    const panel = root.querySelector<HTMLElement>('[data-testid="ide-predicate-recording"]')!;
    expect(panel.textContent).toContain(c.t('ide.predicateCaptureManifest'));
    expect(panel.textContent).toContain(c.store.predicateRecording()!.runId);
    expect(root.querySelectorAll('[data-testid="ide-recorded-predicate-body"]')).toHaveLength(0);
    expect(c.store.result()).toBe(result);
    await new Promise<void>(resolve => setTimeout(resolve, 0)); expect(document.activeElement).toBe(panel);
  });
  it.each(['en', 'ja'] as const)('shows exact original selected arrays only after explicit Source, run and diagnostic reads: %s', async locale => {
    const { c, fixture, root } = await readyPredicate(); c.i18n.setLocale(locale); const result = c.store.result()!;
    await c.loadPredicateRecording(); const original = c.store.predicateRecording()!; await c.loadPredicateRecording('diagnostic');
    expect(c.store.predicateRecording()!.originRunId).toBe(original.runId); expect(c.store.predicateRecording()!.runId).not.toBe(original.runId);
    c.inspectDiagnostic(result.result.diagnostics[0]); await c.loadRecordedPredicatePremises(); fixture.detectChanges();
    expect(root.querySelector('[data-testid="ide-predicate-view-state"]')!.textContent).toContain(c.t('ide.premisesManifestOnly'));
    expect(root.querySelectorAll('[data-testid="ide-recorded-predicate-body"]')).toHaveLength(0);
    await c.loadRecordedPredicatePremises('available'); fixture.detectChanges();
    const snapshot = c.store.recordedPredicatePremises()!; const panel = root.querySelector<HTMLElement>('[data-testid="ide-recorded-predicate"]')!;
    expect(panel.textContent).toContain(c.t('ide.predicateSelectedAvailable'));
    const elements = panel.querySelectorAll('[data-testid="ide-recorded-predicate-body"]'); expect(elements).toHaveLength(1);
    expect(elements[0].querySelector('pre')!.textContent).toBe(snapshot.bodies[0].text);
    expect(elements[0].textContent).toContain(snapshot.bodies[0].digest.value);
    expect(c.store.result()).toBe(result); expect(c.completePass()).toBe(false);
    await new Promise<void>(resolve => setTimeout(resolve, 0)); expect(document.activeElement).toBe(panel);
  });
  it.each(['en', 'ja'] as const)('separates original capture exhaustion from a later viewing limit: %s', async locale => {
    const { c, fixture, root } = await readyPredicate(); c.i18n.setLocale(locale); const result = c.store.result()!;
    await c.loadPredicateRecording('budget'); c.inspectDiagnostic(result.result.diagnostics[0]); await c.loadRecordedPredicatePremises('available'); fixture.detectChanges();
    let panel = root.querySelector('[data-testid="ide-recorded-predicate"]')!;
    expect(panel.textContent).toContain(c.t('ide.predicateCaptureBytes')); expect(panel.textContent).toContain(c.t('ide.predicateSelectedNone'));
    expect(c.store.recordedPredicatePremises()!.originalCaptureState).toBe('BodyBudgetExceeded');
    c.open('source'); await c.loadPredicateRecording('diagnostic'); c.inspectDiagnostic(result.result.diagnostics[0]); await c.loadRecordedPredicatePremises('budget'); fixture.detectChanges();
    panel = root.querySelector('[data-testid="ide-recorded-predicate"]')!;
    expect(panel.textContent).toContain(c.t('ide.predicateCaptureAvailable')); expect(panel.textContent).toContain(c.t('ide.premisesBudgetExceeded'));
    expect(c.store.recordedPredicatePremises()!.originalCaptureState).toBe('CapturedOriginalArrays');
    expect(root.querySelectorAll('[data-testid="ide-recorded-predicate-body"]')).toHaveLength(0); expect(c.store.result()).toBe(result);
  });
  it.each(['en', 'ja'] as const)('shows cancellation without borrowing a result or reconstructing a body: %s', async locale => {
    const { c, fixture, root } = await readyPredicate(); c.i18n.setLocale(locale); const result = c.store.result()!;
    await c.loadPredicateRecording('cancelled'); fixture.detectChanges();
    expect(root.querySelector('[data-testid="ide-predicate-no-result"]')!.textContent).toBe(c.t('ide.predicateNoResult'));
    c.inspectDiagnostic(result.result.diagnostics[0]); fixture.detectChanges();
    const panel = root.querySelector('[data-testid="ide-recorded-predicate"]')!;
    expect(panel.textContent).toContain(c.t('ide.predicateNoResult')); expect(panel.querySelectorAll('button')).toHaveLength(0);
    expect(panel.querySelectorAll('[data-testid="ide-recorded-predicate-body"]')).toHaveLength(0); expect(c.store.result()).toBe(result);
  });
  it.each(['en', 'ja'] as const)('does not expose a successful rule array without a selected diagnostic: %s', async locale => {
    const { c, fixture, root } = await readyPredicate('pass'); c.i18n.setLocale(locale);
    await c.loadPredicateRecording('diagnostic'); fixture.detectChanges();
    expect(root.querySelector('[data-testid="ide-predicate-capture-state"]')!.textContent).toBe(c.t('ide.predicateCaptureAvailable'));
    expect(c.store.result()!.result.diagnostics).toHaveLength(0);
    expect(root.querySelectorAll('[data-testid="ide-recorded-predicate-body"]')).toHaveLength(0); expect(c.store.recordedPredicatePremises()).toBeNull();
  });
  it('requires explicit root Source before offering recorded-run actions', async () => {
    const { c, fixture, root } = await render(); c.choose('fail'); c.loadResult(); c.open('source'); fixture.detectChanges();
    expect(root.querySelector('[data-testid="ide-predicate-recording-actions"]')).toBeNull();
    await c.loadPredicateRecording('diagnostic'); fixture.detectChanges();
    expect(root.querySelector('[data-testid="ide-predicate-recording"]')).toBeNull();
  });
  it('withdraws published selected bodies and metadata on permission loss', async () => {
    const { c, fixture, root, authenticated } = await readyPredicate();
    await c.loadPredicateRecording('diagnostic'); c.inspectDiagnostic(c.store.result()!.result.diagnostics[0]);
    await c.loadRecordedPredicatePremises('available'); fixture.detectChanges();
    expect(root.querySelector('[data-testid="ide-recorded-predicate-body"]')).not.toBeNull();
    authenticated.set(false); fixture.detectChanges(); await fixture.whenStable(); fixture.detectChanges();
    expect(c.store.predicateRecording()).toBeNull(); expect(c.store.recordedPredicatePremises()).toBeNull();
    expect(root.querySelector('[data-testid="ide-recorded-predicate-body"]')).toBeNull();
  });
  it('does not publish pending metadata into a newer input', async () => {
    const { c, fixture, root } = await readyPredicate(); const pending = c.loadPredicateRecording('diagnostic');
    c.choose('unknown'); await pending; fixture.detectChanges();
    expect(c.store.predicateRecording()).toBeNull(); expect(root.querySelector('[data-testid="ide-predicate-recording"]')).toBeNull();
  });
  it('does not publish pending original bodies after selection reset', async () => {
    const { c, fixture, root } = await readyPredicate(); await c.loadPredicateRecording('diagnostic');
    c.inspectDiagnostic(c.store.result()!.result.diagnostics[0]); const pending = c.loadRecordedPredicatePremises('available');
    c.choose('unknown'); await pending; fixture.detectChanges();
    expect(c.store.recordedPredicatePremises()).toBeNull(); expect(root.querySelector('[data-testid="ide-recorded-predicate-body"]')).toBeNull();
  });
  it('clears all published record views through the protected-state callback', async () => {
    const { c, fixture, root, clear } = await readyPredicate(); await c.loadPredicateRecording('retry');
    c.inspectDiagnostic(c.store.result()!.result.diagnostics[0]); await c.loadRecordedPredicatePremises('available'); fixture.detectChanges();
    clear(); fixture.detectChanges(); expect(c.store.predicateRecording()).toBeNull(); expect(c.store.recordedPredicatePremises()).toBeNull();
    expect(root.querySelector('[data-testid="ide-recorded-predicate-body"]')).toBeNull();
  });
});
