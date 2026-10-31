import {
  buildRealBackendPlaywrightPlan,
  composeProjectName,
  composeV2Invocation,
  composeFailureObservation,
  getComposeProjectName,
  isHstsPreloadedHttpUrl,
  isStaticAngularServerUrl,
  legacyComposeInvocation,
  normalizeExitCode,
  redactSecrets,
  selectComposeInvocation
} from './real-backend-smoke-compose-helpers.mjs';
import { EventEmitter } from 'node:events';
import assert from 'node:assert/strict';
import { buildFci04OwnerPlan } from '../functional/fixtures/fci04-owner-plan.mjs';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { readFileSync } from 'node:fs';
import { runInNewContext } from 'node:vm';
import test from 'node:test';
import { captureTaskTitleFocus, readTaskTitleFocusState } from './real-backend-task-focus.mjs';

// Execute the tracked adapter, including its real asynchronous child-event handling.
// Only process boundaries are synthetic; no Docker daemon or browser is invoked.
async function composeCleanupFlow({ scenarioExit = 0, cleanupExit = 0, configExit = 0,
  unavailable = false, mobile = false, signal = null } = {}) {
  const runnerUrl = new URL('./run-real-backend-smoke-compose.mjs', import.meta.url).href;
  const invocations = [], exits = [], handlers = new Map();
  const projectName = 'coglatas-cleanup-control';
  let cleanupFinished = false, finishSignal;
  const signalCompletion = signal ? new Promise(resolve => { finishSignal = resolve; }) : null;
  const runnerProcess = {
    argv: ['node', '/synthetic-importing-owner.mjs'], pid: 1, cwd: () => process.cwd(),
    env: { REAL_BACKEND_SMOKE_COMPOSE_PROJECT_NAME: projectName,
      ...(mobile ? { COGLATAS_REAL_BACKEND_MVP0_MOBILE: '1' } : {}) },
    once: (name, handler) => handlers.set(name, handler),
    stdout: { write: () => undefined },
    exit: code => { exits.push({ code, cleanupFinished }); finishSignal(); },
  };
  const source = readFileSync(new URL(runnerUrl), 'utf8')
    .replace(/^import[\s\S]*?;\r?\n/gmu, '')
    .replace('export async function runRealBackendSmokeCompose', 'async function runRealBackendSmokeCompose')
    .replaceAll('import.meta.url', 'runnerUrl');
  const result = await runInNewContext(`(async () => { ${source}\n return ${mobile
    ? 'runRealBackendSmokeCompose()' : 'process.exitCode'}; })()`, {
    Buffer, setTimeout, clearTimeout, pathToFileURL, getComposeProjectName,
    normalizeExitCode, redactSecrets, selectComposeInvocation, composeFailureObservation,
    runnerUrl, process: runnerProcess,
    console: { log: () => undefined, error: () => undefined },
    spawn: (command, args) => {
      invocations.push({ command, args: [...args] });
      const child = new EventEmitter();
      child.stdout = new EventEmitter(); child.stderr = new EventEmitter();
      queueMicrotask(() => {
        let code = 0, output = '';
        const action = args[0] === 'compose' ? args[5] : null;
        if (args.includes('version')) code = unavailable ? 127 : 0;
        else if (args[0] === 'inspect') output = args.at(-1) === 'container-migrate'
          ? 'exited none 0' : 'running healthy 0';
        else if (action === 'config') code = configExit;
        else if (action === 'ps' && args.includes('-q')) output = `container-${args.at(-1)}`;
        else if (action === 'run') {
          code = scenarioExit;
          if (signal) {
            // Exercise the real registered signal entry and its memoized cleanup.
            handlers.get(signal)();
            handlers.get(signal)();
          }
        } else if (action === 'down') { code = cleanupExit; cleanupFinished = true; }
        if (output) child.stdout.emit('data', Buffer.from(output));
        child.emit('close', code);
      });
      return child;
    },
  });
  if (signalCompletion) await signalCompletion;
  const down = invocations.filter(call => call.args[5] === 'down');
  assert.equal(down.length, unavailable ? 0 : 1);
  for (const call of down) {
    assert.equal(call.command, 'docker');
    assert.deepEqual(call.args, ['compose', '-p', projectName, '-f',
      'infra/compose/test/real-backend-smoke.yml', 'down', '--volumes', '--remove-orphans']);
  }
  return { result, exits, invocations };
}

