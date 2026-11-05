#!/usr/bin/env node
/**
 * PR-only source discovery via original Playwright 1.64 --list Reporter.
 * No browser is launched; this only attests the local test discovery inventory,
 * NEVER execution, disjoint shard union or GitHub producer artifact ownership.
 */
import { spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { constants, lstatSync, openSync, readFileSync, writeFileSync, closeSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const ROOT = path.resolve(fileURLToPath(new URL('../../', import.meta.url)));
const BRANCH = 'ci-opt/playwright-full-source-list-20261105';
const SHA = /^[0-9a-f]{40}$/;
const TEST_ID = /^[0-9a-f]{64}$/;
const ALLOWED_COUNT = 456;

const deny = value => { throw new Error('DISCOVERY_REFUSED:' + value); };

export function extractStaticSpecFiles(text) {
  if (typeof text !== 'string' || !text.length || text.length > 20000)
    deny('BAD_SOURCE_FILE');
  const matches = [...text.matchAll(/^const staticSuiteFiles = \[\r?\n([\s\S]*?)^\];/gm)];
  if (matches.length !== 1) deny('STATIC_SELECTION_NOT_UNIQUE');
  const lines = matches[0][1].trim().split(/\r?\n/);
  if (lines.length !== 24) deny('STATIC_SELECTION_SIZE');
  const seen = new Set();
  const paths = [];
  for (const [index, line] of lines.entries()) {
    const match = /^\s*'(tests\/ui\/[a-z0-9-]+\.spec\.ts)'(,?)\s*$/.exec(line);
    if (!match || (index < lines.length - 1 && match[2] !== ',') ||
        seen.has(match?.[1])) deny('INVALID_STATIC_SELECTION');
    paths.push(match[1]);
    seen.add(match[1]);
  }
  return paths;
}

export function verifySourceDiscovery(data, expectedSha, expectedCount = ALLOWED_COUNT) {
  if (!data || typeof data !== 'object' || Array.isArray(data) ||
      !SHA.test(expectedSha)) deny('SCHEMA');
  const keys = ['schema', 'suite', 'sourceSha', 'tests', 'rosterSha256',
                'artifactTrust', 'requiredCheckQualified', 'qualification'];
  if (Object.keys(data).sort().join('|') !== keys.sort().join('|') ||
      data.schema !== 'coglatas.playwright-source-discovery.v1' ||
      data.suite !== 'angular-static' || data.sourceSha !== expectedSha ||
      data.artifactTrust !== 'EXTERNAL_VERIFICATION_REQUIRED' ||
      data.requiredCheckQualified !== false ||
      data.qualification !== 'SOURCE_LIST_ONLY_NOT_EXECUTION_PROOF') {
    deny('UNTRUSTED_DISCOVERY');
  }
  if (!Array.isArray(data.tests) || data.tests.length !== expectedCount)
    deny('UNEXPECTED_TEST_COUNT');
  let last = '';
  for (const item of data.tests) {
    if (!item || typeof item !== 'object' || Array.isArray(item) ||
        Object.keys(item).sort().join('|') !== 'id|status' ||
        !TEST_ID.test(item.id) || item.id <= last ||
        !['passed', 'skipped'].includes(item.status)) {
      deny('NONCANONICAL_SOURCE_TESTS');
    }
    last = item.id;
  }
  const expected = createHash('sha256').update(JSON.stringify({
    sourceSha: data.sourceSha, suite: data.suite, tests: data.tests,
  })).digest('hex');
  if (data.rosterSha256 !== expected) deny('SOURCE_DIGEST');
  const declaredSkip = data.tests.filter(x => x.status === 'skipped').length;
  if (expectedCount === ALLOWED_COUNT && declaredSkip !== 13) deny('SKIP_BASELINE_DRIFT');
  return {
    cases: data.tests.length,
    expectedPass: data.tests.length - declaredSkip,
    declaredSkip,
    digest: expected,
  };
}

function requireContext(env) {
  if (env.COGLATAS_SOURCE_DISCOVERY_TRIAL !== '1' ||
      env.GITHUB_EVENT_NAME !== 'pull_request' ||
      env.GITHUB_BASE_REF !== 'SecurityTestImplimation' ||
      env.GITHUB_HEAD_REF !== BRANCH ||
      typeof env.GITHUB_SHA !== 'string' || !SHA.test(env.GITHUB_SHA) ||
      env.CI !== 'true' || env.GITHUB_ACTIONS !== 'true') {
    deny('UNAUTHORIZED_TRIAL_CONTEXT');
  }
  const git = spawnSync('git', ['rev-parse', 'HEAD'], {
    cwd: ROOT, encoding: 'utf8', timeout: 10000,
  });
  if (git.error || git.signal || git.status !== 0 ||
      git.stdout.trim() !== env.GITHUB_SHA) deny('SOURCE_CHECKOUT_MISMATCH');
}

export function persistNonpublishingRoster(data, root = ROOT) {
  const artifactRoot = path.join(root, 'artifacts');
  const parent = path.join(artifactRoot, 'ci');
  for (const directory of [artifactRoot, parent]) {
    const s = lstatSync(directory);
    if (!s.isDirectory() || s.isSymbolicLink()) deny('UNSAFE_OUTPUT_PARENT');
  }
  const flags = constants.O_CREAT | constants.O_EXCL | constants.O_WRONLY |
    (constants.O_NOFOLLOW || 0);
  const destination = path.join(parent, 'playwright-source-discovery.json');
  const fd = openSync(destination, flags, 0o600);
  try {
    writeFileSync(fd, JSON.stringify(data) + '\n', { encoding: 'utf8' });
  } finally {
    closeSync(fd);
  }
}

export function runSourceDiscovery(env = process.env) {
  requireContext(env);
  const raw = readFileSync(path.join(ROOT, 'tests/ui/run-angular-playwright.mjs'), 'utf8');
  const files = extractStaticSpecFiles(raw);
  const cli = path.join(ROOT, 'node_modules/@playwright/test/cli.js');
  const reporter = path.join(ROOT, 'tests/ui/playwright-full-source-list-reporter.mjs');
  const args = [cli, 'test', ...files, '--list', '--reporter=' + reporter];
  const child = spawnSync(process.execPath, args, {
    cwd: ROOT, encoding: 'utf8', timeout: 90000, maxBuffer: 1024 * 1024,
    env: {
      ...env,
      COGLATAS_SOURCE_DISCOVERY_LIST: '1',
      COGLATAS_SOURCE_DISCOVERY_SHA: env.GITHUB_SHA,
      COGLATAS_STATIC_PLAYWRIGHT_SHARD_AUTH: '0',
      COGLATAS_STATIC_PLAYWRIGHT_SHARD: '',
      COGLATAS_COMPAT_CRITICAL: '0',
      COGLATAS_REAL_BACKEND_SMOKE: '0',
      COGLATAS_PUBLIC_HTTPS_SMOKE: '0',
      PLAYWRIGHT_SKIP_BUILD: '1',
      CI: 'true',
    },
  });
  if (child.error || child.signal || child.status !== 0 ||
      typeof child.stdout !== 'string' || child.stdout.length > 512 * 1024) {
    deny('PLAYWRIGHT_SOURCE_LIST_FAILED');
  }
  let data;
  try {
    data = JSON.parse(child.stdout);
  } catch {
    deny('UNSANITIZED_REPORTER_STDOUT');
  }
  const stats = verifySourceDiscovery(data, env.GITHUB_SHA);
  persistNonpublishingRoster(data);
  return stats;
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    const result = runSourceDiscovery();
    process.stdout.write('Playwright full-source discovery: cases=' + result.cases +
      ' expectedPass=' + result.expectedPass + ' declaredSkip=' + result.declaredSkip +
      ' rosterSha256=' + result.digest +
      ' qualification=SOURCE_LIST_ONLY_NOT_EXECUTION_PROOF\n');
  } catch {
    // Suppress test names, Playwright diagnostics, filesystem paths and secrets.
    process.stderr.write('Playwright full-source discovery refused: UNQUALIFIED_SOURCE_OR_OUTPUT\n');
    process.exitCode = 1;
  }
}
