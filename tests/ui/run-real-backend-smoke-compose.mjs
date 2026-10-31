import { spawn } from 'node:child_process';
import { pathToFileURL } from 'node:url';
import {
  composeFailureObservation,
  getComposeProjectName,
  normalizeExitCode,
  redactSecrets,
  selectComposeInvocation
} from './real-backend-smoke-compose-helpers.mjs';

const composeFile = 'infra/compose/test/real-backend-smoke.yml';
const projectName = getComposeProjectName(process.env, process.pid);
const composeEnv = { ...process.env, COMPOSE_PROJECT_NAME: projectName };
const reusePrebuiltApp = composeEnv.COGLATAS_REUSE_PREBUILT_APP_IMAGE === '1';

let composeInvocation;
let cleanupPromise;
let signalReceived = false;
let rosterControl = null;

process.once('SIGINT', () => void handleSignal('SIGINT', 130));
process.once('SIGTERM', () => void handleSignal('SIGTERM', 143));

const directInvocation = process.argv[1] && pathToFileURL(process.argv[1]).href === import.meta.url;
if (directInvocation || (process.env.COGLATAS_REAL_BACKEND_UI07_READONLY_ROSTER !== '1' &&
    process.env.COGLATAS_REAL_BACKEND_MVP0_MOBILE !== '1')) {
  process.exitCode = await runRealBackendSmokeCompose();
}

export async function runRealBackendSmokeCompose(control = null) {
  let exitCode = 1;

  try {
    const rosterSelected = composeEnv.COGLATAS_REAL_BACKEND_UI07_READONLY_ROSTER === '1';
    if (rosterSelected !== (control?.kind === 'UI07_READONLY_ROSTER')) {
      throw new Error('ReadOnly roster requires its dedicated closed-selection wrapper.');
    }
    rosterControl = control;
    composeInvocation = await selectComposeInvocation(isCommandAvailable);
    console.log(`Using ${composeInvocation.command} ${composeInvocation.prefix.join(' ')} with isolated project ${projectName}.`);

    const config = await runCompose(['-p', projectName, '-f', composeFile, 'config', '--quiet'], { capture: true, redact: true });
    exitCode = config.exitCode;
    observeRosterComposeFailure('CONFIG', config);
    if (exitCode === 0) {
      if (rosterControl) await rosterControl.admit(rosterIo());
      const upArgs = ['-p', projectName, '-f', composeFile, 'up', '--detach'];
      upArgs.push(reusePrebuiltApp || rosterControl ? '--no-build' : '--build', 'postgres', 'app');
      const up = await runCompose(upArgs);
      exitCode = up.exitCode;
      observeRosterComposeFailure('UP', up);
      if (rosterControl) await rosterControl.capture(rosterIo());
    }

    if (exitCode === 0) {
      if (rosterControl) rosterControl.waiting();
      await waitForHealthy('postgres');
      await waitForMigration();
      await waitForHealthy('app');

      if (rosterControl) await rosterControl.prepare(rosterIo());
      exitCode = (await runCompose(realBackendPlaywrightRunArgs())).exitCode;
      if (rosterControl) await rosterControl.browserFinished(rosterIo());
    }
  } catch (error) {
    if (rosterControl) rosterControl.failed();
    console.error(rosterControl ? 'ReadOnly roster setup or execution failed; inspect its structural receipt.' :
      error instanceof Error ? error.message : error);
    exitCode = 1;
  } finally {
    if (exitCode !== 0) {
      await collectFailureEvidence();
    }

    const cleanup = await cleanupOnce(exitCode === 0);
    if (rosterControl && cleanup?.exitCode !== 0) exitCode = 1;
  }

  return exitCode;
}