await test('generic Compose success requires successful scoped cleanup', async () => {
  assert.equal((await composeCleanupFlow()).result, 0);
  assert.equal((await composeCleanupFlow({ cleanupExit: 7 })).result, 1);
});

await test('generic Compose cleanup preserves original scenario and setup failures', async () => {
  for (const cleanupExit of [0, 7]) {
    assert.equal((await composeCleanupFlow({ scenarioExit: 17, cleanupExit })).result, 17);
    assert.equal((await composeCleanupFlow({ configExit: 7, cleanupExit })).result, 7);
  }
  assert.equal((await composeCleanupFlow({ unavailable: true })).result, 1);
});

await test('explicit MVP0 Compose owner also rejects cleanup-only failure', async () => {
  const run = await composeCleanupFlow({ mobile: true, cleanupExit: 7 });
  assert.equal(run.result, 1);
  const browser = run.invocations.find(call => call.args[5] === 'run');
  assert.ok(browser.args.includes('COGLATAS_REAL_BACKEND_MVP0_MOBILE=1'));
  assert.ok(browser.args.at(-1).endsWith('--retries=0 --workers=1'));
});

await test('Compose signal handlers retain 130 and 143 after exactly one scoped cleanup', async () => {
  for (const [signal, code] of [['SIGINT', 130], ['SIGTERM', 143]]) {
    const run = await composeCleanupFlow({ signal, cleanupExit: 7 });
    assert.deepEqual(run.exits, [{ code, cleanupFinished: true }]);
  }
});

const CORE_RUN_INDEX = 0,
  DEFAULT_PLAN_RUN_COUNT = 2,
  FAILURE_EXIT_CODE = 1,
  FILES_RUN_INDEX = 1,
  LAST_EVENT_INDEX = -1,
  SINGLE_RUN_COUNT = 1,
  SUCCESS_EXIT_CODE = 0,
  runnerUrl = new URL('./run-real-backend-playwright.mjs', import.meta.url),
  sequenceForRunner = async (environment = {}, failedRun = '') => {
    const errors = [], events = [], invocations = [],
      runnerProcess = {
        argv: ['node', fileURLToPath(runnerUrl)],
        cwd: () => process.cwd(),
        env: {
          COGLATAS_BROWSER_SMOKE_EMAIL: 'synthetic@example.test',
          COGLATAS_BROWSER_SMOKE_PASSWORD: 'synthetic-test-only',
          COGLATAS_FCI04_GATES: 'functional-full',
          COGLATAS_FUNCTIONAL_FILES_GATE: 'functional-full',
          COGLATAS_REAL_BACKEND_P0_SETUP: '1',
          COGLATAS_REAL_BACKEND_SMOKE: '1',
          PLAYWRIGHT_BASE_URL: 'http://coglatas-backend:8080',
          ...environment
        },
        execPath: process.execPath
      },
      source = readFileSync(runnerUrl, 'utf8')
        .replace(/^import[\s\S]*?;\r?\n/gmu, '')
        .replaceAll('import.meta.url', 'runnerUrl');

  await runInNewContext(`(async () => { ${source}\n })()`, {
      URL,
      buildFci04OwnerPlan,
      buildRealBackendPlaywrightPlan,
      console: { error: (message) => errors.push(message), log: (message) => events.push(message) },
      fetch: () => Promise.resolve({ ok: true }),
      fileURLToPath,
      isHstsPreloadedHttpUrl,
      isStaticAngularServerUrl,
      prepareRealBackendP0State: () => { events.push('legacy-denial-setup'); },
      process: runnerProcess,
      runnerUrl,
      spawn: (_command, _args, options) => {
        const child = new EventEmitter();
        invocations.push(options.env);
        queueMicrotask(() => {
          let exitCode = SUCCESS_EXIT_CODE;
          if (events.at(LAST_EVENT_INDEX) === failedRun) {
            exitCode = FAILURE_EXIT_CODE;
          }
          child.emit('close', exitCode);
        });
        return child;
      }
    });

  assert.deepEqual(errors, []);
  return { events, exitCode: runnerProcess.exitCode, invocations };
  };

