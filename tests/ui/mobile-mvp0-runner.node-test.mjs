import { execFileSync } from 'node:child_process';
import { mkdtemp, mkdir, writeFile, realpath, rm } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import assert from 'node:assert/strict';
import { runMobileMvp0 } from './run-real-backend-mobile-mvp0.mjs';
import { mobileMvp0Project } from './mobile-mvp0-profile.mjs';
const sha = '1'.repeat(40), tree = '2'.repeat(40), image = 'sha256:' + '3'.repeat(64);
const dir = 'artifacts/mobile-mvp0', junit = 'test-results/playwright-results.xml';
function fixture() {
  const files = new Map(), calls = [], logs = [];
  const geometry = { width: 320, height: 800, screenWidth: 320, screenHeight: 800, scale: 1, touchPoints: 1 };
  const profiles = { project: mobileMvp0Project, retry: 0,
    actors: Object.fromEntries(['manager', 'recipient', 'third', 'newcomer'].map(name => [name, { ...geometry }])) };
  const io = {
    environment: { TARGET_SHA: sha, GITHUB_RUN_ID: '123', GITHUB_RUN_ATTEMPT: '1' }, args: ['node', 'runner'],
    cwdMatchesSource: () => true,
    existsSync: path => files.has(path), mkdirSync: () => calls.push('mkdir'),
    writeFileSync: (path, text, options) => { assert.equal(options.flag, 'wx'); assert.equal(files.has(path), false); files.set(path, text); },
    readFileSync: path => { if (!files.has(path)) throw new Error('missing'); return files.get(path); },
    execFileSync: (command, args) => {
      calls.push(command);
      if (command === 'docker') return image;
      if (args[0] === 'status') return '';
      return args[1] === 'HEAD' ? sha : tree;
    },
    buildPlaywrightGrep: async () => 'closed exact title',
    verifyPlaywrightRequiredTests: async () => { assert.equal(files.has(junit), true); return { requiredCount: 1, discoveredCaseCount: 1 }; },
    runCompose: async () => { calls.push('compose'); files.set(junit, '<testsuite/>'); files.set(`${dir}/profile.json`, JSON.stringify(profiles)); return 0; },
    info: text => logs.push(text), error: text => logs.push(text),
  };
  return { io, files, calls, logs, profiles };
}

test('actual wrapper path requires one successful Compose execution and fresh four-actor receipts', async () => {
  const f = fixture(); assert.equal(await runMobileMvp0(f.io), 0);
  assert.equal(f.calls.filter(x => x === 'compose').length, 1);
  assert.equal(f.calls.filter(x => x === 'docker').length, 1);
  assert.deepEqual(JSON.parse(f.files.get(`${dir}/execution.json`)), { status: 'PASS', sourceSha: sha, treeSha: tree,
    runId: '123', attempt: 1, retries: 0, requiredCases: 1, verifiedActors: 4, imageDigest: image, junitCases: 1 });
});
test('conflicting roster selection with stale receipts never calls Docker, executes or overwrites evidence', async () => {
  const f = fixture(); f.io.environment.COGLATAS_REAL_BACKEND_UI07_READONLY_ROSTER = '1';
  f.files.set(junit, 'OLD_JUNIT'); f.files.set(`${dir}/profile.json`, 'OLD_PROFILE');
  const before = [...f.files]; assert.equal(await runMobileMvp0(f.io), 1);
  assert.deepEqual(f.calls, []); assert.deepEqual([...f.files], before);
});
test('each prior receipt is rejected independently before execution or overwriting', async () => {
  for (const path of [junit, ...['source-sha', 'profile.json', 'execution.json'].map(x => `${dir}/${x}`)]) {
    const f = fixture(); f.files.set(path, 'PRESERVE');
    assert.equal(await runMobileMvp0(f.io), 1); assert.deepEqual(f.calls, []); assert.equal(f.files.size, 1); assert.equal(f.files.get(path), 'PRESERVE');
  }
});
test('source mismatch, dirty tree, wrong directory and reused build cannot qualify', async () => {
  for (const mutate of [f => { f.io.environment.TARGET_SHA = '4'.repeat(40); },
    f => { f.io.environment.COGLATAS_REUSE_PREBUILT_APP_IMAGE = '1'; },
    f => { f.io.cwdMatchesSource = () => false; },
    f => { f.io.execFileSync = (cmd,args) => args[0] === 'status' ? ' M tracked' : args[1] === 'HEAD' ? sha : tree; }]) {
    const f = fixture(); mutate(f); assert.equal(await runMobileMvp0(f.io), 1); assert.equal(f.calls.includes('compose'), false);
  }
});
test('undefined or failed actual execution cannot reach receipt or image acceptance', async () => {
  for (const result of [undefined, 1]) {
    const f = fixture(); f.io.runCompose = async () => { f.calls.push('compose'); return result; };
    assert.equal(await runMobileMvp0(f.io), 1); assert.equal(f.calls.includes('docker'), false);
    assert.notEqual(JSON.parse(f.files.get(`${dir}/execution.json`)).status, 'PASS');
  }
});
test('successful command without newly generated case/profile cannot reuse a pass', async () => {
  const f = fixture(); f.io.runCompose = async () => 0;
  assert.equal(await runMobileMvp0(f.io), 1); assert.equal(f.calls.includes('docker'), false);
});
test('extra cases or a Desktop actor fail before image acceptance', async () => {
  for (const mutate of [f => { f.io.verifyPlaywrightRequiredTests = async () => ({ requiredCount: 1, discoveredCaseCount: 2 }); },
    f => { f.profiles.actors.third.width = 1280; }]) {
    const f = fixture(); mutate(f); assert.equal(await runMobileMvp0(f.io), 1); assert.equal(f.calls.includes('docker'), false);
  }
});
test('invalid image cannot qualify and private failure text is never published', async () => {
  const f = fixture(); const original = f.io.execFileSync;
  f.io.execFileSync = (cmd,args,opts) => cmd === 'docker' ? 'bad-image' : original(cmd,args,opts);
  assert.equal(await runMobileMvp0(f.io), 1);
  assert.equal(JSON.parse(f.files.get(`${dir}/execution.json`)).status, 'FAIL');
  assert.equal(f.logs.some(x => x.includes('bad-image')), false);
});