function realBackendPlaywrightRunArgs() {
  const args = ['-p', projectName, '-f', composeFile, 'run'];
  if (rosterControl) return rosterControl.playwrightArgs(args, composeEnv);
  if (composeEnv.COGLATAS_REAL_BACKEND_P0_SETUP === '1') {
    args.push('--env', 'COGLATAS_REAL_BACKEND_P0_SETUP=1');
  }
  if (composeEnv.COGLATAS_FUNCTIONAL_FILES_GATE) {
    if (!['functional-fast', 'functional-full'].includes(composeEnv.COGLATAS_FUNCTIONAL_FILES_GATE)) {
      throw new Error('Invalid FCI-05 Files owner gate.');
    }
    args.push('--env', `COGLATAS_FUNCTIONAL_FILES_GATE=${composeEnv.COGLATAS_FUNCTIONAL_FILES_GATE}`);
  }
  if (composeEnv.COGLATAS_REAL_BACKEND_MVP0_MOBILE === '1') {
    if (rosterControl || composeEnv.COGLATAS_FCI04_GATES || composeEnv.COGLATAS_FUNCTIONAL_FILES_GATE) {
      throw new Error('MVP0 Mobile is an independent selection, not a canonical owner override.');
    }
    args.push('--env', 'COGLATAS_REAL_BACKEND_MVP0_MOBILE=1', 'real-backend-playwright',
      'bash', '-lc', 'npm ci && node tests/ui/run-real-backend-playwright.mjs --config=playwright.mobile-mvp0-real.config.ts --project=real-backend-mobile-mvp0-320 --retries=0 --workers=1');
    return args;
  }
  args.push('real-backend-playwright');
  return args;
}

async function isCommandAvailable(command, args) {
  const result = await runCommand(command, args, { allowFailure: true, capture: true, silent: true });
  return result.exitCode === 0;
}

async function waitForHealthy(service) {
  const deadline = Date.now() + 240_000;
  let lastState = 'not created';

  while (Date.now() < deadline) {
    const containerId = await getServiceContainerId(service);
    if (containerId) {
      const state = await inspectContainerState(containerId);
      lastState = state || lastState;
      const [status, health, exitCodeText] = lastState.split(/\s+/);

      if (status === 'running' && health === 'healthy') {
        return;
      }

      if (status === 'exited' || status === 'dead') {
        throw new Error(`${service} container stopped before becoming healthy: ${lastState}`);
      }

      const containerExitCode = Number(exitCodeText);
      if (Number.isFinite(containerExitCode) && containerExitCode !== 0) {
        throw new Error(`${service} container failed before becoming healthy: ${lastState}`);
      }
    }

    await delay(1_000);
  }

  throw new Error(`Timed out waiting for ${service} to become healthy. Last state: ${lastState}`);
}

async function waitForMigration() {
  const deadline = Date.now() + 240_000;
  let lastState = 'not created';

  while (Date.now() < deadline) {
    const containerId = await getServiceContainerId('migrate');
    if (containerId) {
      const state = await inspectContainerState(containerId);
      lastState = state || lastState;
      const [status, , exitCodeText] = lastState.split(/\s+/);
      const migrationExitCode = Number(exitCodeText);

      if (status === 'exited' && migrationExitCode === 0) {
        return;
      }

      if ((status === 'exited' || status === 'dead') && Number.isFinite(migrationExitCode)) {
        throw new Error(`migrate container failed before the app could start: ${lastState}`);
      }
    }

    await delay(1_000);
  }

  throw new Error(`Timed out waiting for migrate to complete successfully. Last state: ${lastState}`);
}

async function getServiceContainerId(service) {
  const result = await runCompose(['-p', projectName, '-f', composeFile, 'ps', '--all', '-q', service], {
    allowFailure: true,
    capture: true,
    silent: true
  });
  return result.output.trim().split(/\s+/)[0] || '';
}

async function inspectContainerState(containerId) {
  const result = await runDocker(
    [
      'inspect',
      '--format',
      '{{.State.Status}} {{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}} {{.State.ExitCode}}',
      containerId
    ],
    { allowFailure: true, capture: true, silent: true }
  );
  return result.output.trim();
}

function runDocker(args, options = {}) {
  return runCommand('docker', args, options);
}

