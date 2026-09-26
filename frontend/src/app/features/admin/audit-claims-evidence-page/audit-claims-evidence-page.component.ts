import { ChangeDetectionStrategy, Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { distinctUntilChanged, map } from 'rxjs';

import { CoglatasFilterChipComponent } from '../../../shared/ui/coglatas-filter-chip/coglatas-filter-chip.component';
import { AuditClaimsEvidenceFacade } from './audit-claims-evidence.facade';
import {
  AuditClaimSupportFilter,
  AuditClaimViewModel,
  AuditEvidenceSourceClassification,
  AuditEvidenceSourceKind,
  AuditEvidenceVerificationStatus,
} from './audit-claims-evidence.types';

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-audit-claims-evidence-page',
  standalone: true,
  imports: [CoglatasFilterChipComponent],
  templateUrl: './audit-claims-evidence-page.component.html',
  styleUrl: './audit-claims-evidence-page.component.scss',
})
export class AuditClaimsEvidencePageComponent {
  private readonly facade = inject(AuditClaimsEvidenceFacade);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly routeVersionId = toSignal(
    this.route.queryParamMap.pipe(
      map((params) => params.get('artifactVersion')),
      distinctUntilChanged(),
    ),
    { initialValue: this.route.snapshot.queryParamMap.get('artifactVersion') },
  );
  private readonly routeClaimSupport = toSignal(
    this.route.queryParamMap.pipe(
      map((params) => normalizeClaimSupport(params.get('support'))),
      distinctUntilChanged(),
    ),
    { initialValue: normalizeClaimSupport(this.route.snapshot.queryParamMap.get('support')) },
  );
  private readonly routeClaimId = toSignal(
    this.route.queryParamMap.pipe(
      map((params) => params.get('claim')),
      distinctUntilChanged(),
    ),
    { initialValue: this.route.snapshot.queryParamMap.get('claim') },
  );
  private readonly routeEvidenceId = toSignal(
    this.route.queryParamMap.pipe(
      map((params) => params.get('evidence')),
      distinctUntilChanged(),
    ),
    { initialValue: this.route.snapshot.queryParamMap.get('evidence') },
  );

  readonly vm = this.facade.viewModel;
  readonly actionSummary = this.facade.actionSummary;
  readonly versionInput = signal(this.route.snapshot.queryParamMap.get('artifactVersion') ?? '');
  readonly inputError = signal<string | null>(null);
  readonly selectedClaimId = signal<string | null>(null);
  readonly selectedEvidenceId = signal<string | null>(null);
  readonly traceExpanded = signal(false);
  readonly claimSupportFilter = signal<AuditClaimSupportFilter>(
    normalizeClaimSupport(this.route.snapshot.queryParamMap.get('support')),
  );

  readonly visibleClaims = computed(() => {
    const claims = this.vm().claims;
    return this.claimSupportFilter() === 'Unverified'
      ? claims.filter((claim) => claim.supportStatus === 'Unverified')
      : claims;
  });

  readonly unverifiedClaimCount = computed(() =>
    this.vm().claims.filter((claim) => claim.supportStatus === 'Unverified').length,
  );

  readonly selectedClaim = computed(() => {
    const claimId = this.selectedClaimId();
    return this.visibleClaims().find((claim) => claim.id === claimId) ?? null;
  });

  readonly selectedEvidence = computed(() => {
    const claim = this.selectedClaim();
    const evidenceId = this.selectedEvidenceId();
    if (!claim) {
      return null;
    }
    return claim.evidence.find((evidence) => evidence.id === evidenceId) ?? claim.evidence[0] ?? null;
  });

  readonly selectedSourceReferences = computed(() => {
    const evidence = this.selectedEvidence();
    if (!evidence?.sourceId) {
      return [];
    }

    const references: Array<{
      claimId: string;
      claimOrdinal: number;
      evidenceId: string;
      evidenceOrdinal: number;
      location: string | null;
    }> = [];

    for (const claim of this.vm().claims) {
      for (const candidate of claim.evidence) {
        if (candidate.sourceId !== evidence.sourceId) {
          continue;
        }

        references.push({
          claimId: claim.id,
          claimOrdinal: claim.ordinal,
          evidenceId: candidate.id,
          evidenceOrdinal: candidate.ordinal,
          location: candidate.location,
        });
      }
    }

    return references;
  });

  readonly selectedSourceClaimCount = computed(() =>
    new Set(this.selectedSourceReferences().map((reference) => reference.claimId)).size,
  );

