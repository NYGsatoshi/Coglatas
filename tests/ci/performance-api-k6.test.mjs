import assert from 'node:assert/strict';
import fs from 'node:fs';
import test from 'node:test';
import vm from 'node:vm';

const fixture = {
  assertDiagnosticRows: runner => {
    const rows = runner.state.diagnosticRows;
    assert.equal(rows.length, fixture.iterations * fixture.contract.scenarios.length);
    assert.equal(runner.state.diagnosticHeaders.length, rows.length);
    assert.equal(new Set(rows.map(row => row.captureId)).size, rows.length);
    assert.equal(rows[fixture.zero].sampleOrdinal, String(fixture.one));
    assert.equal(rows.at(-fixture.one).sampleOrdinal, String(fixture.iterations));
    assert.equal(rows.at(-fixture.one).scenario, 'mutation.kanban-move');
    assert.equal(rows.at(-fixture.one).requestElapsedMs, String(fixture.normalDuration));
    fixture.assertSafeSummary(runner);
  },
  assertSafeSummary: runner => {
    runner.context.data = fixture.metricSummary(runner.recorded);
    const text = JSON.stringify(vm.runInContext('handleSummary(data)', runner.context));
    assert.ok(!text.includes('protected'));
    assert.ok(!text.includes('synthetic@example'));
    assert.ok(!text.includes('http://'));
  },
  contract: JSON.parse(fs.readFileSync('performance/api-k6.json', 'utf8')),
  harness: (fault = '', diagnostics = false) => {
    const config = { ...fixture.contract, identities: {
        ganttProjectId: 'gantt', kanbanProjectId: 'kanban', operatorEmail: 'synthetic@example.invalid',
        taskId: 'task', taskListProjectId: 'project', tenantSlug: 'perf-small', workspaceId: 'workspace',
      } },
      runtime = {
        Counter: class {
          constructor(name) { this.name = name; runtime.recorded.set(name, []); }
          add(value, tags) {
            runtime.recorded.get(this.name).push(value);
            if (this.name === 'perf_diagnostic_request_elapsed_ms' && tags) {
              runtime.state.diagnosticRows.push(tags);
            }
          }
        },
        context: null, fault, recorded: new Map(),
        get requests() { return runtime.state.requests; },
        state: { diagnosticHeaders: [], diagnosticRows: [], order: fixture.one,
          requests: fixture.zero, version: fixture.one },
      };
    if (diagnostics) {
      config.diagnostics = { capturePrefix: '0123456789abcdef', trialOrdinal: 1 };
    }
    runtime.context = vm.createContext({
      Counter: runtime.Counter, Date, JSON, Trend: runtime.Counter,
      __ENV: {
        COGLATAS_PERFORMANCE_BASE_URL: 'http://127.0.0.1:18080',
        COGLATAS_PERFORMANCE_PASSWORD: 'protected-password',
        PERF_K6_CONFIG: '/private/config.json', PERF_K6_OUTPUT: '/private/result.json',
      },
      http: fixture.transport(runtime), open: () => JSON.stringify(config),
    });
    vm.runInContext(fixture.source, runtime.context);
    return runtime;
  },
  httpFailure: 500,
  httpOk: 200,
  httpUnauthorized: 401,
  iterations: 20,
  lastOrder: 3,
  metricSummary: recorded => ({ metrics: Object.fromEntries([...recorded].map(([name, values]) => [name, {
    values: { count: values.reduce((sum, value) => sum + value, fixture.zero),
      'p(50)': fixture.normalDuration, 'p(95)': fixture.normalDuration, 'p(99)': fixture.normalDuration },
  }])) }),
  mutate: (runtime, body) => {
    const command = JSON.parse(body);
    assert.equal(command.expectedTaskVersion, runtime.state.version);
    assert.equal(command.expectedBoardVersion, runtime.state.version);
    if (runtime.fault !== 'noop') {
      runtime.state.version += fixture.one;
      runtime.state.order = fixture.lastOrder;
      if (command.targetBeforeTaskId) {
        runtime.state.order = fixture.one;
      }
    }
    return { snapshot: fixture.snapshot(runtime) };
  },
  normalDuration: 10,
  normalResponse: (runtime, details) => {
    const json = fixture.responseBody(runtime, details);
    return { json: () => json, status: fixture.responseStatus(runtime, details.url),
      timings: { blocked: fixture.zero, connecting: fixture.zero, duration: fixture.responseDuration(runtime),
        receiving: fixture.one, sending: fixture.one, waiting: fixture.normalDuration - fixture.one - fixture.one } };
  },
  one: 1,
  otherOrder: 2,
  requestFaultBoundary: 40,
  responseBody: (runtime, { body, method, url }) => {
    if (url.includes('csrf-token')) {
      return { headerName: 'X-CSRF', token: 'protected-token' };
    }
    if (url.includes('/kanban?')) {
      return fixture.snapshot(runtime);
    }
    if (method === 'POST' && url.includes('kanban-move')) {
      return fixture.mutate(runtime, body);
    }
    return {};
  },
  responseDuration: runtime => {
    if (runtime.fault === 'slow') {
      return fixture.slowDuration;
    }
    return fixture.normalDuration;
  },
  responseStatus: (runtime, url) => {
    if (url.includes('/api/notifications') && runtime.state.requests > fixture.requestFaultBoundary) {
      return fixture.statusFor(runtime.fault, '500');
    }
    return fixture.httpOk;
  },
  runIterations: runner => {
    for (let iteration = fixture.zero; iteration < fixture.iterations; iteration += fixture.one) {
      vm.runInContext('measure()', runner.context);
    }
  },
  slowDuration: 4000,
  snapshot: runtime => ({ board: { version: runtime.state.version }, cards: [
    { boardOrder: runtime.state.order, taskId: 'moving', uiPermissions: { canMove: true },
      version: runtime.state.version, workflowStageId: 'stage' },
    { boardOrder: fixture.otherOrder, taskId: 'other', uiPermissions: { canMove: true },
      version: fixture.one, workflowStageId: 'stage' },
  ] }),
  source: fs.readFileSync('scripts/performance/api-k6.js', 'utf8')
    .replace(/^import .*;\n/gmu, '')
    .replace(/^export \{[^}]+\};\n/gmu, '')
    .replace('export default function measure()', 'function measure()'),
  statusFor: (fault, expectedFault) => {
    if (fault === expectedFault) {
      return fixture.httpFailure;
    }
    return fixture.httpOk;
  },
  transport: runtime => ({
    get() { return { status: fixture.statusFor(runtime.fault, 'health') }; },
    request(method, url, ...requestArguments) {
      const [body, options] = requestArguments;
      runtime.state.requests += fixture.one;
      if (options.headers?.['X-Performance-Diagnostic']) {
        runtime.state.diagnosticHeaders.push(options.headers);
      }
      assert.equal(options.redirects, fixture.zero);
      if (runtime.fault === 'auth' && url.endsWith('/api/auth/login')) {
        return { status: fixture.httpUnauthorized };
      }
      return fixture.normalResponse(runtime, { body, method, url });
    },
  }),
  zero: 0
};

