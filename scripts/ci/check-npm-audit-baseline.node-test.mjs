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

function run(reports, findings = []) {
  const directory = mkdtempSync(path.join(os.tmpdir(), 'npm-audit-baseline-test-'));
  try {
    const reportDirectory = path.join(directory, 'reports');
    mkdirSync(reportDirectory);
    for (const [name, report] of Object.entries(reports)) {
      writeFileSync(path.join(reportDirectory, `${name}.json`), JSON.stringify(report));
    }
    const baseline = path.join(directory, 'baseline.json');
    writeFileSync(baseline, JSON.stringify({ version: 1, findings }));
    return spawnSync(process.execPath, [checker, reportDirectory, baseline], { encoding: 'utf8' });
  } finally {
    rmSync(directory, { recursive: true, force: true });
  }
}

test('a complete clean npm v2 report passes', () => {
  assert.equal(run({ frontend: cleanReport() }).status, 0);
});

test('an npm registry error cannot pass as an empty vulnerability report', () => {
  const result = run({ root: cleanReport(), frontend: { error: { code: 'E503' } } });
  assert.notEqual(result.status, 0);
  assert.match(result.stderr, /Invalid npm audit report/);
});

test('missing, malformed and inconsistent npm audit evidence fails closed', () => {
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

test('valid high findings still require the same reviewed advisory identity', () => {
  const finding = { report: 'frontend', package: 'example', severity: 'high', via: ['advisory:12345'] };
  assert.equal(run({ frontend: highReport() }, [finding]).status, 0);
  const unreviewed = run({ frontend: highReport() });
  assert.equal(unreviewed.status, 1);
  assert.match(unreviewed.stderr, /Unreviewed npm audit finding/);
  const changed = highReport();
  changed.vulnerabilities.example.via[0].source = 12346;
  assert.equal(run({ frontend: changed }, [finding]).status, 1);
});
