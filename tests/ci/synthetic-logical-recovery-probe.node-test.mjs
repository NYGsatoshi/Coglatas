import assert from 'node:assert/strict';
import { createHash, randomBytes } from 'node:crypto';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { test } from 'node:test';
import { Actor, runProbe } from '../../scripts/ci/synthetic-logical-recovery-probe.mjs';

const hash = bytes => createHash('sha256').update(bytes).digest('hex');
const identity = (tenant, digit) => {
  const bytes = Buffer.from('Synthetic private file ' + digit + '\n');
  return { tenant, email: digit + '@example.test', userId: digit.repeat(8) + '-1111-1111-1111-' + digit.repeat(12),
    fileId: digit.repeat(8) + '-2222-2222-2222-' + digit.repeat(12), fileBytes: bytes.length, fileSha256: hash(bytes),
    denialMarkers: ['private/storage/' + digit, digit + '@example.test'], bytes };
};
const alpha = identity('security-alpha', 'a'), beta = identity('security-beta', 'b');
const revoked = identity('security-alpha', 'c');
const input = () => ({ password: randomBytes(32).toString('base64url'), actors: [alpha, beta], revoked });
const otherIdentityLeaks = new Map([
  ['alpha-user-id', JSON.stringify({ userId: alpha.userId })],
  ['alpha-email', JSON.stringify({ email: alpha.email })],
  ['beta-user-id', JSON.stringify({ userId: beta.userId.toUpperCase() })],
  ['beta-email', '{"email":"b\\u0040example.test"}'],
]);
const cookieMap = text => Object.fromEntries((text ?? '').split(';').map(pair => pair.trim()).filter(Boolean)
  .map(pair => [pair.slice(0, pair.indexOf('=')), pair.slice(pair.indexOf('=') + 1)]));

// This fixture checks the probe's acceptance and privacy oracles. It is not a
// Product backend, Data Protection, PostgreSQL or actual recovery qualification.
function fixture(fault, { leakAt } = {}) {
  const spec = input(), files = new Map([['/private/probe-input.json', JSON.stringify(spec)]]);
  const sessions = new Map(), deniedSessions = new Set(), calls = [], writes = [];
  let sequence = 0, deniedMeReads = 0;
  const response = (status, body, cookies = []) => {
    const headers = new Headers();
    for (const cookie of cookies) headers.append('Set-Cookie', cookie);
    return new Response(typeof body === 'string' || Buffer.isBuffer(body) ? body : JSON.stringify(body), { status, headers });
  };
  const actorFor = request => {
    const actor = sessions.get(request.cookies.Auth);
    return !deniedSessions.has(request.cookies.Auth) && actor?.tenant === request.tenant ? actor : null;
  };
  const requireCsrf = request => request.headers.get('X-Synthetic-Csrf') === request.cookies.Csrf + '-token';
  const csrf = () => {
    const cookie = 'csrf-' + ++sequence;
    return response(200, { headerName: 'X-Synthetic-Csrf', token: cookie + '-token' }, ['Csrf=' + cookie + '; Path=/']);
  };
  const login = request => {
    if (!requireCsrf(request)) return response(403, { error: 'csrf' });
    const actor = [...spec.actors, spec.revoked].find(candidate => candidate.email === request.body.email && candidate.tenant === request.tenant);
    if (!actor || request.body.password !== spec.password || fault === 'login-denied') return response(401, { error: 'denied' });
    const token = 'session-' + ++sequence + '=opaque'; sessions.set(token, actor);
    return response(200, {}, ['Auth=' + token + '; HttpOnly; Path=/']);
  };
  const me = request => {
    const actor = actorFor(request);
    if (!actor) deniedMeReads++;
    if (!actor && otherIdentityLeaks.has(fault) && (leakAt === undefined || deniedMeReads === leakAt))
      return response(401, otherIdentityLeaks.get(fault));
    if (!actor && fault === 'revoked-identity-leak') return response(401, { userId: revoked.userId, email: revoked.email });
    if (!actor) return response(401, { error: 'denied' });
    return response(200, { userId: fault === 'wrong-identity' ? revoked.userId : actor.userId });
  };
  const logout = request => {
    if (!requireCsrf(request)) return response(403, { error: 'csrf' });
    if (!actorFor(request)) return response(401, { error: 'denied' });
    if (fault === 'logout-denied') return response(503, { error: 'unavailable' });
    if (fault !== 'unrevoked-session') deniedSessions.add(request.cookies.Auth);
    return response(200, {}, ['Auth=; Max-Age=0; Path=/']);
  };
  const download = request => {
    const owner = spec.actors.find(actor => request.path === '/api/files/' + actor.fileId + '/download');
    if (!owner) return response(404, { error: 'not_found' });
    if (actorFor(request)?.userId === owner.userId) {
      if (fault === 'wrong-file-status') return response(404, { error: 'not_found' });
      if (fault === 'wrong-file-size') return response(200, owner.bytes.subarray(1));
      return response(200, fault === 'wrong-file-hash' ? Buffer.alloc(owner.bytes.length) : owner.bytes);
    }
    if (fault === 'foreign-bytes') return response(403, owner.bytes);
    if (fault === 'foreign-marker') return response(403, 'denied ' + owner.denialMarkers[0]);
    if (fault === 'foreign-success') return response(200, { error: 'denied' });
    return response(403, { error: 'denied' });
  };
  const routes = new Map([
    ['GET /api/security/csrf-token', csrf], ['POST /api/auth/login', login],
    ['GET /api/auth/me', me], ['POST /api/auth/logout', logout],
  ]);
  return {
    spec, files, calls, writes,
    io: {
      readFile(path, encoding) { assert.equal(encoding, 'utf8'); assert.ok(files.has(path)); return files.get(path); },
      writeFile(path, data, options) {
        assert.deepEqual(options, { flag: 'wx', mode: 0o600 }); assert.ok(!files.has(path));
        assert.ok(['/private/cookie-checkpoint.json', ...['checkpoint', 'verify', 'rollback'].map(phase => '/private/probe-' + phase + '.json')].includes(path));
        files.set(path, data); writes.push(path);
      },
    },
    async fetch(url, options) {
      assert.equal(new URL(url).origin, 'http://127.0.0.1:8080');
      assert.equal(options.redirect, 'manual'); assert.ok(options.signal instanceof AbortSignal);
      const headers = new Headers(options.headers), path = new URL(url).pathname;
      const request = { path, method: options.method, headers, cookies: cookieMap(headers.get('Cookie')),
        tenant: headers.get('X-Tenant-Slug'), body: options.body === undefined ? null : JSON.parse(options.body) };
      if (options.body !== undefined) assert.equal(headers.get('Content-Type'), 'application/json');
      calls.push(request);
      return (routes.get(options.method + ' ' + path) ?? download)(request);
    },
  };
}

