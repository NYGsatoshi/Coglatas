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



async function outputReady(key='root-base'){const f=await render();f.c.choose('tests-publication-'+key);f.c.loadResult();f.c.open('source');await f.c.loadScopedSources();f.fixture.detectChanges();return f;}
describe('Actual Compiler output snapshot component',()=>{
 it.each(['en','ja'] as const)('defaults to localized manifest without output bodies: %s',async locale=>{const {c,fixture,root}=await outputReady();c.i18n.setLocale(locale);const result=c.store.result();await c.loadCompilerOutputs();fixture.detectChanges();await fixture.whenStable();const panel=root.querySelector('[data-testid="ide-compiler-outputs"]')!;expect(panel.textContent).toContain(c.t('ide.outputsNotice'));expect(panel.textContent).toContain(c.t('ide.outputsManifestOnly'));expect(panel.textContent).toContain(result!.resultDigest.value);expect(panel.querySelectorAll('[data-testid="ide-compiler-output-body"]')).toHaveLength(0);expect(c.store.compilerOutputs()!.retainedBodyBytes).toBe(0);expect(c.store.result()).toBe(result);await new Promise<void>(resolve=>setTimeout(resolve,0));expect(document.activeElement).toBe(panel);});
 it.each(['en','ja'] as const)('shows actual retained bodies and original digests only after explicit reading: %s',async locale=>{const {c,fixture,root}=await outputReady('false-base');c.i18n.setLocale(locale);await c.loadCompilerOutputs('AvailableOutputs');fixture.detectChanges();await fixture.whenStable();const snapshot=c.store.compilerOutputs()!;const panel=root.querySelector('[data-testid="ide-compiler-outputs"]')!;expect(panel.textContent).toContain(c.t('ide.outputsCaptured'));const bodies=panel.querySelectorAll('[data-testid="ide-compiler-output-body"]');expect(bodies).toHaveLength(3);snapshot.bodies.forEach((body,i)=>{expect(bodies[i].querySelector('pre')!.textContent).toBe(body.text);expect(bodies[i].textContent).toContain(body.digest.value);});expect(c.store.result()!.result.verdict).toBe('coglatas.fail');expect(c.completePass()).toBe(false);});
 it('shows an atomic body-budget gap while preserving a passing Compiler result',async()=>{const {c,fixture,root}=await outputReady('root-draft');const original=c.store.result();await c.loadCompilerOutputs('AvailableOutputs');fixture.detectChanges();expect(c.store.compilerOutputs()!.state).toBe('BodyBudgetExceeded');const panel=root.querySelector('[data-testid="ide-compiler-outputs"]')!;expect(panel.textContent).toContain(c.t('ide.outputsBudgetExceeded'));expect(panel.querySelectorAll('[data-testid="ide-compiler-output-body"]')).toHaveLength(0);expect(c.store.result()).toBe(original);expect(original!.result.verdict).toBe('coglatas.pass');});
 it('withdraws the body view when actor access changes',async()=>{const {c,fixture,root,authenticated}=await outputReady();await c.loadCompilerOutputs('AvailableOutputs');fixture.detectChanges();expect(root.querySelector('[data-testid="ide-compiler-outputs"]')).not.toBeNull();authenticated.set(false);fixture.detectChanges();await fixture.whenStable();fixture.detectChanges();expect(c.store.compilerOutputs()).toBeNull();expect(root.querySelector('[data-testid="ide-compiler-outputs"]')).toBeNull();});
 it('refuses old publication after choosing another immutable input',async()=>{const {c,fixture,root}=await outputReady();const pending=c.loadCompilerOutputs('AvailableOutputs');c.choose('tests-publication-root-hypothesis');await pending;fixture.detectChanges();expect(c.store.compilerOutputs()).toBeNull();expect(root.querySelector('[data-testid="ide-compiler-outputs"]')).toBeNull();});
 it('loads the dedicated offline example through the existing read adapter',async()=>{const {c,adapter}=await render();await c.reload('compiler-outputs');expect(adapter.read.mock.calls.at(-1)![0]).toContain('assets/projectide/compiler-outputs-v1.json');});
 it('requires Source publication admission before showing output read actions',async()=>{const {c,fixture,root}=await render();c.choose('tests-publication-root-base');c.loadResult();c.open('source');fixture.detectChanges();expect(Array.from(root.querySelectorAll('button')).some(b=>b.textContent===c.t('ide.outputsReadManifest'))).toBe(false);await c.loadScopedSources();fixture.detectChanges();expect(Array.from(root.querySelectorAll('button')).some(b=>b.textContent===c.t('ide.outputsReadManifest'))).toBe(true);});
});
