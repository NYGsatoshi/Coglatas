import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { injectFilesI18n } from '../../../core/i18n/providers/files-i18n';
import { TranslationKey } from '../../../core/i18n/i18n.service';
import { LucideFileText, LucideRefreshCw, LucideX } from '@lucide/angular';

interface Evidence {
  readonly id: string;
  readonly ordinal: number;
  readonly sourceKind: string;
  readonly sourceTitle: string | null;
  readonly passage: string;
  readonly location: string | null;
  readonly traceEventId: string | null;
}

interface Citation {
  readonly id: string;
  readonly ordinal: number;
  readonly claimId: string;
  readonly logicalClaimId: string;
  readonly evidence: readonly Evidence[];
}

interface Run {
  readonly kind: 'text' | 'citation';
  readonly text: string;
  readonly citation: Citation | null;
}

interface Section {
  readonly id: string;
  readonly logicalSectionId: string;
  readonly ordinal: number;
  readonly heading: string;
  readonly runs: readonly Run[];
}

interface Report {
  readonly projectId: string;
  readonly taskId: string | null;
  readonly artifactId: string;
  readonly artifactVersionId: string;
  readonly versionNumber: number;
  readonly canRefine: boolean;
  readonly title: string;
  readonly sections: readonly Section[];
}

type RefinementTargetKind = 'Section' | 'Claim';
type SourceKind = 'Web' | 'WebSite' | 'ProjectFile' | 'ConnectedApp';
type SourceState = 'Allow' | 'Prioritize' | 'Exclude';

interface SourceRule {
  readonly kind: SourceKind;
  readonly sourceId: string;
  readonly state: SourceState;
}

interface SourcePolicyV2 {
  readonly web: SourceState;
  readonly webSite: SourceState;
  readonly projectFile: SourceState;
  readonly connectedApp: SourceState;
  readonly items: readonly SourceRule[];
}

interface SourcePolicy {
  readonly webEnabled: boolean;
  readonly projectFilesEnabled: boolean;
  readonly policyV2: SourcePolicyV2 | null;
}

interface SourceInventoryItem {
  readonly kind: SourceKind;
  readonly sourceId: string;
  readonly label: string;
}

interface TaskScopeLookup {
  readonly effectivePolicy: SourcePolicy;
  readonly projectDefaultVersion: number;
  readonly taskOverrideVersion: number | null;
  readonly sourceInventory: readonly SourceInventoryItem[];
}

interface RefinementSourceRow {
  readonly key: string;
  readonly kind: SourceKind;
  readonly label: string;
  readonly labelKey?: TranslationKey;
  readonly state: SourceState;
  readonly itemRule: boolean;
}

interface RefinementScope {
  readonly origin: string;
  readonly projectScopeVersion: number;
  readonly taskOverrideVersion: number | null;
  readonly webEnabled: boolean;
  readonly projectFilesEnabled: boolean;
  readonly sourcePolicySchemaVersion: number;
  readonly researchPlanRevisionId: string | null;
  readonly researchPlanRevisionNo: number | null;
  readonly provider: string;
}

interface RefinementPreflight {
  readonly projectId: string;
  readonly taskItemId: string;
  readonly artifactId: string;
  readonly baseArtifactVersionId: string;
  readonly baseVersionNumber: number;
  readonly targetKind: RefinementTargetKind;
  readonly targetLogicalId: string;
  readonly targetLabel: string;
  readonly scope: RefinementScope;
  readonly canRefine: boolean;
  readonly restrictionCode: string | null;
  readonly changesApplyTo: string;
}

interface RefinementResult {
  readonly projectId: string;
  readonly taskItemId: string;
  readonly artifactId: string;
  readonly baseArtifactVersionId: string;
  readonly artifactVersionId: string;
  readonly versionNumber: number;
  readonly targetKind: RefinementTargetKind;
  readonly targetLogicalId: string;
  readonly refreshedClaimCount: number;
  readonly evidenceAdded: number;
}

