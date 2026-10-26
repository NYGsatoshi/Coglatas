import assert from 'node:assert/strict';
import test from 'node:test';
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { randomUUID } from 'node:crypto';
import { createRosterControl, rosterAssetPaths, validateRosterInvocation } from './run-real-backend-readonly-roster.mjs';

const sha = 'b'.repeat(40), token = 'a'.repeat(32), projectName = `coglatas-ui07-roster-${token}`;
const image = `sha256:${'c'.repeat(64)}`, label = 'coglatas.ui07-roster.run';
const uuid = 'd9000000-0000-4000-8000-000000000001';
const safeReceipt = { kind: 'UI07_READONLY_ROSTER', workspaceId: uuid, conversationId: uuid,
  ownerId: uuid, readerId: uuid, memberCount: 43, workspaceMemberCount: 43, tenantMemberCount: 43,
  readOnlyCount: 42, existingAuthorityUnchanged: true, sourceSha: sha, projectName,
  scope: 'DEFENSIVE_PERSISTED_ROLE_FIXTURE_NOT_ROLE_UPDATE_API_PROOF' };

function setup(reuse = false) {
  const token = randomUUID().replaceAll('-', '');
  const projectName = `coglatas-ui07-roster-${token}`;
  const directory = mkdtempSync(join(tmpdir(), 'coglatas-ui07-control-'));
  const calls = [];
  const labels = { 'com.docker.compose.project': projectName, [label]: token };
  const records = {
    container: ['postgres', 'migrate', 'app'].map((name, i) => ({ id: String(i + 1).repeat(64),
      name: `/${projectName}-${name}-1`, labels: { ...labels, 'com.docker.compose.service': name }, created: new Date().toISOString(),
      image, status: name === 'migrate' ? 'exited' : 'running', health: name === 'migrate' ? null : 'healthy', exitCode: 0 })),
    volume: [{ name: `${projectName}_real_backend_smoke_pgdata`,
      created: new Date().toISOString(), driver: 'local', scope: 'local', mountpoint: `/private/${token}`,
      labels: { ...labels, 'com.docker.compose.volume': 'real_backend_smoke_pgdata' } }],
    network: [{ id: 'd'.repeat(64), name: `${projectName}_default`,
      created: new Date().toISOString(), driver: 'bridge', scope: 'local',
      labels: { ...labels, 'com.docker.compose.network': 'default' } }],
  };
  let resources = false;
  let collision = false;
  let failRemoval = false;
  let servedFailure = false;
  const io = { projectName, environment: { SYNCFUSION_LICENSE: 'synthetic-fixture-license', COGLATAS_APP_IMAGE: 'synthetic:test' },
    async docker(args, options = {}) {
      calls.push({ args, input: options.input });
      let output = '';
      if (args[0] === 'image') output = JSON.stringify({ id: image, labels: { 'org.opencontainers.image.revision': sha } });
      else if (args.includes('ls') || args[0] === 'ps') {
        const kind = args[0] === 'ps' ? 'container' : args[0];
        output = collision ? 'collision' : resources ? records[kind].map(x => x.id || x.name).join('\n') : '';
      } else if (args[1] === 'inspect') {
        const record = records[args[0]].find(x => (x.id || x.name) === args.at(-1) || x.name === `/${args.at(-1)}`);
        if (!record) return { exitCode: 1, output: '', stdout: '', stderr: '' };
        output = JSON.stringify(record);
      } else if (args[0] === 'exec') {
        if (args.includes('psql')) output = 'synthetic SQL result';
        else if (args.includes('cat')) output = '<base href="/app/"><script src="main-ABC.js"></script><link href="styles-DEF.css">';
        else if (servedFailure) return { exitCode: 1, output: '', stdout: '', stderr: '' };
        else output = 'a'.repeat(64);
      }
      return { exitCode: failRemoval && args[1] === 'rm' ? 1 : 0, output, stdout: output, stderr: '' };
    } };
  const fixtureEnvironment = Object.fromEntries(['SOURCE_SHA', 'WORKSPACE_ID', 'CONVERSATION_ID', 'MEMBER_COUNT',
    'OWNER_ID', 'OWNER_EMAIL', 'OWNER_PASSWORD', 'READER_ID', 'READER_EMAIL', 'READER_PASSWORD']
    .map(name => [`COGLATAS_UI07_ROSTER_${name}`, name === 'SOURCE_SHA' ? sha : name === 'MEMBER_COUNT' ? '43' : 'synthetic-private-value']));
  const control = createRosterControl({ sourceSha: sha, token, projectName, directory, reuse,
    fixture: async ({ ownership, executeSql }) => {
      assert.equal(ownership.sourceBindingVerified, true);
      assert.equal(ownership.imageSourceSha, sha);
      const sql = await executeSql('synthetic-private-sql');
      assert.equal(sql.stdout, 'synthetic SQL result');
      return { receipt: { ...safeReceipt, projectName }, environment: fixtureEnvironment };
    } });
  let outputDirectory;
  const f = { control, io, calls, records, directory, projectName,
    resources: () => {
      resources = true;
      for (const values of Object.values(records)) for (const value of values) value.created = new Date().toISOString();
    }, collision: () => { collision = true; },
    failRemoval: () => { failRemoval = true; },
    servedFailure: () => { servedFailure = true; },
    async browser(status = 'PASS', count = 2, mutate = () => {}) {
      control.playwrightArgs(['-p', projectName, '-f', 'fixed.yml', 'run'], io.environment);
      records.container.push({ id: 'e'.repeat(64), name: `/${projectName}-browser`, created: new Date().toISOString(),
        labels: { ...labels, 'com.docker.compose.service': 'real-backend-playwright' }, image, status: 'exited', health: null, exitCode: 0 });
      if (!records.volume.some(value => value.name === `${projectName}_real_backend_smoke_node_modules`)) records.volume.push({ name: `${projectName}_real_backend_smoke_node_modules`, created: new Date().toISOString(),
        driver: 'local', scope: 'local', mountpoint: `/private/${token}/node`,
        labels: { ...labels, 'com.docker.compose.volume': 'real_backend_smoke_node_modules' } });
      outputDirectory = io.environment.COGLATAS_UI07_ROSTER_PRIVATE_OUTPUT_DIR;
      const attachments = join(outputDirectory, 'test', 'attachments');
      mkdirSync(attachments, { recursive: true });
      for (let index = 0; index < count; index++) {
        const project = index === 0 ? 'readonly-roster-desktop' : 'readonly-roster-mobile';
        const value = browserReceipt(project, status); mutate(value);
        writeFileSync(join(attachments, `readonly-roster-structural-receipt-json-${String(index + 1).repeat(40)}.json`), JSON.stringify(value));
      }
      return control.browserFinished(io);
    },
    receipt: () => JSON.parse(readFileSync(join(directory, 'execution.json'), 'utf8')),
    dispose: () => { rmSync(directory, { recursive: true, force: true }); if (outputDirectory) rmSync(outputDirectory, { recursive: true, force: true }); } };
  return f;
}

