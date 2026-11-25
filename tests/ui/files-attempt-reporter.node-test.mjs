import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, symlinkSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import FilesAttemptReporter, { filesReportByteLimit, filesReportInput, validateFilesAttempts, summarizeHostedFilesAttempts } from './files-attempt-reporter.mjs';
import { collectFilesAttempts } from '../../scripts/ci/collect-files-attempt-report.mjs';

const failure = { project: 'chromium-desktop', caseId: 'viewport-scroll-1536', attempt: 0, status: 'failed', durationMs: 5021 };
const retry = { ...failure, attempt: 1, status: 'passed', durationMs: 1600 };
const validAttempts = [failure, retry];

test('keeps the failed first attempt when its retry passes', () => {
  assert.deepEqual(validateFilesAttempts(validAttempts), validAttempts);
});

test('keeps attempt numbers scoped to browser profile and test identity', () => {
  const otherProfile = { ...failure, project: 'webkit-mobile', status: 'timedOut', durationMs: 30_004 };
  const otherCase = { ...failure, caseId: 'expanded-focus-1366', status: 'passed' };
  assert.deepEqual(validateFilesAttempts([failure, otherProfile, otherCase, retry]), [failure, otherProfile, otherCase, retry]);
});

test('keeps timeout, interruption and skip as distinct statuses', () => {
  for (const status of ['timedOut', 'skipped', 'interrupted']) {
    const attempt = { ...failure, status };
    assert.deepEqual(validateFilesAttempts([attempt]), [attempt]);
  }
});

test('keeps two failed attempts followed by a passing retry', () => {
  const attempts = [failure, { ...failure, attempt: 1 }, { ...retry, attempt: 2 }];
  assert.deepEqual(validateFilesAttempts(attempts), attempts);
});

const rejectedAttempts = [
  ['empty reports', []],
  ['non-array reports', { failure }],
  ['unknown browser profiles', [{ ...failure, project: 'other-project' }]],
  ['unknown case identities', [{ ...failure, caseId: 'unknown-test-id' }]],
  ['uncontrolled statuses', [{ ...failure, status: 'flaky' }]],
  ['arbitrary diagnostic messages', [{ ...failure, message: 'synthetic private value' }]],
  ['non-numeric durations', [{ ...failure, durationMs: '5000' }]],
  ['out-of-bounds durations', [{ ...failure, durationMs: 600_001 }]],
  ['negative attempt indexes', [{ ...failure, attempt: -1 }]],
  ['attempts above the existing retry limit', [{ ...failure, attempt: 3 }]],
  ['a retry without the first attempt', [retry]],
  ['duplicate attempts', [failure, failure]],
  ['a skipped retry index', [failure, { ...retry, attempt: 2 }]],
  ['a retry after a passing attempt', [{ ...failure, status: 'passed' }, retry]],
  ['records above the approved bound', Array.from({ length: 181 }, () => failure)],
];
for (const [description, attempts] of rejectedAttempts) {
  test(`rejects ${description}`, () => {
    assert.throws(() => validateFilesAttempts(attempts), /approved evidence schema/);
  });
}

test('projects Playwright results without messages, attachments or ambient paths', () => {
  const reporter = new FilesAttemptReporter();
  const titlePath = ['', 'chromium-desktop', 'file-image-viewport.spec.ts', 'Authorized Files image viewport',
    'fits, rotates and scrolls real wide and portrait PNGs at 1536px'];
  const selected = { location: { file: '/ambient/path/tests/ui/file-image-viewport.spec.ts' }, titlePath: () => titlePath };
  reporter.onTestEnd(selected, {
    retry: 0, status: 'failed', duration: 5021, errors: [{ message: 'synthetic secret' }],
    attachments: [{ name: 'trace', path: '/private/fixture.zip' }],
  });
  reporter.onTestEnd({
    location: { file: '/ambient/path/tests/unowned/file-image-viewport.spec.ts' },
    titlePath: () => titlePath,
  }, { retry: 0, status: 'failed', duration: 99 });
  assert.deepEqual(reporter.attempts, [failure]);
});

function fixture(t) {
  const root = mkdtempSync(join(tmpdir(), 'coglatas-files-report-'));
  t.after(() => rmSync(root, { recursive: true, force: true }));
  const git = (...args) => execFileSync('git', args, { cwd: root, encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] }).trim();
  git('init', '-q');
  writeFileSync(join(root, 'tracked.txt'), 'current source\n');
  git('add', 'tracked.txt');
  git('-c', 'user.name=Test Fixture', '-c', 'user.email=fixture@example.invalid', 'commit', '-qm', 'Current source');
  const sourceSha = git('rev-parse', 'HEAD');
  const archive = join(root, 'frontend-build-artifacts.tar');
  writeFileSync(archive, 'synthetic producer archive bytes');
  mkdirSync(join(root, 'artifacts/ci'), { recursive: true });
  mkdirSync(join(root, 'artifacts/files-attempt-report'));
  const marker = join(root, 'artifacts/ci/frontend-build-sha');
  writeFileSync(marker, sourceSha + '\n');
  writeFileSync(join(root, filesReportInput), JSON.stringify(validAttempts) + '\n');
  const options = { sourceSha, runId: '12345', runAttempt: '2', job: 'frontend-test', frontendChecksOutcome: 'failure', archive };
  return { root, marker, options };
}

