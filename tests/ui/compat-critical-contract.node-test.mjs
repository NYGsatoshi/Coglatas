import assert from 'node:assert/strict';
import { mkdtemp, mkdir, rm, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';

import {
  buildCompatCriticalGrep,
  loadCompatCriticalContract,
  selectCompatCriticalTests,
  validateCompatCriticalContract,
  verifyCompatCriticalDiscovery,
  verifyCompatCriticalSources
} from '../../scripts/ci/compat-critical-contract.mjs';

const repositoryContractPath = 'scripts/ci/compat-critical.contract.json';

test('repository compat-critical contract is fail-closed and source-resolvable', async () => {
  const contract = await loadCompatCriticalContract(repositoryContractPath, { now: new Date('2026-09-04T00:00:00Z') });
  await verifyCompatCriticalSources(contract);

  assert.equal(contract.name, 'compat-critical');
  assert.equal(selectCompatCriticalTests(contract, 'browser-engine').length, 4);
  const mobileTests = selectCompatCriticalTests(contract, 'mobile');
  assert.equal(mobileTests.length, 6);
  assert.ok(mobileTests.some((entry) => entry.categories.includes('touch')));
  assert.ok(mobileTests.some((entry) => entry.categories.includes('horizontal-overflow')));
  assert.equal(selectCompatCriticalTests(contract, 'os-portability').length, 2);
  assert.equal(selectCompatCriticalTests(contract, 'real-backend').length, 1);
});

test('grep selection anchors exact test titles instead of file names', () => {
  const contract = validateCompatCriticalContract(baseContract());
  const grep = new RegExp(buildCompatCriticalGrep(contract, 'browser-engine'), 'u');
  assert.equal(grep.test('chromium-desktop › smoke.spec.ts › suite › Shell boot'), true);
  assert.equal(grep.test('chromium-desktop › smoke.spec.ts › suite › Shell boot renamed'), false);
  assert.equal(grep.test('chromium-desktop › smoke.spec.ts › suite › Navigation'), true);
});

test('profile with zero active non-quarantined tests fails closed', () => {
  const contract = baseContract();
  contract.profiles.empty = { description: 'Empty profile', requiredCategories: ['boot'] };
  assert.throws(() => validateCompatCriticalContract(contract), /Profile empty selects zero non-quarantined/u);
});

test('missing required category fails closed', () => {
  const contract = baseContract();
  contract.tests = contract.tests.filter((entry) => !entry.categories.includes('navigation'));
  assert.throws(() => validateCompatCriticalContract(contract), /missing required coverage categories: navigation/u);
});

test('obsolete or superseded tests cannot stay selected', () => {
  const contract = baseContract();
  contract.tests[0] = { ...contract.tests[0], status: 'superseded' };
  assert.throws(() => validateCompatCriticalContract(contract), /must not remain attached to a selection profile/u);
});

test('quarantine requires reason owner issue and expiry and cannot erase required coverage', () => {
  const contract = baseContract();
  contract.quarantines = [{ testId: 'COMPAT-BOOT-001', reason: '', owner: '@ci-owner', issue: '#584', expiresOn: '2026-09-30' }];
  assert.throws(() => validateCompatCriticalContract(contract, { now: new Date('2026-09-04T00:00:00Z') }), /reason.*non-empty/u);

  contract.quarantines[0].reason = 'Firefox focus regression under investigation';
  assert.throws(
    () => validateCompatCriticalContract(contract, { now: new Date('2026-10-01T00:00:00Z') }),
    /expired on 2026-09-30/u
  );

  assert.throws(
    () => validateCompatCriticalContract(contract, { now: new Date('2026-09-04T00:00:00Z') }),
    /missing required coverage categories: boot/u
  );
});

test('source verifier rejects sleeps, randomness, pixel-only assertions, and inline exceptions', async () => {
  const root = await mkdtemp(path.join(os.tmpdir(), 'compat-critical-'));
  try {
    await mkdir(path.join(root, 'tests/ui'), { recursive: true });
    const cases = [
      ['waitForTimeout', "await page.waitForTimeout(100);"],
      ['Math.random', 'Math.random();'],
      ['pixel screenshot', 'await expect(page).toHaveScreenshot();'],
      ['inline skip', "test.skip(true, 'engine exception');"],
      ['expected-failure', "testInfo.fail(true, 'engine exception');"],
      ['expected-failure', "testInfo?.fail(true, 'engine exception');"],
      ['inline skip', "test['skip'](true, 'engine exception');"]
    ];

    for (const [label, statement] of cases) {
      await writeFile(
        path.join(root, 'tests/ui/smoke.spec.ts'),
        `import { test } from '@playwright/test';\ntest.describe('suite', () => {\n  test('Shell boot', async ({ page }) => { ${statement} });\n  test('Navigation', async () => {});\n});\n`
      );
      const contract = validateCompatCriticalContract(baseContract({ singleFile: true }));
      await assert.rejects(() => verifyCompatCriticalSources(contract, root), new RegExp(label, 'u'));
    }
  } finally {
    await rm(root, { recursive: true, force: true });
  }
});

test('discovery verifier rejects empty or partial Playwright selection', () => {
  const contract = validateCompatCriticalContract(baseContract());
  assert.throws(() => verifyCompatCriticalDiscovery(contract, 'browser-engine', 'Listing tests:\nTotal: 0 tests'), /zero/u);

  const partial = 'Listing tests:\n  [chromium-desktop] › smoke.spec.ts:1:1 › suite › Shell boot\nTotal: 1 test';
  assert.throws(() => verifyCompatCriticalDiscovery(contract, 'browser-engine', partial), /Navigation.*found 0/u);

  const complete = [
    'Listing tests:',
    '  [chromium-desktop] › smoke.spec.ts:1:1 › suite › Shell boot',
    '  [chromium-desktop] › navigation.spec.ts:1:1 › suite › Navigation',
    'Total: 2 tests in 2 files'
  ].join('\n');
  assert.deepEqual(verifyCompatCriticalDiscovery(contract, 'browser-engine', complete), {
    selectedCount: 2,
    discoveredCount: 2
  });
});

test('source verifier rejects path traversal attempts to read files outside repository root', async () => {
  const root = await mkdtemp(path.join(os.tmpdir(), 'compat-critical-'));
  try {
    await mkdir(path.join(root, 'tests/ui'), { recursive: true });
    await writeFile(
      path.join(root, 'tests/ui/smoke.spec.ts'),
      `import { test } from '@playwright/test';\ntest.describe('suite', () => {\n  test('Shell boot', async ({ page }) => {});\n});\n`
    );

    // Test various path traversal attack vectors
    const pathTraversalCases = [
      { file: '../../../etc/passwd', label: 'absolute traversal with ../' },
      { file: '../../sensitive-file.txt', label: 'parent directory traversal' },
      { file: 'tests/ui/../../../etc/passwd', label: 'traversal after valid prefix' },
      { file: 'tests/ui/../../etc/passwd', label: 'traversal from valid directory' },
      { file: '/etc/passwd', label: 'absolute path' },
      { file: '/tmp/malicious.txt', label: 'absolute path to tmp' }
    ];

    for (const { file, label } of pathTraversalCases) {
      const contract = {
        schemaVersion: 1,
        name: 'compat-critical',
        defaultProfile: 'browser-engine',
        requiredCategories: ['boot'],
        profiles: {
          'browser-engine': {
            description: 'Browser compatibility',
            requiredCategories: ['boot']
          }
        },
        quarantines: [],
        tests: [
          {
            id: 'COMPAT-BOOT-001',
            file,
            title: 'Shell boot',
            categories: ['boot'],
            profiles: ['browser-engine'],
            status: 'active'
          }
        ]
      };

      const validated = validateCompatCriticalContract(contract);
      await assert.rejects(
        () => verifyCompatCriticalSources(validated, root),
        /Invalid file path/u,
        `Expected path traversal rejection for: ${label}`
      );
    }
  } finally {
    await rm(root, { recursive: true, force: true });
  }
});

test('source verifier accepts valid paths within repository root', async () => {
  const root = await mkdtemp(path.join(os.tmpdir(), 'compat-critical-'));
  try {
    await mkdir(path.join(root, 'tests/ui/subdir'), { recursive: true });
    await writeFile(
      path.join(root, 'tests/ui/smoke.spec.ts'),
      `import { test } from '@playwright/test';\ntest.describe('suite', () => {\n  test('Shell boot', async ({ page }) => {});\n});\n`
    );
    await writeFile(
      path.join(root, 'tests/ui/subdir/nested.spec.ts'),
      `import { test } from '@playwright/test';\ntest.describe('suite', () => {\n  test('Nested test', async ({ page }) => {});\n});\n`
    );

    // Test valid paths that should be accepted
    const validPaths = [
      'tests/ui/smoke.spec.ts',
      'tests/ui/subdir/nested.spec.ts'
    ];

    for (const file of validPaths) {
      const contract = {
        schemaVersion: 1,
        name: 'compat-critical',
        defaultProfile: 'browser-engine',
        requiredCategories: ['boot'],
        profiles: {
          'browser-engine': {
            description: 'Browser compatibility',
            requiredCategories: ['boot']
          }
        },
        quarantines: [],
        tests: [
          {
            id: 'COMPAT-BOOT-001',
            file,
            title: file.includes('nested') ? 'Nested test' : 'Shell boot',
            categories: ['boot'],
            profiles: ['browser-engine'],
            status: 'active'
          }
        ]
      };

      const validated = validateCompatCriticalContract(contract);
      // Should not throw
      await verifyCompatCriticalSources(validated, root);
    }
  } finally {
    await rm(root, { recursive: true, force: true });
  }
});

function baseContract(options = {}) {
  return {
    schemaVersion: 1,
    name: 'compat-critical',
    defaultProfile: 'browser-engine',
    requiredCategories: ['boot', 'navigation'],
    profiles: {
      'browser-engine': {
        description: 'Browser compatibility',
        requiredCategories: ['boot', 'navigation']
      }
    },
    quarantines: [],
    tests: [
      {
        id: 'COMPAT-BOOT-001',
        file: 'tests/ui/smoke.spec.ts',
        title: 'Shell boot',
        categories: ['boot'],
        profiles: ['browser-engine'],
        status: 'active'
      },
      {
        id: 'COMPAT-NAV-001',
        file: options.singleFile ? 'tests/ui/smoke.spec.ts' : 'tests/ui/navigation.spec.ts',
        title: 'Navigation',
        categories: ['navigation'],
        profiles: ['browser-engine'],
        status: 'active'
      }
    ]
  };
}
