import { hasUniquePreviewIdentities, isPreviewCaseShape, isRevisionContextShape } from './projectide-preview.validation';

/** Read-only projection of Core Data envelopes; semantic evaluation stays in .NET. */
declare const identity: unique symbol;
export type Identity<K extends string> = string & { readonly [identity]: K };
export interface Digest {
  readonly algorithm: 'sha-256';
  readonly value: string;
}
interface BranchContext {
    readonly tenantId: Identity<'Tenant'>;
    readonly projectId: Identity<'Project'>;
    readonly branchId: Identity<'Branch'>;
}
export interface CommittedContext {
  readonly kind: 'coglatas.committed';
  readonly branch: BranchContext;
  readonly revisionId: Identity<'Revision'>;
}
export interface CandidateContext {
  readonly kind: 'coglatas.candidate';
  readonly branch: BranchContext;
  readonly proposalId: Identity<'Proposal'>;
  readonly candidateRevisionId: Identity<'CandidateRevision'>;
  readonly baseRevision: CommittedContext;
  readonly capturedTargetHead: CommittedContext;
}
export type RevisionContext = CommittedContext | CandidateContext;
export const contextRevision = (context: RevisionContext): string => context.kind === 'coglatas.committed'
  ? context.revisionId : context.candidateRevisionId;
export interface ProjectLocation {
  readonly format: 'coglatas.project-location/1';
  readonly schemaVersion: 1;
  readonly documentId: Identity<'Document'>;
  readonly entityId?: Identity<'Entity'>;
  readonly relationId?: Identity<'Relation'>;
  readonly propertyPath: string;
  readonly context: RevisionContext;
  readonly textSpan?: { readonly start: number; readonly length: number };
}
export type Execution =
  | 'coglatas.completed'
  | 'coglatas.not-run'
  | 'coglatas.cancelled'
  | 'coglatas.timed-out'
  | 'coglatas.resource-exhausted'
  | 'coglatas.internal-error';
export type Coverage = 'coglatas.complete' | 'coglatas.partial' | 'coglatas.none';
export type Verdict =
  | 'coglatas.pass'
  | 'coglatas.fail'
  | 'coglatas.inconclusive'
  | 'coglatas.error'
  | 'coglatas.not-run';
export type Severity =
  'coglatas.error' | 'coglatas.warning' | 'coglatas.suggestion' | 'coglatas.info';
export interface Evidence {
  readonly kind: string;
  readonly premises: readonly Digest[];
  readonly missingReason: string | null;
}
export interface Diagnostic {
  readonly diagnosticId: Identity<'Diagnostic'>;
  readonly ruleId: string;
  readonly ruleVersion: number;
  readonly code: string;
  readonly stageOrdinal: number;
  readonly severity: Severity;
  readonly primaryLocation: ProjectLocation;
  readonly evidence: Evidence;
}
export interface CompilerResult {
  readonly format: 'coglatas.compiler-result/1';
  readonly resultFormatVersion: 1;
  readonly compilerInputId: Identity<'CompilerInput'>;
  readonly inputDigest: Digest;
  readonly execution: Execution;
  readonly coverage: Coverage;
  readonly verdict: Verdict;
  readonly evidenceComplete: boolean;
  readonly diagnostics: readonly Diagnostic[];
  readonly artifacts: readonly {
    readonly role: string;
    readonly digest: Digest;
    readonly inputDigest: Digest;
  }[];
  readonly rules: readonly {
    readonly ruleId: string;
    readonly version: number;
    readonly execution: Execution;
    readonly coverage: Coverage;
    readonly verdict: Verdict;
    readonly evidence: Evidence;
  }[];
  readonly boundInputStamp: {
    readonly sourceContext: RevisionContext;
    readonly sourceDigest: Digest;
    readonly inputDigest: Digest;
  };
}
interface SourceRecordFields {
  readonly kind: string;
  readonly status: 'Decoded' | 'Unsupported' | 'Invalid';
  readonly data: Readonly<Record<string, unknown>>;
  readonly location: ProjectLocation;
}
export type SourceRecord = SourceRecordFields &
  (
    | { readonly recordType: 'entity'; readonly identity: Identity<'Entity'> }
    | { readonly recordType: 'relation'; readonly identity: Identity<'Relation'> }
  );
export type InspectedSymbol = SemanticInspection['symbols'][number];
export type BoundReference = SemanticInspection['references'][number];
export interface ReferenceCompletion {
  readonly originKey: string;
  readonly inputDigest: Digest;
  readonly state: 'Ready' | 'StaleInput' | 'OutOfScope' | 'Unavailable' | 'Unsupported' | 'InvalidCursor';
  readonly expectedKind: string | null;
  readonly coverageComplete: boolean;
  readonly truncated: boolean;
  readonly candidates: readonly { readonly symbolKey: string; readonly displayName: string | null }[];
}
export interface PreparedDraftRename {
  readonly afterCaseKey: string;
  readonly plan: {
    readonly beforeInputDigest: Digest;
    readonly afterInputDigest: Digest;
    readonly beforeContext: CandidateContext;
    readonly afterContext: CandidateContext;
    readonly target: ProjectLocation;
    readonly previousName: string | null;
    readonly newName: string;
    readonly referenceCoverageComplete: boolean;
    readonly referenceOrigins: readonly ProjectLocation[];
  };
}

