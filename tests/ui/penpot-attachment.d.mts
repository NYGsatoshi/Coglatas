import type { Page, TestInfo } from '@playwright/test';

export declare function attachPenpotEvidence(
  page: Page,
  testInfo: TestInfo,
  fileName: string,
  layout: Record<string, unknown>,
  directory?: string,
): Promise<void>;
