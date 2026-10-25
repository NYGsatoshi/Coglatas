import type { Page, TestInfo } from '@playwright/test';
export const groupKeyboardProfiles: string[];
export const groupKeyboardInputDirectory: string;
export const groupKeyboardOutputDirectory: string;
export const groupKeyboardByteLimit: number;
export const groupKeyboardV1ByteLimit: number;
export function groupKeyboardFilename(profile: string, attempt: number): string;
export function validateGroupKeyboardObservation(value: unknown): {
  schemaVersion: 1 | 2; scope: string; profile: string; attempt: number; bodyOutcome: 'FAILED' | 'COMPLETED';
  captureState: 'CAPTURED' | 'UNAVAILABLE'; classification: 'UNKNOWN' | 'INCOMPLETE';
  events: Record<string, unknown>[]; truncated: boolean;
};
export function withGroupKeyboardObservation<T>(body: () => Promise<T>, finalize: (outcome: 'FAILED' | 'COMPLETED') => Promise<void>): Promise<T>;
export function observeGroupKeyboardCase<T>(page: Page, testInfo: TestInfo, body: () => Promise<T>): Promise<T>;
