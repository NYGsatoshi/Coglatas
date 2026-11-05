/**
 * Bounded opt-in Playwright sharding for a future independently aggregated CI.
 * The protected full frontend-test remains unchanged without opt-in.
 */
export function staticPlaywrightShardArgs(env = {}, passthrough = []) {
  const requested = env?.COGLATAS_STATIC_PLAYWRIGHT_SHARD;
  if (requested === undefined || requested === '') {
    // An enabled authorization without a shard must not silently run the full suite.
    const auth = env?.COGLATAS_STATIC_PLAYWRIGHT_SHARD_AUTH;
    if (auth !== undefined && auth !== '' && auth !== '0') {
      throw new Error('Partial Playwright shard is not permitted in this runtime.');
    }
    return [];
  }
  const eligible = Array.isArray(passthrough) &&
    passthrough.length === 0 &&
    env.CI === 'true' &&
    env.PLAYWRIGHT_SKIP_BUILD === '1' &&
    env.COGLATAS_PLAYWRIGHT_REUSE_HOST_BUILD === 'true' &&
    env.COGLATAS_STATIC_PLAYWRIGHT_WORKERS === '2' &&
    env.COGLATAS_STATIC_PLAYWRIGHT_SHARD_AUTH === '1' &&
    !env.PLAYWRIGHT_BASE_URL &&
    env.COGLATAS_COMPAT_CRITICAL !== '1' &&
    env.COGLATAS_REAL_BACKEND_SMOKE !== '1' &&
    env.COGLATAS_PUBLIC_HTTPS_SMOKE !== '1';
  if (!eligible || (requested !== '1/2' && requested !== '2/2')) {
    throw new Error('Partial Playwright shard is not permitted in this runtime.');
  }
  return ['--shard=' + requested];
}