export interface SemanticChange {
  readonly recordType: 'entity' | 'relation' | 'document' | 'source';
  readonly identity: string;
  readonly path: string;
  readonly kind: 'Added' | 'Removed' | 'DisplayName' | 'Property' | 'Reference' | 'Uninterpreted';
  readonly beforePresent: boolean;
  readonly afterPresent: boolean;
  readonly before: unknown;
  readonly after: unknown;
  readonly beforeLocation: ProjectLocation | null;
  readonly afterLocation: ProjectLocation | null;
}
export interface PreparedSemanticDiff {
  readonly afterCaseKey: string;
  readonly comparison: {
    readonly format: 'coglatas.synthetic-semantic-diff/1';
    readonly state: 'Complete' | 'Incomplete';
    readonly noChanges: boolean;
    readonly beforeInputDigest: Digest;
    readonly afterInputDigest: Digest;
    readonly beforeContext: RevisionContext;
    readonly afterContext: RevisionContext;
    readonly changes: readonly SemanticChange[];
  };
}

export interface SemanticInspection {
  readonly format: 'coglatas.semantic-inspection/1';
  readonly compilerInputId: Identity<'CompilerInput'>;
  readonly inputDigest: Digest;
  readonly sourceDigest: Digest;
  readonly context: RevisionContext;
  readonly bindingComplete: boolean;
  readonly symbols: readonly {
    readonly key: string;
    readonly kind: string;
    readonly status: 'Decoded' | 'Unsupported' | 'Invalid';
    readonly location: ProjectLocation;
    readonly declaredType: Readonly<Record<string, unknown>> | null;
  }[];
  readonly references: readonly {
    readonly origin: ProjectLocation;
    readonly state: 'Resolved' | 'MissingWithinCompleteScope' | 'UnknownDueToCoverage' | 'Ambiguous' | 'Unsupported' | 'OutOfScope';
    readonly symbolKey: string | null;
  }[];
}
export interface PreviewCase {
  readonly preparedHistory?: PreparedHistory | null;
  readonly preparedDiff?: PreparedSemanticDiff | null;
  readonly key: string;
  readonly projectName: string;
  readonly workspaceName: string;
  readonly workspaceId: Identity<'Workspace'>;
  readonly branchName: string;
  readonly context: RevisionContext;
  readonly desiredContext: RevisionContext;
  readonly source: {
    readonly documents: readonly {
      readonly documentId: Identity<'Document'>;
      readonly logicalName: string;
    }[];
  };
  readonly sourceText: string;
  readonly sourceDigest: Digest;
  readonly inputDigest: Digest;
  readonly inputId: Identity<'CompilerInput'>;
  readonly manifest: Readonly<Record<string, unknown>>;
  readonly result: CompilerResult;
  readonly resultId: Identity<'CompilerResult'>;
  readonly resultDigest: Digest;
  readonly freshness: 'Current' | 'Stale' | 'Undetermined';
  readonly changedDimensions: readonly string[];
  readonly declarations: readonly SourceRecord[];
  readonly relations: readonly SourceRecord[];
  readonly semanticInspection?: SemanticInspection | null;
  readonly referenceCompletions?: readonly ReferenceCompletion[] | null;
  readonly preparedRename?: PreparedDraftRename | null;
  readonly sourceMap: {
    readonly format: string;
    readonly compilerInputId: Identity<'CompilerInput'>;
    readonly nodes: readonly { readonly location: ProjectLocation }[];
    readonly relations: readonly { readonly location: ProjectLocation }[];
  } | null;
  readonly sourceMapDigest: Digest | null;
}

export interface HistoryRevisionView {
  readonly caseKey: string;
  readonly context: CommittedContext;
  readonly branchSequence: number;
  readonly sourceDigest: Digest;
  readonly storage: 'Checkpoint' | 'TypedDelta';
  readonly parents: readonly CommittedContext[];
}
export interface PreparedHistory {
  readonly headCaseKey: string;
  readonly state: 'Ready';
  readonly sourceAdmissionStatus: 'StructurallyDecoded' | 'Unsupported';
  readonly missingAnchorState: 'Unavailable';
  readonly compactedCheckpointVerified: true;
  readonly reconstructionPath: readonly CommittedContext[];
  readonly revisions: readonly HistoryRevisionView[];
  readonly deltaRecordCounts: { readonly documents: number; readonly declarations: number; readonly relations: number };
}
export interface PreviewBundle {
  readonly format: 'coglatas.web-preview/1';
  readonly mode: 'synthetic-fixture';
  readonly coreProduct: string;
  readonly cases: readonly PreviewCase[];
}
export type ReadState =
  | 'idle'
  | 'loading'
  | 'ready'
  | 'cancelled'
  | 'unavailable'
  | 'unsupported'
  | 'wrong-scope'
  | 'denied'
  | 'error';