function browserReceipt(browserProject, status = 'PASS') {
  return { sourceSha: sha, browserProject, retry: 0, phase: status === 'PASS' ? 'ASSERTIONS_COMPLETE' : 'END_BOUNDARY', status,
    memberCount: 43, readOnly: true, canPost: false, canManageMembers: false,
    ownerPostStatus: 200, readerPostStatus: 403, readerManageStatus: 400, ownerContextClosed: true,
    metrics: { height: 100, scrollHeight: 1000, tabIndex: 0, focusableChildren: 0 },
    outer: { isAside: browserProject === 'readonly-roster-desktop', height: 200, scrollHeight: 2000 },
    end: { top: 900, max: 900 }, home: 0,
    viewport: browserProject === 'readonly-roster-desktop' ? { width: 1536, height: 600 } : { width: 320, height: 420 },
    failureGeometry: null };
}

function failureGeometry(viewport) {
  const node = () => ({ present: true, connected: true,
    rect: { x: -10, y: -20, width: 100, height: 20, top: -20, right: 90, bottom: 0, left: -10 },
    scroll: { top: 0, left: 0, height: 1000, width: 100, clientHeight: 100, clientWidth: 100 } });
  return { version: 1, activeKind: 'SUMMARY', viewport: { ...viewport }, scroll: { x: 0, y: 20 },
    ...Object.fromEntries(['summary', 'list', 'leave', 'firstName', 'firstStatus', 'lastName', 'lastStatus', 'outer', 'aside'].map(name => [name, node()])) };
}

