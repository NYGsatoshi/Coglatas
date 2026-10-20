export const MESSAGING_OBSERVATION_KEY: string;
export const MESSAGING_EVENT_LIMIT: number;
export interface MessagingObservation {
  documentStartedAtMs: number;
  storageReadable: boolean;
  storageWritable: boolean;
  events: { atMs: number; kind: string; composerPresent: boolean; draftEnabled: boolean; sendEnabled: boolean; draftFocused: boolean; connection: string | null }[];
  eventsTruncated: number;
  webSocketObserved: boolean;
  controls: { atMs: number; openedAtMs: number; kind: string }[];
  controlsTruncated: number;
}
export function validateMessagingObservation(value: unknown): MessagingObservation | null;
export function installMessagingObservation(key: string): void;
export function createMessagingControlBuffer(): {
  observeSocket(openedAtMs?: number): void;
  append(payload: unknown, atMs?: number, openedAtMs?: number): void;
  reset(): void;
  snapshot(documentStartedAtMs?: number): Pick<MessagingObservation, 'webSocketObserved' | 'controls' | 'controlsTruncated'>;
};
