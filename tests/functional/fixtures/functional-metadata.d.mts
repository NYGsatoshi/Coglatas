export type FunctionalGate =
  | 'functional-fast'
  | 'functional-full'
  | 'functional-extended'
  | 'functional-release';

export type FunctionalDomain =
  | 'auth'
  | 'workspace'
  | 'task'
  | 'files'
  | 'messaging'
  | 'notification'
  | 'announcement'
  | 'audit'
  | 'security-negative';

export interface FunctionalTestMetadata {
  journeyId: `FUNC-${string}-${number}`;
  gates: FunctionalGate[];
  domains: FunctionalDomain[];
  priority: 'p0' | 'p1';
  backend: 'real' | 'mock';
  polarity: 'positive' | 'negative';
  negativeAuthz?: boolean;
  releaseEvidence?: boolean;
}

export interface FunctionalPlaywrightDetails {
  tag: string[];
  annotation: Array<{ type: string; description: string }>;
}

export function functionalMetadata(metadata: FunctionalTestMetadata): FunctionalPlaywrightDetails;