test('closed wrapper requires exact clean source and rejects selection overrides', () => {
  const git = args => args[0] === 'rev-parse' ? sha : '';
  assert.equal(validateRosterInvocation({ TARGET_SHA: sha }, [], git), sha);
  for (const [environment, args, reader] of [
    [{ TARGET_SHA: 'c'.repeat(40) }, [], git], [{ TARGET_SHA: sha }, ['--grep', 'one'], git],
    [{ TARGET_SHA: sha }, [], args => args[0] === 'rev-parse' ? sha : ' M tracked.ts'],
    [{ TARGET_SHA: sha, REAL_BACKEND_SMOKE_COMPOSE_PROJECT_NAME: 'existing' }, [], git],
    [{ TARGET_SHA: sha, COGLATAS_FCI04_ONLY: '1' }, [], git],
    [{ TARGET_SHA: sha, COGLATAS_UI07_ROSTER_OWNER_PASSWORD: 'external' }, [], git],
    [{ TARGET_SHA: sha, COGLATAS_REUSE_PREBUILT_DOTNET_BUILD: 'true' }, [], git],
  ]) assert.throws(() => validateRosterInvocation(environment, args, reader), /CONTRACT_REJECTED/);
});

test('served asset plan maps the approved Angular base to its unchanged container payload', () => {
  const plan = rosterAssetPaths('<base href="/app/"><script src="main-ABC.js"></script>' +
    '<link href="assets/vendor/base.css"><script src="/app/chunk-DEF.js"></script><script src="/api/ui/runtime-config.js"></script>');
  assert.deepEqual(plan, [
    { filePath: 'assets/vendor/base.css', requestPath: '/app/assets/vendor/base.css' },
    { filePath: 'chunk-DEF.js', requestPath: '/app/chunk-DEF.js' },
    { filePath: 'index.html', requestPath: '/app/index.html' },
    { filePath: 'main-ABC.js', requestPath: '/app/main-ABC.js' },
  ]);
  for (const html of ['<script src="main.js"></script>', '<base href="/"><script src="main.js"></script>',
    '<base href="/app/"><script src="https://example.test/main.js"></script>',
    '<base href="/app/"><script src="../main.js"></script>',
    '<base href="/app/"><script src="/other/main.js"></script>']) {
    assert.throws(() => rosterAssetPaths(html), /CONTRACT_REJECTED/);
  }
});

test('preexisting resource collision never grants cleanup ownership', async () => {
  const f = setup();
  try {
    f.collision();
    await assert.rejects(f.control.admit(f.io));
    assert.equal((await f.control.cleanup(f.io, false)).exitCode, 1);
    assert.equal(f.receipt().cleanupState, 'NOT_ADMITTED');
    assert.equal(f.calls.some(x => ['rm', 'stop', 'down', 'prune', 'build'].some(v => x.args.includes(v))), false);
  } finally { f.dispose(); }
});

