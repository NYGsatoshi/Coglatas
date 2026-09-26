import { test } from '@playwright/test';

test.describe.skip('legacy static SPA Playwright coverage (non-P0)', () => {
  test('obsolete after the MVP-A P0 Angular frontend migration', async () => {
    // The previous tests targeted the removed vanilla JavaScript SPA in
    // src/Coglatas.Web/wwwroot. These expectations are intentionally not
    // authoritative for MVP-A P0; Angular-facing coverage lives in this suite.
  });
});
