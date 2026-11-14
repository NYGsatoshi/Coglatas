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
  const data = readPreviewBundle(JSON.parse(readFileSync(resolve('public/assets/projectide/simulation-profile-v1.json'), 'utf8')));
  const adapter = {
    read: vi.fn(async () => {
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


async function profileReady(family = 'external') {
  const f = await render(); f.c.choose('tests-simulation-' + family + '-base'); f.c.loadResult(); f.c.open('source');
  await f.c.loadScopedSources(); await f.c.loadSimulation(); f.fixture.detectChanges(); return f;
}
describe('Actual model trace summary component', () => {
  it.each(['en', 'ja'] as const)('shows model kind, definitions, exact units and fixed recording provenance in %s', async locale => {
    const { c, fixture, root } = await profileReady(); c.i18n.setLocale(locale); const run = c.store.simulation();
    await c.loadSimulationTraceProfile(); fixture.detectChanges(); await fixture.whenStable();
    const panel = root.querySelector('[data-testid="ide-trace-profile"]')!;
    expect(panel.textContent).toContain(c.t('ide.profileNotice')); expect(panel.textContent).toContain(c.t('ide.profileComplete'));
    expect(panel.textContent).toContain('100 → 103'); expect(panel.textContent).toContain(run!.digest.value); expect(c.store.simulation()).toBe(run);
    const expected = { recordedElapsedTicks: '3', recordedBusyTicks: '3', unattributedIdleTicks: '0', recordedTaskTicks: '4', peakActiveTasks: '2', openTasks: '0' };
    for (const row of c.traceProfileRows) {
      const metric = panel.querySelector('[data-testid="ide-profile-' + row.key + '"]')!;
      expect(metric.querySelector('dd')!.textContent).toContain(expected[row.key] + ' ' + c.t(row.unit));
      expect(metric.textContent).toContain(c.t(row.definition));
    }
    await new Promise<void>(resolve => setTimeout(resolve, 0));
    expect(document.activeElement).toBe(panel);
  });
  it.each(['en', 'ja'] as const)('labels zero elapsed with two open Tasks as an incomplete prefix in %s', async locale => {
    const { c, fixture, root } = await profileReady('bounded'); c.i18n.setLocale(locale);
    await c.loadSimulationTraceProfile(); fixture.detectChanges(); await fixture.whenStable();
    const panel = root.querySelector('[data-testid="ide-trace-profile"]')!;
    expect(panel.textContent).toContain(c.t('ide.profileIncomplete')); expect(panel.textContent).not.toContain(c.t('ide.profileComplete'));
    expect(panel.querySelector('[data-testid="ide-profile-openTasks"]')!.textContent).toContain('2');
    expect(c.store.simulationTraceProfile()!.state).toBe('Incomplete'); expect(c.store.simulationTraceProfile()!.metrics.recordedElapsedTicks).toBe('0');
  });
  it('shows unavailable rather than metrics for altered original profile bytes', async () => {
    const { c, fixture, root } = await render(); c.choose('tests-simulation-external-base'); c.loadResult(); c.open('source');
    const frame = c.store.context()!.simulationTraceProfile!; Object.assign(frame, { text: frame.text + ' ' });
    await c.loadScopedSources(); await c.loadSimulation(); await c.loadSimulationTraceProfile(); fixture.detectChanges();
    expect(root.textContent).toContain(c.t('ide.profileUnavailable')); expect(root.querySelector('[data-testid="ide-trace-profile"]')).toBeNull();
  });
  it('withdraws a pending read and its focus when current access is lost', async () => {
    const { c, fixture, root, authenticated } = await profileReady(); const pending = c.loadSimulationTraceProfile();
    authenticated.set(false); fixture.detectChanges(); await pending; fixture.detectChanges(); await fixture.whenStable();
    expect(c.store.simulationTraceProfile()).toBeNull(); expect(root.querySelector('[data-testid="ide-trace-profile"]')).toBeNull(); expect(c.store.state()).toBe('denied');
  });
  it('withdraws an admitted profile immediately when current access is lost', async () => {
    const { c, fixture, root, authenticated } = await profileReady(); await c.loadSimulationTraceProfile(); fixture.detectChanges();
    const old = c.store.simulationTraceProfile()!; authenticated.set(false); fixture.detectChanges();
    expect(c.store.simulationTraceProfile()).toBeNull(); expect(c.store.simulation()).toBeNull(); expect(root.querySelector('[data-testid="ide-trace-profile"]')).toBeNull();
    expect(old.metrics.recordedTaskTicks).toBe('4');
  });
  it('does not focus or publish the previous input after a context switch during admission', async () => {
    const { c, fixture, root } = await profileReady(); const pending = c.loadSimulationTraceProfile();
    c.choose('tests-simulation-external-hypothesis'); c.loadResult(); await pending; fixture.detectChanges(); await fixture.whenStable();
    expect(c.store.simulationTraceProfile()).toBeNull(); expect(root.querySelector('[data-testid="ide-trace-profile"]')).toBeNull();
  });
  it('withdraws profile state on the existing protected-state clear callback', async () => {
    const { c, fixture, root, clear } = await profileReady(); await c.loadSimulationTraceProfile(); fixture.detectChanges(); clear(); fixture.detectChanges();
    expect(c.store.simulationTraceProfile()).toBeNull(); expect(root.querySelector('[data-testid="ide-trace-profile"]')).toBeNull();
  });
  it('does not offer a profile when required validation produced no plan or run', async () => {
    const { c, fixture, root } = await render(); c.choose('tests-simulation-unknown-base'); c.loadResult(); c.open('source'); await c.loadScopedSources();
    await c.loadSimulationTraceProfile(); fixture.detectChanges(); expect(c.store.simulationTraceProfile()).toBeNull();
    expect(Array.from(root.querySelectorAll('button')).some(b => b.textContent === c.t('ide.profileLoad'))).toBe(false);
  });
});