const emptyCitation: Citation = {
  claimId: '',
  evidence: [],
  id: '',
  logicalClaimId: '',
  ordinal: Number.NaN
};
const emptyScope: RefinementScope = {
  origin: '',
  projectFilesEnabled: false,
  projectScopeVersion: Number.NaN,
  provider: '',
  researchPlanRevisionId: null,
  researchPlanRevisionNo: null,
  sourcePolicySchemaVersion: Number.NaN,
  taskOverrideVersion: null,
  webEnabled: false
};
const emptyPreflight: RefinementPreflight = {
  artifactId: '',
  baseArtifactVersionId: '',
  baseVersionNumber: Number.NaN,
  canRefine: false,
  changesApplyTo: '',
  projectId: '',
  restrictionCode: null,
  scope: emptyScope,
  targetKind: 'Claim',
  targetLabel: '',
  targetLogicalId: '',
  taskItemId: ''
};
const emptyReport: Report = {
  artifactId: '',
  artifactVersionId: '',
  canRefine: false,
  projectId: '',
  sections: [],
  taskId: null,
  title: '',
  versionNumber: Number.NaN
};

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-report-reader-page',
  imports: [LucideFileText, LucideRefreshCw, LucideX],
  standalone: true,
  templateUrl: './report-reader-page.component.html',
  styleUrl: './report-reader-page.component.scss',
})
export class ReportReaderPageComponent {
  readonly i18n = injectFilesI18n();
  private refinementErrorKey: TranslationKey | null = null;
  public get confirmButtonText(): string {
    return this.i18n.translate(this.refinementSubmitting ? 'reportReader.refining' : 'reportReader.confirm');
  }
  public get refinementError(): string {
    return this.refinementErrorKey ? this.i18n.translate(this.refinementErrorKey) : '';
  }
  public error = false;
  public feedback = '';
  public hasTaskContext = false;
  public inspectorOpen = false;
  public loading = true;
  public refinement = emptyPreflight;
  public refinementDialogOpen = false;
  public refinementLoading = false;
  public refinementSources: readonly RefinementSourceRow[] = [];
  public refinementSubmitting = false;
  public report = emptyReport;
  public selected = emptyCitation;

  private readonly http = inject(HttpClient);
  private readonly route = inject(ActivatedRoute);
  private citationTrigger = globalThis.document.body;
  private refinementTrigger = globalThis.document.body;

  public constructor() {
    const [projectId, versionId, taskId] = [
      String(this.route.snapshot.paramMap.get('projectId')),
      String(this.route.snapshot.paramMap.get('artifactVersionId')),
      this.route.snapshot.paramMap.get('taskId')
    ];
    this.loadReport(projectId, versionId, taskId);
  }

  public select(citation: Readonly<Citation>, event: Readonly<Event>): void {
    if (event.currentTarget instanceof HTMLElement) {
      this.citationTrigger = event.currentTarget;
    }
    this.selected = citation;
    this.inspectorOpen = true;
    queueMicrotask(() => {
      globalThis.document.getElementById('evidence-inspector')?.focus();
    });
  }

  public close(): void {
    this.inspectorOpen = false;
    queueMicrotask(() => {
      this.citationTrigger.focus();
    });
  }

  public openSectionRefinement(logicalSectionId: string, event: Readonly<Event>): void {
    this.openRefinement('Section', logicalSectionId, event);
  }

  public openClaimRefinement(logicalClaimId: string, event: Readonly<Event>): void {
    this.openRefinement('Claim', logicalClaimId, event);
  }

  public closeRefinement(): void {
    this.refinementDialogOpen = false;
    this.refinementErrorKey = null;
    this.refinementSources = [];
    this.feedback = '';
    queueMicrotask(() => {
      this.refinementTrigger.focus();
    });
  }

  public updateFeedback(event: Readonly<Event>): void {
    if (event.target instanceof HTMLTextAreaElement) {
      this.feedback = event.target.value;
    }
  }