test('sanitizes Compose project names and keeps them within the Compose limit', () => {
  const name = composeProjectName(['Coglatas site!', 'RUN/42', 'pid:123', 'x'.repeat(80)]);

  assert.match(name, /^[a-z0-9][a-z0-9_-]*$/);
  assert.ok(name.length <= 63);
  assert.equal(composeProjectName(['---']), 'coglatas-real-backend-smoke');
});

test('prefers Docker Compose v2 when it is available', async () => {
  const calls = [];
  const invocation = await selectComposeInvocation(async (command, args) => {
    calls.push([command, args]);
    return true;
  });

  assert.deepEqual(invocation, composeV2Invocation);
  assert.deepEqual(calls, [['docker', ['compose', 'version']]]);
});

test('falls back to legacy docker-compose when Compose v2 is unavailable', async () => {
  const calls = [];
  const invocation = await selectComposeInvocation(async (command, args) => {
    calls.push([command, args]);
    return command === 'docker-compose';
  });

  assert.deepEqual(invocation, legacyComposeInvocation);
  assert.deepEqual(calls, [
    ['docker', ['compose', 'version']],
    ['docker-compose', ['version']]
  ]);
});

test('reports a clear error when neither Compose command is available', async () => {
  await assert.rejects(
    () => selectComposeInvocation(async () => false),
    /Docker Compose is required for the real-backend browser smoke/
  );
});

