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
  const data = readPreviewBundle(JSON.parse(readFileSync(resolve('public/assets/projectide/compiler-recordings-v1.json'), 'utf8')));
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



async function recordingReady(key='root-base'){const f=await render();f.c.choose('tests-publication-'+key);f.c.loadResult();f.c.open('source');await f.c.loadScopedSources();f.fixture.detectChanges();return f;}
describe('Recorded invocation component',()=>{
 it.each(['en','ja'] as const)('shows the original declaration with no displayed bodies: %s',async locale=>{const {c,fixture,root}=await recordingReady();c.i18n.setLocale(locale);const result=c.store.result();await c.loadCompilerRecording();fixture.detectChanges();await fixture.whenStable();const panel=root.querySelector('[data-testid="ide-compiler-recording"]')!;expect(panel.textContent).toContain(c.t('ide.recordingNotice'));expect(panel.textContent).toContain(c.t('ide.outputsManifestOnly'));expect(panel.textContent).toContain(c.store.compilerRecording()!.runId);expect(panel.querySelectorAll('[data-testid="ide-compiler-recording-body"]')).toHaveLength(0);expect(c.store.result()).toBe(result);await new Promise<void>(r=>setTimeout(r,0));expect(document.activeElement).toBe(panel);});
 it.each(['en','ja'] as const)('shows separate diagnostic identity and exact produced bodies: %s',async locale=>{const {c,fixture,root}=await recordingReady('false-base');c.i18n.setLocale(locale);await c.loadCompilerRecording();const original=c.store.compilerRecording()!;await c.loadCompilerRecording('diagnostic');fixture.detectChanges();const recording=c.store.compilerRecording()!;const panel=root.querySelector('[data-testid="ide-compiler-recording"]')!;expect(panel.textContent).toContain(c.t('ide.recordingDiagnostic'));expect(panel.textContent).toContain(original.runId);expect(recording.runId).not.toBe(original.runId);const bodies=panel.querySelectorAll('[data-testid="ide-compiler-recording-body"]');expect(bodies).toHaveLength(3);recording.output!.bodies.forEach((body,i)=>{expect(bodies[i].querySelector('pre')!.textContent).toBe(body.text);expect(bodies[i].textContent).toContain(body.digest.value);});expect(c.store.result()!.result.verdict).toBe('coglatas.fail');expect(c.completePass()).toBe(false);});
 it.each(['en','ja'] as const)('keeps budget and cancelled outcomes separate from the case result: %s',async locale=>{const {c,fixture,root}=await recordingReady();c.i18n.setLocale(locale);const result=c.store.result();await c.loadCompilerRecording('budget');fixture.detectChanges();expect(root.querySelector('[data-testid="ide-compiler-recording-state"]')!.textContent).toContain(c.t('ide.outputsBudgetExceeded'));expect(c.store.compilerRecording()!.output!.bodies).toHaveLength(0);await c.loadCompilerRecording('cancelled');fixture.detectChanges();expect(root.querySelector('[data-testid="ide-compiler-recording-no-result"]')!.textContent).toContain(c.t('ide.recordingNoResult'));expect(root.querySelectorAll('[data-testid="ide-compiler-recording-body"]')).toHaveLength(0);expect(c.store.compilerRecording()!.output).toBeNull();expect(c.store.result()).toBe(result);expect(result!.result.verdict).toBe('coglatas.pass');});
 it('withdraws published records on actor access changes',async()=>{const {c,fixture,root,authenticated}=await recordingReady();await c.loadCompilerRecording('diagnostic');fixture.detectChanges();expect(root.querySelector('[data-testid="ide-compiler-recording"]')).not.toBeNull();authenticated.set(false);fixture.detectChanges();await fixture.whenStable();fixture.detectChanges();expect(c.store.compilerRecording()).toBeNull();expect(root.querySelector('[data-testid="ide-compiler-recording"]')).toBeNull();});
 it('withdraws old pending records when another input is chosen',async()=>{const {c,fixture,root}=await recordingReady();const pending=c.loadCompilerRecording('diagnostic');c.choose('tests-publication-root-hypothesis');await pending;fixture.detectChanges();expect(c.store.compilerRecording()).toBeNull();expect(root.querySelector('[data-testid="ide-compiler-recording"]')).toBeNull();});
 it('loads the dedicated recording example through the existing adapter',async()=>{const {c,adapter}=await render();await c.reload('compiler-recordings');expect(adapter.read.mock.calls.at(-1)![0]).toContain('assets/projectide/compiler-recordings-v1.json');});
 it('requires Source publication before offering recorded views',async()=>{const {c,fixture,root}=await render();c.choose('tests-publication-root-base');c.loadResult();c.open('source');fixture.detectChanges();expect(Array.from(root.querySelectorAll('button')).some(b=>b.textContent===c.t('ide.recordingOriginal'))).toBe(false);await c.loadScopedSources();fixture.detectChanges();expect(Array.from(root.querySelectorAll('button')).some(b=>b.textContent===c.t('ide.recordingOriginal'))).toBe(true);});
});
