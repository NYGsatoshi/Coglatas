// Internal offline inventory only. No Test execution, producer trust or current authority.
import type { Digest, Identity, ProjectLocation } from './projectide.contracts';

export interface LocalDiscoveredTest {
  readonly testId: Identity<'Entity'>;
  readonly location: ProjectLocation;
  readonly displayName: string | null;
  readonly decodeStatus: 'Decoded' | 'Unsupported' | 'Invalid';
  readonly assertionCount: number | null;
}
export interface LocalProjectTestDiscovery {
  readonly state: 'Ready' | 'StaleInput' | 'Unavailable' | 'ResourceLimit';
  readonly inputDigest: Digest | null;
  readonly planDigest: Digest | null;
  readonly discoveredTests: number;
  readonly bindingComplete: boolean;
  readonly evaluatorMatches: boolean;
  readonly tests: readonly LocalDiscoveredTest[];
}
export interface LocalProjectTestDiscoveryProjection {
  readonly format: 'coglatas.local-test-discovery-projection/1';
  readonly inputText: string;
  readonly requestedInputDigest: Digest;
  readonly binding: {
    readonly inputDigest: Digest;
    readonly sourceDigest: Digest;
    readonly bindingComplete: boolean;
    readonly references: readonly { readonly origin: ProjectLocation; readonly state: string; readonly symbolKey: string | null }[];
  } | null;
  readonly plan: { readonly text: string; readonly digest: Digest };
  readonly definitions: readonly { readonly testId: string; readonly text: string }[];
  readonly inventory: LocalProjectTestDiscovery;
}
export interface AdmittedProjectTestDiscovery {
  readonly basis: string;
  readonly projection: LocalProjectTestDiscoveryProjection;
  readonly inventory: LocalProjectTestDiscovery;
}

