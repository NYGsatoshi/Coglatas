/**
 * Nonpublishing Playwright reporter-shaped adapter for future shard receipts.
 *
 * Never infer the full suite from shard-only reporter discovery. A source-bound
 * independent full roster and GitHub artifact attestation are still required.
 * This adapter does not register in Playwright config or affect any CI gate.
 */
import { constructUnverifiedShardEvidence } from './playwright-shard-evidence.mjs';

const identity = value => typeof value === 'string' &&
  value.length > 0 && value.length <= 512 &&
  !/[\u0000-\u001f\u007f]/.test(value);

export class UnverifiedShardCapture {
  constructor() {
    this.phase = 'new';
    this.invalid = false;
    this.discovered = [];
    this.results = [];
  }

  printsToStdio() { return false; }

  onBegin(_config, suite) {
    if (this.phase !== 'new') {
      this.invalid = true;
      return;
    }
    this.phase = 'running';
    try {
      const tests = suite?.allTests?.();
      if (!Array.isArray(tests) || tests.length === 0 ||
          tests.length > 5000) throw new Error('invalid discovery');
      const seen = new Set();
      for (const t of tests) {
        if (!identity(t?.id) ||
            (t.expectedStatus !== 'passed' &&
             t.expectedStatus !== 'skipped') || seen.has(t.id)) {
          throw new Error('invalid testcase');
        }
        seen.add(t.id);
        this.discovered.push({ id: t.id, expectedStatus: t.expectedStatus });
      }
    } catch {
      // Playwright swallows reporter exceptions; remember faults for onEnd().
      this.invalid = true;
    }
  }

  onTestEnd(test, result) {
    if (this.phase !== 'running' || this.invalid) {
      this.invalid = true;
      return;
    }
    try {
      const expected = this.discovered.find(t => t.id === test?.id);
      const outcome = test?.outcome?.();
      if (!expected || !result ||
          result.retry !== 0 ||
          (result.status !== 'passed' && result.status !== 'skipped') ||
          result.status !== expected.expectedStatus ||
          outcome !== (result.status === 'passed' ? 'expected' : 'skipped') ||
          this.results.some(t => t.id === test.id)) {
        throw new Error('unqualified test event');
      }
      this.results.push({
        id: test.id, status: result.status, retry: 0, outcome,
      });
    } catch {
      this.invalid = true;
    }
  }

  onError(_error) {
    // Never return/log browser errors or identifiers in a receipt.
    this.invalid = true;
  }

  onEnd(result) {
    if (this.phase !== 'running' || result?.status !== 'passed' ||
        this.results.length !== this.discovered.length) {
      this.invalid = true;
    }
    this.phase = 'finished';
    // Playwright's onEnd return value can override an otherwise green runner.
    return this.invalid ? { status: 'failed' } : undefined;
  }

  snapshot({ metadata, independentlyDiscoveredFullTests }) {
    if (this.invalid || this.phase !== 'finished') {
      throw new Error('SHARD_EVIDENCE_REFUSED:UNQUALIFIED_REPORTER_EVENTS');
    }
    return constructUnverifiedShardEvidence({
      metadata, fullTests: independentlyDiscoveredFullTests,
      shardTests: this.discovered, results: this.results,
    });
  }
}
