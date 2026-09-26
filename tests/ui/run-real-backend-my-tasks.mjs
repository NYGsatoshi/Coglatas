import { buildPlaywrightGrep } from '../../scripts/ci/build-playwright-grep.mjs';
import { verifyPlaywrightRequiredTests } from '../../scripts/ci/verify-playwright-required-tests.mjs';

const manifestUrl = new URL('../../scripts/ci/real-backend-my-tasks-required-tests.txt', import.meta.url);
const specUrl = new URL('./real-backend-smoke.spec.ts', import.meta.url);
const junitUrl = new URL('../../test-results/playwright-results.xml', import.meta.url);

process.env.COGLATAS_REAL_BACKEND_SMOKE_GREP = await buildPlaywrightGrep(manifestUrl, {
  verifyPath: specUrl
});
process.env.COGLATAS_REAL_BACKEND_SMOKE_SCOPE = 'My Tasks / PR04 required set';

await import('./run-real-backend-smoke-compose.mjs');

if (!process.exitCode) {
  try {
    const result = await verifyPlaywrightRequiredTests(manifestUrl, junitUrl);
    console.log(
      `Real-backend My Tasks required-test verification passed: ${result.requiredCount} required tests; ${result.discoveredCaseCount} JUnit cases discovered.`
    );
  } catch (error) {
    console.error(error instanceof Error ? error.message : error);
    process.exitCode = 1;
  }
}
