import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';

const workflowPath = new URL('../../.github/workflows/projectide-test-discovery-web-e2e.yml', import.meta.url);
const workflow = readFileSync(workflowPath, 'utf8');
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

function triggerPaths(source, event) {
  const header = "  " + event + ":\n";
  const index = source.indexOf(header);
  assert.notEqual(index, -1, "Missing " + event + " event");
  const lines = source.slice(index + header.length).split(/\r?\n/);
  const pathLine = lines.indexOf('    paths:');
  assert.notEqual(pathLine, -1, "Missing " + event + " paths filter");
  const paths = [];
  for (const line of lines.slice(pathLine + 1)) {
    if (!line.startsWith('      - ')) break;
    paths.push(line.slice('      - '.length).trim());
  }
  return paths;
}

function verifyRequiredRoutes(source) {
  const pathsByEvent = ['push', 'pull_request'].map(event => {
    const paths = triggerPaths(source, event);
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
