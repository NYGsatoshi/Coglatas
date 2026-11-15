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
  const data = readPreviewBundle(JSON.parse(readFileSync(resolve('public/assets/projectide/operator-recordings-v1.json'), 'utf8')));
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




async function readyOperator(key = 'fail') {
  const view = await render(); view.c.choose(key); view.c.loadResult(); view.c.open('source');
  await view.c.loadRootSource(); view.fixture.detectChanges(); return view;
}
describe('Recorded operator Workbench panels', () => {
  it.each(['en', 'ja'] as const)('keeps original Manifest metadata body-free and focuses its verified run: %s', async locale => {
    const { c, fixture, root } = await readyOperator(); c.i18n.setLocale(locale); const result = c.store.result();
    await c.loadOperatorRecording(); fixture.detectChanges(); await fixture.whenStable(); fixture.detectChanges();
    const panel = root.querySelector<HTMLElement>('[data-testid="ide-operator-recording"]')!;
    expect(panel.textContent).toContain(c.t('ide.operatorCaptureManifest'));
    expect(panel.textContent).toContain(c.store.operatorRecording()!.runId);
    expect(root.querySelectorAll('[data-testid="ide-operator-node"]')).toHaveLength(0);
    expect(c.store.result()).toBe(result);
    await new Promise<void>(resolve => setTimeout(resolve, 0)); expect(document.activeElement).toBe(panel);
  });
  it.each(['en', 'ja'] as const)('shows exact original selected operators only after explicit Source, run and diagnostic reads: %s', async locale => {
    const { c, fixture, root } = await readyOperator(); c.i18n.setLocale(locale); const result = c.store.result()!;
    await c.loadOperatorRecording(); const original = c.store.operatorRecording()!; await c.loadOperatorRecording('diagnostic');
    expect(c.store.operatorRecording()!.originRunId).toBe(original.runId); expect(c.store.operatorRecording()!.runId).not.toBe(original.runId);
    c.inspectDiagnostic(result.result.diagnostics[0]); await c.loadRecordedOperators(); fixture.detectChanges();
    expect(root.querySelector('[data-testid="ide-operator-view-state"]')!.textContent).toContain(c.t('ide.premisesManifestOnly'));
    expect(root.querySelectorAll('[data-testid="ide-operator-node"]')).toHaveLength(0);
    await c.loadRecordedOperators('available'); fixture.detectChanges();
    const snapshot = c.store.recordedOperators()!; const panel = root.querySelector<HTMLElement>('[data-testid="ide-recorded-operators"]')!;
    expect(panel.textContent).toContain(c.t('ide.operatorViewAvailable'));
    const elements = panel.querySelectorAll('[data-testid="ide-operator-node"]'); expect(elements).toHaveLength(1);
    expect(elements[0].querySelector('pre')!.textContent).toBe(snapshot.evaluations[0].nodes[0].original.text);
    expect(elements[0].textContent).toContain(snapshot.evaluations[0].nodes[0].original.digest.value);
    expect(c.store.result()).toBe(result); expect(c.completePass()).toBe(false);
    await new Promise<void>(resolve => setTimeout(resolve, 0)); expect(document.activeElement).toBe(panel);
  });
  it.each(['en', 'ja'] as const)('separates original capture exhaustion from a later viewing limit: %s', async locale => {
    const { c, fixture, root } = await readyOperator(); c.i18n.setLocale(locale); const result = c.store.result()!;
    await c.loadOperatorRecording('budget'); c.inspectDiagnostic(result.result.diagnostics[0]); await c.loadRecordedOperators('available'); fixture.detectChanges();
    let panel = root.querySelector('[data-testid="ide-recorded-operators"]')!;
    expect(panel.textContent).toContain(c.t('ide.predicateCaptureBytes')); expect(panel.textContent).toContain(c.t('ide.operatorNoRecorded'));
    expect(c.store.recordedOperators()!.originalCaptureState).toBe('BodyBudgetExceeded');
    c.open('source'); await c.loadOperatorRecording('diagnostic'); c.inspectDiagnostic(result.result.diagnostics[0]); await c.loadRecordedOperators('budget'); fixture.detectChanges();
    panel = root.querySelector('[data-testid="ide-recorded-operators"]')!;
    expect(panel.textContent).toContain(c.t('ide.operatorCaptureAvailable')); expect(panel.textContent).toContain(c.t('ide.premisesBudgetExceeded'));
    expect(c.store.recordedOperators()!.originalCaptureState).toBe('RecordedInitialRootEvaluations');
    expect(root.querySelectorAll('[data-testid="ide-operator-node"]')).toHaveLength(0); expect(c.store.result()).toBe(result);
  });
  it.each(['en', 'ja'] as const)('shows cancellation without borrowing a result or reconstructing a body: %s', async locale => {
    const { c, fixture, root } = await readyOperator(); c.i18n.setLocale(locale); const result = c.store.result()!;
    await c.loadOperatorRecording('cancelled'); fixture.detectChanges();
    expect(root.querySelector('[data-testid="ide-operator-no-result"]')!.textContent).toBe(c.t('ide.operatorNoResult'));
    c.inspectDiagnostic(result.result.diagnostics[0]); fixture.detectChanges();
    const panel = root.querySelector('[data-testid="ide-recorded-operators"]')!;
    expect(panel.textContent).toContain(c.t('ide.operatorNoResult')); expect(panel.querySelectorAll('button')).toHaveLength(0);
    expect(panel.querySelectorAll('[data-testid="ide-operator-node"]')).toHaveLength(0); expect(c.store.result()).toBe(result);
  });
  it.each(['en', 'ja'] as const)('does not expose a successful rule graph without a selected diagnostic: %s', async locale => {
    const { c, fixture, root } = await readyOperator('pass'); c.i18n.setLocale(locale);
    await c.loadOperatorRecording('diagnostic'); fixture.detectChanges();
    expect(root.querySelector('[data-testid="ide-operator-capture-state"]')!.textContent).toBe(c.t('ide.operatorCaptureAvailable'));
    expect(c.store.result()!.result.diagnostics).toHaveLength(0);
    expect(root.querySelectorAll('[data-testid="ide-operator-node"]')).toHaveLength(0); expect(c.store.recordedOperators()).toBeNull();
  });
  it('requires explicit root Source before offering recorded-run actions', async () => {
    const { c, fixture, root } = await render(); c.choose('fail'); c.loadResult(); c.open('source'); fixture.detectChanges();
    expect(root.querySelector('[data-testid="ide-operator-recording-actions"]')).toBeNull();
    await c.loadOperatorRecording('diagnostic'); fixture.detectChanges();
    expect(root.querySelector('[data-testid="ide-operator-recording"]')).toBeNull();
  });
  it('withdraws published selected bodies and metadata on permission loss', async () => {
    const { c, fixture, root, authenticated } = await readyOperator();
    await c.loadOperatorRecording('diagnostic'); c.inspectDiagnostic(c.store.result()!.result.diagnostics[0]);
    await c.loadRecordedOperators('available'); fixture.detectChanges();
    expect(root.querySelector('[data-testid="ide-operator-node"]')).not.toBeNull();
    authenticated.set(false); fixture.detectChanges(); await fixture.whenStable(); fixture.detectChanges();
    expect(c.store.operatorRecording()).toBeNull(); expect(c.store.recordedOperators()).toBeNull();
    expect(root.querySelector('[data-testid="ide-operator-node"]')).toBeNull();
  });
  it('does not publish pending metadata into a newer input', async () => {
    const { c, fixture, root } = await readyOperator(); const pending = c.loadOperatorRecording('diagnostic');
    c.choose('unknown'); await pending; fixture.detectChanges();
    expect(c.store.operatorRecording()).toBeNull(); expect(root.querySelector('[data-testid="ide-operator-recording"]')).toBeNull();
  });
  it('does not publish pending original bodies after selection reset', async () => {
    const { c, fixture, root } = await readyOperator(); await c.loadOperatorRecording('diagnostic');
    c.inspectDiagnostic(c.store.result()!.result.diagnostics[0]); const pending = c.loadRecordedOperators('available');
    c.choose('unknown'); await pending; fixture.detectChanges();
    expect(c.store.recordedOperators()).toBeNull(); expect(root.querySelector('[data-testid="ide-operator-node"]')).toBeNull();
  });
  it('clears all published record views through the protected-state callback', async () => {
    const { c, fixture, root, clear } = await readyOperator(); await c.loadOperatorRecording('retry');
    c.inspectDiagnostic(c.store.result()!.result.diagnostics[0]); await c.loadRecordedOperators('available'); fixture.detectChanges();
    clear(); fixture.detectChanges(); expect(c.store.operatorRecording()).toBeNull(); expect(c.store.recordedOperators()).toBeNull();
    expect(root.querySelector('[data-testid="ide-operator-node"]')).toBeNull();
  });
});

