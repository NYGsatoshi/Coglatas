import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';

const workflowPath = new URL('../../.github/workflows/projectide-test-discovery-web-e2e.yml', import.meta.url);
const workflow = readFileSync(workflowPath, 'utf8').replaceAll('\r\n', '\n');
const requiredPaths = [
  '.github/workflows/projectide-test-discovery-web-e2e.yml',
  'tests/ui/projectide-local-tests.spec.ts',
  'tests/ui/run-angular-playwright.mjs',
  'tests/ci/projectide-test-discovery-browser-receipt.mjs',
  'tests/ci/projectide-test-discovery-browser-receipt.node-test.mjs',
  'tests/ci/projectide-test-discovery-workflow-routing.node-test.mjs',
  'frontend/src/app/features/projectide/**',
  'frontend/src/app/core/i18n/messages/projectide.messages.ts',
  'frontend/public/assets/projectide/**',
  'frontend/scripts/generate-projectide-test-discovery-evidence.mjs',
  'frontend/scripts/projectide-test-discovery-original-profiles.json',
  'frontend/scripts/projectide-test-discovery-evidence-generator.node-test.mjs'
];

// This dependency-free guard reads the fixed block indentation of the workflow.
function blockContents(lines, header, missingMessage) {
  const start = lines.indexOf(header);
  assert.notEqual(start, -1, missingMessage);
  const childIndent = ' '.repeat(header.search(/\S/) + 2);
  let end = start + 1;
  while (end < lines.length) {
    const line = lines[end];
    if (line.trim() && !line.trimStart().startsWith('#') && !line.startsWith(childIndent)) break;
    end++;
  }
  return lines.slice(start + 1, end);
}

function triggerPaths(source, event) {
  const triggers = blockContents(source.split(/\r?\n/), 'on:', 'Missing workflow triggers');
  const eventLines = blockContents(triggers, '  ' + event + ':', 'Missing ' + event + ' event');
  return blockContents(eventLines, '    paths:', 'Missing ' + event + ' paths filter')
    .filter(line => line.startsWith('      - '))
    .map(line => line.slice('      - '.length).trim());
}

function verifyRequiredRoutes(source) {
  const pathsByEvent = ['push', 'pull_request'].map(event => {
    const paths = triggerPaths(source, event);
    assert.ok(paths.every(path => !/^['"]?!/.test(path)), event + ' paths must use only inclusion patterns');
    assert.equal(new Set(paths).size, paths.length, "Duplicate " + event + " paths");
    for (const path of requiredPaths) {
      assert.ok(paths.includes(path), event + " route omits " + path);
    }
    return paths;
  });
  assert.deepEqual(pathsByEvent[0], pathsByEvent[1], 'Push and PR path selectors must match');
}

test('dedicated Web E2E routes both events for Product UI, locale, fixtures and validator changes', () => {
  verifyRequiredRoutes(workflow);
});

test('a missing push filter cannot borrow the pull request paths', () => {
  const pullRequest = workflow.indexOf('  pull_request:\n');
  assert.notEqual(pullRequest, -1, 'Pull request trigger must exist in the fixture');
  const push = workflow.slice(0, pullRequest);
  const withoutPaths = push.replace(/^    paths:\n(?:      - [^\n]*\n)+/m, '');
  assert.notEqual(withoutPaths, push, 'Push paths filter must exist in the fixture');
  assert.throws(() => verifyRequiredRoutes(withoutPaths + workflow.slice(pullRequest)), /Missing push paths filter/);
});

test('accepts a CRLF workflow checkout', () => {
  verifyRequiredRoutes(workflow.replaceAll('\n', '\r\n'));
});

test('missing translation trigger fails the route check', () => {
  const missing = workflow.replace('      - frontend/src/app/core/i18n/messages/projectide.messages.ts\n', '');
  assert.notEqual(missing, workflow, 'Localization path must exist in the fixture');
  assert.throws(() => verifyRequiredRoutes(missing), /push route omits .*projectide\.messages\.ts/);
});

test('asymmetric PR-only source routes fail the route check', () => {
  const missing = workflow.replace(
    '  pull_request:\n    paths:\n',
    '  pull_request:\n    paths:\n      - frontend/scripts/extra-only-in-pr.mjs\n'
  );
  assert.notEqual(missing, workflow, 'PR path list must exist in the fixture');
  assert.throws(() => verifyRequiredRoutes(missing), /Push and PR path selectors must match/);
});

test('matching exclusions cannot disable required routes', () => {
  const lastSelector = '      - .github/workflows/projectide-test-discovery-web-e2e.yml\n';
  assert.equal(workflow.split(lastSelector).length - 1, 2, 'Both path lists must contain the workflow path');
  const excluded = workflow.replaceAll(lastSelector, lastSelector + "      - '!frontend/**'\n");
  assert.throws(() => verifyRequiredRoutes(excluded), /push paths must use only inclusion patterns/);
});
