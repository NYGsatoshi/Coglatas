/**
 * Return a bounded Playwright CLI worker override only for the complete
 * GitHub Actions *static* Angular suite consuming a prebuilt frontend.
 *
 * Project-wide playwright.config.ts intentionally stays at workers: 1:
 * real-backend, compatibility, public HTTPS, focused and local runs are
 * not qualified for extra parallelism. Never alter retries or test selection.
 */
export function staticPlaywrightWorkerArgs(env = {}, passthrough = []) {
  if (!Array.isArray(passthrough) || passthrough.length !== 0) {
    return [];
  }

  if (
    env?.CI !== 'true' ||
    env?.PLAYWRIGHT_SKIP_BUILD !== '1' ||
    env?.COGLATAS_PLAYWRIGHT_REUSE_HOST_BUILD !== 'true' ||
    env?.COGLATAS_STATIC_PLAYWRIGHT_WORKERS !== '2' ||
    env?.PLAYWRIGHT_BASE_URL ||
    env?.COGLATAS_COMPAT_CRITICAL === '1' ||
    env?.COGLATAS_REAL_BACKEND_SMOKE === '1' ||
    env?.COGLATAS_PUBLIC_HTTPS_SMOKE === '1'
  ) {
    return [];
  }

  return ['--workers=2'];
}