async function withFetch(fetch, action) {
  const original = globalThis.fetch;
  try { globalThis.fetch = fetch; return await action(); }
  finally { globalThis.fetch = original; }
}

await test('actual probe phases check saved identities, both owners and foreign denials, revocation and fresh login/logout', async () => {
  const server = fixture();
  await withFetch(server.fetch, async () => {
    for (const phase of ['checkpoint', 'verify', 'rollback']) await runProbe(phase, server.io);
  });
  assert.equal(server.calls.filter(call => call.path === '/api/auth/login').length, 5);
  assert.equal(server.calls.filter(call => call.path === '/api/auth/logout').length, 3);
  assert.equal(server.calls.filter(call => call.path.includes('/download')).length, 12);
  for (const phase of ['checkpoint', 'verify', 'rollback']) {
    const receipt = JSON.parse(server.files.get('/private/probe-' + phase + '.json'));
    assert.deepEqual(receipt, { phase, status: 'PASS', actualActors: 3, currentCookieIdentities: 2,
      fileByteChecks: 2, crossTenantDenials: 2, revokedSessionDenied: true,
      freshLoginLogout: phase !== 'checkpoint', deploymentHttps: 'NOT_EVALUATED' });
    const serialized = JSON.stringify(receipt);
    for (const privateValue of [server.spec.password, alpha.email, alpha.fileId, alpha.bytes.toString(), 'session-'])
      assert.ok(!serialized.includes(privateValue));
  }
  assert.deepEqual(server.writes, ['/private/cookie-checkpoint.json', '/private/probe-checkpoint.json',
    '/private/probe-verify.json', '/private/probe-rollback.json']);
});

await test('a rejected login, wrong identity or accepted revoked cookie cannot publish a checkpoint PASS', async () => {
  for (const fault of ['login-denied', 'logout-denied', 'wrong-identity', 'unrevoked-session']) {
    const server = fixture(fault);
    await withFetch(server.fetch, () => assert.rejects(() => runProbe('checkpoint', server.io)));
    assert.ok(!server.files.has('/private/probe-checkpoint.json'));
  }
});

