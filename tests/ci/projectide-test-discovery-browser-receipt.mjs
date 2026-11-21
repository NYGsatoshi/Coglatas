import { createHash } from 'node:crypto';
import { execFileSync } from 'node:child_process';
import { createReadStream } from 'node:fs';
import { mkdir, readFile, readdir, stat, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

export const REQUIRED_SPEC = 'tests/ui/projectide-local-tests.spec.ts';
export const EXPECTED_PROJECTS = Object.freeze(['chromium-desktop', 'chromium-mobile']);
export const EXPECTED_TITLES = Object.freeze([
  'shows local Test outcomes separately from Compiler results in en',
  'shows local Test outcomes separately from Compiler results in ja',
  'lists exact Test definitions independently of execution in en',
  'lists exact Test definitions independently of execution in ja'
]);

export function validateBrowserReport(report) {
  if (!report || !Array.isArray(report.suites)) throw new Error('Missing actual Playwright suite inventory');
  if (!Array.isArray(report.errors) || report.errors.length !== 0) throw new Error('Playwright reported suite errors or omitted error inventory');
  const rows = [];
  function visit(suites, inheritedFile = '') {
    for (const suite of suites) {
      const file = String(suite.file || inheritedFile).replace(/\\/g, '/');
      for (const spec of (suite.specs || [])) {
        const location = String(spec.file || file).replace(/\\/g, '/');
        if (location !== REQUIRED_SPEC && !location.endsWith('/' + REQUIRED_SPEC)) {
          throw new Error('Unexpected or unpinned browser spec selected: ' + location);
        }
        for (const test of (spec.tests || [])) {
          rows.push({
            title: spec.title,
            project: test.projectName,
            expectedStatus: test.expectedStatus,
            outcome: test.status,
            attempts: Array.isArray(test.results) ? test.results.map(r => r.status) : []
          });
        }
      }
      if (Array.isArray(suite.suites)) visit(suite.suites, file);
    }
  }
  visit(report.suites);
  const expected = new Set(EXPECTED_PROJECTS.flatMap(p => EXPECTED_TITLES.map(t => p + '\0' + t)));
  if (rows.length !== expected.size) throw new Error('Browser roster count mismatch: ' + rows.length);
  const observed = new Set();
  for (const row of rows) {
    const key = row.project + '\0' + row.title;
    if (!expected.has(key) || observed.has(key)) throw new Error('Missing, duplicate or unexpected browser test identity');
    observed.add(key);
    if (row.expectedStatus !== 'passed' || row.outcome !== 'expected' ||
        row.attempts.length !== 1 || row.attempts[0] !== 'passed') {
      throw new Error('Skipped, failed, flaky, retried or inconclusive browser test: ' + key);
    }
  }
  if (observed.size !== expected.size) throw new Error('Incomplete English/Japanese desktop/mobile roster');
  const s = report.stats;
  if (!s || s.expected !== expected.size || s.unexpected !== 0 || s.skipped !== 0 || s.flaky !== 0) {
    throw new Error('Missing or non-passing Playwright global outcome counters');
  }
  return rows.sort((a, b) => (a.project + a.title).localeCompare(b.project + b.title));
}

async function buildDigest(root) {
  const inventory = [];
  async function walk(dir) {
    for (const entry of await readdir(dir, { withFileTypes: true })) {
      const full = path.join(dir, entry.name);
      if (entry.isSymbolicLink()) throw new Error('Build output has a symlink: ' + entry.name);
      if (entry.isDirectory()) await walk(full);
      else if (entry.isFile()) {
        const h = createHash('sha256');
        for await (const chunk of createReadStream(full)) h.update(chunk);
        inventory.push([path.relative(root, full).replace(/\\/g, '/'), h.digest('hex')]);
      } else throw new Error('Unexpected build entry type');
    }
  }
  const index = path.join(root, 'index.html');
  if (!(await stat(index)).isFile()) throw new Error('Missing served Angular build index');
  await walk(root);
  inventory.sort(([a], [b]) => a.localeCompare(b));
  const hash = createHash('sha256');
  for (const [p, digest] of inventory) hash.update(p + '\0' + digest + '\n');
  if (inventory.length < 2) throw new Error('Empty or partial compiled Angular build');
  return { sha256: hash.digest('hex'), fileCount: inventory.length };
}

function git(...args) {
  return execFileSync('git', args, { encoding: 'utf8' }).trim();
}

async function main() {
  const head = git('rev-parse', 'HEAD');
  if (!/^[0-9a-f]{40}$/.test(head) || head !== process.env.EXPECTED_HEAD) {
    throw new Error('Actual source HEAD does not match the PR event HEAD');
  }
  if (process.env.PLAYWRIGHT_BASE_URL) throw new Error('External Playwright server must not substitute served source');
  const specBlob = git('rev-parse', 'HEAD:' + REQUIRED_SPEC);
  if (specBlob !== git('hash-object', REQUIRED_SPEC)) throw new Error('Browser Test source differs from committed blob');
  const runnerPath = 'tests/ui/run-angular-playwright.mjs';
  const runnerBlob = git('rev-parse', 'HEAD:' + runnerPath);
  if (runnerBlob !== git('hash-object', runnerPath)) throw new Error('Browser runner source differs from committed blob');
  const runner = await readFile(runnerPath, 'utf8');
  if (!runner.includes("'" + REQUIRED_SPEC + "'")) throw new Error('Default browser roster omitted local Tests spec');
  const raw = process.env.PLAYWRIGHT_JSON_OUTPUT_NAME;
  if (!raw) throw new Error('Missing raw Playwright report path');
  const report = JSON.parse(await readFile(raw, 'utf8'));
  const rows = validateBrowserReport(report);
  const bundle = await buildDigest('frontend/dist/coglatas-web');
  const receipt = {
    schema: 'coglatas.projectide.web-test-discovery-e2e.v1',
    candidateHeadSha: head,
    browserSpecBlob: specBlob,
    runnerBlob,
    servedBuild: bundle,
    environment: 'ubuntu-24.04 / Chromium desktop + mobile / en + ja',
    completedCases: rows.map(({ title, project, attempts }) => ({ title, project, attempts: attempts.length, passed: true })),
    result: 'PASS',
    nativeWindowsUiQualified: false,
    runtimeAuthorityQualified: false,
    trustQualified: false,
    releaseQualified: false
  };
  const output = process.env.PROJECTIDE_BROWSER_RECEIPT_PATH;
  if (!output) throw new Error('Missing sanitized receipt output path');
  await mkdir(path.dirname(output), { recursive: true });
  await writeFile(output, JSON.stringify(receipt, null, 2) + '\n', { flag: 'wx' });
  process.stdout.write('Exact-source ProjectIDE browser evidence PASS: ' + rows.length + ' cases, ' + head + '\n');
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  main().catch(err => {
    process.stderr.write('ProjectIDE browser evidence FAIL: ' + (err instanceof Error ? err.message : 'unknown error') + '\n');
    process.exitCode = 1;
  });
}
