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
  const data = readPreviewBundle(JSON.parse(readFileSync(resolve('public/assets/projectide/regional-operator-recordings-v1.json'), 'utf8')));
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




async function readyRegional(key = 'tests-regional-versioned-base') {
  const view = await render(); view.c.choose(key); view.c.loadResult(); view.c.open('source');
  await view.c.loadScopedSources(); view.fixture.detectChanges(); return view;
}
function selectObserved(c: ProjectIdeWorkbenchComponent): void {
  const region = c.store.regionalOperatorRecording()!.regions.find(r => r.diagnostics.some(d => d.code === 'coglatas.analysis.constraint-false'))!;
  c.selectRegionalOperatorDiagnostic(region, region.diagnostics.find(d => d.code === 'coglatas.analysis.constraint-false')!);
}
describe('Regional recorded operator Workbench panels', () => {
  it.each(['en', 'ja'] as const)('keeps original metadata body-free and focuses its verified host pair: %s', async locale => {
    const { c, fixture, root } = await readyRegional(); c.i18n.setLocale(locale); const result = c.store.result();
    await c.loadRegionalOperatorRecording(); fixture.detectChanges(); await fixture.whenStable(); fixture.detectChanges();
    const panel = root.querySelector<HTMLElement>('[data-testid="ide-regional-operator-recording"]')!;
    expect(panel.textContent).toContain(c.t('ide.operatorCaptureManifest')); expect(panel.textContent).toContain(c.store.regionalOperatorRecording()!.runId);
    expect(root.querySelectorAll('[data-testid="ide-regional-region"]')).toHaveLength(3);
    expect(root.querySelectorAll('[data-testid="ide-regional-node"]')).toHaveLength(0); expect(c.store.result()).toBe(result);
    await new Promise<void>(resolve => setTimeout(resolve, 0)); expect(document.activeElement).toBe(panel);
  });
  it.each(['en', 'ja'] as const)('selects an actual regional diagnostic and shows only its original supplied frames: %s', async locale => {
    const { c, fixture, root } = await readyRegional(); c.i18n.setLocale(locale); const result = c.store.result();
    await c.loadRegionalOperatorRecording('diagnostic'); selectObserved(c); await c.loadRecordedRegionalOperators(); fixture.detectChanges();
    expect(root.querySelector('[data-testid="ide-regional-view-state"]')!.textContent).toContain(c.t('ide.premisesManifestOnly'));
    expect(root.querySelectorAll('[data-testid="ide-regional-node"]')).toHaveLength(0);
    await c.loadRecordedRegionalOperators('available'); fixture.detectChanges();
    const snapshot = c.store.recordedRegionalOperators()!, panel = root.querySelector<HTMLElement>('[data-testid="ide-recorded-regional-operators"]')!;
    expect(panel.textContent).toContain(c.t('ide.operatorViewAvailable')); expect(panel.querySelectorAll('[data-testid="ide-regional-node"]')).toHaveLength(1);
    expect(panel.querySelector('[data-testid="ide-regional-node"] pre')!.textContent).toBe(snapshot.evaluations[0].nodes[0].original.text);
    expect(panel.textContent).toContain(snapshot.evaluations[0].nodes[0].original.digest.value); expect(c.store.result()).toBe(result); expect(c.completePass()).toBe(false);
    await new Promise<void>(resolve => setTimeout(resolve, 0)); expect(document.activeElement).toBe(panel);
  });
  it.each(['en', 'ja'] as const)('keeps capture exhaustion separate from the viewing budget: %s', async locale => {
    const { c, fixture, root } = await readyRegional(); c.i18n.setLocale(locale);
    await c.loadRegionalOperatorRecording('budget'); selectObserved(c); await c.loadRecordedRegionalOperators('available'); fixture.detectChanges();
    let panel = root.querySelector('[data-testid="ide-recorded-regional-operators"]')!;
    expect(panel.textContent).toContain(c.t('ide.predicateCaptureBytes')); expect(panel.textContent).toContain(c.t('ide.operatorNoRecorded'));
    await c.loadRegionalOperatorRecording('diagnostic'); selectObserved(c); await c.loadRecordedRegionalOperators('budget'); fixture.detectChanges();
    panel = root.querySelector('[data-testid="ide-recorded-regional-operators"]')!;
    expect(panel.textContent).toContain(c.t('ide.regionalOperatorCaptured')); expect(panel.textContent).toContain(c.t('ide.premisesBudgetExceeded'));
    expect(root.querySelectorAll('[data-testid="ide-regional-node"]')).toHaveLength(0);
  });
  it.each(['en', 'ja'] as const)('keeps the other captured version’s unobserved diagnostic body-free: %s', async locale => {
    const { c, fixture, root } = await readyRegional(); c.i18n.setLocale(locale); await c.loadRegionalOperatorRecording('diagnostic');
    const region = c.store.regionalOperatorRecording()!.regions.find(r => r.diagnostics.some(d => d.code !== 'coglatas.analysis.constraint-false'))!;
    const diagnostic = region.diagnostics.find(d => d.code !== 'coglatas.analysis.constraint-false')!;
    c.selectRegionalOperatorDiagnostic(region, diagnostic); await c.loadRecordedRegionalOperators('available'); fixture.detectChanges();
    expect(c.store.recordedRegionalOperators()!.state).toBe('NoObservedRegionalDiagnostic');
    expect(root.querySelector('[data-testid="ide-regional-view-state"]')!.textContent).toContain(c.t('ide.regionalOperatorNoObserved'));
    expect(root.querySelectorAll('[data-testid="ide-regional-node"]')).toHaveLength(0);
  });
  it.each(['en', 'ja'] as const)('shows a cancelled attempt without result or diagnostic selectors: %s', async locale => {
    const { c, fixture, root } = await readyRegional(); c.i18n.setLocale(locale); await c.loadRegionalOperatorRecording('cancelled'); fixture.detectChanges();
    expect(root.querySelector('[data-testid="ide-regional-no-result"]')!.textContent).toBe(c.t('ide.operatorNoResult'));
    expect(root.querySelectorAll('[data-testid="ide-regional-select-diagnostic"]')).toHaveLength(0);
    expect(root.querySelector('[data-testid="ide-recorded-regional-operators"]')).toBeNull();
  });
  it('requires Source admission and keeps external regions hidden with only the root Source', async () => {
    const { c, fixture, root } = await render(); c.choose('tests-regional-versioned-base'); c.loadResult(); c.open('source'); fixture.detectChanges();
    expect(root.querySelector('[data-testid="ide-regional-require-source"]')).not.toBeNull();
    expect(root.querySelector('[data-testid="ide-regional-diagnostic"]')).toBeNull();
    await c.loadRootSource(); await c.loadRegionalOperatorRecording('diagnostic'); fixture.detectChanges();
    expect(root.querySelectorAll('[data-testid="ide-regional-region"]')).toHaveLength(1);
    expect(root.querySelectorAll('[data-testid="ide-regional-select-diagnostic"]')).toHaveLength(0);
    await c.loadScopedSources(); fixture.detectChanges(); expect(root.querySelector('[data-testid="ide-regional-operator-recording"]')).toBeNull();
  });
  it('uses native buttons to select a region and explicitly read original bodies', async () => {
    const { c, fixture, root } = await readyRegional(); await c.loadRegionalOperatorRecording('diagnostic'); fixture.detectChanges();
    const buttons = [...root.querySelectorAll<HTMLButtonElement>('[data-testid="ide-regional-select-diagnostic"]')];
    const button = buttons.find(b => b.textContent!.includes('coglatas.analysis.constraint-false'))!;
    expect(button.type).toBe('button'); expect(button.getAttribute('aria-label')).toContain('Select regional diagnostic'); button.click(); fixture.detectChanges();
    expect(button.getAttribute('aria-pressed')).toBe('true');
    root.querySelector<HTMLButtonElement>('[data-testid="ide-regional-read-available"]')!.click();
    await vi.waitFor(() => { fixture.detectChanges(); expect(root.querySelectorAll('[data-testid="ide-regional-node"]')).toHaveLength(1); });
    root.querySelector<HTMLButtonElement>('[data-testid="ide-regional-navigate"]')!.click(); fixture.detectChanges();
    expect(c.panel()).toBe('source'); expect(c.store.scopedSourceTarget()!.location).toBe(c.store.regionalOperatorSelection()!.diagnostic.primaryLocation);
  });
  it('never offers a successful evaluation body without an actual published diagnostic', async () => {
    const { c, fixture, root } = await readyRegional('tests-regional-true-base'); await c.loadRegionalOperatorRecording('diagnostic'); fixture.detectChanges();
    expect(c.store.regionalOperatorRecording()!.observedNodes).toBe(1);
    expect(root.querySelectorAll('[data-testid="ide-regional-select-diagnostic"]')).toHaveLength(0); expect(root.querySelectorAll('[data-testid="ide-regional-node"]')).toHaveLength(0);
  });
  it('loads the separate original regional fixture through the existing offline adapter', async () => {
    const { c, adapter } = await render(); await c.reload('regional-operator-recordings');
    expect(adapter.read.mock.calls.at(-1)?.[0]).toContain('/assets/projectide/regional-operator-recordings-v1.json'); expect(c.store.bundle()!.cases).toHaveLength(30);
  });
  it('withdraws metadata, selected bodies and focus targets on permission loss', async () => {
    const { c, fixture, root, authenticated } = await readyRegional(); await c.loadRegionalOperatorRecording('diagnostic'); selectObserved(c);
    await c.loadRecordedRegionalOperators('available'); fixture.detectChanges(); expect(root.querySelector('[data-testid="ide-regional-node"]')).not.toBeNull();
    authenticated.set(false); await fixture.whenStable(); fixture.detectChanges();
    expect(c.store.regionalOperatorRecording()).toBeNull(); expect(c.store.recordedRegionalOperators()).toBeNull(); expect(root.querySelector('[data-testid="ide-regional-node"]')).toBeNull();
  });
  it('refuses pending metadata after an input switch', async () => {
    const { c, fixture, root } = await readyRegional(); const pending = c.loadRegionalOperatorRecording('diagnostic'); c.choose('tests-regional-unknown-base');
    await pending; fixture.detectChanges(); expect(c.store.regionalOperatorRecording()).toBeNull(); expect(root.querySelector('[data-testid="ide-regional-operator-recording"]')).toBeNull();
  });
  it('refuses pending selected bodies after selection reset', async () => {
    const { c, fixture, root } = await readyRegional(); await c.loadRegionalOperatorRecording('diagnostic'); selectObserved(c);
    const pending = c.loadRecordedRegionalOperators('available'); c.store.clearSelection(); await pending; fixture.detectChanges();
    expect(c.store.recordedRegionalOperators()).toBeNull(); expect(root.querySelector('[data-testid="ide-regional-node"]')).toBeNull();
  });
  it('clears through the protected-state callback without preserving a selected graph', async () => {
    const { c, fixture, root, clear } = await readyRegional(); await c.loadRegionalOperatorRecording('retry'); selectObserved(c); await c.loadRecordedRegionalOperators('available');
    clear(); fixture.detectChanges(); expect(c.store.regionalOperatorRecording()).toBeNull(); expect(c.store.regionalOperatorSelection()).toBeNull();
    expect(root.querySelector('[data-testid="ide-regional-node"]')).toBeNull();
  });
  it('withdraws an in-flight recorded run on component destruction', async () => {
    const { c, fixture } = await readyRegional(); const pending = c.loadRegionalOperatorRecording('retry'); fixture.destroy(); await pending;
    expect(c.store.state()).toBe('idle'); expect(c.store.regionalOperatorRecording()).toBeNull(); expect(c.store.recordedRegionalOperators()).toBeNull();
  });
});