await test('missing or altered owned bytes and private content in a denied response cannot publish PASS', async () => {
  for (const fault of ['wrong-file-status', 'wrong-file-size', 'wrong-file-hash', 'foreign-bytes', 'foreign-marker', 'foreign-success']) {
    const server = fixture(fault);
    await withFetch(server.fetch, () => assert.rejects(() => runProbe('checkpoint', server.io)));
    assert.ok(!server.files.has('/private/probe-checkpoint.json'));
  }
});

await test('a denied revoked-session response containing its protected identity cannot publish PASS', async () => {
  const server = fixture('revoked-identity-leak');
  await withFetch(server.fetch, () => assert.rejects(() => runProbe('checkpoint', server.io)));
  assert.ok(!server.files.has('/private/probe-checkpoint.json'));
});

for (const fault of otherIdentityLeaks.keys()) {
  await test('a denied session response containing another known ' + fault + ' cannot publish PASS', async () => {
    const server = fixture(fault);
    await withFetch(server.fetch, () => assert.rejects(() => runProbe('checkpoint', server.io)));
    assert.ok(!server.files.has('/private/probe-checkpoint.json'));
  });
}

for (const [phase, leakAt] of [['checkpoint', 2], ['verify', 4], ['rollback', 6]]) {
  await test('known-identity protection reaches the final revoked check or fresh logout in ' + phase, async () => {
    const server = fixture('beta-email', { leakAt });
    await withFetch(server.fetch, async () => {
      for (const prior of ['checkpoint', 'verify', 'rollback']) {
        if (prior === phase) break;
        await runProbe(prior, server.io);
      }
      await assert.rejects(() => runProbe(phase, server.io));
    });
    assert.ok(!server.files.has('/private/probe-' + phase + '.json'));
  });
}

await test('a direct logout without additional identities retains protection for its own actor', async () => {
  const server = fixture('revoked-identity-leak'), actor = new Actor(revoked);
  await withFetch(server.fetch, async () => {
    await actor.login(server.spec.password);
    await assert.rejects(() => actor.logout());
  });
});

await test('denied identity checks retain safe empty, plain and JSON errors but reject raw, embedded, case-varied and JSON-escaped markers', async () => {
  for (const body of ['', 'Unauthenticated', '{"error":"userId and email are unavailable"}'])
    await withFetch(async () => new Response(body, { status: 401 }), () => new Actor(alpha).me(401));
  const escapedId = alpha.userId.replaceAll('a', '\\u0061');
  for (const body of [alpha.userId, 'denied ' + alpha.userId, alpha.email, alpha.email.toUpperCase(),
    JSON.stringify({ userId: alpha.userId.toUpperCase() }), '{"userId":"' + escapedId + '"}',
    '{"email":"a\\u0040example.test"}']) {
    await withFetch(async () => new Response(body, { status: 401 }),
      () => assert.rejects(() => new Actor(alpha).me(401)));
  }
  await withFetch(async () => Response.json({ userId: alpha.userId }), () => new Actor(alpha).me());
  await withFetch(async () => Response.json({ userId: alpha.userId.toUpperCase() }),
    () => assert.rejects(() => new Actor(alpha).me()));
  await withFetch(async () => new Response('Unavailable', { status: 503 }),
    () => assert.rejects(() => new Actor(alpha).me(401)));
  await withFetch(async () => Response.json({ error: 'unauthenticated', traceId: '99999999-9999-4999-8999-999999999999' }, { status: 401 }),
    () => new Actor(revoked).me(401, [alpha, beta, revoked]));
  await withFetch(async () => Response.json({ userId: revoked.userId }, { status: 401 }),
    () => assert.rejects(() => new Actor(revoked).me(401, [])));
});

await test('saved cookie identities must remain authenticated after restore rather than falling back to fresh login', async () => {
  const server = fixture();
  await withFetch(server.fetch, async () => {
    await runProbe('checkpoint', server.io);
    const state = JSON.parse(server.files.get('/private/cookie-checkpoint.json'));
    state.actors[1].Auth = 'not-an-authenticated-session';
    server.files.set('/private/cookie-checkpoint.json', JSON.stringify(state));
    const priorLogins = server.calls.filter(call => call.path === '/api/auth/login').length;
    await assert.rejects(() => runProbe('verify', server.io));
    assert.equal(server.calls.filter(call => call.path === '/api/auth/login').length, priorLogins);
    assert.ok(!server.files.has('/private/probe-verify.json'));
  });
});

