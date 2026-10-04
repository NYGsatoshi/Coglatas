import type { ConsoleMessage, Request, Response } from '@playwright/test';
export const OBSERVATION_ATTACHMENT: string;
export const PROJECTION_KEYS: readonly string[];
export interface BrowserObservation {
  schemaVersion: number;
  scope: string;
  network: object[];
  networkTruncated: number;
  consoleCounts: Record<string, number>;
  projection: Record<string, boolean | null>;
  projectionCheckpoint: string;
  structuralSnapshotState: string;
  structuralSnapshot: { name: string; sha256: string } | null;
}
export function createBrowserObservation(baseURL: string): {
  data: BrowserObservation;
  onRequest(request: Request): void;
  onResponse(response: Response): void;
  onRequestFailed(request: Request): void;
  onConsole(message: ConsoleMessage): void;
  onPageError(): void;
};
export function validateBrowserObservation(data: unknown): BrowserObservation;
export function structuralSnapshotHtml(data: unknown, identity?: Record<string, unknown>): string;