  public confirmRefinement(): void {
    if (this.refinementSubmitting || !this.refinement.canRefine) {
      return;
    }

    this.refinementSubmitting = true;
    this.refinementErrorKey = null;
    this.http.post<RefinementResult>(
      `/api/projects/${this.refinement.projectId}/artifact-versions/${this.refinement.baseArtifactVersionId}/report/refinements`,
      {
        confirmedProjectScopeVersion: this.refinement.scope.projectScopeVersion,
        confirmedResearchPlanRevisionId: this.refinement.scope.researchPlanRevisionId,
        confirmedResearchPlanRevisionNo: this.refinement.scope.researchPlanRevisionNo,
        confirmedTaskOverrideVersion: this.refinement.scope.taskOverrideVersion,
        feedback: this.feedback.trim(),
        targetKind: this.refinement.targetKind,
        targetLogicalId: this.refinement.targetLogicalId
      }
    ).subscribe({
      error: () => {
        this.refinementErrorKey = 'reportReader.refinementError';
        this.refinementSubmitting = false;
      },
      next: (result) => {
        this.feedback = '';
        this.refinementDialogOpen = false;
        this.refinementSubmitting = false;
        globalThis.location.assign(
          `/app/projects/${result.projectId}/tasks/${result.taskItemId}/reports/${result.artifactVersionId}`
        );
      }
    });
  }

  private loadReport(projectId: string, versionId: string, taskId: string | null): void {
    this.error = false;
    this.inspectorOpen = false;
    this.loading = true;
    this.refinementDialogOpen = false;
    let url = `/api/projects/${projectId}/artifact-versions/${versionId}/report`;
    if (this.route.snapshot.paramMap.has('taskId')) {
      url += `?taskId=${encodeURIComponent(String(taskId))}`;
    }

    this.http.get<Report>(url).subscribe({
      error: () => {
        this.error = true;
        this.loading = false;
      },
      next: (report) => {
        this.hasTaskContext = report.canRefine && report.taskId !== null;
        this.loading = false;
        this.report = report;
      }
    });
  }

  private openRefinement(
    kind: RefinementTargetKind,
    logicalId: string,
    event: Readonly<Event>
  ): void {
    if (this.refinementLoading) {
      return;
    }
    if (event.currentTarget instanceof HTMLElement) {
      this.refinementTrigger = event.currentTarget;
    }

    this.refinementErrorKey = null;
    this.refinementLoading = true;
    this.refinementSources = [];
    const query = new URLSearchParams({ targetKind: kind, targetLogicalId: logicalId });
    this.http.get<RefinementPreflight>(
      `/api/projects/${this.report.projectId}/artifact-versions/${this.report.artifactVersionId}/report/refinement-preflight?${query}`
    ).subscribe({
      error: () => this.failRefinementPreflight('reportReader.preflightError'),
      next: (preflight) => this.loadSourceScope(preflight)
    });
  }

  private loadSourceScope(preflight: Readonly<RefinementPreflight>): void {
    this.http.get<TaskScopeLookup>(
      `/api/tasks/${encodeURIComponent(preflight.taskItemId)}/execution-scope`
    ).subscribe({
      error: () => this.failRefinementPreflight('reportReader.scopeError'),
      next: (taskScope) => {
        if (
          taskScope.projectDefaultVersion !== preflight.scope.projectScopeVersion ||
          taskScope.taskOverrideVersion !== preflight.scope.taskOverrideVersion
        ) {
          this.failRefinementPreflight('reportReader.scopeChanged');
          return;
        }

        this.feedback = '';
        this.refinement = preflight;
        this.refinementSources = buildRefinementSourceRows(taskScope, preflight.scope);
        this.refinementDialogOpen = true;
        this.refinementLoading = false;
        queueMicrotask(() => {
          globalThis.document.getElementById('report-refinement-dialog')?.focus();
        });
      }
    });
  }

  private failRefinementPreflight(key: TranslationKey): void {
    this.refinementErrorKey = key;
    this.refinementLoading = false;
  }

  public get researchPlanLabel(): string {
    const number = this.refinement.scope.researchPlanRevisionNo;
    return number === null ? this.i18n.translate('reportReader.noRevision')
      : this.i18n.translate('reportReader.revision', { number: this.i18n.formatNumber(number) });
  }

  public get scopeVersionLabel(): string {
    const scope = this.refinement.scope;
    const project = this.i18n.translate('reportReader.projectVersion', { number: this.i18n.formatNumber(scope.projectScopeVersion) });
    return scope.taskOverrideVersion === null ? project : `${project} · ${this.i18n.translate('reportReader.taskVersion', { number: this.i18n.formatNumber(scope.taskOverrideVersion) })}`;
  }

