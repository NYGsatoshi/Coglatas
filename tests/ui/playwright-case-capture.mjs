/** Opt-in, memory-only native TestCase event capture. Never registers or publishes itself. */
import { hashPlaywrightTestId } from './playwright-shard-evidence.mjs';

const statuses = new Set(['passed', 'failed', 'timedOut', 'skipped', 'interrupted']);
const outcomes = new Set(['expected', 'unexpected', 'flaky', 'skipped']);
const expectedStatuses = new Set(['passed', 'skipped']);
const maxCases = 5000;
const maxAttempts = 10;
const deny = () => { throw new Error('CASE_CAPTURE_REFUSED'); };

/** Retains failures/retries instead of converting a later pass into first-attempt success. */
export class NativeCaseCapture {
  constructor() {
    this.phase = 'new';
    this.cases = new Map();
    this.errors = new Set();
    this.globalErrors = 0;
    this.status = null;
  }

  // No stdout, file writes, attachments, titles, URLs, bodies or exception details.
  printsToStdio() { return false; }

  onBegin(config, suite) {
    if (this.phase !== 'new') { this.errors.add('DUPLICATE_BEGIN'); return; }
    this.phase = 'running';
    try {
      const tests = suite.allTests();
      if (!Array.isArray(tests) || !tests.length || tests.length > maxCases) deny();
      for (const test of tests) {
        const id = hashPlaywrightTestId(test.id);
        if (this.cases.has(id) || !expectedStatuses.has(test.expectedStatus)) deny();
        this.cases.set(id, { test, listed: true, declaredExpectedStatus: test.expectedStatus, attempts: [] });
      }
    } catch { this.errors.add('INVALID_BEGIN'); }
  }

  onTestEnd(test, result) {
    if (this.phase !== 'running') { this.errors.add('EVENT_PHASE'); return; }
    try {
      const id = hashPlaywrightTestId(test.id);
      let record = this.cases.get(id);
      if (!record) {
        if (this.cases.size >= maxCases) deny();
        record = { test, listed: false, declaredExpectedStatus: null, attempts: [] };
        this.cases.set(id, record);
        this.errors.add('UNLISTED_CASE');
      }
      if (!result || !statuses.has(result.status) || !Number.isSafeInteger(result.retry) ||
          result.retry < 0 || result.retry >= maxAttempts || record.attempts.length >= maxAttempts) deny();
      if (record.attempts.some(attempt => attempt.retry === result.retry)) this.errors.add('DUPLICATE_RESULT');
      record.attempts.push({ retry: result.retry, status: result.status });
      record.test = test;
    } catch { this.errors.add('INVALID_RESULT'); }
  }

  onError() { this.globalErrors = Math.min(this.globalErrors + 1, 1000000); }

  onEnd(result) {
    if (this.phase !== 'running') this.errors.add('END_PHASE');
    this.phase = 'finished';
    this.status = result?.status;
    if (!['passed', 'failed', 'timedout', 'interrupted'].includes(this.status)) {
      this.status = 'interrupted'; this.errors.add('INVALID_END');
    }
    // Observer only: never overrides the existing native runner's conclusion.
  }

  snapshot(metadata = {}) {
    if (!metadata || typeof metadata !== 'object' || Array.isArray(metadata)) deny();
    const { sourceSha, executionKind, runId, runAttempt } = metadata;
    if (this.phase !== 'finished' || typeof sourceSha !== 'string' || !/^[a-f0-9]{40}$/.test(sourceSha) ||
        !['LOCAL', 'GITHUB_ACTIONS'].includes(executionKind) ||
        typeof runId !== 'string' || !(executionKind === 'LOCAL'
          ? /^local-[a-f0-9]{32}$/.test(runId) : /^[1-9][0-9]{0,18}$/.test(runId)) ||
        !Number.isSafeInteger(runAttempt) || runAttempt < 1 || runAttempt > 1000000) deny();
    const tests = [...this.cases].map(([id, record]) => {
      let outcome = 'unexpected';
      try {
        if (hashPlaywrightTestId(record.test.id) !== id) deny();
        const actual = record.test.outcome();
        if (!outcomes.has(actual)) deny();
        outcome = actual;
        if (!expectedStatuses.has(record.test.expectedStatus)) deny();
      } catch { this.errors.add('INVALID_CASE_END'); }
      return { id, listed: record.listed, declaredExpectedStatus: record.declaredExpectedStatus,
        expectedStatus: expectedStatuses.has(record.test.expectedStatus) ? record.test.expectedStatus : null,
        outcome, attempts: record.attempts.map(attempt => ({ ...attempt })) };
    }).sort((a, b) => a.id < b.id ? -1 : a.id > b.id ? 1 : 0);
    return {
      schema: 'coglatas.playwright-case-execution.v1', suite: 'angular-static', sourceSha,
      executionKind, runId, runAttempt, producer: 'playwright-testcase-events-v1',
      status: this.status, captureComplete: this.errors.size === 0 && this.globalErrors === 0 &&
        tests.length > 0 && tests.every(test => test.attempts.length > 0),
      globalErrors: this.globalErrors, captureErrors: [...this.errors].sort(), tests,
    };
  }
}
