/** Read-only projection of Core Data envelopes; semantic evaluation stays in .NET. */
declare const identity: unique symbol;
export type Identity<K extends string> = string & { readonly [identity]: K };
export interface Digest { readonly algorithm: 'sha-256'; readonly value: string; }
export interface RevisionContext {
  readonly kind: 'coglatas.committed';
  readonly branch: { readonly tenantId: Identity<'Tenant'>; readonly projectId: Identity<'Project'>; readonly branchId: Identity<'Branch'> };
  readonly revisionId: Identity<'Revision'>;
}
export interface ProjectLocation {
  readonly format: 'coglatas.project-location/1'; readonly schemaVersion: 1;
  readonly documentId: Identity<'Document'>; readonly entityId?: Identity<'Entity'>; readonly relationId?: Identity<'Relation'>;
  readonly propertyPath: string; readonly context: RevisionContext;
  readonly textSpan?: { readonly start: number; readonly length: number };
}
export type Execution = 'coglatas.completed' | 'coglatas.not-run' | 'coglatas.cancelled' | 'coglatas.timed-out' | 'coglatas.resource-exhausted' | 'coglatas.internal-error';
export type Coverage = 'coglatas.complete' | 'coglatas.partial' | 'coglatas.none';
export type Verdict = 'coglatas.pass' | 'coglatas.fail' | 'coglatas.inconclusive' | 'coglatas.error' | 'coglatas.not-run';
export type Severity = 'coglatas.error' | 'coglatas.warning' | 'coglatas.suggestion' | 'coglatas.info';
export interface Evidence { readonly kind: string; readonly premises: readonly Digest[]; readonly missingReason: string | null; }
export interface Diagnostic {
  readonly diagnosticId: Identity<'Diagnostic'>; readonly ruleId: string; readonly ruleVersion: number;
  readonly code: string; readonly stageOrdinal: number; readonly severity: Severity;
  readonly primaryLocation: ProjectLocation; readonly evidence: Evidence;
}
export interface CompilerResult {
  readonly format: 'coglatas.compiler-result/1'; readonly resultFormatVersion: 1;
  readonly compilerInputId: Identity<'CompilerInput'>; readonly inputDigest: Digest;
  readonly execution: Execution; readonly coverage: Coverage; readonly verdict: Verdict; readonly evidenceComplete: boolean;
  readonly diagnostics: readonly Diagnostic[];
  readonly artifacts: readonly { readonly role: string; readonly digest: Digest; readonly inputDigest: Digest }[];
  readonly rules: readonly { readonly ruleId: string; readonly version: number; readonly execution: Execution; readonly coverage: Coverage; readonly verdict: Verdict; readonly evidence: Evidence }[];
  readonly boundInputStamp: { readonly sourceContext: RevisionContext; readonly sourceDigest: Digest; readonly inputDigest: Digest };
}
export interface SourceRecord {
  readonly identity: Identity<'Entity'> | Identity<'Relation'>; readonly kind: string;
  readonly status: 'Decoded' | 'Unsupported' | 'Invalid'; readonly data: Readonly<Record<string, unknown>>; readonly location: ProjectLocation;
}
export interface PreviewCase {
  readonly key: string; readonly projectName: string; readonly workspaceName: string; readonly workspaceId: Identity<'Workspace'>; readonly branchName: string;
  readonly context: RevisionContext; readonly desiredContext: RevisionContext;
  readonly source: { readonly documents: readonly { readonly documentId: Identity<'Document'>; readonly logicalName: string }[] };
  readonly sourceText: string; readonly sourceDigest: Digest; readonly inputDigest: Digest; readonly inputId: Identity<'CompilerInput'>;
  readonly manifest: Readonly<Record<string, unknown>>;
  readonly result: CompilerResult; readonly resultId: Identity<'CompilerResult'>; readonly resultDigest: Digest;
  readonly freshness: 'Current' | 'Stale' | 'Undetermined'; readonly changedDimensions: readonly string[];
  readonly declarations: readonly SourceRecord[]; readonly relations: readonly SourceRecord[];
  readonly sourceMap: { readonly format: string; readonly compilerInputId: Identity<'CompilerInput'>; readonly nodes: readonly { readonly location: ProjectLocation }[]; readonly relations: readonly { readonly location: ProjectLocation }[] } | null;
  readonly sourceMapDigest: Digest | null;
}
export interface PreviewBundle { readonly format: 'coglatas.web-preview/1'; readonly mode: 'synthetic-fixture'; readonly coreProduct: string; readonly cases: readonly PreviewCase[]; }
export type ReadState = 'idle' | 'loading' | 'ready' | 'cancelled' | 'unavailable' | 'unsupported' | 'wrong-scope' | 'denied' | 'error';
export type Selection = { readonly kind: 'diagnostic'; readonly id: Identity<'Diagnostic'> } | { readonly kind: 'record'; readonly id: SourceRecord['identity'] };
export const sameContext = (a: RevisionContext, b: RevisionContext): boolean => a.kind === b.kind && a.revisionId === b.revisionId && a.branch.tenantId === b.branch.tenantId && a.branch.projectId === b.branch.projectId && a.branch.branchId === b.branch.branchId;
export const sameDigest = (a: Digest, b: Digest): boolean => a.algorithm === b.algorithm && a.value === b.value;

/** View policy only: zero findings alone says nothing about completeness. */
export function hasCompleteCurrentPass(value: PreviewCase): boolean {
  return value.freshness === 'Current' && sameContext(value.context, value.desiredContext) && value.result.execution === 'coglatas.completed' && value.result.coverage === 'coglatas.complete' && value.result.verdict === 'coglatas.pass' && value.result.evidenceComplete;
}
