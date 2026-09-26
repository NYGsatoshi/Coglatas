import { spawn } from 'node:child_process';
import { stat } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const port = Number(process.env.PLAYWRIGHT_PORT ?? 4173);
const baseURL = process.env.PLAYWRIGHT_BASE_URL ?? `http://127.0.0.1:${port}`;
const shouldSkipBuild = process.env.PLAYWRIGHT_SKIP_BUILD === '1';
const distRoot = path.resolve(process.env.PLAYWRIGHT_STATIC_ROOT ?? path.join(process.cwd(), 'frontend/dist/coglatas-web'));
const distIndexPath = path.join(distRoot, 'index.html');
const serverPath = fileURLToPath(new URL('./serve-static.mjs', import.meta.url));
const playwrightCli = fileURLToPath(new URL('../../node_modules/@playwright/test/cli.js', import.meta.url));
const playwrightArgs = process.argv.slice(2);
const staticSuiteFiles = [
  'tests/ui/angular-smoke.spec.ts',
  'tests/ui/workspace-needs-attention.spec.ts',
  'tests/ui/task-execution-source-policy-v2.spec.ts',
  'tests/ui/file-activity-version-history.spec.ts',
  'tests/ui/message-mobile-navigation.spec.ts',
  'tests/ui/message-search-filters.spec.ts',
  'tests/ui/message-actions.spec.ts',
  'tests/ui/audit-claims-evidence.spec.ts',
  'tests/ui/audit-accessibility.spec.ts',
  'tests/ui/message-thread-context.spec.ts',
  'tests/ui/message-follow-ups.spec.ts',
  'tests/ui/app.spec.ts'
];
let server = null;

let exitCode = 1;

try {
  await ensureAngularBuild();
  server = spawn(process.execPath, [serverPath, '--port', String(port)], {
    cwd: process.cwd(),
    env: { ...process.env, PLAYWRIGHT_PORT: String(port) },
    stdio: ['ignore', 'inherit', 'inherit']
  });

  await waitForHealth(`${baseURL}/health`);

  exitCode = await runPlaywright();
} finally {
  await stopServer();
}

process.exit(exitCode);

function runPlaywright() {
  return new Promise((resolve) => {
    const child = spawn(process.execPath, [playwrightCli, 'test', ...staticSuiteFiles, ...playwrightArgs], {
      cwd: process.cwd(),
      env: { ...process.env, PLAYWRIGHT_BASE_URL: baseURL },
      stdio: 'inherit'
    });

    child.on('exit', (code) => resolve(code ?? 1));
  });
}

async function waitForHealth(url) {
  const deadline = Date.now() + 120_000;
  let lastError;

  while (Date.now() < deadline) {
    if (server?.exitCode !== null) {
      throw new Error(`Angular Playwright static server exited with code ${server.exitCode}.`);
    }

    try {
      const response = await fetch(url);
      if (response.ok) {
        return;
      }
    } catch (error) {
      lastError = error;
    }

    await new Promise((resolve) => setTimeout(resolve, 250));
  }

  throw new Error(`Timed out waiting for ${url}.${lastError ? ` Last error: ${lastError}` : ''}`);
}

async function ensureAngularBuild() {
  if (!shouldSkipBuild) {
    const buildExitCode = await runBuildCommand();
    if (buildExitCode !== 0) {
      throw new Error(`Angular build failed with exit code ${buildExitCode}.`);
    }
  }

  try {
    const stats = await stat(distIndexPath);
    if (!stats.isFile()) {
      throw new Error('Angular build output index.html was not created.');
    }
  } catch (error) {
    throw new Error(
      shouldSkipBuild
        ? `PLAYWRIGHT_SKIP_BUILD=1 was set, but ${distIndexPath} is missing.`
        : `Angular build completed without producing ${distIndexPath}.`,
      { cause: error }
    );
  }
}

function runBuildCommand() {
  if (process.platform === 'win32') {
    return runCommand(process.env.ComSpec ?? 'cmd.exe', ['/d', '/s', '/c', 'npm --prefix frontend run build']);
  }

  return runCommand('npm', ['--prefix', 'frontend', 'run', 'build']);
}

function runCommand(command, args) {
  return new Promise((resolve) => {
    const child = spawn(command, args, {
      cwd: process.cwd(),
      env: { ...process.env },
      stdio: 'inherit'
    });

    child.on('exit', (code) => resolve(code ?? 1));
  });
}

function stopServer() {
  return new Promise((resolve) => {
    if (!server || server.exitCode !== null) {
      resolve();
      return;
    }

    const timeout = setTimeout(() => {
      server.kill('SIGKILL');
      resolve();
    }, 2_000);

    server.once('exit', () => {
      clearTimeout(timeout);
      resolve();
    });

    server.kill('SIGTERM');
  });
}
