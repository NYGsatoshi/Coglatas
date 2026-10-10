import { spawn } from 'node:child_process';
import { createHash, randomUUID } from 'node:crypto';
import { open, mkdir, mkdtemp, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { dirname, isAbsolute, join, relative, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

// Native scanner/tooling controls only; no product target or external callback.
const zapImage = 'zaproxy/zap-stable:2.17.0@sha256:781a2bdaea47324e7bab583e2263f21d257b0aee61ed51521a5be45f5f5081ef';
const pythonImage = 'python@sha256:bb1f2fdb1065c85468775c9d680dcd344f6442a2d1181ef7916b60a623f11d40';
const root = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
const sources = ['scripts/security/sec14-browser-fixture.py', 'scripts/security/sec14-browser-fixture.yaml',
  'scripts/security/sec14-browser-fixture-run.sh', 'scripts/security/run-sec14-browser-fixture.mjs',
  'scripts/ci/sec14_browser_evidence.py'];
const args = process.argv.slice(2);
let development = false;
let mode = 'normal';
let candidate;
let report;
const seen = new Set();
for (let index = 0; index < args.length; index++) {
  const key = args[index];
  if (seen.has(key)) throw new Error('Duplicate fixture option');
  seen.add(key);
  if (key === '--development') development = true;
  else if (['--case', '--candidate-sha', '--report'].includes(key)) {
    const value = args[++index];
    if (typeof value !== 'string' || value.length > 4096) throw new Error('Fixture option invalid');
    if (key === '--case') mode = value;
    else if (key === '--candidate-sha') candidate = value;
    else report = value;
  } else throw new Error('Unknown fixture option');
}
if (!['normal', 'weakened', 'disabled'].includes(mode) || !/^[0-9a-f]{40}$/.test(candidate ?? '') || !report)
  throw new Error('Fixture requires exact candidate, bounded case and report');
if (resolve('.') !== root) throw new Error('Run fixture from its repository root');
const reportPath = resolve(report);
const outputRoot = join(root, 'artifacts/sec14-native');
const outputRelative = relative(outputRoot, reportPath);
if (outputRelative === '' || outputRelative.startsWith('..') || isAbsolute(outputRelative) || !reportPath.endsWith('.json'))
  throw new Error('Fixture output must be a JSON artifact inside artifacts/sec14-native');

async function command(executable, commandArgs, timeout = 30000) {
  return await new Promise((done, reject) => {
    const child = spawn(executable, commandArgs, { windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] });
    const chunks = [];
    let bytes = 0;
    let exceeded = false;
    let timedOut = false;
    const timer = setTimeout(() => { timedOut = true; child.kill('SIGKILL'); }, timeout);
    const capture = data => {
      bytes += data.length;
      if (bytes <= 1024 * 1024) chunks.push(data);
      else { exceeded = true; child.kill('SIGKILL'); }
    };
    child.stdout.on('data', capture);
    child.stderr.on('data', capture);
    child.on('error', () => { clearTimeout(timer); reject(new Error('Fixture executable unavailable')); });
    child.on('close', code => {
      clearTimeout(timer);
      done({ code, output: Buffer.concat(chunks).toString('utf8'), exceeded, timedOut });
    });
  });
}
function requireCommand(result) {
  if (result.code !== 0 || result.exceeded || result.timedOut) throw new Error('Fixture subprocess failed');
  return result.output;
}
async function fileHash(path) {
  const handle = await open(path, 'r');
  try {
    if (!(await handle.stat()).isFile()) throw new Error('Fixture input must be regular');
    const hash = createHash('sha256');
    const buffer = Buffer.alloc(65536);
    let bytes = 0;
    while (true) {
      const { bytesRead } = await handle.read(buffer, 0, Math.min(buffer.length, 32 * 1024 * 1024 - bytes + 1), null);
      if (bytesRead === 0) break;
      bytes += bytesRead;
      if (bytes > 32 * 1024 * 1024) throw new Error('Fixture input limit exceeded');
      hash.update(buffer.subarray(0, bytesRead));
    }
    if (bytes === 0) throw new Error('Fixture input cannot be empty');
    return hash.digest('hex');
  } finally { await handle.close(); }
}
async function sourceHashes() {
  const hashes = {};
  for (const source of sources) hashes[source] = await fileHash(join(root, source));
  return hashes;
}

let temporary;
let containerStarted = false;
const container = `coglatas-sec14-browser-${randomUUID().replaceAll('-', '')}`;
const runIdentity = `local:${randomUUID().replaceAll('-', '')}`;
try {
  const revision = requireCommand(await command('git', ['rev-parse', 'HEAD'])).trim();
  if (revision !== candidate) throw new Error('Fixture candidate differs from actual HEAD');
  const dirty = requireCommand(await command('git', ['status', '--porcelain'])).trim() !== '';
  if (dirty && !development) throw new Error('Dirty source requires development mode');
  const snapshot = await sourceHashes();
  temporary = await mkdtemp(join(tmpdir(), 'coglatas-sec14-browser-'));
  await mkdir(dirname(reportPath), { recursive: true });
  const startedAtUtc = new Date().toISOString();
  containerStarted = true;
  const native = await command('docker', ['run', '--rm', '--name', container,
    '--label', 'coglatas.fixture=sec14-browser-tooling', '--network', 'none', '--platform', 'linux/amd64',
    '-v', `${join(root, 'scripts/security')}:/fixtures:ro`, '-v', `${temporary}:/out`,
    '--entrypoint', '/bin/bash', zapImage, '/fixtures/sec14-browser-fixture-run.sh', mode], 300000);
  if (native.exceeded || native.timedOut) throw new Error('Native scanner exceeded process bounds');
  const completedAtUtc = new Date().toISOString();
  if (requireCommand(await command('git', ['rev-parse', 'HEAD'])).trim() !== revision
    || JSON.stringify(await sourceHashes()) !== JSON.stringify(snapshot)) throw new Error('Fixture source changed during execution');
  const context = {
    schema: 'sec14-browser-execution-v1', candidateSha: revision, runIdentity,
    scope: 'TEST_OWNED_LOOPBACK_FIXTURE_ONLY', mode, startedAtUtc, completedAtUtc,
    sourceSha256: snapshot, scannerImage: zapImage, scannerVersion: '2.17.0', processExit: native.code,
    sourceState: dirty ? 'DEVELOPMENT' : 'CLEAN', network: 'none', platform: 'linux/amd64',
    nativeSha256: { report: await fileHash(join(temporary, 'browser-native-v1.json')),
      counters: await fileHash(join(temporary, 'browser-counters.json')) },
  };
  await writeFile(join(temporary, 'context.json'), `${JSON.stringify(context, null, 2)}\n`, { flag: 'wx' });
  const shared = ['run', '--rm', '--network', 'none', '-e', 'PYTHONDONTWRITEBYTECODE=1',
    '-v', `${root}:/repo:ro`, '-v', `${temporary}:/raw:ro`, '-v', `${dirname(reportPath)}:/reports`,
    '-w', '/repo', pythonImage, 'python3', 'scripts/ci/sec14_browser_evidence.py',
    '--expected-candidate-sha', revision, '--expected-run-identity', runIdentity, '--raw-directory', '/raw'];
  const capture = await command('docker', [...shared, '--output', `/reports/${reportPath.split(/[\\/]/).at(-1)}`]);
  const verify = await command('docker', [...shared, '--verify-report', `/reports/${reportPath.split(/[\\/]/).at(-1)}`]);
  if (capture.exceeded || capture.timedOut || verify.exceeded || verify.timedOut) throw new Error('Evidence verifier exceeded bounds');
  process.exitCode = capture.code === 0 && verify.code === 0 ? 0 : 1;
  console.log(`SEC-14 native browser case=${mode} evidence=${process.exitCode === 0 ? 'MATCHED' : 'FAILED'} product=UNVERIFIED`);
} catch {
  try {
    await mkdir(dirname(reportPath), { recursive: true });
    await writeFile(reportPath, `${JSON.stringify({
      schema: 'coglatas-sec14-browser-tooling-advisory-v1', mode: 'ADVISORY', integrityStatus: 'ERROR',
      errorCode: 'LAUNCH_OR_EVIDENCE_FAILURE', candidateSha: candidate, runIdentity, fixtureCase: mode,
      rawArtifactUploadAllowed: false, productSourceBinding: 'UNVERIFIED', productImageBinding: 'UNVERIFIED',
      scannerExecutionAuthentication: 'UNVERIFIED', personalOwnerApproval: 'UNVERIFIED',
      releaseAcceptance: 'BLOCKED', preAvaloniaVerdict: 'BLOCKED',
    }, null, 2)}\n`, { flag: 'wx' });
  } catch { /* Preserve existing reports and keep the original nonzero outcome. */ }
  console.error('SEC-14 native browser fixture failed; no product qualification');
  process.exitCode = 1;
} finally {
  if (containerStarted) {
    const label = await command('docker', ['inspect', '--format', '{{index .Config.Labels "coglatas.fixture"}}', container]);
    if (label.code === 0 && label.output.trim() === 'sec14-browser-tooling')
      await command('docker', ['rm', '--force', container]);
  }
  if (temporary && resolve(temporary).startsWith(resolve(tmpdir()) + (process.platform === 'win32' ? '\\' : '/'))
    && dirname(temporary) === resolve(tmpdir()) && temporary.split(/[\\/]/).at(-1).startsWith('coglatas-sec14-browser-'))
    await rm(temporary, { recursive: true });
}