test('real Git rejects an untracked application build input and still admits clean source with ignored evidence', async t => {
  const folder = await realpath(await mkdtemp(path.join(os.tmpdir(), 'coglatas-mobile-source-')));
  const tempRoot = await realpath(os.tmpdir());
  const assertOwned = () => { assert.ok(folder.startsWith(tempRoot + path.sep)); assert.ok(path.basename(folder).startsWith('coglatas-mobile-source-')); };
  assertOwned();
  t.after(async () => { assertOwned(); await rm(folder, { recursive: true, force: true }); });
  const git = args => execFileSync('git', args, { cwd: folder, encoding: 'utf8' }).trim();
  git(['init', '--quiet']);
  await mkdir(path.join(folder, 'src/Coglatas.Web'), { recursive: true });
  await writeFile(path.join(folder, '.gitignore'), 'artifacts/\n');
  await writeFile(path.join(folder, 'Dockerfile'), 'COPY . .\n');
  await writeFile(path.join(folder, 'src/Coglatas.Web/Tracked.cs'), '// Synthetic tracked source\n');
  git(['add', '.']);
  git(['-c', 'user.name=Synthetic Source Control', '-c', 'user.email=synthetic@example.test', 'commit', '--quiet', '-m', 'Synthetic source fixture']);
  await mkdir(path.join(folder, 'artifacts'), { recursive: true });
  await writeFile(path.join(folder, 'artifacts/ignored-evidence.json'), '{}\n');
  const prepare = () => {
    const f = fixture(); const original = f.io.execFileSync;
    f.io.environment.TARGET_SHA = git(['rev-parse', 'HEAD']);
    f.io.execFileSync = (command, args, options) => command === 'git' ? git(args) : original(command, args, options);
    return f;
  };
  const clean = prepare(); assert.equal(await runMobileMvp0(clean.io), 0);
  await writeFile(path.join(folder, 'src/Coglatas.Web/UnreviewedInput.cs'), '// Synthetic untracked application input\n');
  const blocked = prepare(); assert.equal(await runMobileMvp0(blocked.io), 1);
  assert.equal(blocked.calls.includes('compose'), false);
  assert.equal(blocked.calls.includes('docker'), false);
  assert.notEqual(JSON.parse(blocked.files.get(`${dir}/execution.json`)).status, 'PASS');
});