test('fresh labeled resources and actual image proof precede private fixture and scoped cleanup', async () => {
  const f = setup();
  try {
    await f.control.admit(f.io);
    f.resources();
    await f.control.capture(f.io);
    await f.control.prepare(f.io);
    const sqlCall = f.calls.find(x => x.args[0] === 'exec' && x.args.includes('psql'));
    assert.equal(sqlCall.input, 'synthetic-private-sql');
    assert.equal(sqlCall.args.some(x => x.includes('private-sql')), false);
    await f.browser();
    const args = f.calls.find(x => x.args[0] === 'exec' && x.args.includes('psql')).args;
    assert.ok(!args.includes('synthetic-private-sql'));
    assert.equal((await f.control.cleanup(f.io, true)).exitCode, 0);
    assert.equal(f.receipt().outcome, 'PASS');
    assert.equal(f.receipt().cleanupState, 'REMOVED_OWNED_COMPUTE_RETAINED_VOLUMES');
    assert.equal(f.receipt().servedFrontend.containerPayloadMatchedHttp, true);
    const hashCalls = f.calls.filter(value => value.args[0] === 'exec' && value.args.includes('sh'));
    assert.ok(hashCalls.length > 0 && hashCalls.every(value => value.args.at(-1).startsWith('/app/')));
    assert.equal(f.calls.some(x => x.args[0] === 'volume' && x.args[1] === 'rm'), false);
    assert.equal(f.calls.some(x => x.args.includes('down') || x.args.includes('prune')), false);
    assert.equal(JSON.stringify(f.receipt()).includes('synthetic-private-sql'), false);
    assert.equal(JSON.stringify(f.receipt()).includes('synthetic-fixture-license'), false);
  } finally { f.dispose(); }
});

test('failure stops only verified owned containers and preserves volumes for inspection', async () => {
  const f = setup();
  try {
    await f.control.admit(f.io); f.resources(); await f.control.capture(f.io);
    assert.equal((await f.control.cleanup(f.io, false)).exitCode, 1);
    assert.equal(f.calls.filter(x => x.args[1] === 'stop').length, 3);
    assert.equal(f.calls.some(x => x.args[1] === 'rm'), false);
    assert.equal(f.receipt().cleanupState, 'STOPPED_OWNED_RETAINED_FAILURE');
    assert.equal(f.receipt().outcome, 'FAIL');
  } finally { f.dispose(); }
});

test('changed ownership label denies every cleanup operation', async () => {
  const f = setup();
  try {
    await f.control.admit(f.io); f.resources(); await f.control.capture(f.io);
    f.records.volume[0].labels[label] = 'foreign';
    assert.equal((await f.control.cleanup(f.io, true)).exitCode, 1);
    assert.equal(f.calls.some(x => x.args[1] === 'rm' || x.args[1] === 'stop'), false);
    assert.equal(f.receipt().cleanupState, 'PARTIAL_OR_UNVERIFIED_RETAINED');
  } finally { f.dispose(); }
});

test('cleanup failure and empty admission never retain PASS', async () => {
  for (const partial of [true, false]) {
    const f = setup();
    try {
      await f.control.admit(f.io);
      if (partial) { f.resources(); await f.control.capture(f.io); f.failRemoval(); }
      assert.equal((await f.control.cleanup(f.io, true)).exitCode, 1);
      assert.equal(f.receipt().outcome, 'FAIL');
      assert.equal(f.receipt().cleanupState, 'PARTIAL_OR_UNVERIFIED_RETAINED');
    } finally { f.dispose(); }
  }
});

test('tag label or running-image mismatch cannot manufacture source binding', async () => {
  const f = setup();
  try {
    const docker = f.io.docker;
    f.io.docker = async (args, options) => args[0] === 'image'
      ? { exitCode: 0, output: JSON.stringify({ id: image, labels: { 'org.opencontainers.image.revision': 'e'.repeat(40) } }) }
      : docker(args, options);
    await assert.rejects(f.control.admit(f.io));
    assert.equal((await f.control.cleanup(f.io, false)).exitCode, 1);
    assert.equal(f.receipt().sourceBindingVerified, false);
  } finally { f.dispose(); }
  const running = setup();
  try {
    await running.control.admit(running.io); running.resources();
    await running.control.capture(running.io);
    running.records.container[2].image = `sha256:${'f'.repeat(64)}`;
    await assert.rejects(running.control.prepare(running.io));
    assert.equal(running.calls.some(x => x.args[0] === 'exec'), false);
  } finally { running.dispose(); }
});

