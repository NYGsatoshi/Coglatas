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
describe('Recorded whole-pass Workbench panels', () => {
  it.each(['en', 'ja'] as const)('keeps original Manifest metadata body-free and focuses the verified run: %s', async locale => {
    const {c, fixture, root} = await readyPass(); c.i18n.setLocale(locale); const result = c.store.result();
    await c.loadPassRecording(); fixture.detectChanges(); await fixture.whenStable(); fixture.detectChanges();
    const panel = root.querySelector<HTMLElement>('[data-testid="ide-pass-recording"]')!;
    expect(panel.textContent).toContain(c.t('ide.passCaptureManifest')); expect(panel.textContent).toContain(c.store.passRecording()!.runId);
    expect(root.querySelector('[data-testid="ide-pass-visited"]')!.textContent).toBe('10');
    expect(panel.querySelectorAll('li')).toHaveLength(10); expect(root.querySelectorAll('[data-testid="ide-pass-body"]')).toHaveLength(0);
    expect(c.store.result()).toBe(result); await new Promise<void>(resolve => setTimeout(resolve, 0)); expect(document.activeElement).toBe(panel);
  });
  it.each(['en', 'ja'] as const)('reads exact original whole-pass bodies with no diagnostic selection: %s', async locale => {
    const {c, fixture, root} = await readyPass('pass'); c.i18n.setLocale(locale); const result = c.store.result();
    expect(result!.result.diagnostics).toHaveLength(0); await c.loadPassRecording('diagnostic'); await c.loadRecordedPasses(); fixture.detectChanges();
    expect(root.querySelector('[data-testid="ide-recorded-passes"]')!.textContent).toContain(c.t('ide.passManifestOnly'));
    expect(root.querySelectorAll('[data-testid="ide-pass-body"]')).toHaveLength(0);
    await c.loadRecordedPasses('available'); fixture.detectChanges(); await fixture.whenStable(); fixture.detectChanges();
    const snapshot = c.store.recordedPasses()!; const panel = root.querySelector<HTMLElement>('[data-testid="ide-recorded-passes"]')!;
    expect(panel.textContent).toContain(c.t('ide.passAvailable')); const bodies = panel.querySelectorAll('[data-testid="ide-pass-body"]');
    expect(bodies.length).toBe(snapshot.bodies.length); expect(bodies.length).toBeGreaterThan(0);
    bodies.forEach((body, i) => {expect(body.querySelector('pre')!.textContent).toBe(snapshot.bodies[i].text); expect(body.textContent).toContain(snapshot.bodies[i].digest.value);});
    expect(c.store.result()).toBe(result); await new Promise<void>(resolve => setTimeout(resolve, 0)); expect(document.activeElement).toBe(panel);
  });
  it.each(['en', 'ja'] as const)('separates original byte/event gaps from a later viewer limit: %s', async locale => {
    const {c, fixture, root} = await readyPass(); c.i18n.setLocale(locale); const result = c.store.result();
    for (const [label, state, message] of [['budget', 'BodyBudgetExceeded', 'ide.passCaptureBytes'], ['eventGap', 'EventLimitExceeded', 'ide.passCaptureEvents']] as const) {
      await c.loadPassRecording(label); await c.loadRecordedPasses('available'); fixture.detectChanges();
      expect(root.querySelector('[data-testid="ide-pass-recording"]')!.textContent).toContain(c.t(message));
      expect(root.querySelector('[data-testid="ide-recorded-passes"]')!.textContent).toContain(c.t('ide.passNoBodies'));
      expect(c.store.recordedPasses()!.originalCaptureState).toBe(state); expect(root.querySelectorAll('[data-testid="ide-pass-body"]')).toHaveLength(0);
    }
    await c.loadPassRecording('diagnostic'); await c.loadRecordedPasses('budget'); fixture.detectChanges();
    expect(root.querySelector('[data-testid="ide-pass-recording"]')!.textContent).toContain(c.t('ide.passCaptureAvailable'));
    expect(root.querySelector('[data-testid="ide-recorded-passes"]')!.textContent).toContain(c.t('ide.passBudgetExceeded'));
    expect(c.store.recordedPasses()!.originalCaptureState).toBe('AvailableOriginalJson'); expect(c.store.result()).toBe(result);
  });
  it.each(['en', 'ja'] as const)('shows cancelled host outcome and a distinct genuinely linked retry: %s', async locale => {
    const {c, fixture, root} = await readyPass(); c.i18n.setLocale(locale); const result = c.store.result();
    await c.loadPassRecording('cancelled'); fixture.detectChanges(); const cancelled = c.store.passRecording()!;
    expect(root.querySelector('[data-testid="ide-pass-no-result"]')!.textContent).toBe(c.t('ide.passNoResult'));
    expect(root.querySelector('[data-testid="ide-recorded-passes"]')).toBeNull(); expect(c.store.recordedPasses()).toBeNull();
    await c.loadRecordedPasses('available'); expect(c.store.recordedPasses()).toBeNull();
    await c.loadPassRecording('retry'); await c.loadRecordedPasses('available'); fixture.detectChanges();
    expect(c.store.passRecording()!.originRunId).toBe(cancelled.runId); expect(c.store.passRecording()!.runId).not.toBe(cancelled.runId);
    expect(root.querySelectorAll('[data-testid="ide-pass-body"]').length).toBeGreaterThan(0); expect(c.store.result()).toBe(result);
  });
  it.each(['en', 'ja'] as const)('keeps actual ordered boundaries and explicit missing IR separate from final result: %s', async locale => {
    const {c, fixture, root} = await readyPass('test-assertion-navigation'); c.i18n.setLocale(locale);
    await c.loadPassRecording('diagnostic'); await c.loadRecordedPasses('available'); fixture.detectChanges();
    const recording = c.store.passRecording()!; const steps = root.querySelectorAll('[data-testid="ide-pass-events"] li');
    expect(steps).toHaveLength(10); recording.events.forEach((event, i) => {expect(steps[i].querySelector('strong')!.textContent).toContain(c.passName(event.passId)); expect(steps[i].querySelector('p')!.textContent).toBe(event.gap);});
    expect(steps[5].textContent).toContain('No original IR was produced.'); expect(c.store.recordedPasses()!.bodies.some(b => b.domain === 'coglatas.project-ir/1')).toBe(false);
  });
  it('requires explicit root Source before offering run actions', async () => {
    const {c, fixture, root} = await render(); c.choose('fail'); c.loadResult(); c.open('source'); fixture.detectChanges();
    expect(root.querySelector('[data-testid="ide-pass-recording-actions"]')).toBeNull(); await c.loadPassRecording('diagnostic'); fixture.detectChanges();
    expect(root.querySelector('[data-testid="ide-pass-recording"]')).toBeNull();
  });
  it.each(['permission', 'callback', 'input', 'source'] as const)('withdraws current bodies and metadata on invalidation: %s', async change => {
    const {c, fixture, root, authenticated, clear} = await readyPass(); await c.loadPassRecording('diagnostic'); await c.loadRecordedPasses('available'); fixture.detectChanges();
    expect(root.querySelector('[data-testid="ide-pass-body"]')).not.toBeNull();
    if (change === 'permission') {authenticated.set(false); await fixture.whenStable();}
    if (change === 'callback') clear(); if (change === 'input') c.choose('unknown'); if (change === 'source') await c.loadRootSource();
    fixture.detectChanges(); expect(c.store.passRecording()).toBeNull(); expect(c.store.recordedPasses()).toBeNull(); expect(root.querySelector('[data-testid="ide-pass-body"]')).toBeNull();
  });
  it('never attaches pending bodies to a newer context or focus target', async () => {
    const {c, fixture, root} = await readyPass(); await c.loadPassRecording('diagnostic'); const pending = c.loadRecordedPasses('available');
    c.choose('unknown'); await pending; fixture.detectChanges(); expect(c.store.recordedPasses()).toBeNull(); expect(root.querySelector('[data-testid="ide-recorded-passes"]')).toBeNull();
  });
  it('loads the actual pass profile through the existing offline adapter', async () => {
    const {c, adapter} = await render(); await c.reload('pass-recordings');
    expect(adapter.read.mock.calls.at(-1)?.[0]).toContain('/assets/projectide/pass-recordings-v1.json'); expect(c.store.bundle()!.cases).toHaveLength(11);
  });
});
