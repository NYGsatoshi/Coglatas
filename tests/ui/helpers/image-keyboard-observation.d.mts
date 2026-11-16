import type { Page, TestInfo } from '@playwright/test';
export const imageKeyboardInputDirectory: string;
export const imageKeyboardOutputDirectory: string;
export const imageKeyboardByteLimit: number;
export function imageKeyboardFilename(profile: string, width: number, image: string, attempt: number): string;
export function validateImageKeyboardObservation(value: unknown): {
  schemaVersion: 1; scope: string; profile: string; width: number; image: string; attempt: number;
  bodyOutcome: 'FAILED' | 'COMPLETED'; captureState: 'CAPTURED' | 'UNAVAILABLE';
  classification: 'UNKNOWN' | 'INCOMPLETE'; events: Record<string, unknown>[]; truncated: boolean;
};
export function withImageKeyboardObservation<T>(body: () => Promise<T>, finalize: (outcome: 'FAILED' | 'COMPLETED') => Promise<void>): Promise<T>;
export function observeImageKeyboardScroll<T>(page: Page, testInfo: TestInfo, width: number, image: 'WIDE' | 'PORTRAIT', body: () => Promise<T>): Promise<T>;
