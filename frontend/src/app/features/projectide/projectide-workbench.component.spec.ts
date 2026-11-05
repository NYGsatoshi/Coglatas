/// <reference types="node" />
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
  const data = readPreviewBundle(
    JSON.parse(readFileSync(resolve('public/assets/projectide/preview-v1.json'), 'utf8')),
  );
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

describe('ProjectIDE Workbench components', () => {
  it('is disabled by default and never reads fixtures when disabled', async () => {
    const { root, adapter } = await render(false);
    expect(root.textContent).toContain('preview is disabled');
    expect(adapter.read).not.toHaveBeenCalled();
  });
  it('denies unauthenticated preview reads', async () => {
    const { root, adapter } = await render(true, false);
    expect(root.textContent).toContain('denied');
    expect(adapter.read).not.toHaveBeenCalled();
  });
  it('labels synthetic artifacts and disables real Compile', async () => {
    const { root } = await render();
    expect(root.textContent).toContain('Loading a fixture does not run the Compiler');
    expect(root.querySelector<HTMLButtonElement>('button[disabled]')?.textContent).toContain(
      'Compile unavailable',
    );
  });
  it('shows project, workspace, branch and revision identities', async () => {
    const { c, fixture, root } = await render();
    c.choose('fail');
    fixture.detectChanges();
    expect(root.textContent).toContain('Synthetic Observatory');
    expect(root.textContent).toContain(c.store.context()!.context.revisionId);
    expect(root.textContent).toContain(c.store.context()!.workspaceId);
  });
  it('keeps unloaded results NotRun', async () => {
    const { c, fixture, root } = await render();
    c.choose('fail');
    fixture.detectChanges();
    expect(root.textContent).toContain('Compiler execution: NotRun');
  });
  it('connects Problems to Inspector and a stable source target', async () => {
    const { c, fixture, root } = await render();
    c.choose('fail');
    c.store.loadResult();
    c.open('problems');
    fixture.detectChanges();
    expect(root.textContent).toContain('1 of 1 diagnostics');
    c.inspectDiagnostic(c.diagnostics()[0]);
    fixture.detectChanges();
    expect(root.textContent).toContain('coglatas.constraint');
    expect(root.textContent).toContain('Declared properties');
    c.navigate();
    fixture.detectChanges();
    expect(root.querySelector('[data-testid="ide-source-target"]')).not.toBeNull();
  });
  it('filters without reordering diagnostic identities', async () => {
    const { c } = await render();
    c.choose('unsupported');
    c.store.loadResult();
    const before = c.diagnostics().map((d) => d.diagnosticId);
    c.severity.set('coglatas.warning');
    expect(c.filtered()).toHaveLength(0);
    c.severity.set('');
    c.search.set('unsupported-declaration');
    expect(c.filtered()).toHaveLength(1);
    expect(c.diagnostics().map((d) => d.diagnosticId)).toEqual(before);
  });
  it.each(['severity', 'kind'] as const)(
    'restores the visible %s filter after recreating Problems',
    async (field) => {
      const { c, fixture, root } = await render();
      c.choose('fail');
      c.store.loadResult();
      c.open('problems');
      fixture.detectChanges();
      const select = () =>
        root.querySelectorAll<HTMLSelectElement>('.filters select')[field === 'severity' ? 0 : 1];
      const value = field === 'severity' ? 'coglatas.warning' : c.kinds()[0];
      select().value = value;
      select().dispatchEvent(new Event('change'));
      fixture.detectChanges();
      const visible = c.filtered().length;
      expect(c[field]()).toBe(value);
      c.open('overview');
      fixture.detectChanges();
      c.open('problems');
      fixture.detectChanges();
      expect(select().value).toBe(value);
      expect(c.filtered()).toHaveLength(visible);
      select().value = '';
      select().dispatchEvent(new Event('change'));
      fixture.detectChanges();
      c.open('overview');
      fixture.detectChanges();
      c.open('problems');
      fixture.detectChanges();
      expect(select().value).toBe('');
      expect(c.filtered()).toHaveLength(c.diagnostics().length);
    },
  );
  it.each(['not-run', 'cancelled', 'timeout', 'unknown', 'unsupported'])(
    'does not call zero/incomplete %s results successful',
    async (key) => {
      const { c, fixture, root } = await render();
      c.choose(key);
      c.store.loadResult();
      c.open('problems');
      fixture.detectChanges();
      expect(root.textContent).not.toContain('Complete current PASS');
      expect(root.textContent).toContain('Analysis is incomplete');
    },
  );
  it('keeps stale pass visibly stale and refuses current source navigation', async () => {
    const { c, fixture, root } = await render();
    c.choose('stale-pass');
    c.store.loadResult();
    c.inspectRecord(c.records()[0]);
    c.navigate();
    fixture.detectChanges();
    expect(root.textContent).toContain('Stale result');
    expect(root.querySelector('[data-testid="ide-source-target"]')).toBeNull();
    expect(c.completePass()).toBe(false);
  });
  it('restores focus to Inspector navigation on close and clears selection', async () => {
    const { c, fixture, root } = await render();
    c.choose('fail');
    c.store.loadResult();
    c.inspectDiagnostic(c.diagnostics()[0]);
    fixture.detectChanges();
    await new Promise((resolve) => setTimeout(resolve, 0));
    expect(document.activeElement).toBe(root.querySelector('h2[tabindex]'));
    c.closeInspector();
    fixture.detectChanges();
    await new Promise((resolve) => setTimeout(resolve, 0));
    expect(document.activeElement?.textContent?.trim()).toBe('Inspector');
    expect(c.store.selection()).toBeNull();
  });
  it('discards an obsolete focus request when Revision changes before the timer', async () => {
    const { c, fixture, root } = await render();
    c.choose('fail');
    c.store.loadResult();
    c.inspectDiagnostic(c.diagnostics()[0]);
    const picker = root.querySelector<HTMLSelectElement>('[data-testid="ide-context"]')!;
    picker.focus();
    c.choose('branch-pass');
    fixture.detectChanges();
    await new Promise((resolve) => setTimeout(resolve, 0));
    expect(document.activeElement).toBe(picker);
  });
  it('resets panels and filters and cancels deferred focus on a preview reload', async () => {
    const { c, fixture, root } = await render();
    c.choose('fail');
    c.store.loadResult();
    c.inspectDiagnostic(c.diagnostics()[0]);
    c.search.set('previous');
    c.severity.set('coglatas.error');
    c.kind.set('previous-rule');
    fixture.detectChanges();
    const picker = root.querySelector<HTMLSelectElement>('[data-testid="ide-context"]')!;
    picker.focus();
    await c.reload();
    fixture.detectChanges();
    await new Promise((resolve) => setTimeout(resolve, 0));
    expect(c.panel()).toBe('overview');
    expect(c.store.result()).toBeNull();
    expect(c.store.selection()).toBeNull();
    expect(document.activeElement).toBe(picker);
    expect([c.search(), c.severity(), c.kind()]).toEqual(['', '', '']);
  });
  it('announces errors without private details', async () => {
    const { root } = await render(true, true, true);
    expect(root.querySelector('[role="alert"]')?.textContent).toContain('could not be loaded');
    expect(root.textContent).not.toContain('private');
  });
  it('announces loading, allows cancellation and discards the late result', async () => {
    const { c, fixture, root } = await render();
    const data = c.store.bundle()!;
    let release!: (v: typeof data) => void;
    const pending = c.store.load(() => new Promise((resolve) => (release = resolve)));
    fixture.detectChanges();
    expect(root.textContent).toContain('Loading synthetic preview');
    Array.from(root.querySelectorAll('button'))
      .find((b) => b.textContent === 'Cancel loading')!
      .click();
    release(data);
    await pending;
    fixture.detectChanges();
    expect(root.textContent).toContain('Preview loading cancelled');
    expect(c.store.bundle()).toBeNull();
  });
  it('shows unknown selection instead of inventing a target', async () => {
    const { c, fixture, root } = await render();
    c.choose('fail');
    c.store.select({ kind: 'entity', id: 'missing' as never });
    c.open('inspector');
    fixture.detectChanges();
    expect(root.textContent).toContain('Unknown / Unavailable target');
  });
  it('clears context and data immediately on permission revocation', async () => {
    const { c, fixture, root, authenticated } = await render();
    c.choose('fail');
    c.store.loadResult();
    authenticated.set(false);
    fixture.detectChanges();
    expect(c.store.context()).toBeNull();
    expect(root.textContent).not.toContain('Launch readiness');
  });
  it('clears data on authorization invalidation even with unchanged session identity', async () => {
    const { c, fixture, root, clear } = await render();
    c.choose('fail');
    c.store.loadResult();
    clear();
    fixture.detectChanges();
    expect(c.store.bundle()).toBeNull();
    expect(root.textContent).toContain('denied');
  });
  it('localizes view copy without translating semantic codes', async () => {
    const { c, fixture, root } = await render();
    c.choose('fail');
    c.store.loadResult();
    c.open('problems');
    c.i18n.setLocale('ja');
    fixture.detectChanges();
    expect(root.textContent).toContain('合成Fixtureプレビュー');
    expect(root.textContent).toContain('coglatas.analysis.constraint-false');
  });
  it('invalidates selection when active Workspace changes', async () => {
    const { c, fixture } = await render();
    c.choose('fail');
    c.store.loadResult();
    TestBed.inject(ActiveWorkspaceFacade).setActiveWorkspace({ id: 'other', label: 'Other' });
    fixture.detectChanges();
    expect(c.store.context()).toBeNull();
  });
});
