import type { Page, Request } from '@playwright/test';
import type { ThreadTarget } from './thread-probe-diagnostics.mjs';
export interface ThreadObservation {
  events: { kind: string; request: number | null; status: number | null }[];
  truncated: boolean; orphanResponse: boolean; freshRequest: number | null;
  outcome: 'COMPLETED' | 'FAILED'; classification: 'UNKNOWN' | 'INCOMPLETE';
  actionFailure: string; failureTarget: ThreadTarget | null;
}
export function validateThreadObservation(value: unknown): ThreadObservation;
export function actionFailureKind(error: unknown): string;
export function observeThreadRequests(page: Page, matches: (request: Request) => boolean): {
  mark(kind: string): void; select(request: Request): void; selectAuthorized(request: Request): void;
  finish(outcome: 'COMPLETED' | 'FAILED', failureTarget?: ThreadTarget | null, actionFailure?: string): ThreadObservation;
};
export function withThreadProbeLifecycle<T>(run: () => Promise<T>, capture: (error: unknown) => Promise<void>,
  publish: () => Promise<void>, restore: () => Promise<unknown>, reportSecondary: (stage: 'CAPTURE' | 'PUBLISH' | 'RESTORE') => void): Promise<T>;
