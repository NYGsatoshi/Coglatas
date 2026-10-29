import type { FullProject } from '@playwright/test';
export const mobileMvp0Project: string;
export const mobileMvp0Title: string;
export const mobileMvp0Device: { viewport: { width: number; height: number }; screen: { width: number; height: number }; isMobile: boolean; hasTouch: boolean; deviceScaleFactor: number };
export function assertMobileMvp0Profile(project: Pick<FullProject, 'name' | 'use'>, retry: number, enabled?: string): void;
