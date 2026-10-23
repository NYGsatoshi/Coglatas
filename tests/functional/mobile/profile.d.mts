import type { BrowserContextOptions, FullProject } from '@playwright/test';
export const mobileProject: string;
export function mobileDeviceOptions(projectName: string): Pick<BrowserContextOptions, 'viewport' | 'screen' | 'isMobile' | 'hasTouch' | 'deviceScaleFactor'>;
export function assertMobileProfile(browserName: string, project: Pick<FullProject, 'name' | 'use'>, enabled?: string): void;
export const mobileOwners: Readonly<Record<string, readonly string[]>>;
export function validateMobileOwners(records: unknown[]): unknown[];