test('late unknown IDs and changed volume birth metadata are never adopted during cleanup', async () => {
  for (const kind of ['late-container', 'volume-replacement']) {
    const f = setup();
    try {
      await f.control.admit(f.io); f.resources(); await f.control.capture(f.io);
      if (kind === 'late-container') f.records.container.push({ ...f.records.container[0], id: 'f'.repeat(64) });
      else f.records.volume[0].created = new Date(Date.now() + 1000).toISOString();
      assert.equal((await f.control.cleanup(f.io, true)).exitCode, 1);
      assert.equal(f.calls.some(x => x.args[1] === 'rm' || x.args[1] === 'stop'), false);
      assert.equal(f.receipt().cleanupState, 'PARTIAL_OR_UNVERIFIED_RETAINED');
    } finally { f.dispose(); }
  }
});

test('served asset mismatch prevents fixture SQL and stronger source assurance', async () => {
  const f = setup();
  try {
    await f.control.admit(f.io); f.resources(); await f.control.capture(f.io); f.servedFailure();
    await assert.rejects(f.control.prepare(f.io));
    assert.equal(f.calls.some(x => x.args.includes('psql')), false);
    await f.control.cleanup(f.io, false);
    assert.equal(f.receipt().sourceBindingVerified, false);
    assert.equal(f.receipt().servedFrontend, null);
  } finally { f.dispose(); }
});

test('two profile receipts are mandatory and bounded failure evidence remains unqualified', async () => {
  for (const [status, count] of [['PASS', 1], ['NOT_VERIFIED', 2]]) {
    const f = setup();
    try {
      await f.control.admit(f.io); f.resources(); await f.control.capture(f.io); await f.control.prepare(f.io);
      await assert.rejects(f.browser(status, count));
      await f.control.cleanup(f.io, false);
      assert.equal(f.receipt().outcome, 'FAIL');
      assert.equal(f.receipt().browserReceipts.length, count);
      assert.ok(f.receipt().browserReceipts.every(value => Object.keys(value).length === 19));
    } finally { f.dispose(); }
  }
});

test('private fields, wrong source and retries reject without publishing their payload', async () => {
  for (const mutate of [value => { value.privateBody = 'must-not-publish'; }, value => { value.sourceSha = 'e'.repeat(40); },
    value => { value.retry = 1; }, value => { value.metrics.height = null; }, value => { value.phase = 'UNREGISTERED_STAGE'; }]) {
    const f = setup();
    try {
      await f.control.admit(f.io); f.resources(); await f.control.capture(f.io); await f.control.prepare(f.io);
      await assert.rejects(f.browser('PASS', 2, mutate));
      await f.control.cleanup(f.io, false);
      assert.equal(f.receipt().outcome, 'FAIL');
      assert.deepEqual(f.receipt().browserReceipts, []);
      assert.equal(JSON.stringify(f.receipt()).includes('must-not-publish'), false);
    } finally { f.dispose(); }
  }
});

test('finite End, Home and final failure stages remain diagnostic evidence without qualification', async () => {
  for (const phase of ['END_LEAVE_FOCUS', 'END_LAST_NAME_VISIBILITY', 'END_LAST_STATUS_VISIBILITY', 'END_RETURN_FOCUS',
    'HOME_RETURN_FOCUS', 'HOME_FIRST_NAME_VISIBILITY', 'HOME_FIRST_STATUS_VISIBILITY', 'FINAL_PAGE_ERRORS', 'FINAL_HTTP_STATUSES']) {
    const f = setup();
    try {
      await f.control.admit(f.io); f.resources(); await f.control.capture(f.io); await f.control.prepare(f.io);
      await assert.rejects(f.browser('NOT_VERIFIED', 2, value => { value.phase = phase; }));
      await f.control.cleanup(f.io, false);
      const receipt = f.receipt();
      assert.equal(receipt.outcome, 'FAIL');
      assert.equal(receipt.browserReceipts.length, 2);
      assert.ok(receipt.browserReceipts.every(value => value.phase === phase && value.status === 'NOT_VERIFIED'));
    } finally { f.dispose(); }
  }
});