function runCompose(args, options = {}) {
  if (!composeInvocation) {
    return Promise.resolve({ exitCode: 1, output: 'Docker Compose command was not selected.' });
  }

  const selectedArgs = rosterControl ? [...args.slice(0, 4), '-f', rosterControl.overlay, ...args.slice(4)] : args;
  return runCommand(composeInvocation.command, [...composeInvocation.prefix, ...selectedArgs],
    rosterControl ? { ...options, capture: true, silent: true } : options);
}

function rosterIo() {
  return {
    environment: composeEnv,
    projectName,
    docker: (args, options = {}) => runDocker(args, { ...options, capture: true, silent: true }),
  };
}

function observeRosterComposeFailure(operation, result) {
  if (!rosterControl || result.exitCode === 0) return;
  // Preserve the original failure before later capture/cleanup advances phase.
  rosterControl.failed();
  try {
    const observation = composeFailureObservation(operation, result);
    if (observation) console.error(`UI07_ROSTER_COMPOSE_OBSERVATION ${JSON.stringify(observation)}`);
  } catch { /* Diagnostics must never replace the command failure or cleanup verdict. */ }
}

function runCommand(command, args, options = {}) {
  return new Promise((resolve) => {
    let settled = false;
    let stdout = '';
    let stderr = '';
    let spawnError;
    let outputOverflow = false;
    const child = spawn(command, args, {
      cwd: process.cwd(),
      env: composeEnv,
      stdio: options.capture ? [options.input === undefined ? 'ignore' : 'pipe', 'pipe', 'pipe'] : 'inherit'
    });
    if (options.input !== undefined) {
      child.stdin.on('error', () => { /* Child exit determines the failed stdin operation. */ });
      child.stdin.end(options.input);
    }

    const finish = (code) => {
      if (settled) {
        return;
      }

      settled = true;
      const output = `${stdout}${stderr}${spawnError ? `${spawnError.message}\n` : ''}`;
      if (options.capture && output.length > 0 && !options.silent) {
        process.stdout.write(options.redact ? redactSecrets(output) : output);
      }

      resolve({ exitCode: outputOverflow ? 1 : normalizeExitCode(code), output, stdout, stderr, outputOverflow });
    };

    if (options.capture) {
      child.stdout?.on('data', (chunk) => {
        if (rosterControl && stdout.length + stderr.length + chunk.length > 2 * 1024 * 1024) {
          outputOverflow = true;
          child.kill();
        } else stdout += chunk;
      });
      child.stderr?.on('data', (chunk) => {
        if (rosterControl && stdout.length + stderr.length + chunk.length > 2 * 1024 * 1024) {
          outputOverflow = true;
          child.kill();
        } else stderr += chunk;
      });
    }

    child.once('error', (error) => {
      spawnError = error;
      finish(127);
    });
    child.once('close', (code) => finish(code));
  });
}

async function collectFailureEvidence() {
  if (rosterControl) return;
  if (!composeInvocation) {
    return;
  }

  console.error('Collecting real-backend smoke failure evidence (secrets redacted).');
  await runCompose(['-p', projectName, '-f', composeFile, 'ps', '--all'], { allowFailure: true, capture: true, redact: true });
  for (const service of ['postgres', 'migrate', 'app', 'real-backend-playwright']) {
    await runCompose(['-p', projectName, '-f', composeFile, 'logs', '--no-color', '--tail', '300', service], {
      allowFailure: true,
      capture: true,
      redact: true
    });
  }
}

function cleanupOnce(succeeded = false) {
  if (cleanupPromise) {
    return cleanupPromise;
  }

  cleanupPromise = rosterControl
    ? rosterControl.cleanup(rosterIo(), succeeded)
    : composeInvocation
    ? runCompose(['-p', projectName, '-f', composeFile, 'down', '--volumes', '--remove-orphans'], {
        allowFailure: true,
        capture: true,
        redact: true
      })
    : Promise.resolve();
  return cleanupPromise;
}

async function handleSignal(signal, exitCode) {
  if (signalReceived) {
    return;
  }

  signalReceived = true;
  console.error(`Received ${signal}; cleaning up real-backend smoke containers.`);
  await cleanupOnce();
  process.exit(exitCode);
}

function delay(milliseconds) {
  return new Promise((resolve) => setTimeout(resolve, milliseconds));
}
