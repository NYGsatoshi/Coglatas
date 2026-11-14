/** Opt-in source-only companion. Existing reporters and roster schema are unchanged. */
import { collectApplicability } from '../../scripts/ci/playwright-source-applicability.mjs';
export default class ApplicabilityReporter {
  constructor() { this.phase = 'new'; this.record = null; this.failed = false; }
  printsToStdio() { return true; }
  onBegin(config, suite) {
    try {
      if (this.phase !== 'new' || process.env.COGLATAS_SOURCE_APPLICABILITY_LIST !== '1') throw new Error();
      this.phase = 'listed';
      this.record = collectApplicability(process.cwd(), process.env.COGLATAS_SOURCE_DISCOVERY_SHA, config, suite);
    } catch { this.failed = true; }
  }
  onTestBegin() { this.failed = true; }
  onTestEnd() { this.failed = true; }
  onError() { this.failed = true; }
  onEnd(result) {
    if (this.phase !== 'listed' || this.failed || result.status !== 'passed' || !this.record) {
      process.stderr.write('SOURCE_APPLICABILITY_REFUSED\n'); return { status: 'failed' };
    }
    this.phase = 'finished';
    process.stdout.write(JSON.stringify(this.record) + '\n');
  }
}