test('closed numeric failure geometry retains clipping and focus evidence without qualification', async () => {
  const f = setup();
  try {
    await f.control.admit(f.io); f.resources(); await f.control.capture(f.io); await f.control.prepare(f.io);
    await assert.rejects(f.browser('NOT_VERIFIED', 2, value => {
      value.phase = 'HOME_FIRST_NAME_VISIBILITY'; value.failureGeometry = failureGeometry(value.viewport);
      value.failureGeometry.leave = { present: false, connected: false, rect: null, scroll: null };
      value.failureGeometry.outer.rect.top = 40;
      value.failureGeometry.outer.scroll.top = 120;
      value.failureGeometry.aside.scroll.top = 90;
      value.failureGeometry.viewport.width = 980;
    }));
    await f.control.cleanup(f.io, false);
    assert.equal(f.receipt().outcome, 'FAIL');
    assert.equal(f.receipt().cleanupState, 'STOPPED_OWNED_RETAINED_FAILURE');
    assert.equal(f.receipt().browserReceipts.length, 2);
    assert.ok(f.receipt().browserReceipts.every(value => value.failureGeometry.firstName.rect.y === -20 &&
      value.failureGeometry.activeKind === 'SUMMARY' && value.failureGeometry.leave.present === false &&
      value.failureGeometry.outer.rect.top === 40 && value.failureGeometry.outer.scroll.top === 120 &&
      value.failureGeometry.aside.scroll.top === 90 && value.failureGeometry.viewport.width === 980 &&
      value.viewport.width === (value.browserProject === 'readonly-roster-desktop' ? 1536 : 320)));
  } finally { f.dispose(); }
});

test('unknown, textual, unbounded or contradictory geometry is rejected without publication', async () => {
  for (const mutate of [
    geometry => { geometry.privateBody = 'must-not-publish'; },
    geometry => { geometry.summary.text = 'must-not-publish'; },
    geometry => { geometry.summary.rect.url = 'must-not-publish'; },
    geometry => { geometry.activeKind = 'must-not-publish'; },
    geometry => { geometry.version = 2; },
    geometry => { geometry.viewport.width = 0; },
    geometry => { geometry.viewport.height = 10_000_001; },
    geometry => { geometry.firstName.rect.y = 'must-not-publish'; },
    geometry => { geometry.firstName.rect.y = Infinity; },
    geometry => { geometry.scroll.y = 10_000_001; },
    geometry => { geometry.list.scroll.clientHeight = -1; },
    geometry => { geometry.lastStatus.present = false; },
    geometry => { delete geometry.outer; },
    geometry => { geometry.outer.scroll.selector = 'must-not-publish'; },
    geometry => { geometry.aside.rect.attributes = 'must-not-publish'; },
  ]) {
    const f = setup();
    try {
      await f.control.admit(f.io); f.resources(); await f.control.capture(f.io); await f.control.prepare(f.io);
      await assert.rejects(f.browser('NOT_VERIFIED', 2, value => {
        value.failureGeometry = failureGeometry(value.viewport); mutate(value.failureGeometry);
      }));
      await f.control.cleanup(f.io, false);
      assert.equal(f.receipt().outcome, 'FAIL');
      assert.deepEqual(f.receipt().browserReceipts, []);
      assert.equal(JSON.stringify(f.receipt()).includes('must-not-publish'), false);
    } finally { f.dispose(); }
  }
  const qualified = setup();
  try {
    await qualified.control.admit(qualified.io); qualified.resources(); await qualified.control.capture(qualified.io); await qualified.control.prepare(qualified.io);
    await assert.rejects(qualified.browser('PASS', 2, value => { value.failureGeometry = failureGeometry(value.viewport); }));
    await qualified.control.cleanup(qualified.io, false);
    assert.deepEqual(qualified.receipt().browserReceipts, []);
    assert.equal(qualified.receipt().outcome, 'FAIL');
  } finally { qualified.dispose(); }
});