export type Selection =
  | { readonly kind: 'diagnostic'; readonly id: Identity<'Diagnostic'> }
  | { readonly kind: 'entity'; readonly id: Identity<'Entity'> }
  | { readonly kind: 'relation'; readonly id: Identity<'Relation'> };
export const sameContext = (a: RevisionContext, b: RevisionContext): boolean => {
  if (!isRevisionContextShape(a) || !isRevisionContextShape(b)) return false;
  if (a.kind !== b.kind || a.branch.tenantId !== b.branch.tenantId || a.branch.projectId !== b.branch.projectId ||
      a.branch.branchId !== b.branch.branchId) return false;
  if (a.kind === 'coglatas.committed' && b.kind === 'coglatas.committed') return a.revisionId === b.revisionId;
  return a.kind === 'coglatas.candidate' && b.kind === 'coglatas.candidate' && a.proposalId === b.proposalId &&
    a.candidateRevisionId === b.candidateRevisionId && sameContext(a.baseRevision, b.baseRevision) &&
    sameContext(a.capturedTargetHead, b.capturedTargetHead);
};
export const sameDigest = (a: Digest, b: Digest): boolean =>
  a.algorithm === b.algorithm && a.value === b.value;

/** Bounded presentation of Core compiler axes, not trusted evidence or eligibility. */
export type LocalEvidenceState =
  | 'not-loaded'
  | 'stale'
  | 'not-executed'
  | 'partial-coverage'
  | 'missing-evidence'
  | 'not-passed'
  | 'complete-local-pass';

/** Classify a synthetic result only after its input, source, context and required coverage agree. */
export function assessLocalEvidence(value: PreviewCase | null): LocalEvidenceState {
  if (!value) return 'not-loaded';
  if (!isPreviewCaseShape(value) || !hasUniquePreviewIdentities(value)) return 'missing-evidence';
  const result = value.result;
  const manifest = value.manifest['manifest'] as Record<string, unknown> | null | undefined;
  const manifestContext = manifest?.['sourceContext'] as RevisionContext | null | undefined;
  const manifestSourceDigest = manifest?.['sourceDigest'] as Digest | null | undefined;
  const envelopeDigest = value.manifest['inputDigest'] as Digest | null | undefined;
  // Recheck exact source/input context even when a caller bypasses preview loading.
  if (value.freshness !== 'Current' ||
      value.manifest['compilerInputId'] !== value.inputId ||
      !envelopeDigest || !sameDigest(envelopeDigest, value.inputDigest) ||
      !manifestSourceDigest || !sameDigest(manifestSourceDigest, value.sourceDigest) ||
      !manifestContext?.branch || !sameContext(manifestContext, value.context) ||
      !sameContext(value.context, value.desiredContext) ||
      result.compilerInputId !== value.inputId ||
      !sameDigest(result.inputDigest, value.inputDigest) ||
      !sameDigest(result.boundInputStamp.inputDigest, value.inputDigest) ||
      !sameDigest(result.boundInputStamp.sourceDigest, value.sourceDigest) ||
      !sameContext(result.boundInputStamp.sourceContext, value.context)) {
    return 'stale';
  }
  if (result.execution !== 'coglatas.completed') return 'not-executed';
  const settings = manifest?.['settings'] as {
    sourceCoverage?: string;
    rules?: readonly { ruleId: string; version: number; required: boolean }[];
  } | null | undefined;
  const required = Array.isArray(settings?.rules) ? settings.rules.filter((rule) => rule?.required === true) : [];
  const outcomes = required.map((rule) => result.rules.find((outcome) =>
    outcome.ruleId === rule.ruleId && outcome.version === rule.version));
  if (result.coverage !== 'coglatas.complete' || settings?.sourceCoverage !== 'coglatas.complete' ||
      required.length === 0 || outcomes.some((rule) => !rule ||
        rule.execution !== 'coglatas.completed' || rule.coverage !== 'coglatas.complete') ||
      [...value.declarations, ...value.relations].some((record) => record.status !== 'Decoded') ||
      (value.semanticInspection && (!value.semanticInspection.bindingComplete ||
        value.semanticInspection.symbols.some((symbol) => symbol.status !== 'Decoded') ||
        value.semanticInspection.references.some((reference) => reference.state !== 'Resolved')))) {
    return 'partial-coverage';
  }
  if (result.evidenceComplete !== true || outcomes.some((rule) => rule?.evidence.kind === 'coglatas.missing') ||
      result.diagnostics.some((diagnostic) => diagnostic.evidence.kind === 'coglatas.missing')) return 'missing-evidence';
  if (result.verdict !== 'coglatas.pass' || outcomes.some((rule) => rule?.verdict !== 'coglatas.pass')) return 'not-passed';
  return 'complete-local-pass';
}

/** A local fixture PASS is never Producer trust, current access or Evidence Eligibility. */
export function hasCompleteCurrentPass(value: PreviewCase): boolean {
  return assessLocalEvidence(value) === 'complete-local-pass';
}
