import path from 'node:path';

// The static CI server has no mutable asset pipeline during a run. Only
// Angular's explicitly fingerprinted JS/CSS can be reused in one browser
// context. HTML, mock API responses and non-fingerprinted assets stay fresh.
const fingerprintedAsset = /-[A-Z0-9]{8}\.(?:js|css)$/u;
const immutable = 'public, max-age=31536000, immutable';

export function cacheControlForStaticAsset(filePath, method = 'GET', env = process.env) {
  if (
    env.GITHUB_ACTIONS !== 'true' ||
    env.GITHUB_EVENT_NAME !== 'pull_request' ||
    env.GITHUB_BASE_REF !== 'SecurityTestImplimation' ||
    env.COGLATAS_STATIC_ASSET_CACHE_SHADOW !== '1' ||
    !['GET', 'HEAD'].includes(method)
  ) {
    return 'no-store';
  }

  return fingerprintedAsset.test(path.basename(filePath)) ? immutable : 'no-store';
}
