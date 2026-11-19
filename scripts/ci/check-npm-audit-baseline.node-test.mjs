import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { mkdtempSync, mkdirSync, rmSync, writeFileSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import test from 'node:test';

const checker = fileURLToPath(new URL('./check-npm-audit-baseline.mjs', import.meta.url));

function cleanReport() {
  return {
    auditReportVersion: 2,
    vulnerabilities: {},
    metadata: { vulnerabilities: { info: 0, low: 0, moderate: 0, high: 0, critical: 0, total: 0 } },
  };
}

function highReport() {
  const report = cleanReport();
  report.vulnerabilities.example = { severity: 'high', via: [{ source: 12345 }] };
  report.metadata.vulnerabilities.high = 1;
  report.metadata.vulnerabilities.total = 1;
  return report;
}

function run(reports, findings = [], document = { version: 1, findings }) {
  const directory = mkdtempSync(path.join(os.homedir(), 'npm-audit-baseline-test-'));
  try {
    const reportDirectory = path.join(directory, 'reports');
    mkdirSync(reportDirectory);
    for (const [name, report] of Object.entries(reports)) {
      writeFileSync(path.join(reportDirectory, `${name}.json`), JSON.stringify(report));
    }
    const baseline = path.join(directory, 'baseline.json');
    writeFileSync(baseline, JSON.stringify(document));
    return spawnSync(process.execPath, [checker, reportDirectory, baseline], { encoding: 'utf8' });
  } finally {
    rmSync(directory, { recursive: true, force: true });
  }
}

// Node's test runner owns completion and reports callback failures.
void test('a complete clean npm v2 report passes', () => {
  assert.equal(run({ frontend: cleanReport() }).status, 0);
});

void test('an npm registry error cannot pass as an empty vulnerability report', () => {
  const result = run({ root: cleanReport(), frontend: { error: { code: 'E503' } } });
  assert.notEqual(result.status, 0);
  assert.match(result.stderr, /Invalid npm audit report/);
});

void test('missing, malformed and inconsistent npm audit evidence fails closed', () => {
  const mutations = [
    () => ({}),
    () => null,
    report => ({ ...report, auditReportVersion: 1 }),
    report => ({ ...report, error: null }),
    report => ({ ...report, vulnerabilities: [] }),
    report => ({ ...report, metadata: {} }),
    report => { report.metadata.vulnerabilities.high = 1; return report; },
    report => { report.metadata.vulnerabilities.total = -1; return report; },
    report => { report.vulnerabilities.example = { severity: 'unknown', via: [] }; return report; },
    report => { report.vulnerabilities.example = { severity: 'high', via: [] }; return report; },
    report => { report.vulnerabilities.example = { severity: 'high', via: [null] }; return report; },
  ];
  for (const [index, mutation] of mutations.entries()) {
    const result = run({ frontend: mutation(cleanReport()) });
    assert.notEqual(result.status, 0, `mutation ${index} must be rejected`);
    assert.match(result.stderr, /Invalid npm audit report/, `mutation ${index}`);
  }
});

void test('valid high findings still require the same reviewed advisory identity', () => {
  const finding = { report: 'frontend', package: 'example', severity: 'high', via: ['advisory:12345'] };
  assert.equal(run({ frontend: highReport() }, [finding]).status, 0);
  const unreviewed = run({ frontend: highReport() });
  assert.equal(unreviewed.status, 1);
  assert.match(unreviewed.stderr, /Unreviewed npm audit finding/);
  const changed = highReport();
  changed.vulnerabilities.example.via[0].source = 12346;
  assert.equal(run({ frontend: changed }, [finding]).status, 1);
});


void test('missing CLI inputs, invalid baselines and empty report inventories fail closed', () => {
  const missingArgs = spawnSync(process.execPath, [checker], { encoding: 'utf8' });
  assert.equal(missingArgs.status, 2);
  assert.match(missingArgs.stderr, /Usage: node check-npm-audit-baseline/u);

  assert.notEqual(run({ frontend: cleanReport() }, [], { version: 2, findings: [] }).status, 0);
  assert.notEqual(run({ frontend: cleanReport() }, [], { version: 1, findings: null }).status, 0);
  const empty = run({});
  assert.notEqual(empty.status, 0);
  assert.match(empty.stderr, /No npm audit JSON reports found/u);
});

void test('all low/moderate findings remain outside the high severity admission', () => {
  const report = cleanReport();
  report.vulnerabilities.minor = { severity: 'low', via: ['nested-package'] };
  report.vulnerabilities.medium = { severity: 'moderate', via: [{ source: 2345 }] };
  report.metadata.vulnerabilities.low = 1;
  report.metadata.vulnerabilities.moderate = 1;
  report.metadata.vulnerabilities.total = 2;
  const result = run({ frontend: report });
  assert.equal(result.status, 0, result.stderr);
  assert.match(result.stdout, /actual=0, baseline=0/u);
});

void test('stale accepted history emits an explicit warning without suppressing current findings', () => {
  const historic = { report: 'frontend', package: 'old-example', severity: 'high', via: ['advisory:12345'] };
  const stale = run({ frontend: cleanReport() }, [historic]);
  assert.equal(stale.status, 0, stale.stderr);
  assert.match(stale.stderr, /Reviewed npm audit baseline entry is not currently reported/u);
  assert.match(stale.stdout, /retains 1 reviewed finding/u);

  const mismatched = run({ frontend: highReport() }, [historic]);
  assert.equal(mismatched.status, 1);
  assert.match(mismatched.stderr, /Unreviewed npm audit finding/u);
});

void test('critical, dependency and duplicate-via identities use exact reviewed evidence', () => {
  const critical = highReport();
  critical.vulnerabilities.example.severity = 'critical';
  critical.metadata.vulnerabilities.high = 0;
  critical.metadata.vulnerabilities.critical = 1;
  const criticalIdentity = { report: 'frontend', package: 'example', severity: 'critical', via: ['advisory:12345'] };
  assert.equal(run({ frontend: critical }, [criticalIdentity]).status, 0);

  const dependency = highReport();
  dependency.vulnerabilities.example.via = ['picomatch', 'picomatch'];
  const dependencyIdentity = { report: 'frontend', package: 'example', severity: 'high', via: ['dependency:picomatch'] };
  assert.equal(run({ frontend: dependency }, [dependencyIdentity]).status, 0);

  const withLow = highReport();
  withLow.vulnerabilities.other = { severity: 'low', via: ['indirect'] };
  withLow.metadata.vulnerabilities.low = 1;
  withLow.metadata.vulnerabilities.total = 2;
  const highIdentity = { report: 'frontend', package: 'example', severity: 'high', via: ['advisory:12345'] };
  assert.equal(run({ frontend: withLow }, [highIdentity]).status, 0);
});

void test('invalid baseline finding fields and tampered audit evidence reject every mutation', () => {
  const validFinding = { report: 'frontend', package: 'example', severity: 'high', via: ['advisory:12345'] };
  const invalidBaselines = [
    null,
    1,
    { ...validFinding, report: '' },
    { ...validFinding, package: '' },
    { ...validFinding, severity: 'low' },
    { ...validFinding, via: [null] },
  ];
  for (const [index, finding] of invalidBaselines.entries()) {
    const result = run({ frontend: cleanReport() }, [finding]);
    assert.notEqual(result.status, 0, 'baseline mutation ' + index);
    assert.match(result.stderr, /npm audit baseline|Invalid npm audit baseline finding/u);
  }

  const mutations = [
    report => { report.auditReportVersion = '2'; },
    report => { report.metadata.vulnerabilities.info = -1; },
    report => { report.metadata.vulnerabilities.low = 0.25; },
    report => { report.metadata.vulnerabilities.total = 1; },
    report => { report.vulnerabilities.example = { severity: 'high', via: [''] }; },
    report => { report.vulnerabilities.example = { severity: 'high', via: [{ source: 0 }] }; },
    report => { report.vulnerabilities.example = { severity: 'critical', via: [{ source: '12345' }] }; },
    report => { report.vulnerabilities.example = { severity: 'high', via: [{}] }; },
  ];
  for (const [index, mutate] of mutations.entries()) {
    const report = cleanReport();
    mutate(report);
    const result = run({ frontend: report });
    assert.notEqual(result.status, 0, 'audit mutation ' + index);
    assert.match(result.stderr, /Invalid npm audit report/u);
  }
});
