import path from 'node:path';

// The static CI server has no mutable asset pipeline during a run. Only
// Angular's explicitly fingerprinted JS/CSS can be reused in one browser
// context. HTML, mock API responses and non-fingerprinted assets stay fresh.
// Angular's eight-character bundle fingerprints are mixed-case base64url-like
// identifiers. Constrain the *whole filename* to known emitted bundle stems,
// never arbitrary user/files or API paths, even when the suffix looks hashed.
const fingerprintedAsset = /^(?:main|styles|polyfills|runtime|scripts|chunk)-[A-Za-z0-9_-]{8}\.(?:js|css)$/u;
const immutable = 'public, max-age=31536000, immutable';

export function staticAssetCacheShadowEligible(env = process.env) {
  return env.GITHUB_ACTIONS === 'true' &&
    env.GITHUB_EVENT_NAME === 'pull_request' &&
    env.GITHUB_BASE_REF === 'SecurityTestImplimation' &&
    env.COGLATAS_STATIC_ASSET_CACHE_SHADOW === '1';
}

export function cacheControlForStaticAsset(filePath, method = 'GET', env = process.env) {
  if (!staticAssetCacheShadowEligible(env) || !['GET', 'HEAD'].includes(method)) {
    return 'no-store';
  }

  return fingerprintedAsset.test(path.basename(filePath)) ? immutable : 'no-store';
}
