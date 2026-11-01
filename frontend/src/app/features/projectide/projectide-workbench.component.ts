import { DOCUMENT, JsonPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, computed, effect, inject, signal, viewChild } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuthSessionFacade } from '../../core/auth/auth-session.facade';
import { FrontendFeatureFlagsService } from '../../core/feature-flags/frontend-feature-flags.service';
import { I18nService, TranslationKey } from '../../core/i18n/i18n.service';
import { PROJECTIDE_ENGLISH_MESSAGES, PROJECTIDE_JAPANESE_MESSAGES } from '../../core/i18n/messages/projectide.messages';
import { RealtimeFacade } from '../../core/realtime/realtime.facade';
import { ActiveWorkspaceFacade } from '../../core/workspace/active-workspace.facade';
import { Diagnostic, SourceRecord, hasCompleteCurrentPass } from './projectide.contracts';
import { ProjectIdePreviewAdapter } from './projectide-preview.adapter';
import { ProjectIdeWorkbenchStore } from './projectide-workbench.store';

@Component({
  selector: 'app-projectide-workbench', standalone: true, imports: [RouterLink, JsonPipe],
  providers: [ProjectIdeWorkbenchStore, ProjectIdePreviewAdapter],
  templateUrl: './projectide-workbench.component.html', styleUrl: './projectide-workbench.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ProjectIdeWorkbenchComponent {
  readonly store = inject(ProjectIdeWorkbenchStore);
  readonly flags = inject(FrontendFeatureFlagsService);
  readonly i18n = inject(I18nService);
  private readonly auth = inject(AuthSessionFacade);
  private readonly workspace = inject(ActiveWorkspaceFacade);
  private readonly adapter = inject(ProjectIdePreviewAdapter);
  private readonly realtime = inject(RealtimeFacade);
  private readonly document = inject(DOCUMENT);
  readonly panel = signal<'overview' | 'source' | 'problems' | 'inspector'>('overview');
  readonly severity = signal(''); readonly kind = signal(''); readonly search = signal('');
  readonly heading = viewChild<ElementRef<HTMLElement>>('panelHeading');
  private readonly inspectorButton = viewChild<ElementRef<HTMLButtonElement>>('inspectorButton');
  readonly tabs = ['overview', 'source', 'problems', 'inspector'] as const;
  readonly canRead = computed(() => this.flags.projectIdePreviewEnabled() && this.auth.isAuthenticated() && this.auth.currentTenant()?.isAvailable === true && this.auth.session().capabilities.includes('projects:view'));
  readonly diagnostics = computed(() => this.store.result()?.result.diagnostics ?? []);
  readonly kinds = computed(() => [...new Set(this.diagnostics().map(d => d.ruleId))]);
  readonly filtered = computed(() => this.diagnostics().filter(d => (!this.severity() || d.severity === this.severity()) && (!this.kind() || d.ruleId === this.kind()) &&
    `${d.code} ${d.ruleId} ${d.diagnosticId} ${d.primaryLocation.entityId ?? ''} ${d.primaryLocation.relationId ?? ''}`.toLowerCase().includes(this.search().toLowerCase())));
  readonly completePass = computed(() => { const value = this.store.result(); return !!value && hasCompleteCurrentPass(value); });
  readonly records = computed(() => [...(this.store.context()?.declarations ?? []), ...(this.store.context()?.relations ?? [])]);
  readonly related = computed(() => this.diagnostics().filter(d => d.primaryLocation.entityId === this.store.inspectedRecord()?.location.entityId && !!d.primaryLocation.entityId || d.primaryLocation.relationId === this.store.inspectedRecord()?.location.relationId && !!d.primaryLocation.relationId));
  readonly t = (key: keyof typeof PROJECTIDE_ENGLISH_MESSAGES, parameters: Record<string, string | number> = {}) => this.i18n.translate(key as TranslationKey, parameters);
  constructor() {
    this.i18n.registerMessages(PROJECTIDE_ENGLISH_MESSAGES, PROJECTIDE_JAPANESE_MESSAGES);
    const unregister = this.realtime.registerProtectedStateClearer('projectide-preview', () => { this.store.clear('denied'); this.panel.set('overview'); });
    inject(DestroyRef).onDestroy(() => { unregister(); this.store.clear(); });
    effect(() => {
      // Identity and active Workspace are invalidation boundaries, never fixture authority.
      this.auth.currentTenant()?.tenantId; this.auth.currentUser()?.userId; this.workspace.activeWorkspace()?.id;
      const allowed = this.canRead();
      this.store.clear(allowed ? 'idle' : 'denied'); this.panel.set('overview');
      if (allowed) { void this.reload(); }
    });
  }
  reload(): Promise<void> {
    if (!this.canRead()) { this.store.clear('denied'); return Promise.resolve(); }
    return this.store.load(signal => this.adapter.read(new URL('assets/projectide/preview-v1.json', this.document.baseURI).href, signal));
  }
  choose(key: string): void {
    this.store.choose(key); this.panel.set('overview'); this.severity.set(''); this.kind.set(''); this.search.set('');
  }
  open(panel: typeof this.tabs[number], focus = false): void {
    this.panel.set(panel);
    if (focus) { setTimeout(() => this.heading()?.nativeElement.focus()); }
  }
  inspectDiagnostic(diagnostic: Diagnostic): void { this.store.select({ kind: 'diagnostic', id: diagnostic.diagnosticId }); this.open('inspector', true); }
  inspectRecord(record: SourceRecord): void { this.store.select(record.recordType === 'entity' ? { kind: 'entity', id: record.identity } : { kind: 'relation', id: record.identity }); this.open('inspector', true); }
  navigate(): void { if (this.store.navigate()) { this.open('source', true); } }
  closeInspector(): void { this.store.selection.set(null); this.open('overview'); setTimeout(() => this.inspectorButton()?.nativeElement.focus()); }
  stateMessage(): string {
    const state = this.store.state();
    return this.t(state === 'loading' ? 'ide.loading' : state === 'denied' ? 'ide.denied' : state === 'cancelled' ? 'ide.cancelled' : state === 'wrong-scope' ? 'ide.wrong-scope' : state === 'unsupported' ? 'ide.unsupportedState' : state === 'unavailable' ? 'ide.unavailable' : 'ide.error');
  }
}
