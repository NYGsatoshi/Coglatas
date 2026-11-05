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
    this.record = null;
  }

  // Keep Playwright from appending its default list-mode title reporter.
  printsToStdio() { return true; }

  onBegin(_config, suite) {
    if (this.phase !== 'new') {
      this.failed = true;
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
      this.record = null;
    }
  }

  onTestBegin() { this.failed = true; }
  onTestEnd() { this.failed = true; }
  onError() { this.failed = true; }

  onEnd(full) {
    if (this.phase !== 'listed' || this.failed ||
        full?.status !== 'passed' || !this.record) {
      this.failed = true;
      this.phase = 'finished';
      return { status: 'failed' };
    }
    this.phase = 'finished';
    try {
      const encoded = JSON.stringify(this.record);
      if (encoded.length > 512 * 1024) throw new Error('report too large');
      this.write(encoded + '\n');
    } catch {
      this.failed = true;
      return { status: 'failed' };
    }
    return undefined;
  }
}
