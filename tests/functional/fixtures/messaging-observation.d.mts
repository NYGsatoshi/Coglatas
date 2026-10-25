export const MESSAGING_OBSERVATION_KEY: string;
export const MESSAGING_EVENT_LIMIT: number;
export interface MessagingObservation {
  documentStartedAtMs: number;
  storageReadable: boolean;
  storageWritable: boolean;
  events: { atMs: number; kind: string; composerGeneration: number; composerPresent: boolean; draftEnabled: boolean; sendEnabled: boolean; draftFocused: boolean; connection: string | null }[];
  eventsTruncated: number;
  webSocketObserved: boolean;
  controls: { atMs: number; openedAtMs: number; kind: string }[];
  controlsTruncated: number;
}
export interface ComposerFillObservation {
  startedAtMs: number;
  finishedAtMs: number;
  outcome: 'resolved' | 'rejected';
}
export function validateComposerFill(value: unknown): ComposerFillObservation | null;
export function createComposerFillTracker(clock?: () => number): {
  track<T>(action: () => Promise<T>): Promise<T>;
  snapshot(documentStartedAtMs?: number): ComposerFillObservation | null;
};
export function validateMessagingObservation(value: unknown, schemaVersion?: 2 | 3): MessagingObservation | null;
export function installMessagingObservation(key: string, environment?: Pick<typeof globalThis, 'window' | 'document' | 'crypto' | 'Date' | 'MutationObserver'>): void;
export function createMessagingControlBuffer(): {
  observeSocket(openedAtMs?: number): void;
  append(payload: unknown, atMs?: number, openedAtMs?: number): void;
  reset(): void;
  snapshot(documentStartedAtMs?: number): Pick<MessagingObservation, 'webSocketObserved' | 'controls' | 'controlsTruncated'>;
};
