import { mkdirSync, rmSync, writeFileSync } from 'node:fs';
import { CASES, REGRESSIONS, validateExecutedCases } from './thread-matrix-contract.mjs';
import { failureKind } from '../functional/fixtures/functional-diagnostics.mjs';

/** Mocked UI investigation only; no raw title, error, URL, text or pixels. */
export default class ThreadMatrixReporter {
  onBegin(config, suite) {
    this.config = config; this.tests = suite.allTests();
    this.mode = process.env.THREAD_INVESTIGATION_MODE ?? 'matrix';
    this.path = `artifacts/thread-investigation/${this.mode === 'default-fixture' ? 'default-fixture' : 'matrix'}.json`;
    this.listOnly = process.argv.includes('--list'); rmSync(this.path, { force: true });
    if (this.listOnly) { console.log(`Mocked Thread diagnostic discovery: ${this.tests.length}; NOT_EXECUTED.`); }
  }
  onEnd(result) {
    if (this.listOnly) { return; }
    const rows = this.tests.map((test) => {
      const annotated = test.annotations.find((item) => item.type === 'investigationCase')?.description;
      const id = this.mode === 'matrix' ? CASES.includes(annotated) ? annotated : 'UNKNOWN' :
        REGRESSIONS.includes(test.title) ? `EXISTING-0${REGRESSIONS.indexOf(test.title) + 1}` : 'UNKNOWN';
      const selected = (test.results[0]?.attachments ?? []).filter((item) => item.name === 'thread-matrix-observation-v1');
      let observation = null;
      try { if (selected.length === 1 && selected[0].body?.length <= 4096) { observation = JSON.parse(selected[0].body.toString('utf8')); } } catch { /* Refuse incomplete data below. */ }
      return { id, expectedStatus: test.expectedStatus, attempts: test.results.map((attempt) => ({ status: attempt.status, retry: attempt.retry })), observation,
        failureKind: test.results.length ? failureKind(test.results[0]) : 'NOT_EXECUTED' };
    });
    try {
      const use = this.config.projects[0]?.use;
      if (result.status !== 'passed' || this.config.projects.length !== 1 || this.config.workers !== 1 || this.config.maxFailures !== 1 ||
          this.config.projects[0].retries !== 0 || use.browserName !== 'chromium' || use.viewport?.width !== 320 || use.viewport?.height !== 800 ||
          use.screen?.width !== 320 || use.screen?.height !== 800 || use.hasTouch !== true || use.isMobile !== true || use.deviceScaleFactor !== 1) {
        throw new Error('Invalid diagnostic execution.');
      }
      const cases = validateExecutedCases(rows, this.mode);
      mkdirSync('artifacts/thread-investigation', { recursive: true });
      writeFileSync(this.path, JSON.stringify({ schemaVersion: 1, scope: 'MOCKED_TRANSPORT_UI_MECHANISM_ONLY',
        status: 'DIAGNOSTIC_COMPLETE', mode: this.mode, sourceSha: /^[0-9a-f]{40}$/.test(process.env.THREAD_MATRIX_SOURCE_SHA ?? '') ? process.env.THREAD_MATRIX_SOURCE_SHA : 'UNCOMMITTED_DIAGNOSTIC',
        profile: 'mocked-thread-matrix-320-touch', viewport: { width: 320, height: 800 }, workers: 1, retries: 0,
        testTimeoutMs: this.mode === 'matrix' ? 120000 : 30000, expectTimeoutMs: this.mode === 'matrix' ? 15000 : 5000,
        testDefinedViewportOverrides: this.mode === 'default-fixture', cases,
        limits: ['No real backend, cookie or actor-isolation qualification', 'No historical bed6 root-cause proof', 'No release authorization'],
      }) + '\n');
      console.log(`Mocked Thread ${this.mode} diagnostic complete: 3 cases; 0 failed/skipped/retried. No backend or release credit.`);
    } catch {
      console.error('Mocked Thread diagnostic incomplete: ' + JSON.stringify(rows.map((row) => ({ id: row.id,
        status: ['passed', 'failed', 'timedOut', 'skipped', 'interrupted'].includes(row.attempts[0]?.status) ? row.attempts[0].status : 'NOT_EXECUTED',
        failureKind: row.failureKind }))));
      return { status: 'failed' };
    }
  }
}
