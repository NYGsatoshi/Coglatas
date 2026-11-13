import { DOCUMENT, JsonPipe } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  computed,
  effect,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuthSessionFacade } from '../../core/auth/auth-session.facade';
import { FrontendFeatureFlagsService } from '../../core/feature-flags/frontend-feature-flags.service';
import { I18nService, TranslationKey } from '../../core/i18n/i18n.service';
import {
  PROJECTIDE_ENGLISH_MESSAGES,
  PROJECTIDE_JAPANESE_MESSAGES,
} from '../../core/i18n/messages/projectide.messages';
import { RealtimeFacade } from '../../core/realtime/realtime.facade';
import { ActiveWorkspaceFacade } from '../../core/workspace/active-workspace.facade';
import { BoundReference, Diagnostic, HistoryRevisionView, InspectedSymbol, PreparedDraftRename, PreparedDraftRevert, ReferenceCompletion, RequirementTaskSuggestions, SemanticChange, SourceRecord, assessLocalEvidence, contextRevision } from './projectide.contracts';
import { ProjectIdePreviewAdapter } from './projectide-preview.adapter';
import { ProjectIdeWorkbenchStore } from './projectide-workbench.store';

@Component({
  selector: 'app-projectide-workbench',
  standalone: true,
  imports: [RouterLink, JsonPipe],
  providers: [ProjectIdeWorkbenchStore, ProjectIdePreviewAdapter],
  templateUrl: './projectide-workbench.component.html',
  styleUrl: './projectide-workbench.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ProjectIdeWorkbenchComponent {
  readonly contextRevision = contextRevision;
  readonly store = inject(ProjectIdeWorkbenchStore);
  readonly flags = inject(FrontendFeatureFlagsService);
  readonly i18n = inject(I18nService);
  private readonly auth = inject(AuthSessionFacade);
  private readonly workspace = inject(ActiveWorkspaceFacade);
  private readonly adapter = inject(ProjectIdePreviewAdapter);
  private readonly realtime = inject(RealtimeFacade);
  private readonly document = inject(DOCUMENT);
  readonly panel = signal<'overview' | 'source' | 'problems' | 'inspector' | 'diff' | 'history'>('overview');
  readonly diffPage = signal(0);
  readonly historyPage = signal(0);
  readonly historyRows = computed(() => this.store.history()?.revisions.slice(this.historyPage() * 32, (this.historyPage() + 1) * 32) ?? []);
  readonly diffRows = computed(() => this.store.semanticDiff()?.comparison.changes.slice(this.diffPage() * 32, (this.diffPage() + 1) * 32) ?? []);
  readonly severity = signal('');
  readonly kind = signal('');
  readonly search = signal('');
  readonly heading = viewChild<ElementRef<HTMLElement>>('panelHeading');
  private readonly sourceTarget = viewChild<ElementRef<HTMLElement>>('sourceTarget');
  private readonly inspectorButton = viewChild<ElementRef<HTMLButtonElement>>('inspectorButton');
  private focusGeneration = 0;
  readonly tabs = ['overview', 'source', 'problems', 'inspector', 'diff', 'history'] as const;
  readonly canRead = computed(
    () =>
      this.flags.projectIdePreviewEnabled() &&
      this.auth.isAuthenticated() &&
      this.auth.currentTenant()?.isAvailable === true &&
      this.auth.session().capabilities.includes('projects:view'),
  );
  readonly diagnostics = computed(() => this.store.result()?.result.diagnostics ?? []);
  readonly kinds = computed(() => [...new Set(this.diagnostics().map((d) => d.ruleId))]);
  readonly filtered = computed(() =>
    this.diagnostics().filter(
      (d) =>
        (!this.severity() || d.severity === this.severity()) &&
        (!this.kind() || d.ruleId === this.kind()) &&
        `${d.code} ${d.ruleId} ${d.diagnosticId} ${d.primaryLocation.entityId ?? ''} ${d.primaryLocation.relationId ?? ''}`
          .toLowerCase()
          .includes(this.search().toLowerCase()),
    ),
  );
  readonly evidenceState = computed(() => {
    const result = this.store.result();
    // The selected case instance belongs to this load generation; a reused key does not.
    return result && result !== this.store.context() ? 'stale' : assessLocalEvidence(result);
  });
  readonly completePass = computed(() => this.evidenceState() === 'complete-local-pass');
  readonly evidenceMessage = computed(() => {
    const keys = {
      'not-loaded': 'ide.evidenceNotLoaded',
      stale: 'ide.evidenceStale',
      'not-executed': 'ide.evidenceNotExecuted',
      'partial-coverage': 'ide.evidencePartial',
      'missing-evidence': 'ide.evidenceMissing',
      'not-passed': 'ide.evidenceNotPassed',
      'complete-local-pass': 'ide.evidenceLocalPass',
    } as const;
    return this.t(keys[this.evidenceState()]);
  });
  readonly records = computed(() => [
    ...(this.store.context()?.declarations ?? []),
    ...(this.store.context()?.relations ?? []),
  ]);
  readonly related = this.store.relatedDiagnostics;
  readonly t = (
    key: keyof typeof PROJECTIDE_ENGLISH_MESSAGES,
    parameters: Record<string, string | number> = {},
  ) => this.i18n.translate(key as TranslationKey, parameters);
  constructor() {
    this.i18n.registerMessages(PROJECTIDE_ENGLISH_MESSAGES, PROJECTIDE_JAPANESE_MESSAGES);
    const unregister = this.realtime.registerProtectedStateClearer('projectide-preview', () => {
      this.focusGeneration++;
      this.store.clear('denied');
      this.panel.set('overview');
    });
    inject(DestroyRef).onDestroy(() => {
      this.focusGeneration++;
      unregister();
      this.store.clear();
    });
    effect(() => {
      // Identity and active Workspace are invalidation boundaries, never fixture authority.
      this.auth.currentTenant()?.tenantId;
      this.auth.currentUser()?.userId;
      this.workspace.activeWorkspace()?.id;
      const allowed = this.canRead();
      this.focusGeneration++;
      this.store.clear(allowed ? 'idle' : 'denied');
      this.panel.set('overview');
      if (allowed) {
        void this.reload();
      }
    });
  }
  reload(): Promise<void> {
    this.focusGeneration++;
    this.panel.set('overview');
    this.severity.set('');
    this.kind.set('');
    this.search.set('');
    if (!this.canRead()) {
      this.store.clear('denied');
      return Promise.resolve();
    }
    return this.store.load((signal) =>
      this.adapter.read(
        new URL('assets/projectide/preview-v1.json', this.document.baseURI).href,
        signal,
      ),
    );
  }
  choose(key: string): void {
    this.focusGeneration++;
    this.store.choose(key);
    this.panel.set('overview');
    this.severity.set('');
    this.kind.set('');
    this.search.set('');
    this.diffPage.set(0);
    this.historyPage.set(0);
  }
  navigateDiff(change: SemanticChange, side: 'before' | 'after'): void {
    this.focusGeneration++;
    if (!this.canRead()) this.store.clear('denied');
    else if (this.store.navigateDiff(change, side)) { this.diffPage.set(0); this.open('source', true); }
  }
  openHistoryRevision(revision: HistoryRevisionView): void {
    this.focusGeneration++;
    if (!this.canRead()) this.store.clear('denied');
    else if (this.store.openHistoryRevision(revision)) { this.historyPage.set(0); this.open('source', true); }
  }
  loadPreparedRevert(prepared: PreparedDraftRevert): void {
    this.focusGeneration++;
    if (!this.canRead()) this.store.clear('denied');
    else if (this.store.loadPreparedRevert(prepared)) this.open('source', true);
  }
  open(panel: (typeof this.tabs)[number], focus = false): void {
    const generation = ++this.focusGeneration;
    const target = this.store.sourceLocation();
    this.panel.set(panel);
    if (focus) {
      setTimeout(() => {
        if (generation === this.focusGeneration && this.panel() === panel && this.canRead() &&
            (panel !== 'source' || this.store.sourceLocation() === target)) {
          (panel === 'source' && this.store.sourceTarget()
            ? this.sourceTarget()
            : this.heading()
          )?.nativeElement.focus();
        }
      });
    }
  }
  inspectDiagnostic(diagnostic: Diagnostic): void {
    this.focusGeneration++;
    if (this.store.selectDiagnostic(diagnostic)) this.open('inspector', true);
  }
  inspectRecord(record: SourceRecord): void {
    this.store.select(
      record.recordType === 'entity'
        ? { kind: 'entity', id: record.identity }
        : { kind: 'relation', id: record.identity },
    );
    this.open('inspector', true);
  }
  navigate(toDiagnosticSymbol = false): void {
    this.focusGeneration++;
    const navigated = toDiagnosticSymbol ? this.store.navigateDiagnosticSymbol() : this.store.navigate();
    if (navigated) {
      this.open('source', true);
    }
  }
  findReferences(symbol: InspectedSymbol): void {
    this.focusGeneration++;
    if (this.canRead()) void this.store.findReferences(symbol);
    else this.store.clear('denied');
  }
  navigateCompletion(page: ReferenceCompletion, candidate: ReferenceCompletion['candidates'][number]): void {
    this.focusGeneration++;
    if (!this.canRead()) this.store.clear('denied');
    else if (this.store.navigateCompletion(page, candidate)) this.open('source', true);
  }
  navigateTaskSuggestion(page: RequirementTaskSuggestions, candidate: RequirementTaskSuggestions['candidates'][number]): void {
    this.focusGeneration++;
    if (!this.canRead()) this.store.clear('denied');
    else if (this.store.navigateTaskSuggestion(page, candidate)) this.open('source', true);
  }
  loadPreparedRename(prepared: PreparedDraftRename): void {
    this.focusGeneration++;
    if (!this.canRead()) this.store.clear('denied');
    else if (this.store.loadPreparedRename(prepared)) this.open('source', true);
  }
  inspectReference(reference: BoundReference): void {
    this.focusGeneration++;
    if (this.canRead()) this.store.selectReference(reference);
    else this.store.clear('denied');
  }
  navigateReference(destination: 'origin' | 'definition'): void {
    this.focusGeneration++;
    if (!this.canRead()) this.store.clear('denied');
    else if (this.store.navigateReference(destination)) this.open('source', true);
  }
  cancel(): void {
    this.focusGeneration++;
    this.store.clear('cancelled');
  }
  loadResult(): void {
    this.focusGeneration++;
    this.store.loadResult();
  }
  closeInspector(): void {
    this.store.clearSelection();
    this.open('overview');
    const generation = this.focusGeneration;
    setTimeout(() => {
      if (generation === this.focusGeneration) {
        this.inspectorButton()?.nativeElement.focus();
      }
    });
  }
  stateMessage(): string {
    const state = this.store.state();
    return this.t(
      state === 'loading'
        ? 'ide.loading'
        : state === 'denied'
          ? 'ide.denied'
          : state === 'cancelled'
            ? 'ide.cancelled'
            : state === 'wrong-scope'
              ? 'ide.wrong-scope'
              : state === 'unsupported'
                ? 'ide.unsupportedState'
                : state === 'unavailable'
                  ? 'ide.unavailable'
                  : 'ide.error',
    );
  }
}
