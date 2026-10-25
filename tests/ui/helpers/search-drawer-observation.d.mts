import type { Page, TestInfo } from '@playwright/test';
export const searchDrawerInputDirectory: string;
export const searchDrawerOutputDirectory: string;
export const searchDrawerByteLimit: number;
export function searchDrawerFilename(profile: string, attempt: number): string;
export function validateSearchDrawerObservation(value: unknown): {
  schemaVersion: 1; scope: string; profile: string; attempt: number; bodyOutcome: 'FAILED' | 'COMPLETED';
  captureState: 'CAPTURED' | 'UNAVAILABLE'; classification: 'UNKNOWN' | 'INCOMPLETE';
  events: Record<string, unknown>[]; truncated: boolean;
};
export function withSearchDrawerObservation<T>(body: () => Promise<T>, finalize: (outcome: 'FAILED' | 'COMPLETED') => Promise<void>): Promise<T>;
export function observeSearchDrawerCase<T>(page: Page, testInfo: TestInfo, body: () => Promise<T>): Promise<T>;
