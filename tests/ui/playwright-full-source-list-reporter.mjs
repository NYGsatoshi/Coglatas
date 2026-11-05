/**
 * Nonpublishing Playwright 1.64 --list Reporter for full static source discovery.
 * Only a fixed JSON object of SHA-256 IDs and their declared expected statuses
 * is printed, never raw TestCase titles, file paths or browser data.
 */
import { constructSourceOnlyDiscovery } from './playwright-shard-evidence.mjs';

export default class SourceListReporter {
  constructor(options = {}) {
    this.sourceSha = options.sourceSha ?? process.env.COGLATAS_SOURCE_DISCOVERY_SHA;
    this.listOnly = options.listOnly ?? process.env.COGLATAS_SOURCE_DISCOVERY_LIST;
    this.write = options.write ?? (value => process.stdout.write(value));
    this.phase = 'new';
    this.failed = false;
    this.reason = 'UNQUALIFIED_LIST';
    this.record = null;
  }

  // Keep Playwright from appending its default list-mode title reporter.
  printsToStdio() { return true; }

  onBegin(_config, suite) {
    if (this.phase !== 'new') {
      this.failed = true;
      this.reason = 'DUPLICATE_ONBEGIN';
      return;
    }
    this.phase = 'listed';
    try {
      if (this.listOnly !== '1') throw new Error('not opted in');
      const tests = suite?.allTests?.();
      this.record = constructSourceOnlyDiscovery({
        sourceSha: this.sourceSha,
        discoveredTests: tests?.map(t => ({
          id: t.id, expectedStatus: t.expectedStatus,
        })),
      });
    } catch {
      this.failed = true;
      this.reason = 'ONBEGIN_INVALID_CASES';
      this.record = null;
    }
  }

  onTestBegin() { this.failed = true; this.reason = 'UNEXPECTED_TEST_EXECUTION'; }
  onTestEnd() { this.failed = true; this.reason = 'UNEXPECTED_TEST_EXECUTION'; }
  onError() { this.failed = true; this.reason = 'PLAYWRIGHT_GLOBAL_ERROR'; }

  onEnd(full) {
    if (full?.status !== 'passed' && !this.failed) {
      this.reason = 'PLAYWRIGHT_LIST_STATUS_FAILED';
    }
    if (this.phase !== 'listed' || this.failed ||
        full?.status !== 'passed' || !this.record) {
      this.failed = true;
      this.phase = 'finished';
      // The parent captures stderr and only prints this predefined code.
      process.stderr.write('COGLATAS_SOURCE_ROSTER_STATUS=' + this.reason + '\n');
      return { status: 'failed' };
    }
    this.phase = 'finished';
    try {
      const encoded = JSON.stringify(this.record);
      if (encoded.length > 512 * 1024) throw new Error('report too large');
      this.write(encoded + '\n');
    } catch {
      this.failed = true;
      this.reason = 'REPORTER_OUTPUT_FAILED';
      process.stderr.write('COGLATAS_SOURCE_ROSTER_STATUS=' + this.reason + '\n');
      return { status: 'failed' };
    }
    return undefined;
  }
}
