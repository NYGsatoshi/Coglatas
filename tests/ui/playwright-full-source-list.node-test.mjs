import assert from 'node:assert/strict';
import test from 'node:test';
import { mkdtempSync, mkdirSync, readFileSync, writeFileSync, rmSync, symlinkSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { spawnSync } from 'node:child_process';
import { join } from 'node:path';
import SourceListReporter from './playwright-full-source-list-reporter.mjs';
import {
  extractStaticSpecFiles, verifySourceDiscovery, persistNonpublishingRoster,
  verifyPinnedSourceFiles,
} from '../../scripts/ci/qualify-playwright-full-source-list.mjs';

const SHA = 'a'.repeat(40);
const fixture = () => Array.from({ length: 456 }, (_, i) => ({
  id: 'private/playwright-case-' + String(i).padStart(4, '0'),
  expectedStatus: i < 13 ? 'skipped' : 'passed',
}));

function report(tests = fixture(), { sourceSha = SHA, listOnly = '1' } = {}) {
  const stdout = [];
  const reporter = new SourceListReporter({
    sourceSha, listOnly, write: value => stdout.push(value),
  });
  reporter.onBegin({}, { allTests: () => tests });
  return { reporter, stdout };
}

function successfulDiscovery() {
  const { reporter, stdout } = report();
  assert.equal(reporter.onEnd({ status: 'passed' }), undefined);
  assert.equal(stdout.length, 1);
  return JSON.parse(stdout[0]);
}

test('exact original static spec inventory is extracted from its source, not copied', () => {
  const runner = readFileSync('tests/ui/run-angular-playwright.mjs', 'utf8');
  const suiteFiles = extractStaticSpecFiles(runner);
  assert.equal(suiteFiles.length, 24);
  assert.ok(suiteFiles.includes('tests/ui/angular-smoke.spec.ts'));
  assert.ok(suiteFiles.includes('tests/ui/app.spec.ts'));
  assert.equal(new Set(suiteFiles).size, 24);
  assert.ok(suiteFiles.every(file => /^tests\/ui\/[a-z0-9-]+\.spec\.ts$/.test(file)));
});

test('duplicate source selectors, arbitrary JS and unknown test files are rejected', () => {
  const runner = readFileSync('tests/ui/run-angular-playwright.mjs', 'utf8');
  for (const mutated of [
    runner.replace("'tests/ui/app.spec.ts'", "'tests/ui/app.spec.ts',\n  'tests/ui/app.spec.ts'"),
    runner.replace("'tests/ui/app.spec.ts'", "'tests/ui/../app.spec.ts'"),
    runner.replace("'tests/ui/app.spec.ts'", 'getUntrustedTestList()'),
    runner.replace('const staticSuiteFiles = [', 'const obsoleteSuiteFiles = ['),
    runner + '\nconst staticSuiteFiles = [\n  \'tests/ui/app.spec.ts\'\n];\n',
  ]) {
    assert.throws(() => extractStaticSpecFiles(mutated), /DISCOVERY_REFUSED/);
  }
});

test('source discovery rejects filtered invocation and extra selectors while preserving argument helpers', () => {
  const runner = readFileSync('tests/ui/run-angular-playwright.mjs', 'utf8');
  assert.equal(extractStaticSpecFiles(runner).length, 24);
  for (const changed of [
    runner.replace('...staticSuiteFiles, ...playwrightArgs,',
      '...staticSuiteFiles.filter(() => false), ...playwrightArgs,'),
    runner.replace('...staticSuiteFiles, ...playwrightArgs,',
      "...staticSuiteFiles, '--grep=partial', ...playwrightArgs,"),
    runner.replace('...staticSuiteFiles, ...playwrightArgs,',
      '...staticSuiteFiles, ...playwrightArgs.slice(0, 1),'),
    runner.replace('...staticPlaywrightWorkerArgs(process.env, playwrightArgs),',
      '...staticPlaywrightWorkerArgs(process.env, []),'),
    runner.replace('...staticPlaywrightShardArgs(process.env, playwrightArgs),',
      "'--project=chromium-desktop',"),
    runner.replace('const playwrightArgs = process.argv.slice(2);',
      "const playwrightArgs = ['--grep=partial'];"),
    runner.replace('exitCode = await runPlaywright();',
      'staticSuiteFiles.splice(0, 1); exitCode = await runPlaywright();'),
    runner.replace('exitCode = await runPlaywright();',
      'const selectedAlias = staticSuiteFiles; selectedAlias.pop(); exitCode = await runPlaywright();'),
    runner.replace('exitCode = await runPlaywright();',
      "staticSuiteFiles[0] = 'tests/ui/app.spec.ts'; exitCode = await runPlaywright();"),
  ]) {
    assert.notEqual(changed, runner);
    assert.throws(() => extractStaticSpecFiles(changed), /DISCOVERY_REFUSED/);
  }
});

/** A matching HEAD must never qualify mutated tracked source bytes. */
test('pinned tracked source refuses same-size working-tree edits', () => {
  const root = mkdtempSync(join(tmpdir(), 'coglatas-source-content-'));
  const selected = ['tests/ui/example.spec.ts'];
  const runnerPath = join(root, 'tests/ui/run-angular-playwright.mjs');
  const configPath = join(root, 'playwright.config.ts');
  const specPath = join(root, selected[0]);
  const content = new Map([
    [runnerPath, 'const full = true;\n'],
    [configPath, 'export default {};\n'],
    [specPath, "test('one',()=>{});\n"],
  ]);
  const git = args => {
    const result = spawnSync('git', args, {
      cwd: root, encoding: 'utf8', timeout: 15000, maxBuffer: 65536,
    });
    assert.equal(result.status, 0, 'Synthetic Git control failed');
    return result.stdout.trim();
  };
  try {
    mkdirSync(join(root, 'tests/ui'), { recursive: true });
    for (const [file, source] of content) writeFileSync(file, source);
    git(['init', '--quiet']);
    git(['add', '--', 'playwright.config.ts',
      'tests/ui/run-angular-playwright.mjs', selected[0]]);
    git(['-c', 'user.name=CI', '-c', 'user.email=ci@example.invalid',
      '-c', 'commit.gpgsign=false', 'commit', '--quiet', '-m', 'pinned fixture']);
    const sha = git(['rev-parse', 'HEAD']);
    assert.match(sha, /^[a-f0-9]{40}$/);
    verifyPinnedSourceFiles(root, sha, selected);
    for (const [file, original] of content) {
      const mutated = original.replace(/one|true|default/, word =>
        ({ one: 'two', true: 'nope', default: 'defaUlt' })[word]);
      assert.notEqual(mutated, original);
      assert.equal(mutated.length, original.length);
      writeFileSync(file, mutated);
      assert.throws(() => verifyPinnedSourceFiles(root, sha, selected),
        /DISCOVERY_REFUSED:SOURCE_CONTENT_MISMATCH/);
      writeFileSync(file, original);
      verifyPinnedSourceFiles(root, sha, selected);
    }
    assert.throws(() => verifyPinnedSourceFiles(root, sha,
      ['tests/ui/../example.spec.ts']), /DISCOVERY_REFUSED:SOURCE_CONTENT_MISMATCH/);
  } finally { rmSync(root, { recursive: true, force: true }); }
});

test('valid list reporter emits one source-only, privacy-safe discovery record', () => {
  const { reporter, stdout } = report();
  assert.equal(reporter.printsToStdio(), true);
  assert.equal(reporter.onEnd({ status: 'passed' }), undefined);
  assert.equal(stdout.length, 1);
  const record = JSON.parse(stdout[0]);
  assert.deepEqual(verifySourceDiscovery(record, SHA), {
    cases: 456, declaredPass: 443, declaredSkip: 13,
    runtimeSkipAttestation: 'NOT_PERFORMED',
    digest: record.rosterSha256,
  });
  assert.equal(record.qualification, 'SOURCE_LIST_ONLY_NOT_EXECUTION_PROOF');
  assert.equal(record.requiredCheckQualified, false);
  assert.equal(record.artifactTrust, 'EXTERNAL_VERIFICATION_REQUIRED');
  for (const item of fixture()) {
    assert.ok(!stdout[0].includes(item.id));
  }
});

test('declaration-time skip count is not mistaken for runtime skip evidence', () => {
  const staticOnly = fixture().map(testCase => ({
    ...testCase, expectedStatus: 'passed',
  }));
  const { reporter, stdout } = report(staticOnly);
  assert.equal(reporter.onEnd({ status: 'passed' }), undefined);
  const source = JSON.parse(stdout[0]);
  assert.deepEqual(verifySourceDiscovery(source, SHA), {
    cases: 456, declaredPass: 456, declaredSkip: 0,
    runtimeSkipAttestation: 'NOT_PERFORMED',
    digest: source.rosterSha256,
  });
  assert.equal(source.requiredCheckQualified, false);
  // More than 13 declared skips is not an acceptable source drift.
  const drift = fixture();
  drift[13].expectedStatus = 'skipped';
  const candidate = report(drift);
  assert.equal(candidate.reporter.onEnd({ status: 'passed' }), undefined);
  assert.throws(
    () => verifySourceDiscovery(JSON.parse(candidate.stdout[0]), SHA),
    /DISCOVERY_REFUSED:DECLARED_SKIP_EXCEEDS_BASELINE/,
  );
});

test('source order does not affect digest, but changed tests do', () => {
  const forward = successfulDiscovery();
  const reversed = report(fixture().reverse());
  assert.equal(reversed.reporter.onEnd({ status: 'passed' }), undefined);
  const backward = JSON.parse(reversed.stdout[0]);
  assert.deepEqual(backward, forward);
  const changed = fixture();
  changed[0].id = 'private/changed-case';
  const different = report(changed);
  assert.equal(different.reporter.onEnd({ status: 'passed' }), undefined);
  const newRoster = JSON.parse(different.stdout[0]);
  assert.notEqual(newRoster.rosterSha256, forward.rosterSha256);
});

test('invalid source inputs and unexpected reporter execution cannot qualify', () => {
  for (const cases of [
    [], null, false,
    [...fixture().slice(0, 455), fixture()[0]],
    [{ id: 'private/one', expectedStatus: 'failed' }],
    [{ id: '../bad\ncase', expectedStatus: 'passed' }],
  ]) {
    const { reporter, stdout } = report(cases);
    assert.deepEqual(reporter.onEnd({ status: 'passed' }), { status: 'failed' });
    assert.equal(stdout.length, 0);
  }
  // JavaScript default parameters replace undefined at call-sites, so feed
  // an actual undefined discovery result through the Reporter API instead.
  const absent = new SourceListReporter({
    sourceSha: SHA, listOnly: '1', write: () => assert.fail('untrusted data'),
  });
  absent.onBegin({}, { allTests: () => undefined });
  assert.deepEqual(absent.onEnd({ status: 'passed' }), { status: 'failed' });
  const forbidden = report(fixture(), { listOnly: '0' });
  assert.deepEqual(forbidden.reporter.onEnd({ status: 'passed' }), { status: 'failed' });
  const other = report(fixture());
  other.reporter.onTestBegin();
  assert.deepEqual(other.reporter.onEnd({ status: 'passed' }), { status: 'failed' });
  assert.equal(other.stdout.length, 0);
  const error = report();
  error.reporter.onError({ message: 'private auth token' });
  assert.deepEqual(error.reporter.onEnd({ status: 'passed' }), { status: 'failed' });
  const interrupted = report();
  assert.deepEqual(interrupted.reporter.onEnd({ status: 'interrupted' }), { status: 'failed' });
});

test('onBegin duplicates and writer failures never publish partial roster', () => {
  const source = report();
  source.reporter.onBegin({}, { allTests: () => fixture() });
  assert.deepEqual(source.reporter.onEnd({ status: 'passed' }), { status: 'failed' });
  assert.deepEqual(source.stdout, []);
  const reporter = new SourceListReporter({
    sourceSha: SHA, listOnly: '1',
    write: () => { throw new Error('private filesystem details'); },
  });
  reporter.onBegin({}, { allTests: fixture });
  assert.deepEqual(reporter.onEnd({ status: 'passed' }), { status: 'failed' });
});

test('tampered source SHA, sorted IDs, statuses, digest and trust flags are rejected', () => {
  const record = successfulDiscovery();
  const variants = [
    { ...record, sourceSha: 'b'.repeat(40) },
    { ...record, rosterSha256: '0'.repeat(64) },
    { ...record, tests: [...record.tests].reverse() },
    { ...record, tests: record.tests.slice(0, 455) },
    { ...record, requiredCheckQualified: true },
    { ...record, qualification: 'FULL_SUITE_ATTESTED' },
    { ...record, artifactTrust: 'TRUSTED' },
    { ...record, extra: 'unsafe' },
    { ...record, tests: record.tests.map((row, i) => i ? row : { ...row, status: 'flaky' }) },
  ];
  for (const value of variants) {
    assert.throws(() => verifySourceDiscovery(value, SHA), /DISCOVERY_REFUSED/);
  }
});

test('local source-only output is exclusive and rejects symlinked parents', () => {
  const roster = successfulDiscovery();
  const tmp = mkdtempSync(join(tmpdir(), 'coglatas-source-roster-'));
  try {
    mkdirSync(join(tmp, 'artifacts/ci'), { recursive: true });
    persistNonpublishingRoster(roster, tmp);
    const saved = JSON.parse(readFileSync(
      join(tmp, 'artifacts/ci/playwright-source-discovery.json'), 'utf8'));
    assert.deepEqual(saved, roster);
    assert.throws(() => persistNonpublishingRoster(roster, tmp), /DISCOVERY_REFUSED/);
    // Disallow a symlink in the path to the repository root as well as
    // a symlink in the artifacts/ or artifacts/ci directory components.
    const rootAlias = join(tmp, 'root-alias');
    const rootActual = join(tmp, 'root-real');
    mkdirSync(join(rootActual, 'artifacts/ci'), { recursive: true });
    symlinkSync(rootActual, rootAlias, 'dir');
    assert.throws(() => persistNonpublishingRoster(roster, rootAlias),
      /DISCOVERY_REFUSED/);
    const second = mkdtempSync(join(tmpdir(), 'coglatas-roster-elsewhere-'));
    try {
      mkdirSync(join(second, 'ci'));
      const alias = mkdtempSync(join(tmpdir(), 'coglatas-roster-symlink-'));
      try {
        mkdirSync(join(alias, 'artifacts'));
        symlinkSync(join(second, 'ci'), join(alias, 'artifacts/ci'), 'dir');
        assert.throws(() => persistNonpublishingRoster(roster, alias),
          /DISCOVERY_REFUSED/);
      } finally {
        rmSync(alias, { recursive: true, force: true });
      }
    } finally {
      rmSync(second, { recursive: true, force: true });
    }
  } finally {
    rmSync(tmp, { recursive: true, force: true });
  }
});

test('source-only opt-in cannot silently change the protected frontend runner', () => {
  const wf = readFileSync('.github/workflows/ci.yml', 'utf8');
  const launch = readFileSync('scripts/ci/run-frontend-parallel.sh', 'utf8');
  const runner = readFileSync('tests/ui/run-angular-playwright.mjs', 'utf8');
  const compose = readFileSync('infra/compose/dev/playwright.yml', 'utf8');
  assert.match(launch, /Protected frontend-test must never execute a partial Playwright shard/);
  assert.match(wf, /run: bash scripts\/ci\/run-frontend-parallel\.sh/);
  assert.doesNotMatch(runner, /playwright-full-source-list-reporter/);
  assert.doesNotMatch(compose, /playwright-full-source-list-reporter/);
  assert.match(wf, /ci-opt\/playwright-full-source-list-20261105/);
});