  public get changesApplyToLabel(): string {
    // The typed target defines these existing preflight semantics; retain the DTO unchanged.
    return this.refinement.targetKind === 'Claim' ? this.i18n.translate('reportReader.impactClaim')
      : this.refinement.targetKind === 'Section' ? this.i18n.translate('reportReader.impactSection') : this.refinement.changesApplyTo;
  }

  public get restrictionText(): string {
    const code = this.refinement.restrictionCode;
    return this.i18n.translate(code === 'ReportRefinementProjectFilesRequired' ? 'reportReader.filesRequired'
      : code === 'ReportRefinementUnsupportedSources' ? 'reportReader.unsupportedSources' : 'reportReader.restriction');
  }

  public targetKindLabel(kind: RefinementTargetKind): string {
    return kind === 'Section' ? this.i18n.translate('reportReader.section')
      : kind === 'Claim' ? this.i18n.translate('reportReader.claimKind') : kind;
  }

  public sourceKindLabel(kind: string): string {
    const key = ({ Web: 'reportReader.web', WebSite: 'reportReader.website', ProjectFile: 'reportReader.projectFiles', ConnectedApp: 'reportReader.connectedApp' } as const)[kind as SourceKind];
    return key ? this.i18n.translate(key) : kind;
  }

  public sourceStateLabel(state: SourceState): string {
    const key = ({ Allow: 'reportReader.allow', Prioritize: 'reportReader.prioritize', Exclude: 'reportReader.exclude' } as const)[state];
    return key ? this.i18n.translate(key) : state;
  }

}

function buildRefinementSourceRows(
  taskScope: Readonly<TaskScopeLookup>,
  fallback: Readonly<RefinementScope>
): readonly RefinementSourceRow[] {
  const policy = taskScope.effectivePolicy.policyV2 ?? legacySourcePolicy(fallback);
  const rows: RefinementSourceRow[] = [
    { key: 'default:Web', kind: 'Web', label: 'Web default', labelKey: 'reportReader.webDefault', state: policy.web, itemRule: false },
    { key: 'default:WebSite', kind: 'WebSite', label: 'Website default', labelKey: 'reportReader.websiteDefault', state: policy.webSite, itemRule: false },
    { key: 'default:ProjectFile', kind: 'ProjectFile', label: 'Project Files default', labelKey: 'reportReader.projectFilesDefault', state: policy.projectFile, itemRule: false },
    { key: 'default:ConnectedApp', kind: 'ConnectedApp', label: 'Connected Apps default', labelKey: 'reportReader.connectedAppsDefault', state: policy.connectedApp, itemRule: false }
  ];
  const seen = new Set<string>();
  const configured = new Set(policy.items.map((rule) => `${rule.kind}:${rule.sourceId}`));

  for (const source of taskScope.sourceInventory) {
    const key = `${source.kind}:${source.sourceId}`;
    if (seen.has(key)) {
      continue;
    }
    seen.add(key);
    rows.push({
      key,
      kind: source.kind,
      label: source.label,
      state: resolveSourceState(policy, source.kind, source.sourceId),
      itemRule: configured.has(key)
    });
  }

  for (const rule of policy.items) {
    const key = `${rule.kind}:${rule.sourceId}`;
    if (seen.has(key)) {
      continue;
    }
    seen.add(key);
    rows.push({ key, kind: rule.kind, label: rule.sourceId, state: rule.state, itemRule: true });
  }

  return rows;
}

function legacySourcePolicy(scope: Readonly<RefinementScope>): SourcePolicyV2 {
  return {
    connectedApp: 'Exclude',
    items: [],
    projectFile: scope.projectFilesEnabled ? 'Allow' : 'Exclude',
    web: scope.webEnabled ? 'Allow' : 'Exclude',
    webSite: 'Exclude'
  };
}

function resolveSourceState(
  policy: Readonly<SourcePolicyV2>,
  kind: SourceKind,
  sourceId: string
): SourceState {
  const rule = policy.items.find((item) => item.kind === kind && item.sourceId === sourceId);
  if (rule) {
    return rule.state;
  }
  switch (kind) {
    case 'Web': return policy.web;
    case 'WebSite': return policy.webSite;
    case 'ProjectFile': return policy.projectFile;
    case 'ConnectedApp': return policy.connectedApp;
  }
}
