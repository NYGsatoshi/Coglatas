import { mobileMvp0FailureLocations } from './mobile-mvp0-profile.mjs';
const statuses = new Set(['passed', 'failed', 'timedOut', 'timedout', 'interrupted', 'skipped']);
const safeStatus = value => statuses.has(value) ? value : 'UNKNOWN';

/** Explicit stdout ownership prevents Playwright's raw terminal-reporter fallback. */
export default class MobileMvp0Reporter {
  constructor(options = {}) { this.write = options.write ?? console.log; }
  printsToStdio() { return true; }
  onBegin() { this.write('Independent native Mobile MVP0 scenario running; output is closed metadata only.'); }
  onStdOut() { /* Worker output can contain protected values; JUnit retains it only in the workspace. */ }
  onStdErr() { /* Never mirror arbitrary worker output. */ }
  onError() { this.write(JSON.stringify({ mobileMvp0Error: 'GLOBAL_ERROR' })); }
  onTestEnd(_test, result) {
    const errors = Array.isArray(result.errors) ? result.errors.slice(0, 3) : [result.error];
    const locations = mobileMvp0FailureLocations(errors.map(error => error?.stack ?? '').join('\n'));
    this.write(JSON.stringify({ mobileMvp0Case: safeStatus(result.status),
      retry: Number.isSafeInteger(result.retry) && result.retry >= 0 && result.retry <= 10 ? result.retry : null,
      locations }));
  }
  onEnd(result) { this.write(JSON.stringify({ mobileMvp0Run: safeStatus(result.status) })); }
}