test('binds source, build, workflow run, workflow attempt and artifact bytes', async (t) => {
  const { root, options } = fixture(t);
  assert.equal(await collectFilesAttempts(root, options), 2);
  const artifact = join(root, 'artifacts/files-attempt-validated/files-attempts.json');
  const bytes = readFileSync(artifact);
  assert.deepEqual(JSON.parse(bytes), {
    schemaVersion: 1,
    sourceSha: options.sourceSha,
    producerSha: options.sourceSha,
    producerArchiveSha256: createHash('sha256').update(readFileSync(options.archive)).digest('hex'),
    runId: options.runId,
    runAttempt: options.runAttempt,
    job: options.job,
    frontendChecksOutcome: options.frontendChecksOutcome,
    coverage: { complete: false, expectedCaseCount: 24, completedCaseCount: 1, missingCaseCount: 23 },
    suite: 'tests/ui/file-image-viewport.spec.ts',
    attempts: validAttempts,
  });
  assert.equal(readFileSync(artifact + '.sha256', 'utf8'), createHash('sha256').update(bytes).digest('hex') + '  files-attempts.json\n');
});

test('withholds output for the wrong source or a dirty tracked checkout', async (t) => {
  const { root, options } = fixture(t);
  await assert.rejects(collectFilesAttempts(root, { ...options, sourceSha: '0'.repeat(40) }));
  writeFileSync(join(root, 'tracked.txt'), 'changed source\n');
  await assert.rejects(collectFilesAttempts(root, options));
  assert.equal(existsSync(join(root, 'artifacts/files-attempt-validated')), false);
});

test('withholds output for wrong producer, missing archive or invalid run identity', async (t) => {
  const { root, marker, options } = fixture(t);
  await assert.rejects(collectFilesAttempts(root, { ...options, job: 'other-job' }));
  await assert.rejects(collectFilesAttempts(root, { ...options, runId: 'not-a-run-id' }));
  await assert.rejects(collectFilesAttempts(root, { ...options, runAttempt: '0' }));
  await assert.rejects(collectFilesAttempts(root, { ...options, archive: join(root, 'missing.tar') }));
  writeFileSync(marker, '0'.repeat(40) + '\n');
  await assert.rejects(collectFilesAttempts(root, options));
  assert.equal(existsSync(join(root, 'artifacts/files-attempt-validated')), false);
});

test('withholds output if the report is oversized or symlinked', async (t) => {
  const { root, options } = fixture(t);
  const input = join(root, filesReportInput);
  writeFileSync(input, Buffer.alloc(filesReportByteLimit + 1));
  await assert.rejects(collectFilesAttempts(root, options));
  rmSync(input);
  symlinkSync(options.archive, input);
  await assert.rejects(collectFilesAttempts(root, options));
  assert.equal(existsSync(join(root, 'artifacts/files-attempt-validated')), false);
});

test('rejects raw report fields and keeps raw browser artifacts out of the output', async (t) => {
  const { root, options } = fixture(t);
  writeFileSync(join(root, filesReportInput), JSON.stringify([{ ...failure, screenshot: 'private.png' }]));
  writeFileSync(join(root, 'artifacts/files-attempt-report/raw-trace.zip'), 'trace content');
  await assert.rejects(collectFilesAttempts(root, options));
  assert.equal(existsSync(join(root, 'artifacts/files-attempt-validated')), false);
});

test('refuses to replace already validated evidence', async (t) => {
  const { root, options } = fixture(t);
  await collectFilesAttempts(root, options);
  const artifact = join(root, 'artifacts/files-attempt-validated/files-attempts.json');
  const original = readFileSync(artifact);
  writeFileSync(join(root, filesReportInput), JSON.stringify([failure]) + '\n');
  await assert.rejects(collectFilesAttempts(root, options));
  assert.deepEqual(readFileSync(artifact), original);
});

test('refuses a successful hosted lane with incomplete Files coverage', async (t) => {
  const { root, options } = fixture(t);
  await assert.rejects(collectFilesAttempts(root, { ...options, frontendChecksOutcome: 'success' }));
  assert.equal(existsSync(join(root, 'artifacts/files-attempt-validated')), false);
});

test('requires every hosted case to finish with a pass on successful lanes', () => {
  const cases = ['download-session-expiry', 'protected-state-reset'];
  for (const width of [320, 390, 768, 1366, 1536]) {
    cases.push(`expanded-focus-${width}`, `viewport-scroll-${width}`);
  }
  const attempts = ['chromium-desktop', 'chromium-mobile'].flatMap((project) =>
    cases.map((caseId) => ({ ...failure, project, caseId, status: 'passed' })));
  const firstAttempt = { ...attempts[0], status: 'failed' };
  attempts[0] = firstAttempt;
  attempts.push({ ...firstAttempt, attempt: 1, status: 'passed' });
  assert.deepEqual(summarizeHostedFilesAttempts(validateFilesAttempts(attempts), 'success'), {
    complete: true, expectedCaseCount: 24, completedCaseCount: 24, missingCaseCount: 0,
  });
  attempts.pop();
  assert.throws(() => summarizeHostedFilesAttempts(validateFilesAttempts(attempts), 'success'));
  assert.equal(summarizeHostedFilesAttempts(validateFilesAttempts(attempts), 'failure').complete, true);
  assert.equal(summarizeHostedFilesAttempts(validateFilesAttempts([failure]), 'cancelled').complete, false);
});
