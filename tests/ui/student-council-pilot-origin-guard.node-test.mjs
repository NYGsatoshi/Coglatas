import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import test from 'node:test';
import { chromium } from '@playwright/test';
import { installChromiumOriginGuard } from './student-council-pilot-origin-guard.mjs';

// These loopback servers are transport regression fixtures only. They are not
// product APIs, a public deployment, or a Student Council qualification receipt.
test('Chromium guard blocks credential bodies on actual 307 and 308 redirect hops', async () => {
  const browser = await chromium.launch();
  let sourceOrigin;
  let sinkOrigin;
  let redirectStatus = 307;
  let sourcePosts = 0;
  let foreignBodies = 0;
  const sink = createServer(async (request, response) => {
    response.setHeader('Access-Control-Allow-Origin', sourceOrigin);
    response.setHeader('Access-Control-Allow-Credentials', 'true');
    response.setHeader('Access-Control-Allow-Headers', 'content-type');
    response.setHeader('Access-Control-Allow-Methods', 'POST, OPTIONS');
    if (request.method === 'OPTIONS') { response.writeHead(204).end(); return; }
    let body = '';
    for await (const chunk of request) body += chunk;
    if (request.method === 'POST' && body.includes('synthetic-redirect-password')) foreignBodies++;
    response.end('received');
  });
  const source = createServer(async (request, response) => {
    if (request.url === '/app/login') { response.setHeader('Content-Type', 'text/html'); response.end('<!doctype html><title>Synthetic login transport</title>'); return; }
    for await (const _ of request) { /* Consume the synthetic request without logging it. */ }
    if (request.method === 'POST') sourcePosts++;
    if (request.url === '/api/auth/login') { response.writeHead(redirectStatus, { Location: `${sinkOrigin}/credential-sink` }).end(); return; }
    response.end('same-origin-ok');
  });
  try {
    sinkOrigin = await listen(sink);
    sourceOrigin = await listen(source);
    for (const status of [307, 308]) {
      redirectStatus = status;
      // A positive counterexample proves the servers/browser really forward
      // the POST body through a redirect despite Playwright route interception.
      const unguarded = await browser.newContext();
      const unguardedPage = await unguarded.newPage();
      await unguarded.route('**/*', async (route) => {
        if (new URL(route.request().url()).origin === sourceOrigin) await route.continue();
        else await route.abort();
      });
      await unguardedPage.goto(`${sourceOrigin}/app/login`);
      const beforeCounterexample = foreignBodies;
      await loginRequest(unguardedPage);
      assert.equal(foreignBodies, beforeCounterexample + 1, 'The route-only counterexample must reach the foreign sink.');
      await unguarded.close();

      const context = await browser.newContext();
      const page = await context.newPage();
      let blocked = false;
      await installChromiumOriginGuard(page, sourceOrigin, () => { blocked = true; });
      await page.goto(`${sourceOrigin}/app/login`);
      const beforeGuarded = foreignBodies;
      const outcome = await loginRequest(page);
      assert.equal(outcome, 'blocked');
      assert.equal(blocked, true);
      assert.equal(foreignBodies, beforeGuarded, 'The foreign server must receive no guarded credential body.');
      const sameOrigin = await page.evaluate(async () => (await fetch('/same-origin-control', { method: 'POST', body: 'synthetic control' })).text());
      assert.equal(sameOrigin, 'same-origin-ok');
      await context.close();
    }
    assert.equal(sourcePosts, 6, 'Both guarded and route-only login attempts plus same-origin controls must reach the real source.');
  } finally {
    await browser.close();
    source.closeAllConnections(); sink.closeAllConnections();
    await Promise.all([close(source), close(sink)]);
  }
});

async function loginRequest(page) {
  return page.evaluate(() => new Promise((resolve) => {
    const xhr = new XMLHttpRequest();
    xhr.open('POST', '/api/auth/login');
    xhr.withCredentials = true;
    xhr.setRequestHeader('Content-Type', 'application/json');
    xhr.onload = () => resolve('received');
    xhr.onerror = () => resolve('blocked');
    xhr.send(JSON.stringify({ email: 'redirect-control@example.test', password: 'synthetic-redirect-password' }));
  }));
}
async function listen(server) {
  await new Promise((resolve) => server.listen(0, '127.0.0.1', resolve));
  return `http://127.0.0.1:${server.address().port}`;
}
async function close(server) { await new Promise((resolve) => server.close(resolve)); }
