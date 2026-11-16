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

describe('Selected pass boundary Workbench', () => {
  it.each(['en','ja'] as const)('explicitly selects original boundary metadata and focuses it without reading bodies: %s', async locale => {
    const {c,fixture,root}=await readyPass('pass'); c.i18n.setLocale(locale); const result=c.store.result();
    await c.loadPassRecording('diagnostic');fixture.detectChanges();
    expect(root.querySelectorAll('[data-testid="ide-pass-inspect-boundary"]')).toHaveLength(10);
    await c.loadRecordedPassBoundary(0);fixture.detectChanges();await fixture.whenStable();fixture.detectChanges();
    const panel=root.querySelector<HTMLElement>('[data-testid="ide-pass-boundary"]')!;
    expect(panel.textContent).toContain(c.t('ide.passBoundaryTitle'));expect(panel.textContent).toContain(c.t('ide.passManifestOnly'));
    expect(panel.querySelectorAll('[data-testid="ide-pass-boundary-body"]')).toHaveLength(0);
    expect(c.store.recordedPassBoundary()!.originalBoundary).toBe(c.store.passRecording()!.events[0]);
    expect(c.store.diagnostic()).toBeNull();expect(c.store.result()).toBe(result);
    await new Promise<void>(resolve=>setTimeout(resolve,0));expect(document.activeElement).toBe(panel);
  });
  it.each(['en','ja'] as const)('shows only selected original JSON and keeps its viewer budget separate: %s',async locale=>{
    const {c,fixture,root}=await readyPass('pass');c.i18n.setLocale(locale);await c.loadPassRecording('diagnostic');
    await c.loadRecordedPassBoundary(0,'available');fixture.detectChanges();
    const saved=c.store.recordedPassBoundary()!;const panel=root.querySelector<HTMLElement>('[data-testid="ide-pass-boundary"]')!;
    const bodies=panel.querySelectorAll('[data-testid="ide-pass-boundary-body"]');expect(bodies).toHaveLength(1);
    expect(bodies[0].querySelector('pre')!.textContent).toBe(c.store.context()!.sourceText);
    expect(bodies[0].textContent).toContain(saved.bodies[0].digest.value);
    await c.loadRecordedPassBoundary(0,'budget');fixture.detectChanges();
    expect(panel.textContent).toContain(c.t('ide.passBudgetExceeded'));expect(panel.querySelectorAll('[data-testid="ide-pass-boundary-body"]')).toHaveLength(0);
    expect(c.store.passRecording()!.originalCaptureState).toBe('AvailableOriginalJson');expect(saved.bodies).toHaveLength(1);
    await c.loadRecordedPassBoundary(3,'available');fixture.detectChanges();
    expect(root.querySelector('[data-testid="ide-pass-boundary-gap"]')!.textContent).toBe(c.store.passRecording()!.events[3].gap);
    expect(c.store.recordedPassBoundary()!.bodies).toHaveLength(0);
  });
  it.each(['en','ja'] as const)('does not fill an original capture gap or invent missing IR: %s',async locale=>{
    const {c,fixture,root}=await readyPass('test-assertion-navigation');c.i18n.setLocale(locale);
    await c.loadPassRecording('diagnostic');await c.loadRecordedPassBoundary(5,'available');fixture.detectChanges();
    expect(root.querySelector('[data-testid="ide-pass-boundary-gap"]')!.textContent).toBe('No original IR was produced.');
    expect(c.store.recordedPassBoundary()!.bodies).toHaveLength(0);
    await c.loadPassRecording('budget');await c.loadRecordedPassBoundary(0,'available');fixture.detectChanges();
    expect(c.store.recordedPassBoundary()!.state).toBe('NoRecordedOriginalBodies');
    expect(root.querySelectorAll('[data-testid="ide-pass-boundary-body"]')).toHaveLength(0);
    await c.loadPassRecording('eventGap');fixture.detectChanges();
    expect(root.querySelector('[data-testid="ide-pass-boundary"]')).toBeNull();
    expect(root.querySelectorAll('[data-testid="ide-pass-inspect-boundary"]')).toHaveLength(0);
  });
  it.each(['permission','callback','input','source','metadata','whole'] as const)('withdraws the selected detail on %s',async change=>{
    const {c,fixture,root,authenticated,clear}=await readyPass();await c.loadPassRecording('diagnostic');
    await c.loadRecordedPassBoundary(0,'available');fixture.detectChanges();const saved=c.store.recordedPassBoundary()!;
    expect(root.querySelector('[data-testid="ide-pass-boundary-body"]')).not.toBeNull();
    if(change==='permission'){authenticated.set(false);await fixture.whenStable();}
    if(change==='callback')clear();if(change==='input')c.choose('unknown');if(change==='source')await c.loadRootSource();
    if(change==='metadata')await c.loadPassRecording();if(change==='whole')await c.loadRecordedPasses();
    fixture.detectChanges();expect(c.store.recordedPassBoundary()).toBeNull();expect(root.querySelector('[data-testid="ide-pass-boundary"]')).toBeNull();
    expect(saved.bodies).toHaveLength(1);
  });
  it('never attaches a pending selected read or old focus callback to a newer context',async()=>{
    const {c,fixture,root}=await readyPass();await c.loadPassRecording('diagnostic');
    const pending=c.loadRecordedPassBoundary(0,'available');c.choose('unknown');await pending;fixture.detectChanges();
    await new Promise<void>(resolve=>setTimeout(resolve,0));expect(c.store.recordedPassBoundary()).toBeNull();
    expect(root.querySelector('[data-testid="ide-pass-boundary"]')).toBeNull();
  });
  it('retains Requirement evidence incompleteness while reading actual selected IR',async()=>{
    const {c,fixture}=await readyPass('operator-requirement-false-nested');const result=c.store.result();
    await c.loadPassRecording('diagnostic');await c.loadRecordedPassBoundary(5,'available');fixture.detectChanges();
    expect(result!.result.evidenceComplete).toBe(false);expect(c.store.result()).toBe(result);
    expect(c.store.recordedPassBoundary()!.bodies.length).toBeGreaterThan(0);
  });
});