describe('Original operator traversal in the actual Inspector', () => {
  it.each(['en', 'ja'] as const)('renders original nested preorder, duplicates and skipped operands: %s', async locale => {
    const { c, fixture, root } = await readyOperator('operator-nested-duplicates'); c.i18n.setLocale(locale);
    await c.loadOperatorRecording(); fixture.detectChanges();
    expect(root.querySelector('[data-testid="ide-operator-visited"]')!.textContent).toBe('6');
    expect(c.store.operatorRecording()!.retainedBodyBytes).toBe(0);
    await c.loadOperatorRecording('diagnostic'); c.inspectDiagnostic(c.store.result()!.result.diagnostics[0]); await c.loadRecordedOperators('available'); fixture.detectChanges();
    const snapshot = c.store.recordedOperators()!; const nodes = snapshot.evaluations[0].nodes;
    const elements = root.querySelectorAll('[data-testid="ide-operator-node"]'); expect(elements).toHaveLength(6);
    elements.forEach((element, index) => {
      expect(element.querySelector('strong')!.textContent).toContain(c.t('ide.operatorNode') + ' ' + index);
      expect(element.querySelector('pre')!.textContent).toBe(nodes[index].original.text);
    });
    expect(elements[0].textContent).toContain('1, 4'); expect(nodes[0].skippedOperands).toBe(1);
    expect(nodes.filter(n => n.literal).map(n => n.literal!.premiseOrdinal)).toEqual([0, 1, 2]);
    expect(nodes[3].literal!.digest).toEqual(nodes[5].literal!.digest);
    expect(root.querySelector('[data-testid="ide-recorded-operators"]')!.textContent).toContain(c.t('ide.operatorSelectedNotice'));
  });
  it('loads the actual operator example through its separate existing offline adapter', async () => {
    const { c, adapter } = await render(); await c.reload('operator-recordings');
    expect(adapter.read.mock.calls.at(-1)?.[0]).toContain('/assets/projectide/operator-recordings-v1.json');
    expect(c.store.bundle()!.cases).toHaveLength(8);
  });
});