test('Main reuse requires observed engine identity and matching producer/source/build stamps', async () => {
  for (const altered of [false, true]) {
    const f = setup(true);
    try {
      const archiveDirectory = join(f.directory, 'producer');
      mkdirSync(archiveDirectory);
      const stamp = join(archiveDirectory, 'dotnet-build-sha');
      for (const [name, value] of [['source-sha', altered ? 'e'.repeat(40) : sha],
        ['runtime-image-name', 'synthetic:test'], ['runtime-image-id', image], ['dotnet-build-sha', sha]]) {
        writeFileSync(join(archiveDirectory, name), value + '\n');
      }
      writeFileSync(join(archiveDirectory, 'runtime-image.tar.gz'), 'synthetic archive');
      Object.assign(f.io.environment, { COGLATAS_REUSE_PREBUILT_APP_SOURCE_SHA: sha,
        COGLATAS_REUSE_PREBUILT_APP_IMAGE_ID: image, COGLATAS_REUSE_PREBUILT_APP_ENGINE_ID: image,
        COGLATAS_REUSE_PREBUILT_APP_IMAGE_ARCHIVE: join(archiveDirectory, 'runtime-image.tar.gz'),
        COGLATAS_REUSE_PREBUILT_DOTNET_STAMP: stamp });
      if (altered) {
        await assert.rejects(f.control.admit(f.io));
        assert.equal((await f.control.cleanup(f.io, false)).exitCode, 1);
        assert.equal(f.receipt().sourceBindingVerified, false);
      } else {
        await f.control.admit(f.io); f.resources(); await f.control.capture(f.io); await f.control.prepare(f.io);
        assert.equal(f.receipt().sourceBindingVerified, true);
        assert.equal(f.receipt().expectedEngineImageId, image);
      }
      assert.equal(f.calls.some(x => x.args[0] === 'build'), false);
    } finally { f.dispose(); }
  }
});

test('an owned node volume created with the initial stack keeps its frozen birth identity', async () => {
  const f = setup();
  try {
    await f.control.admit(f.io); f.resources();
    f.records.volume.push({ ...f.records.volume[0], name: `${f.projectName}_real_backend_smoke_node_modules`,
      labels: { ...f.records.volume[0].labels, 'com.docker.compose.volume': 'real_backend_smoke_node_modules' } });
    await f.control.capture(f.io); await f.control.prepare(f.io); await f.browser();
    assert.equal((await f.control.cleanup(f.io, true)).exitCode, 0);
    assert.equal(f.receipt().outcome, 'PASS');
  } finally { f.dispose(); }
});

test('unavailable migration metadata preserves its finite capture phase without leaking errors or adopting cleanup resources', async () => {
  const f = setup();
  try {
    await f.control.admit(f.io); f.resources();
    const docker = f.io.docker;
    f.io.docker = async (args, options) => args[0] === 'container' && args[1] === 'inspect' && args.at(-1) === '2'.repeat(64)
      ? { exitCode: 1, output: 'private-container-diagnostic', stderr: 'private-container-diagnostic' }
      : docker(args, options);
    await assert.rejects(f.control.capture(f.io));
    f.control.failed();
    assert.equal((await f.control.cleanup(f.io, false)).exitCode, 1);
    const receipt = f.receipt();
    assert.equal(receipt.failurePhase, 'STACK_CAPTURE_CONTAINER');
    assert.equal(receipt.failureClass, 'DOCKER_COMMAND_FAILED');
    assert.equal(receipt.cleanupState, 'PARTIAL_OR_UNVERIFIED_RETAINED');
    assert.equal(receipt.ownedResources.container, 1);
    assert.equal(f.calls.some(x => x.args[1] === 'rm' || x.args[1] === 'stop'), false);
    assert.equal(JSON.stringify(receipt).includes('private-container-diagnostic'), false);
  } finally { f.dispose(); }
});
