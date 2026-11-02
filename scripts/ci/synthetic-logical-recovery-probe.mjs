import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { readFileSync, writeFileSync, realpathSync } from 'node:fs';
import { pathToFileURL } from 'node:url';

// Run inside only the explicitly owned app's network namespace. Credentials and
// cookie state stay in this private mount, never in stdout or uploaded output.
const origin = 'http://127.0.0.1:8080';
const digest = bytes => createHash('sha256').update(bytes).digest('hex');

export class Actor {
  constructor(identity, cookies = {}) { this.identity = identity; this.cookies = { ...cookies }; }
  async request(path, { method = 'GET', body, csrf } = {}) {
    assert.ok(path.startsWith('/api/') && !path.includes('://'));
    const headers = { 'X-Tenant-Slug': this.identity.tenant,
      Cookie: Object.entries(this.cookies).map(([key, value]) => `${key}=${value}`).join('; ') };
    if (body !== undefined) headers['Content-Type'] = 'application/json';
    if (csrf) headers[csrf.headerName] = csrf.token;
    const response = await fetch(origin + path, { method, headers, redirect: 'manual',
      body: body === undefined ? undefined : JSON.stringify(body), signal: AbortSignal.timeout(15000) });
    for (const cookie of response.headers.getSetCookie()) {
      const pair = cookie.split(';', 1)[0], position = pair.indexOf('=');
      assert.ok(position > 0);
      const name = pair.slice(0, position), value = pair.slice(position + 1);
      if (value) this.cookies[name] = value; else delete this.cookies[name];
    }
    const chunks = []; let length = 0;
    for await (const chunk of response.body ?? []) {
      length += chunk.length; assert.ok(length <= 1024 * 1024); chunks.push(chunk);
    }
    return { status: response.status, body: Buffer.concat(chunks) };
  }
  async csrf() {
    const response = await this.request('/api/security/csrf-token');
    assert.equal(response.status, 200);
    const token = JSON.parse(response.body);
    assert.match(token.headerName, /^[A-Za-z][A-Za-z0-9-]{0,63}$/);
    assert.equal(typeof token.token, 'string'); assert.ok(token.token.length > 0);
    return token;
  }
  async login(password) {
    const response = await this.request('/api/auth/login', { method: 'POST', csrf: await this.csrf(),
      body: { email: this.identity.email, password } });
    assert.equal(response.status, 200); await this.me();
  }
  async me(status = 200) {
    const response = await this.request('/api/auth/me');
    assert.equal(response.status, status);
    if (status === 200) assert.equal(JSON.parse(response.body).userId, this.identity.userId);
  }
  async logout() {
    const response = await this.request('/api/auth/logout', { method: 'POST', csrf: await this.csrf() });
    assert.equal(response.status, 200); await this.me(401);
  }
  async ownFile() {
    const response = await this.request('/api/files/' + this.identity.fileId + '/download');
    assert.equal(response.status, 200);
    assert.equal(response.body.length, this.identity.fileBytes);
    assert.equal(digest(response.body), this.identity.fileSha256);
    return response.body;
  }
  async foreignFile(foreign, expectedBytes) {
    // Bind the denial oracle to bytes just downloaded by their rightful owner,
    // rather than accepting an error status that still returns private content.
    assert.ok(Buffer.isBuffer(expectedBytes) && expectedBytes.length > 0);
    assert.equal(expectedBytes.length, foreign.fileBytes);
    assert.equal(digest(expectedBytes), foreign.fileSha256);
    const response = await this.request('/api/files/' + foreign.fileId + '/download');
    assert.ok([400, 403, 404].includes(response.status));
    assert.ok(!response.body.includes(expectedBytes));
    const text = response.body.toString('utf8');
    const canary = expectedBytes.toString('utf8').trim();
    assert.ok(canary.length > 0);
    assert.ok(!text.includes(canary));
    for (const marker of foreign.denialMarkers) assert.ok(!text.includes(marker));
  }
}

async function main() {
  const [phase] = process.argv.slice(2);
  assert.equal(process.argv.length, 3);
  assert.ok(['checkpoint', 'verify', 'rollback'].includes(phase));
  const input = JSON.parse(readFileSync('/private/probe-input.json', 'utf8'));
  assert.equal(input.actors.length, 2);
  assert.deepEqual(input.actors.map(actor => actor.tenant), ['security-alpha', 'security-beta']);
  for (const actor of [...input.actors, input.revoked]) {
    assert.match(actor.userId, /^[a-f0-9-]{36}$/);
    assert.ok(actor.email.endsWith('@example.test'));
  }
  let actors, revoked;
  if (phase === 'checkpoint') {
    actors = input.actors.map(identity => new Actor(identity));
    for (const actor of actors) await actor.login(input.password);
    const disposable = new Actor(input.revoked);
    await disposable.login(input.password);
    revoked = { ...disposable.cookies };
    await disposable.logout();
    writeFileSync('/private/cookie-checkpoint.json', JSON.stringify({
      actors: actors.map(actor => actor.cookies), revoked,
    }), { flag: 'wx', mode: 0o600 });
  } else {
    const state = JSON.parse(readFileSync('/private/cookie-checkpoint.json', 'utf8'));
    actors = input.actors.map((identity, index) => new Actor(identity, state.actors[index]));
    revoked = state.revoked;
  }
  const ownedBytes = [];
  for (const actor of actors) { await actor.me(); ownedBytes.push(await actor.ownFile()); }
  await actors[0].foreignFile(input.actors[1], ownedBytes[1]);
  await actors[1].foreignFile(input.actors[0], ownedBytes[0]);
  await new Actor(input.revoked, revoked).me(401);
  if (phase !== 'checkpoint') {
    const fresh = new Actor(input.actors[0]);
    await fresh.login(input.password); await fresh.logout();
  }
  writeFileSync('/private/probe-' + phase + '.json', JSON.stringify({
    phase, status: 'PASS', actualActors: 3, currentCookieIdentities: 2,
    fileByteChecks: 2, crossTenantDenials: 2, revokedSessionDenied: true,
    freshLoginLogout: phase !== 'checkpoint', deploymentHttps: 'NOT_EVALUATED',
  }), { flag: 'wx', mode: 0o600 });
}

if (process.argv[1] && pathToFileURL(realpathSync(process.argv[1])).href === import.meta.url) {
  main().catch(() => { process.stderr.write('Synthetic logical recovery API probe failed.\n'); process.exitCode = 1; });
}
