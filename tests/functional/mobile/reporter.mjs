import { mkdirSync, readFileSync, writeFileSync, rmSync } from 'node:fs';
import { assertMobileProfile, mobileProject, validateMobileOwners } from './profile.mjs';

/** Publish finite outcomes only; ignore protected attachments and assertion messages. */
export default class MobileReporter {
  onBegin(config, suite) {
    this.listOnly = process.argv.includes('--list');
    this.config = config;
    this.tests = suite.allTests();
    rmSync('artifacts/mobile/owners.json', { force: true });
    if (this.listOnly) { console.log(`Mobile discovery: ${this.tests.length} owners; NOT_EXECUTED.`); }
  }

  onEnd(result) {
    if (this.listOnly) { return undefined; }
    try {
      if (result.status !== 'passed' || this.config.projects.length !== 1 || this.config.workers !== 1 ||
          this.config.projects[0].retries !== 0) { throw new Error('Mobile suite did not pass its fixed configuration.'); }
      assertMobileProfile('chromium', this.config.projects[0]);
      const sha = process.env.TARGET_SHA;
      if (!/^[0-9a-f]{40}$/.test(sha ?? '') || readFileSync('artifacts/mobile/source-sha', 'utf8').trim() !== sha) {
        throw new Error('Mobile source identity is unavailable.');
      }
      const records = this.tests.map((test) => ({
        id: test.annotations.find((item) => item.type === 'journey')?.description,
        backend: test.annotations.find((item) => item.type === 'backend')?.description,
        project: test.parent.project().name, expectedStatus: test.expectedStatus,
        attempts: test.results.map((attempt) => ({ status: attempt.status, retry: attempt.retry,
          steps: attempt.steps.filter((step) => step.category === 'test.step' && !step.error)
            .map((step) => step.title.match(/\/\s+(MSG-[A-Z0-9-]+|NOTIF-\d+|F05-FAST-\d+|MOB-\d+)\b/)?.[1]) })),
      }));
      const journeys = validateMobileOwners(records);
      mkdirSync('artifacts/mobile', { recursive: true });
      writeFileSync('artifacts/mobile/owners.json', JSON.stringify({ schemaVersion: 1, commitSha: sha,
        runId: process.env.GITHUB_RUN_ID ?? 'local', runAttempt: process.env.GITHUB_RUN_ATTEMPT ?? 'local',
        profile: mobileProject, viewport: { width: 320, height: 800 }, touch: true,
        workers: 1, retries: 0, journeys,
        limits: ['API-prepared Files fixture; UI upload unavailable', 'No physical IME or target HTTPS qualification',
          'No held Pilot-01 private-channel lifecycle qualification', 'No latest-design approval or release authorization'],
      }) + '\n');
      console.log('Real-backend mobile acceptance: 4 owners passed; 0 failed, skipped or retried.');
    } catch {
      console.error('Real-backend mobile acceptance failed; no owner PASS receipt was issued.');
      return { status: 'failed' };
    }
    return undefined;
  }
}