await test('input phase, ordered tenant pair and synthetic actor identity are required before requests or receipt writes', async () => {
  for (const mutate of [value => { value.actors.pop(); }, value => { value.actors.reverse(); },
    value => { value.revoked.userId = 'invalid'; }, value => { value.actors[0].email = 'outside@invalid.example'; }]) {
    const server = fixture(), value = JSON.parse(server.files.get('/private/probe-input.json'));
    mutate(value); server.files.set('/private/probe-input.json', JSON.stringify(value));
    await withFetch(server.fetch, () => assert.rejects(() => runProbe('checkpoint', server.io)));
    assert.equal(server.calls.length, 0); assert.equal(server.writes.length, 0);
  }
  const server = fixture();
  await assert.rejects(() => runProbe('unrecognized', server.io));
  assert.equal(server.writes.length, 0);
});

await test('request processing retains cookie values, removes expired cookies and applies tenant, CSRF and JSON headers', async () => {
  const actor = new Actor(alpha, { Retained: 'opaque=value', Removed: 'old' });
  await withFetch(async (url, options) => {
    assert.equal(url, 'http://127.0.0.1:8080/api/example');
    assert.equal(options.method, 'POST'); assert.equal(options.redirect, 'manual');
    assert.equal(options.headers['X-Tenant-Slug'], alpha.tenant);
    assert.equal(options.headers.Cookie, 'Retained=opaque=value; Removed=old');
    assert.equal(options.headers['X-Synthetic-Csrf'], 'synthetic-token');
    assert.equal(options.headers['Content-Type'], 'application/json');
    assert.equal(options.body, '{"safe":true}');
    const headers = new Headers(); headers.append('Set-Cookie', 'Added=opaque=new; Path=/');
    headers.append('Set-Cookie', 'Removed=; Max-Age=0; Path=/');
    return new Response(null, { status: 204, headers });
  }, async () => {
    const response = await actor.request('/api/example', { method: 'POST', body: { safe: true },
      csrf: { headerName: 'X-Synthetic-Csrf', token: 'synthetic-token' } });
    assert.equal(response.status, 204); assert.equal(response.body.length, 0);
  });
  assert.deepEqual(actor.cookies, { Retained: 'opaque=value', Added: 'opaque=new' });
});

await test('non-API and absolute request paths are rejected before fetch', async () => {
  let calls = 0;
  await withFetch(async () => { calls++; throw new Error('Unexpected fetch'); }, async () => {
    for (const path of ['/outside', 'https://outside.invalid/api/example', '/api/http://outside.invalid'])
      await assert.rejects(() => new Actor(alpha).request(path));
  });
  assert.equal(calls, 0);
});

await test('CSRF response status, header syntax, nonempty token and parseability must be valid', async () => {
  for (const response of [new Response('{}', { status: 403 }), new Response('{'),
    Response.json({ headerName: 'bad header', token: 'token' }),
    Response.json({ headerName: 'X-Csrf', token: '' }), Response.json({ headerName: 'X-Csrf', token: 7 })]) {
    await withFetch(async () => response, () => assert.rejects(() => new Actor(alpha).csrf()));
  }
});

await test('response byte limit, malformed cookies and original fetch failures cannot be converted to success', async () => {
  await withFetch(async () => new Response(Buffer.alloc(1024 * 1024 + 1)),
    () => assert.rejects(() => new Actor(alpha).request('/api/example')));
  await withFetch(async () => new Response('closed', { headers: { 'Set-Cookie': 'not-a-cookie-pair' } }),
    () => assert.rejects(() => new Actor(alpha).request('/api/example')));
  const error = new Error('Synthetic network failure');
  await withFetch(async () => { throw error; },
    () => assert.rejects(() => new Actor(alpha).request('/api/example'), caught => caught === error));
});

await test('actual CLI rejects invalid phase or extra target arguments with only the original closed error', () => {
  const script = fileURLToPath(new URL('../../scripts/ci/synthetic-logical-recovery-probe.mjs', import.meta.url));
  for (const args of [[], ['unrecognized'], ['checkpoint', '--target=SYNTHETIC_PRIVATE_ARGUMENT']]) {
    const result = spawnSync(process.execPath, [script, ...args], { encoding: 'utf8' });
    assert.equal(result.status, 1); assert.equal(result.stdout, '');
    assert.equal(result.stderr, 'Synthetic logical recovery API probe failed.\n');
  }
});