test('warm-up and login are excluded, mutation advances state, summary has no protected values', () => {
  const runner = fixture.harness();
  fixture.runIterations(runner);
  vm.runInContext('teardown()', runner.context);
  assert.equal(runner.recorded.get('perf_workspace_list_requests').length, fixture.iterations);
  assert.equal(runner.recorded.get('perf_mutation_kanban_move_requests').length, fixture.iterations);
  assert.ok(runner.requests > fixture.iterations * fixture.contract.scenarios.length);
  fixture.assertSafeSummary(runner);
});

test('authentication failure aborts and records failure', () => {
  const runner = fixture.harness('auth');
  assert.throws(() => vm.runInContext('measure()', runner.context), /preflight/u);
  assert.equal(runner.recorded.get('perf_auth_failures')[fixture.zero], fixture.one);
  assert.equal(runner.recorded.get('perf_workspace_list_requests').length, fixture.zero);
});

test('mutation success without persistence cannot report a complete sample group', () => {
  const runner = fixture.harness('noop');
  assert.throws(() => vm.runInContext('measure()', runner.context), /version advance/u);
  assert.equal(runner.recorded.get('perf_mutation_kanban_move_requests').length, fixture.zero);
});

test('post-run health failure is captured', () => {
  const runner = fixture.harness('health');
  vm.runInContext('teardown()', runner.context);
  assert.equal(runner.recorded.get('perf_health_failures')[fixture.zero], fixture.one);
});

test('injected 500 is an error and delay remains visible to the comparator', () => {
  const failed = fixture.harness('500'), slow = fixture.harness('slow');
  fixture.runIterations(failed);
  assert.ok(failed.recorded.get('perf_notification_list_errors').some(value => value === fixture.one));
  vm.runInContext('measure()', slow.context);
  assert.equal(slow.recorded.get('perf_workspace_list_latency')[fixture.zero], fixture.slowDuration);
});

test('opt-in diagnostics bind each measured request and preserve ordinary metric values', () => {
  const diagnostic = fixture.harness('', true), ordinary = fixture.harness();
  fixture.runIterations(ordinary);
  fixture.runIterations(diagnostic);
  assert.deepEqual(diagnostic.recorded.get('perf_mutation_kanban_move_latency'), ordinary.recorded.get('perf_mutation_kanban_move_latency'));
  fixture.assertDiagnosticRows(diagnostic);
});

test('diagnostics are absent without opt-in and send no preflight or warm-up capture headers', () => {
  const diagnostic = fixture.harness('', true), ordinary = fixture.harness();
  fixture.runIterations(ordinary);
  assert.equal(ordinary.state.diagnosticHeaders.length, fixture.zero);
  assert.equal(ordinary.state.diagnosticRows.length, fixture.zero);
  vm.runInContext('measure()', diagnostic.context);
  assert.equal(diagnostic.state.diagnosticHeaders.length, fixture.contract.scenarios.length);
  assert.ok(diagnostic.requests > diagnostic.state.diagnosticHeaders.length);
});