test('redacts connection, browser, cookie, CSRF, authorization, and invite secrets', () => {
  const redacted = redactSecrets([
    'Password=database-secret;Host=postgres',
    'COGLATAS_BROWSER_SMOKE_PASSWORD: browser-secret',
    'Authorization: Bearer api-secret',
    'Cookie: session=secret',
    'X-CSRF-Token: csrf-secret',
    'InvitationToken: invite-secret',
    '{"token":"json-secret","password":"json-password"}'
  ].join('\n'));

  for (const secret of ['database-secret', 'browser-secret', 'api-secret', 'session=secret', 'csrf-secret', 'invite-secret', 'json-secret', 'json-password']) {
    assert.doesNotMatch(redacted, new RegExp(secret.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')));
  }
});

test('rejects the static Angular server URL and preserves child exit codes', () => {
  assert.equal(isStaticAngularServerUrl('http://127.0.0.1:4173'), true);
  assert.equal(isStaticAngularServerUrl('http://localhost:4173/app/login'), true);
  assert.equal(isStaticAngularServerUrl('http://coglatas-backend:8080'), false);
  assert.equal(isHstsPreloadedHttpUrl('http://app:8080'), true);
  assert.equal(isHstsPreloadedHttpUrl('http://service.example.app:8080'), true);
  assert.equal(isHstsPreloadedHttpUrl('https://service.example.app:8080'), false);
  assert.equal(isHstsPreloadedHttpUrl('http://coglatas-backend:8080'), false);
  assert.equal(normalizeExitCode(37), 37);
  assert.equal(normalizeExitCode(null), 1);
});

test('runs migrated Functional owners with their config before legacy regression', () => {
  const plan = buildRealBackendPlaywrightPlan();
  const [functionalRun, legacyRun] = plan;
  assert.deepEqual(plan.map((entry) => entry.name), [
    'Functional real-backend owners',
    'legacy real-backend regression'
  ]);
  assert.equal(plan.length, DEFAULT_PLAN_RUN_COUNT);
  const [configFlag, configPath] = functionalRun.args;
  assert.deepEqual([configFlag, configPath], ['--config', 'playwright.functional.config.ts']);
  assert.ok(functionalRun.args.includes('project-task/core-golden-journey.spec.ts'));
  assert.ok(functionalRun.args.includes('--project=functional-chromium'));
  assert.equal(functionalRun.args.includes('--pass-with-no-tests'), false);
  assert.ok(legacyRun.args.includes('tests/ui/real-backend-smoke.spec.ts'));
  assert.ok(legacyRun.args.includes('--project=chromium-desktop'));
});

test('keeps manifest-focused and custom runs on the legacy-compatible single invocation', () => {
  const focused = buildRealBackendPlaywrightPlan([], 'required title');
  const [focusedRun] = focused;
  const grepIndex = focusedRun.args.indexOf('--grep');
  assert.equal(focused.length, SINGLE_RUN_COUNT);
  assert.deepEqual(focusedRun.args.slice(grepIndex), ['--grep', 'required title']);
  assert.ok(focusedRun.args.includes('tests/ui/real-backend-smoke.spec.ts'));

  const custom = buildRealBackendPlaywrightPlan(['custom.spec.ts'], 'focused');
  assert.deepEqual(custom, [{
    name: 'custom',
    args: ['custom.spec.ts', '--grep', 'focused']
  }]);
});

test('P0 selection executes one Files owner independently of the legacy title grep', () => {
  for (const gate of ['functional-fast', 'functional-full']) {
    const [owner, legacy] = buildRealBackendPlaywrightPlan([], 'legacy required title', gate);
    assert.equal(owner.functionalGate, gate);
    assert.ok(owner.args.includes('files/files-fast-journey.spec.ts'));
    assert.equal(owner.args.includes('--grep'), false);
    assert.ok(legacy.args.includes('legacy required title'));
  }
  assert.throws(() => buildRealBackendPlaywrightPlan([], '', 'typo'), /Files owner gate/u);
});

test('actual runner executes Core Full and Files Full before preparing the real legacy denial fixture', async () => {
  const result = await sequenceForRunner();
  assert.deepEqual(result.events, [
    'Running required FUNC-TASK-001 owner at functional-full.',
    'Running Functional real-backend owners.',
    'legacy-denial-setup',
    'Running legacy real-backend regression.'
  ]);
  assert.equal(result.exitCode, SUCCESS_EXIT_CODE);
  assert.equal(result.invocations[CORE_RUN_INDEX].COGLATAS_FCI04_REQUIRED, '1');
  assert.equal(result.invocations[FILES_RUN_INDEX].COGLATAS_FUNCTIONAL_SELECTED_GATES, 'functional-full');
  assert.equal(result.invocations[FILES_RUN_INDEX].COGLATAS_FUNCTIONAL_DIAGNOSTICS, '1');
});

test('actual runner prepares legacy denial only after Files when no Core owner is selected', async () => {
  const result = await sequenceForRunner({ COGLATAS_FCI04_GATES: '' });
  assert.deepEqual(result.events, [
    'Running Functional real-backend owners.',
    'legacy-denial-setup',
    'Running legacy real-backend regression.'
  ]);
  assert.equal(result.exitCode, SUCCESS_EXIT_CODE);
});

test('actual runner stops after a failed canonical Files owner without mutating the legacy fixture', async () => {
  const result = await sequenceForRunner({}, 'Running Functional real-backend owners.');
  assert.deepEqual(result.events, [
    'Running required FUNC-TASK-001 owner at functional-full.',
    'Running Functional real-backend owners.'
  ]);
  assert.equal(result.exitCode, FAILURE_EXIT_CODE);
});

test('actual Core-only runner never prepares a legacy denial fixture', async () => {
  const result = await sequenceForRunner({ COGLATAS_FCI04_ONLY: '1' });
  assert.deepEqual(result.events, ['Running required FUNC-TASK-001 owner at functional-full.']);
  assert.equal(result.exitCode, SUCCESS_EXIT_CODE);
});

test('actual runner preserves disabled legacy setup while executing every selected owner and regression', async () => {
  const result = await sequenceForRunner({ COGLATAS_REAL_BACKEND_P0_SETUP: '0' });
  assert.deepEqual(result.events, [
    'Running required FUNC-TASK-001 owner at functional-full.',
    'Running Functional real-backend owners.',
    'Running legacy real-backend regression.'
  ]);
  assert.equal(result.exitCode, SUCCESS_EXIT_CODE);
});

const state = {
  activeControlKind: 'task-title',
  documentHasFocus: true,
  documentReadyState: 'complete',
  titlePresent: true,
  titleIsActiveElement: true,
  titleDisabled: false,
  titleConnected: true,
};
const capture = (value, phase = 'before-assertion') => captureTaskTitleFocus({ evaluate: async () => value }, phase);
const unavailable = { phase: 'before-assertion', availability: 'unavailable' };

// Node's test runner owns completion and reports callback failures.
void test('Task focus diagnostic: captures document focus independently from the active title without changing either', async () => {
  const result = await capture({ ...state, documentHasFocus: false });
  assert.deepEqual(result, { phase: 'before-assertion', availability: 'available', ...state, documentHasFocus: false });
  assert.equal(state.documentHasFocus, true);
});

void test('Task focus diagnostic: preserves only the closed diagnostic shape at the failure boundary', async () => {
  const result = await capture(state, 'assertion-failed');
  assert.deepEqual(result, { phase: 'assertion-failed', availability: 'available', ...state });
  assert.equal(JSON.stringify(result).includes('task-create-title'), false);
});

void test('Task focus diagnostic: rejects extra private fields rather than serializing them', async () => {
  for (const key of ['text', 'value', 'body', 'userId', 'url', 'headers', 'error', 'toJSON']) {
    assert.deepEqual(await capture({ ...state, [key]: 'private-sentinel' }), unavailable);
  }
});

void test('Task focus diagnostic: rejects malformed, incomplete, nested, and unbounded enum metadata', async () => {
  for (const value of [null, [], {}, { ...state, titlePresent: 'true' },
    { ...state, documentHasFocus: 1 }, { ...state, titleDisabled: null },
    { ...state, activeControlKind: 'private-sentinel' },
    { ...state, documentReadyState: { body: 'private-sentinel' } }]) {
    assert.deepEqual(await capture(value), unavailable);
  }
});

void test('Task focus diagnostic: capture errors and disappeared contexts produce no error text', async () => {
  const page = { evaluate: async () => { throw new Error('private-sentinel'); } };
  assert.deepEqual(await captureTaskTitleFocus(page, 'assertion-failed'), {
    phase: 'assertion-failed', availability: 'unavailable',
  });
});

void test('Task focus diagnostic: a stalled evaluation has a finite unavailable result', async () => {
  const page = { evaluate: () => new Promise(() => {}) };
  assert.deepEqual(await captureTaskTitleFocus(page, 'before-assertion'), unavailable);
});

void test('Task focus diagnostic: does not accept arbitrary phase strings', async () => {
  await assert.rejects(capture(state, 'private-sentinel'), { message: 'Unsupported Task focus diagnostic phase.' });
});

void test('Task focus diagnostic: the browser read returns only closed control kinds without inspecting text or values', () => {
  const original = Object.getOwnPropertyDescriptor(globalThis, 'document');
  const title = { isConnected: true, matches: (selector) => selector === ':disabled' };
  const privateElement = (tagName) => Object.defineProperties({ tagName }, {
    value: { get() { throw new Error('Must not read values.'); } },
    textContent: { get() { throw new Error('Must not read text.'); } },
    id: { get() { throw new Error('Must not read identities.'); } },
  });
  try {
    for (const [active, kind] of [[title, 'task-title'], [null, 'none'],
      ...['INPUT', 'TEXTAREA', 'SELECT', 'BUTTON', 'DIALOG', 'BODY'].map((tag) => [privateElement(tag), tag.toLowerCase()]),
      [privateElement('PRIVATE-SENTINEL'), 'other']]) {
      Object.defineProperty(globalThis, 'document', { configurable: true, value: {
        querySelector: (selector) => {
          assert.equal(selector, '[data-testid="task-create-title"]');
          return title;
        },
        activeElement: active, hasFocus: () => false, readyState: 'interactive',
      } });
      assert.deepEqual(readTaskTitleFocusState(), {
        activeControlKind: kind, documentHasFocus: false, documentReadyState: 'interactive',
        titlePresent: true, titleIsActiveElement: active === title, titleDisabled: true, titleConnected: true,
      });
    }
    Object.defineProperty(globalThis, 'document', { configurable: true, value: {
      querySelector: () => null, activeElement: null, hasFocus: () => true, readyState: 'loading',
    } });
    assert.deepEqual(readTaskTitleFocusState(), {
      activeControlKind: 'none', documentHasFocus: true, documentReadyState: 'loading',
      titlePresent: false, titleIsActiveElement: false, titleDisabled: false, titleConnected: false,
    });
  } finally {
    if (original) {
      Object.defineProperty(globalThis, 'document', original);
    } else {
      delete globalThis.document;
    }
  }
});
