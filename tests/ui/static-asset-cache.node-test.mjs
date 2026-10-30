import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { once } from 'node:events';
import { mkdtemp, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import test from 'node:test';

import { cacheControlForStaticAsset, staticAssetCacheShadowEligible } from './static-asset-cache.mjs';

const root = path.dirname(fileURLToPath(import.meta.url));
const serverFile = path.join(root, 'serve-static.mjs');
const immutable = 'public, max-age=31536000, immutable';
const enabled = {
  GITHUB_ACTIONS: 'true',
  GITHUB_EVENT_NAME: 'pull_request',
  GITHUB_BASE_REF: 'SecurityTestImplimation',
  COGLATAS_STATIC_ASSET_CACHE_SHADOW: '1'
};

test('fingerprinted JS/CSS only: no regression in HTML, JSON, unhashed or unsafe methods', () => {
  assert.equal(staticAssetCacheShadowEligible(enabled), true);
  assert.equal(cacheControlForStaticAsset('/work/main-A1B2C3D4.js', 'GET', enabled), immutable);
  assert.equal(cacheControlForStaticAsset('/work/styles-1234ABCD.css', 'HEAD', enabled), immutable);
  for (const file of ['/work/index.html', '/work/main.js', '/work/app-ABCDEFGH.json',
                      '/work/app-abcdefgh.js', '/work/logo-ABCDEFGH.svg']) {
    assert.equal(cacheControlForStaticAsset(file, 'GET', enabled), 'no-store', file);
  }
  for (const method of ['POST', 'PUT', 'DELETE']) {
    assert.equal(cacheControlForStaticAsset('/work/main-A1B2C3D4.js', method, enabled), 'no-store');
  }
});

test('independent flag and exact event/base fail closed', () => {
  for (const [key, variants] of Object.entries({
    GITHUB_ACTIONS: ['false', ''],
    GITHUB_EVENT_NAME: ['push', 'workflow_dispatch', 'pull_request_target', ''],
    GITHUB_BASE_REF: ['main', 'securitytestimplimation', 'SecurityTestImplementation', ''],
    COGLATAS_STATIC_ASSET_CACHE_SHADOW: ['0', '', 'true', '2']
  })) {
    for (const value of variants) {
      const fixture = { ...enabled, [key]: value };
      assert.equal(staticAssetCacheShadowEligible(fixture), false, `${key}=${value}`);
      assert.equal(cacheControlForStaticAsset('/work/main-A1B2C3D4.js', 'GET', fixture), 'no-store',
        `${key}=${value}`);
    }
  }
});

async function withStaticServer(env, callback) {
  const temp = await mkdtemp(path.join(tmpdir(), 'coglatas-static-cache-'));
  await writeFile(path.join(temp, 'index.html'), '<!doctype html><title>synthetic</title>');
  await writeFile(path.join(temp, 'main-A1B2C3D4.js'), 'window.synthetic = true;');
  await writeFile(path.join(temp, 'main.js'), 'window.synthetic = false;');
  const child = spawn(process.execPath, [serverFile, '--port', '0'], {
    cwd: root,
    env: {
      ...process.env,
      ...env,
      PLAYWRIGHT_STATIC_ROOT: temp,
      PLAYWRIGHT_HOST: '127.0.0.1',
    },
    stdio: ['ignore', 'pipe', 'pipe'],
  });
  let errors = '';
  child.stderr.setEncoding('utf8');
  child.stderr.on('data', chunk => { errors += chunk; });
  try {
    const port = await new Promise((resolve, reject) => {
      const timeout = setTimeout(() => reject(new Error('static server did not start')), 10000);
      let text = '';
      child.stdout.setEncoding('utf8');
      child.stdout.on('data', chunk => {
        text += chunk;
        const match = text.match(/Serving .* at http:\/\/127\.0\.0\.1:(\d+)/u);
        if (match) {
          clearTimeout(timeout);
          resolve(Number(match[1]));
        }
      });
      child.once('error', error => { clearTimeout(timeout); reject(error); });
      child.once('exit', code => {
        clearTimeout(timeout);
        reject(new Error(`static server exited early with code ${code}: ${errors}`));
      });
    });
    await callback(`http://127.0.0.1:${port}`);
  } finally {
    if (child.exitCode === null) {
      child.kill('SIGTERM');
      await Promise.race([
        once(child, 'exit').catch(() => undefined),
        new Promise(resolve => setTimeout(resolve, 2500)),
      ]);
      if (child.exitCode === null) child.kill('SIGKILL');
    }
    await rm(temp, { recursive: true, force: true });
  }
}

test('real static server opts in for hashed assets without caching HTML or mock APIs', async () => {
  await withStaticServer(enabled, async base => {
    const hashed = await fetch(`${base}/app/main-A1B2C3D4.js`);
    assert.equal(hashed.status, 200);
    assert.equal(hashed.headers.get('cache-control'), immutable);
    assert.equal(await hashed.text(), 'window.synthetic = true;');

    const head = await fetch(`${base}/app/main-A1B2C3D4.js`, { method: 'HEAD' });
    assert.equal(head.status, 200);
    assert.equal(head.headers.get('cache-control'), immutable);

    for (const route of ['/app/main.js', '/app/login', '/app/']) {
      const response = await fetch(`${base}${route}`);
      assert.equal(response.status, 200);
      assert.equal(response.headers.get('cache-control'), 'no-store', route);
      await response.arrayBuffer();
    }
    const api = await fetch(`${base}/api/auth/me`);
    assert.equal(api.status, 200);
    assert.notEqual(api.headers.get('cache-control'), immutable);
    const data = await api.json();
    assert.equal(data.userId, 'mock-user-a');
  });
});

test('real static server stays no-store when the flag is absent', async () => {
  await withStaticServer({ ...enabled, COGLATAS_STATIC_ASSET_CACHE_SHADOW: '0' }, async base => {
    const response = await fetch(`${base}/app/main-A1B2C3D4.js`);
    assert.equal(response.status, 200);
    assert.equal(response.headers.get('cache-control'), 'no-store');
    await response.arrayBuffer();
  });
});