  constructor() {
    this.facade.loadActionSummary();

    effect(() => {
      const versionId = this.routeVersionId();
      untracked(() => this.loadFromRoute(versionId));
    });

    effect(() => {
      const support = this.routeClaimSupport();
      untracked(() => {
        this.claimSupportFilter.set(support);
        this.selectedClaimId.set(null);
        this.selectedEvidenceId.set(null);
        this.traceExpanded.set(false);
      });
    });

    effect(() => {
      const page = this.vm();
      const claims = this.visibleClaims();
      const requestedClaimId = this.routeClaimId();
      const requestedEvidenceId = this.routeEvidenceId();
      if (page.status !== 'ready' || claims.length === 0) {
        untracked(() => {
          this.selectedClaimId.set(null);
          this.selectedEvidenceId.set(null);
          this.traceExpanded.set(false);
        });
        return;
      }

      untracked(() => {
        const requested = requestedClaimId
          ? claims.find((claim) => claim.id === requestedClaimId)
          : null;
        const current = claims.find((claim) => claim.id === this.selectedClaimId());
        this.selectClaim(requested ?? current ?? claims[0]);
        const selected = this.selectedClaim();
        if (selected && requestedEvidenceId && selected.evidence.some((evidence) => evidence.id === requestedEvidenceId)) {
          this.selectedEvidenceId.set(requestedEvidenceId);
        }
      });
    });
  }

  updateVersionInput(value: string): void {
    this.versionInput.set(value);
    if (this.inputError()) {
      this.inputError.set(null);
    }
  }

  openVersion(): void {
    const versionId = this.versionInput().trim();
    if (!isGuid(versionId)) {
      this.inputError.set('Enter a valid artifact version ID.');
      return;
    }

    this.inputError.set(null);
    if (this.routeVersionId() === versionId) {
      this.facade.load(versionId);
      return;
    }

    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { artifactVersion: versionId, claim: null, evidence: null },
      queryParamsHandling: 'merge',
    });
  }

  activateUnverifiedClaims(): void {
    if (this.vm().status !== 'ready') {
      return;
    }

    this.claimSupportFilter.set('Unverified');
    this.selectedClaimId.set(null);
    this.selectedEvidenceId.set(null);
    this.traceExpanded.set(false);
    void this.updateClaimFilter('Unverified');
  }

  clearClaimFilter(): void {
    this.claimSupportFilter.set('');
    this.selectedClaimId.set(null);
    this.selectedEvidenceId.set(null);
    this.traceExpanded.set(false);
    void this.updateClaimFilter('');
  }

  openWarningEvents(): void {
    if (this.actionSummary().status !== 'ready') {
      return;
    }

    void this.router.navigate(['/admin/audit'], {
      queryParams: { severity: 'warning' },
    });
  }

  openErrorEvents(): void {
    if (this.actionSummary().status !== 'ready') {
      return;
    }

    void this.router.navigate(['/admin/audit'], {
      queryParams: { status: 'failed' },
    });
  }

  selectClaim(claim: AuditClaimViewModel): void {
    if (!this.visibleClaims().some((candidate) => candidate.id === claim.id)) {
      return;
    }
    this.selectedClaimId.set(claim.id);
    this.selectedEvidenceId.set(claim.evidence[0]?.id ?? null);
    this.traceExpanded.set(false);
  }

  selectEvidence(evidenceId: string): void {
    if (this.selectedClaim()?.evidence.some((evidence) => evidence.id === evidenceId)) {
      this.selectedEvidenceId.set(evidenceId);
      this.traceExpanded.set(false);
    }
  }

  toggleTrace(): void {
    if (!this.selectedEvidence()) {
      return;
    }
    this.traceExpanded.update((expanded) => !expanded);
  }

  sourceKindLabel(kind: AuditEvidenceSourceKind): string {
    switch (kind) {
      case 'WebSnapshot': return 'Web snapshot';
      case 'FileAttachment': return 'File attachment';
      case 'ArtifactVersion': return 'Artifact version';
      default: return 'Source';
    }
  }

  sourceClassificationLabel(classification: AuditEvidenceSourceClassification): string {
    switch (classification) {
      case 'Primary': return 'Primary source';
      case 'Secondary': return 'Secondary source';
      default: return 'Not classified';
    }
  }

  verificationLabel(status: AuditEvidenceVerificationStatus): string {
    switch (status) {
      case 'Verified': return 'Verified';
      case 'Rejected': return 'Rejected';
      default: return 'Not verified';
    }
  }

  formatTimestamp(value: string | null): string {
    if (!value) {
      return 'Not recorded';
    }

    const parsed = new Date(value);
    return Number.isNaN(parsed.getTime()) ? 'Not recorded' : parsed.toISOString();
  }

  private async updateClaimFilter(filter: AuditClaimSupportFilter): Promise<void> {
    await this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { support: filter || null },
      queryParamsHandling: 'merge',
    });
  }

  private loadFromRoute(versionId: string | null): void {
    this.selectedClaimId.set(null);
    this.selectedEvidenceId.set(null);
    this.traceExpanded.set(false);
    this.versionInput.set(versionId ?? '');

    if (!versionId) {
      this.inputError.set(null);
      this.facade.clear();
      return;
    }

    if (!isGuid(versionId)) {
      this.inputError.set('Enter a valid artifact version ID.');
      this.facade.clear();
      return;
    }

    this.inputError.set(null);
    this.facade.load(versionId);
  }
}

function normalizeClaimSupport(value: string | null): AuditClaimSupportFilter {
  return value === 'Unverified' ? 'Unverified' : '';
}

function isGuid(value: string): boolean {
  return /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(value.trim());
}