describe('Original regional compound and Requirement graphs in the actual panel', () => {
  it.each(['en', 'ja'] as const)('renders actual regional nested order without replacing original frame text: %s', async locale => {
    const { c, fixture, root } = await readyRegional('tests-regional-nested-duplicates-base'); c.i18n.setLocale(locale);
    await c.loadRegionalOperatorRecording('diagnostic'); selectObserved(c); await c.loadRecordedRegionalOperators('available'); fixture.detectChanges();
    const graph = c.store.recordedRegionalOperators()!.evaluations[0], elements = root.querySelectorAll('[data-testid="ide-regional-node"]');
    expect(elements).toHaveLength(6); elements.forEach((element, ordinal) => expect(element.querySelector('pre')!.textContent).toBe(graph.nodes[ordinal].original.text));
    expect(graph.nodes[0].evaluatedChildren).toEqual([1, 4]); expect(graph.nodes[0].skippedOperands).toBe(1);
  });
  for (const [truth, nodes] of [['false', 6], ['true', 2], ['unknown', 3]] as const) {
    it.each(['en', 'ja'] as const)('keeps actual regional Requirement evidence separate: ' + truth + ' %s', async locale => {
      const { c, fixture, root } = await readyRegional('tests-regional-requirement-' + truth + '-base'); c.i18n.setLocale(locale);
      const result = c.store.result(); await c.loadRegionalOperatorRecording('diagnostic'); expect(c.store.regionalOperatorRecording()!.observedNodes).toBe(nodes);
      expect(result!.result.evidenceComplete).toBe(false);
      for (const region of c.store.regionalOperatorRecording()!.regions) for (const diagnostic of region.diagnostics) {
        c.selectRegionalOperatorDiagnostic(region, diagnostic); await c.loadRecordedRegionalOperators('available'); fixture.detectChanges();
        const condition = diagnostic.code === 'coglatas.analysis.requirement-condition-false';
        expect(root.querySelectorAll('[data-testid="ide-regional-node"]')).toHaveLength(condition ? nodes : 0);
        if (!condition) expect(root.querySelector('[data-testid="ide-regional-view-state"]')!.textContent).toContain(c.t('ide.regionalOperatorNoObserved'));
        expect(c.store.result()).toBe(result); expect(c.completePass()).toBe(false);
      }
    });
  }
});
