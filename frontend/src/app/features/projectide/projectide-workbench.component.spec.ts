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

async function render(enabled = true, allowed = true, fail = false, profile: 'main' | 'test-edit' | 'scenario' | 'scoped' | 'plan' | 'simulation' = 'main') {
  localStorage.setItem('coglatas.locale', 'en');
  const authenticated = signal(allowed);
  let clear!: () => void;
  const data = readPreviewBundle(
    JSON.parse(readFileSync(resolve(profile === 'simulation' ? 'public/assets/projectide/simulation-v1.json' : profile === 'plan' ? 'public/assets/projectide/execution-plan-v1.json' : profile === 'scoped' ? 'public/assets/projectide/scoped-sources-v1.json' : profile === 'scenario' ? 'public/assets/projectide/scenario-v1.json' : profile === 'test-edit' ? 'public/assets/projectide/test-edit-v1.json' : 'public/assets/projectide/preview-v1.json'), 'utf8')),
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

vi.stubGlobal('crypto', webcrypto);

describe('ProjectIDE Workbench components', () => {
  it.each(['en', 'ja'] as const)('separates Scenario assumptions, Source changes and fixed result axes in %s', async locale => {
    const { c, fixture, root, authenticated } = await render(true, true, false, 'scenario'); c.i18n.setLocale(locale);
    c.choose('tests-scenario-base'); c.loadResult(); c.open('scenario');
    const prepared = c.store.preparedScenario()!; await c.loadScenarioComparison(prepared); fixture.detectChanges(); await fixture.whenStable();
    expect(root.querySelector('[data-testid="ide-scenario-assumptions"]')!.textContent).toContain('true');
    expect(root.querySelector('[data-testid="ide-scenario-changes"]')!.textContent).toContain('/payload/ports');
    const baseline = root.querySelector('[data-testid="ide-scenario-result-0"]')!;
    expect(baseline.textContent).toContain(c.t('ide.testFail')); expect(baseline.textContent).toContain(c.t('ide.testPass'));
    expect(root.querySelector('[data-testid="ide-scenario-result-1"]')!.textContent).toContain(c.t('ide.testPass'));
    await c.loadScenarioStage(prepared, 'scenario'); fixture.detectChanges(); await fixture.whenStable();
    expect(c.store.context()!.context.kind).toBe('coglatas.scenario'); expect(c.store.localTests()).toBeNull(); expect(c.panel()).toBe('scenario');
    await c.loadScenarioStage(c.store.preparedScenario()!, 'draft'); fixture.detectChanges(); await fixture.whenStable();
    expect(c.store.context()!.context.kind).toBe('coglatas.candidate');
    authenticated.set(false); fixture.detectChanges(); expect(c.store.scenario()).toBeNull(); expect(root.querySelector('[data-testid="ide-scenario-results"]')).toBeNull();
  });

  it.each(['en', 'ja'] as const)('loads an exact Core-prepared Test definition edit in %s without retaining the previous result', async locale => {
    const { c, fixture, root, authenticated } = await render(true, true, false, 'test-edit'); c.i18n.setLocale(locale);
    c.choose('tests-edit-before'); c.loadResult(); c.open('tests'); await c.loadLocalTests(); fixture.detectChanges();
    const prepared = c.store.preparedTestEdit()!; expect(prepared).toBeTruthy();
    expect(root.querySelector('[data-testid="ide-test-definition"]')!.textContent).toContain('"kind":"coglatas.project-test"');
    expect(c.store.localTests()!.run.verdict).toBe('Fail'); await c.loadTestEdit(prepared); fixture.detectChanges(); await fixture.whenStable();
    expect(c.store.context()!.key).toBe('tests-edit-after'); expect(c.store.localTests()).toBeNull();
    expect(root.querySelector('pre.source-buffer')!.textContent).toContain('coglatas.candidate');
    c.open('tests'); await c.loadLocalTests(); fixture.detectChanges(); expect(c.store.localTests()!.run.verdict).toBe('Pass');
    authenticated.set(false); fixture.detectChanges(); expect(c.store.preparedTestEdit()).toBeNull(); expect(c.store.localTests()).toBeNull();
  });

  it.each([
    ['tests-pass', 'Passed'], ['tests-fail', 'Failed'], ['tests-unknown', 'Inconclusive'], ['tests-empty', 'Not run'],
    ['tests-unsupported', 'Not run'], ['tests-cancelled', 'Cancelled'], ['tests-stale', 'Stale'], ['tests-evaluator-mismatch', 'Not run'],
    ['tests-error', 'Error'], ['tests-exhausted', 'Resource limit reached'], ['tests-unicode', 'Passed'], ['tests-many', 'Passed'],
  ])('shows the actual local %s axes separately from Compiler verdict', async (key, outcome) => {
    const { c, fixture, root } = await render(); c.choose(key); c.loadResult(); c.open('tests');
    expect(c.store.localTests()).toBeNull(); await c.loadLocalTests(); fixture.detectChanges();
    const summary = root.querySelector('[data-testid="ide-tests-summary"]')!;
    expect(summary.textContent).toContain(outcome);
    expect(summary.textContent).toContain('Tests discovered: ' + c.store.localTests()!.run.discoveredTests);
    expect(summary.textContent).toContain('Assertions evaluated: ' + c.store.localTests()!.run.executedAssertions);
    expect(c.store.result()!.result.verdict).toBe(key === 'tests-unsupported' ? 'coglatas.inconclusive' : 'coglatas.pass');
  });
  it.each(['en', 'ja'] as const)('opens an assertion from the %s Tests panel and clears it when read permission is lost', async locale => {
    const { c, fixture, root, authenticated } = await render(); c.choose('tests-fail'); c.loadResult(); c.i18n.setLocale(locale);
    c.open('tests'); await c.loadLocalTests(); fixture.detectChanges();
    expect(root.querySelector('[data-testid="ide-tests-summary"]')!.textContent).toContain(locale === 'en' ? 'Failed' : '失敗');
    const action = Array.from(root.querySelectorAll('button')).find(item => item.textContent?.includes(locale === 'en' ? 'Open assertion in Source' : 'アサーションのソースを開く'))!;
    action.click(); fixture.detectChanges(); await fixture.whenStable();
    const target = root.querySelector('[data-testid="ide-source-target"]'); expect(target).toBeTruthy();
    expect(root.querySelector('[data-testid="ide-source-location"]')!.textContent).toContain('/payload/assertions/example.ready');
    authenticated.set(false); fixture.detectChanges();
    expect(c.store.localTests()).toBeNull(); expect(c.store.sourceTarget()).toBeNull();
    expect(root.querySelector('[data-testid="ide-test-rows"]')).toBeNull();
  });
  it('pages actual Core-produced Test rows without changing the full result counts', async () => {
    const { c, fixture, root } = await render(); c.choose('tests-many'); c.loadResult(); c.open('tests');
    await c.loadLocalTests(); fixture.detectChanges();
    const rows = () => root.querySelector('[data-testid="ide-test-rows"]')!.querySelectorAll(':scope > li');
    expect(rows()).toHaveLength(32); expect(c.store.localTests()!.run.discoveredTests).toBe(33);
    Array.from(root.querySelectorAll('button')).find(item => item.textContent?.trim() === 'Next Tests')!.click(); fixture.detectChanges();
    expect(rows()).toHaveLength(1); expect(c.store.localTests()!.run.executedAssertions).toBe(33);
    Array.from(root.querySelectorAll('button')).find(item => item.textContent?.trim() === 'Previous Tests')!.click(); fixture.detectChanges();
    expect(rows()).toHaveLength(32);
  });
  it('does not display missing or corrupted local Test projections as successful checks', async () => {
    const { c, fixture, root } = await render(); c.choose('pass'); c.loadResult(); c.open('tests');
    await c.loadLocalTests(); fixture.detectChanges(); expect(c.store.testReadState()).toBe('unavailable');
    expect(root.querySelector('[data-testid="ide-tests-summary"]')).toBeNull();
    c.choose('tests-pass'); c.loadResult(); c.open('tests');
    Object.assign(c.store.context()!.localProjectTests!.result, { text: '{}' });
    await c.loadLocalTests(); fixture.detectChanges(); expect(c.store.testReadState()).toBe('unsupported');
    expect(root.querySelector('[role="alert"]')!.textContent).toContain('could not be verified');
    expect(root.querySelector('[data-testid="ide-tests-summary"]')).toBeNull();
  });

  it.each(['en', 'ja'] as const)('shows reasoned %s Task candidates and rechecks permission without changing the Requirement', async locale => {
    const { c, fixture, root, authenticated } = await render(); c.choose('requirement-candidates'); c.loadResult(); c.i18n.setLocale(locale);
    const record = c.store.context()!.declarations.find(item => item.kind === 'coglatas.requirement')!;
    if (record.recordType !== 'entity') throw new Error('Requirement entity fixture required');
    c.store.select({ kind: 'entity', id: record.identity }); c.open('inspector'); fixture.detectChanges();
    const source = c.store.context()!.sourceText; const section = root.querySelector('[data-testid="ide-requirement-task-suggestions"]')!;
    expect(section.textContent).toContain(locale === 'en' ? 'They do not prove requirement satisfaction' : '要件充足の証明にはなりません');
    expect(section.textContent).toContain(locale === 'en' ? 'Reason: declared Task' : '理由: 読み込み済みスナップショット内のTask宣言');
    section.querySelector<HTMLButtonElement>('button')!.click(); fixture.detectChanges(); expect(c.panel()).toBe('source');
    expect(c.store.sourceLocation()?.entityId).toBe(c.store.context()!.declarations.find(item => item.kind === 'coglatas.task')?.identity);
    expect(c.store.context()!.sourceText).toBe(source);
    c.store.select({ kind: 'entity', id: record.identity }); const page = c.store.requirementTaskSuggestions()[0];
    authenticated.set(false); c.navigateTaskSuggestion(page, page.candidates[0]); fixture.detectChanges();
    expect(c.store.state()).toBe('denied'); expect(c.store.bundle()).toBeNull(); expect(c.store.requirementTaskSuggestions()).toEqual([]);
  });
  it.each(['en', 'ja'] as const)('opens the localized %s inverse Draft and clears the action on denied authority', async locale => {
    const { c, fixture, root, authenticated } = await render(); c.choose('document-after'); c.loadResult(); c.i18n.setLocale(locale); c.open('history'); fixture.detectChanges();
    expect(root.querySelector('[data-testid="ide-draft-revert"]')?.textContent).toContain(locale === 'en' ? 'Prepared inverse Draft' : '準備済みの逆変更Draft');
    root.querySelector<HTMLButtonElement>('[data-testid="ide-draft-revert"] button')!.click(); fixture.detectChanges();
    expect(c.store.context()?.key).toBe('revert-draft'); expect(c.store.context()?.context.kind).toBe('coglatas.candidate');
    c.choose('document-after'); c.loadResult(); const prepared = c.store.preparedRevert()!;
    authenticated.set(false); c.loadPreparedRevert(prepared); fixture.detectChanges();
    expect(c.store.bundle()).toBeNull(); expect(root.textContent).not.toContain('Opaque document annotation');
  });
  it.each(['en', 'ja'] as const)('opens localized %s history without confusing reconstruction and Compiler support', async locale => {
    const { c, fixture, root, authenticated } = await render(); c.choose('document-before'); c.loadResult(); c.i18n.setLocale(locale); c.open('history'); fixture.detectChanges();
    expect(root.querySelector('[data-testid="ide-history-state"]')?.textContent).toContain('Ready · Unsupported');
    expect(root.querySelector('[data-testid="ide-history-revisions"]')?.textContent).toContain('TypedDelta');
    const row = c.store.history()!.revisions[1];
    const button = Array.from(root.querySelectorAll('button')).find(button => button.textContent?.includes(row.context.revisionId))!;
    button.click(); fixture.detectChanges(); expect(c.store.context()?.key).toBe('document-after'); expect(c.panel()).toBe('source');
    c.choose('document-before'); c.loadResult(); c.open('history'); fixture.detectChanges();
    authenticated.set(false); c.openHistoryRevision(c.store.history()!.revisions[1]); fixture.detectChanges();
    expect(c.store.state()).toBe('denied'); expect(c.store.history()).toBeNull(); expect(root.textContent).not.toContain('TypedDelta');
  });
  it.each(['en', 'ja'] as const)('renders a localized %s difference and rechecks permission before opening its historical side', async locale => {
    const { c, fixture, root, authenticated } = await render(); c.choose('draft-before'); c.loadResult();
    c.i18n.setLocale(locale); c.open('diff'); fixture.detectChanges();
    expect(root.querySelector('[data-testid="ide-diff-state"]')?.textContent).toContain('Complete');
    expect(root.querySelector('[data-testid="ide-diff-rows"]')?.textContent).toContain('DisplayName');
    const change = c.store.semanticDiff()!.comparison.changes[0];
    const after = Array.from(root.querySelectorAll('button')).find(button => button.textContent?.includes(locale === 'en' ? 'Open after snapshot' : '変更後のスナップショットを開く'))!;
    after.click(); fixture.detectChanges(); expect(c.store.context()?.key).toBe('draft-after'); expect(c.panel()).toBe('source');
    c.choose('draft-before'); c.loadResult(); c.open('diff'); fixture.detectChanges();
    authenticated.set(false); c.navigateDiff(change, 'before'); fixture.detectChanges();
    expect(c.store.state()).toBe('denied'); expect(c.store.semanticDiff()).toBeNull(); expect(root.textContent).not.toContain('Observe renamed');
  });

  it('withdraws a pending Test read before loading the prepared rename context', async () => {
    const { c, fixture } = await render(); c.choose('draft-before'); c.loadResult();
    const task = c.records().find(item => item.kind === 'coglatas.task')!; c.inspectRecord(task);
    const prepared = c.store.preparedRename()!; expect(prepared).not.toBeNull();
    const pending = c.store.loadLocalTests(); expect(c.store.testReadState()).toBe('loading');
    c.inspectRecord(task); expect(c.store.loadPreparedRename(prepared)).toBe(true);
    expect(await pending).toBe(false); expect(c.store.context()!.key).toBe('draft-after');
    expect(c.store.testReadState()).toBe('idle'); expect(c.store.testEditState()).toBe('idle');
    expect(c.store.scenarioReadState()).toBe('idle'); expect(c.store.localTests()).toBeNull();
    fixture.detectChanges();
  });

  it.each(['en', 'ja'] as const)('previews the prepared Draft rename in %s and rechecks permission before loading it', async locale => {
    const { c, fixture, root, authenticated } = await render(); c.choose('draft-before'); c.loadResult();
    c.i18n.setLocale(locale); fixture.detectChanges();
    const task = c.records().find(item => item.kind === 'coglatas.task')!; c.inspectRecord(task); fixture.detectChanges();
    const prepared = c.store.preparedRename()!; expect(prepared).not.toBeNull();
    expect(root.textContent).toContain(prepared.plan.beforeContext.proposalId);
    expect(root.textContent).toContain(prepared.plan.beforeContext.baseRevision.revisionId);
    const button = root.querySelector<HTMLButtonElement>('[data-testid="ide-draft-rename"] button')!;
    expect(button.textContent).toContain(locale === 'en' ? 'Load prepared Draft fixture' : '準備済みDraft Fixtureを読み込む');
    button.click(); fixture.detectChanges(); expect(c.store.context()?.key).toBe('draft-after'); expect(c.panel()).toBe('source');
    c.choose('draft-before'); c.loadResult(); c.inspectRecord(c.records().find(item => item.kind === 'coglatas.task')!); fixture.detectChanges();
    const retained = c.store.preparedRename()!; expect(retained).not.toBeNull();
    authenticated.set(false); c.loadPreparedRename(retained);
    expect(c.store.context()).toBeNull(); expect(c.store.state()).toBe('denied');
    fixture.detectChanges();
  });

  it.each(['en', 'ja'] as const)('opens a localized %s typed candidate and clears it after permission loss', async locale => {
    const { c, fixture, root, authenticated } = await render();
    c.choose('pass'); c.loadResult();
    const task = c.records().find(item => item.kind === 'coglatas.task')!;
    c.inspectRecord(task); c.i18n.setLocale(locale); fixture.detectChanges();
    const section = root.querySelector<HTMLElement>('[data-testid="ide-reference-completion"]')!;
    expect(section.textContent).toContain(locale === 'en' ? 'Typed reference suggestions' : '型に基づく参照候補');
    const button = section.querySelector<HTMLButtonElement>('button')!;
    expect(button.type).toBe('button'); button.click(); fixture.detectChanges();
    expect(c.panel()).toBe('source'); expect(c.store.sourceTarget()).toBe(task);
    authenticated.set(false); fixture.detectChanges();
    button.click(); fixture.detectChanges();
    expect(c.store.referenceCompletions()).toEqual([]); expect(c.store.sourceTarget()).toBeNull();
    expect(c.store.state()).toBe('denied');
  });

  it.each(['en', 'ja'] as const)('activates a localized %s related diagnostic button and follows its source location', async locale => {
    const { c, fixture, root } = await render();
    c.choose('fail');
    c.loadResult();
    const record = c.records().find(item => item.kind === 'coglatas.constraint')!;
    c.inspectRecord(record);
    c.i18n.setLocale(locale);
    fixture.detectChanges();
    const button = root.querySelector<HTMLButtonElement>('[data-testid="ide-related-diagnostics"] button')!;
    expect(button.type).toBe('button');
    expect(button.textContent).toContain(`${locale === 'en' ? 'Inspect diagnostic' : '診断を調べる'}: coglatas.analysis.constraint-false`);
    expect(button.getAttribute('aria-pressed')).toBe('false');
    button.click();
    fixture.detectChanges();
    const diagnostic = c.diagnostics()[0];
    expect(c.store.diagnostic()).toBe(diagnostic);
    expect(root.querySelector('[data-testid="ide-related-diagnostics"] button')?.getAttribute('aria-pressed')).toBe('true');
    c.navigate();
    fixture.detectChanges();
    expect(c.store.sourceLocation()).toBe(diagnostic.primaryLocation);
    expect(root.querySelector('[data-testid="ide-source-target"]')?.textContent).toContain(record.identity);
  });

  it('rejects a retained related-diagnostic button after the accepted bundle is replaced', async () => {
    const { c, fixture, root, adapter } = await render();
    c.choose('fail');
    c.loadResult();
    c.inspectRecord(c.records().find(item => item.kind === 'coglatas.constraint')!);
    fixture.detectChanges();
    const button = root.querySelector<HTMLButtonElement>('[data-testid="ide-related-diagnostics"] button')!;
    adapter.read.mockImplementationOnce(async () => readPreviewBundle(
      JSON.parse(readFileSync(resolve('public/assets/projectide/preview-v1.json'), 'utf8')),
    ));
    await c.reload();
    c.choose('fail');
    c.loadResult();
    // Invoke the old DOM listener before Angular removes the old view.
    button.click();
    expect(c.store.diagnostic()).toBeNull();
    expect(c.store.relatedDiagnostics()).toEqual([]);
    expect(c.store.sourceTarget()).toBeNull();
  });

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
    expect(root.textContent).toContain(c.contextRevision(c.store.context()!.context));
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

it('does not apply deferred Source focus after the current result is reloaded', async () => {
  const { c, fixture, root } = await render();
  c.choose('fail');
  c.loadResult();
  c.inspectDiagnostic(c.diagnostics()[0]);
  c.navigate();
  c.loadResult();
  fixture.detectChanges();
  const picker = root.querySelector<HTMLSelectElement>('[data-testid="ide-context"]')!;
  picker.focus();
  await new Promise((resolve) => setTimeout(resolve, 0));
  expect(document.activeElement).toBe(picker);
  expect(c.store.sourceTarget()).toBeNull();
});

for (const mode of ['focus', 'reload', 'denied'] as const) it('guards scoped content focus and selection: ' + mode, async () => {
  const { c, fixture, root, authenticated } = await render(true, true, false, 'scoped');
  c.choose('tests-scoped-base'); c.loadResult(); await c.loadScopedSources();
  const region = c.store.scopedSources().find(region => region.context.branch.projectId !== c.store.context()!.context.branch.projectId)!;
  const record = region.records[0];
  if (mode === 'denied') { authenticated.set(false); c.openScopedSource(record); expect(c.store.context()).toBeNull(); expect(c.store.scopedSourceTarget()).toBeNull(); return; }
  c.openScopedSource(record);
  if (mode === 'reload') c.loadResult();
  fixture.detectChanges();
  const picker = root.querySelector<HTMLSelectElement>('[data-testid="ide-context"]')!;
  if (mode === 'reload') picker.focus();
  await new Promise(resolve => setTimeout(resolve, 0));
  expect(document.activeElement).toBe(mode === 'focus' ? root.querySelector('[data-testid="ide-scoped-source-target"]') : picker);
  if (mode === 'reload') expect(c.store.scopedSourceTarget()).toBeNull();
});

for (const locale of ['en', 'ja'] as const) it('opens the actual fixed plan asset and keeps production separate in ' + locale, async () => {
  const { c, fixture, root, adapter, authenticated } = await render(true, true, false, 'plan'); c.i18n.setLocale(locale); fixture.detectChanges();
  const button = [...root.querySelectorAll('button')].find(button => button.textContent?.includes(c.t('ide.planExample')))!;
  expect(button).toBeTruthy(); button.click(); await fixture.whenStable(); fixture.detectChanges();
  expect(adapter.read).toHaveBeenLastCalledWith(expect.stringContaining('/assets/projectide/execution-plan-v1.json'), expect.objectContaining({ aborted: false }));
  c.choose('tests-plan-root-base'); c.loadResult(); c.open('source'); fixture.detectChanges(); expect(root.querySelector('[data-testid="ide-plan-definition"]')).toBeNull();
  await c.loadScopedSources(); fixture.detectChanges(); await fixture.whenStable();
  expect(root.querySelector('[data-testid="ide-plan-definition"]')!.textContent).toContain(c.t('ide.planNotice'));
  expect(c.store.planDefinition()!.taskCount).toBe(3); authenticated.set(false); fixture.detectChanges(); await fixture.whenStable();
  expect(root.querySelector('[data-testid="ide-plan-definition"]')).toBeNull(); expect(c.store.planDefinition()).toBeNull();
});
it('rejects a direct plan example handler after permission is revoked', async () => {
  const { c, authenticated, adapter } = await render(); const calls = adapter.read.mock.calls.length;
  authenticated.set(false); await c.reload('plan'); expect(adapter.read.mock.calls.length).toBe(calls); expect(c.store.state()).toBe('denied');
});

for (const locale of ['en', 'ja'] as const) it('loads the actual recorded simulation explicitly and revokes it in ' + locale, async () => {
  const { c, fixture, root, adapter, authenticated } = await render(true, true, false, 'simulation'); c.i18n.setLocale(locale); fixture.detectChanges();
  const example = [...root.querySelectorAll('button')].find(button => button.textContent?.includes(c.t('ide.simulationExample')))!;
  example.click(); await fixture.whenStable(); fixture.detectChanges();
  expect(adapter.read).toHaveBeenLastCalledWith(expect.stringContaining('/assets/projectide/simulation-v1.json'), expect.objectContaining({ aborted: false }));
  c.choose('tests-simulation-root-base'); c.loadResult(); c.open('source'); await c.loadScopedSources(); fixture.detectChanges();
  expect(root.querySelector('[data-testid="ide-simulation"]')).toBeNull();
  const load = [...root.querySelectorAll('button')].find(button => button.textContent?.includes(c.t('ide.simulationLoad')))!;
  load.click(); await vi.waitFor(() => expect(c.store.simulationReadState()).toBe('ready')); fixture.detectChanges(); expect(c.store.simulation()!.events).toHaveLength(6);
  expect(root.querySelector('[data-testid="ide-simulation"]')!.textContent).toContain(c.t('ide.simulationNotice'));
  c.openSimulationEvent(c.store.simulation()!.events[0]); fixture.detectChanges(); await new Promise(resolve => setTimeout(resolve, 0));
  expect(document.activeElement).toBe(root.querySelector('[data-testid="ide-scoped-source-target"]'));
  authenticated.set(false); fixture.detectChanges(); await fixture.whenStable(); expect(c.store.simulation()).toBeNull();
  expect(root.querySelector('[data-testid="ide-simulation"]')).toBeNull();
});
it('refuses direct simulation handlers after permission revocation', async () => {
  const { c, adapter, authenticated } = await render(true, true, false, 'simulation'); const calls = adapter.read.mock.calls.length;
  authenticated.set(false); await c.reload('simulation'); await c.loadSimulation();
  expect(adapter.read.mock.calls.length).toBe(calls); expect(c.store.state()).toBe('denied'); expect(c.store.simulation()).toBeNull();
});
