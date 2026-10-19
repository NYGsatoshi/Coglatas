import type { Page } from '@playwright/test';
export function installChromiumOriginGuard(page: Page, expectedOrigin: string, onViolation: () => void): Promise<void>;
