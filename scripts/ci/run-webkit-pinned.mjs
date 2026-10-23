import { spawn } from 'node:child_process';
import { randomUUID } from 'node:crypto';
import { readFile } from 'node:fs/promises';
import process from 'node:process';

// Reuse the existing Noble browser/runtime image; do not select a floating tag.
const digest = 'sha256:06a9939e57531807f8d5fd76ce44b53165ffb7d7501d87ab10e285c20b1e971f';
const image = `mcr.microsoft.com/playwright:v1.64.0-noble@${digest}`;
const owner = randomUUID();
const container = `coglatas-compat-webkit-${owner}`;
let activeChild;
let interrupted = 0;

for (const [signal, code] of [['SIGINT', 130], ['SIGTERM', 143]]) {
  process.on(signal, () => {
    interrupted = code;
    activeChild?.kill(signal);
  });
}

async function command(executable, args, capture = false) {
  if (interrupted) throw new Error('WebKit execution interrupted.');
  return new Promise((resolve, reject) => {
    const child = spawn(executable, args, { stdio: capture ? ['ignore', 'pipe', 'pipe'] : 'inherit' });
    activeChild = child;
    let stdout = '';
    let stderr = '';
    if (capture) {
      child.stdout.on('data', (value) => { stdout += value; });
      child.stderr.on('data', (value) => { stderr += value; });
    }
    child.on('error', reject);
    child.on('close', (code) => {
      activeChild = undefined;
      resolve({ code: code ?? 1, stdout, stderr });
    });
  });
}

async function required(executable, args, capture = false) {
  const result = await command(executable, args, capture);
  if (result.code !== 0) throw new Error(`${executable} ${args[0]} failed (exit ${result.code}).`);
  return result.stdout.trim();
}

async function verifyImage() {
  const data = JSON.parse(await required('docker', ['image', 'inspect', image], true));
  const item = data[0];
  if (data.length !== 1 || item.Os !== 'linux' || item.Architecture !== 'amd64' ||
      !item.RepoDigests?.includes(`mcr.microsoft.com/playwright@${digest}`)) {
    throw new Error('Pinned WebKit image identity/platform mismatch.');
  }
  console.log(`WebKit runtime: ${image}; image ID ${item.Id}; linux/amd64`);
}

async function cleanup() {
  // Select only this invocation's label and name, then remove by immutable ID.
  const previous = interrupted;
  interrupted = 0;
  try {
    const ids = await required('docker', ['ps', '--all', '--no-trunc', '--filter',
      `label=coglatas.compat-webkit-owner=${owner}`, '--filter', `name=^/${container}$`, '--format', '{{.ID}}'], true);
    for (const id of ids.split(/\s+/u).filter(Boolean)) {
      if (!/^[a-f0-9]{64}$/u.test(id)) throw new Error('Unexpected owned-container ID.');
      await required('docker', ['rm', '--force', id]);
    }
  } finally {
    interrupted = previous;
  }
}

try {
  const mode = process.argv[2];
  if (process.argv.length !== 3 || !['prepare', 'run'].includes(mode)) {
    throw new Error('Usage: node scripts/ci/run-webkit-pinned.mjs prepare|run');
  }
  if (mode === 'prepare') {
    await required('docker', ['pull', '--platform', 'linux/amd64', image]);
    await verifyImage();
  } else {
    const expected = process.env.GITHUB_SHA;
    if (!/^[a-f0-9]{40}$/u.test(expected ?? '')) throw new Error('GITHUB_SHA is required.');
    const actual = await required('git', ['rev-parse', 'HEAD'], true);
    const stamp = (await readFile('artifacts/ci/frontend-build-sha', 'utf8')).trim();
    if (actual !== expected || stamp !== expected) throw new Error('Checkout/frontend producer SHA mismatch.');
    await verifyImage();
    // Windows local diagnostics have no Unix UID; authoritative CI uses its real UID/GID.
    const uid = process.getuid?.() ?? 1000;
    const gid = process.getgid?.() ?? 1000;
    const args = ['create', '--pull', 'never', '--platform', 'linux/amd64', '--init',
      '--name', container, '--label', `coglatas.compat-webkit-owner=${owner}`,
      '--user', `${uid}:${gid}`, '--workdir', '/work',
      '--mount', `type=bind,source=${process.cwd()},target=/work`,
      '--env', 'CI=true', '--env', 'HOME=/tmp', '--env', 'PLAYWRIGHT_SKIP_BUILD=1',
      image, 'bash', '-lc',
      'node scripts/ci/verify-webkit-pinned-runtime.mjs && npm run test:ui:compat-critical -- --profile browser-engine -- --project=webkit-desktop'];
    await required('docker', args, true);
    const result = await command('docker', ['start', '--attach', container]);
    if (result.code !== 0) process.exitCode = result.code;
    // Verify the container's exit, independently of the attached Docker client.
    if (!interrupted) {
      const state = JSON.parse(await required('docker', ['inspect', '--format', '{{json .State}}', container], true));
      if (state.Running !== false || state.Status !== 'exited' || state.ExitCode !== 0) {
        process.exitCode = Number(state.ExitCode) || 1;
      }
    }
  }
} catch (error) {
  console.error(error instanceof Error ? error.message : error);
  process.exitCode = interrupted || 1;
} finally {
  try {
    await cleanup();
  } catch (error) {
    console.error(error instanceof Error ? error.message : error);
    process.exitCode = interrupted || 1;
  }
  if (interrupted) process.exitCode = interrupted;
}
