// Local Core-produced Test projections only; no runtime authority or browser evaluation.
import type { Digest, Identity, ProjectLocation } from './projectide.contracts';

export type LocalTestVerdict = 'Pass' | 'Fail' | 'Inconclusive' | 'Error' | 'NotRun';
export type LocalTestExecution = 'Completed' | 'NotRun' | 'Cancelled' | 'TimedOut' | 'ResourceExhausted' | 'InternalError';
export type LocalTestCoverage = 'Complete' | 'Partial' | 'None';
export type LocalTestFreshness = 'Current' | 'Stale' | 'Undetermined';
export type LocalTestEvidence = 'coglatas.model-proof' | 'coglatas.missing';

/** Core-produced immutable assertion result. Expected/actual values stay in their admitted Source. */
export interface LocalTestAssertion {
  readonly location: ProjectLocation;
  readonly verdict: LocalTestVerdict;
  readonly reason: string;
  readonly assertionDigest: Digest;
  readonly actualDigest: Digest | null;
}

export interface LocalTestCase {
  readonly testId: Identity<'Entity'>;
  readonly verdict: LocalTestVerdict;
  readonly reason: string;
  readonly assertions: readonly LocalTestAssertion[];
}

/** Exact format v1, with its own enum spelling; never infer success from missing/future fields. */
export interface LocalProjectTestRun {
  readonly format: 'coglatas.project-test-run/1';
  readonly compilerInputId: Identity<'CompilerInput'>;
  readonly inputDigest: Digest;
  readonly sourceDigest: Digest;
  readonly planDigest: Digest;
  readonly execution: LocalTestExecution;
  readonly coverage: LocalTestCoverage;
  readonly verdict: LocalTestVerdict;
  readonly freshness: LocalTestFreshness;
  readonly evidenceKind: LocalTestEvidence;
  readonly discoveredTests: number;
  readonly executedAssertions: number;
  readonly reason: string;
  readonly tests: readonly LocalTestCase[];
}

export interface LocalProjectTestEnvelope {
  readonly format: 'coglatas.project-test-result-envelope/1';
  readonly resultDigest: Digest;
  readonly result: LocalProjectTestRun;
}

/** Local UI limits describe rendering, not Domain capacity, policy requirements or permission. */
export interface LocalTestPanelState {
  readonly run: LocalProjectTestEnvelope | null;
  readonly selectedTest: Identity<'Entity'> | null;
  readonly selectedAssertion: LocalTestAssertion | null;
  readonly page: number;
}

export interface LocalProjectTestProjection {
  readonly format: 'coglatas.local-test-projection/1';
  readonly inputText: string;
  readonly plan: { readonly text: string; readonly digest: Digest };
  readonly result: { readonly text: string; readonly digest: Digest };
  readonly declarations: readonly { readonly testId: string; readonly text: string }[];
  readonly assertions: readonly { readonly testId: string; readonly key: string; readonly text: string }[];
}
export interface AdmittedLocalTests {
  readonly basis: string;
  readonly projection: LocalProjectTestProjection;
  readonly run: LocalProjectTestRun;
}
