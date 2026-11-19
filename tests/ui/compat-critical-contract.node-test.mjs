import assert from 'node:assert/strict';
import { mkdtemp, mkdir, rm, symlink, writeFile } from 'node:fs/promises';
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

test('contract schema rejects invalid and traversing source paths', () => {
  for (const file of [
    '../../../etc/passwd',
    '/etc/passwd',
    'tests/ui/../../../outside.spec.ts',
    'tests/ui/../../outside.spec.ts',
    'tests/ui/../nested.spec.ts',
    'tests/ui/\\\\outside.spec.ts'
  ]) {
    const contract = baseContract();
    contract.tests[0].file = file;
    assert.throws(
      () => validateCompatCriticalContract(contract),
      /compat-critical source must be a tests\/ui\/\*\.spec\.ts Playwright file/u,
      `Expected source schema rejection for ${file}`
    );
  }
});

test('source verifier independently rejects escaped paths even after contract validation', async () => {
  const root = await mkdtemp(path.join(os.tmpdir(), 'compat-critical-'));
  try {
    const valid = validateCompatCriticalContract(baseContract());
    for (const file of ['tests/ui/../../../outside.spec.ts', '/tmp/outside.spec.ts']) {
      const tampered = {
        ...valid,
        tests: valid.tests.map((entry, index) => index === 0 ? { ...entry, file } : entry)
      };
      await assert.rejects(
        () => verifyCompatCriticalSources(tampered, root),
        /Invalid file path/u,
        `Expected canonical containment rejection for ${file}`
      );
    }
  } finally {
    await rm(root, { recursive: true, force: true });
  }
});

test('source verifier rejects a symlink to a source outside the repository root', async () => {
  const root = await mkdtemp(path.join(os.tmpdir(), 'compat-critical-'));
  const outside = await mkdtemp(path.join(os.tmpdir(), 'compat-critical-outside-'));
  try {
    await mkdir(path.join(root, 'tests/ui'), { recursive: true });
    await writeFile(path.join(outside, 'outside.spec.ts'),
      "import { test } from '@playwright/test';\\ntest('Shell boot', async () => {});\\n");
    await symlink(path.join(outside, 'outside.spec.ts'), path.join(root, 'tests/ui/alias.spec.ts'));
    const contract = validateCompatCriticalContract(baseContract());
    contract.tests[0].file = 'tests/ui/alias.spec.ts';
    await assert.rejects(() => verifyCompatCriticalSources(contract, root), /Invalid file path/u);
  } finally {
    await rm(root, { recursive: true, force: true });
    await rm(outside, { recursive: true, force: true });
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


test('compat-critical schema and owner negative cases remain fail-closed', () => {
  const quarantine = { testId: 'COMPAT-BOOT-001', reason: 'controlled test outage',
    owner: '@ci-owner', issue: '#584', expiresOn: '2026-12-31' };
  const mutations = [
    ['schema revision', c => { c.schemaVersion = 2; }, /schemaVersion must be 1/u],
    ['contract identity', c => { c.name = 'unrelated'; }, /contract name must be compat-critical/u],
    ['unknown default profile', c => { c.defaultProfile = 'unknown'; }, /defaultProfile does not exist/u],
    ['missing tests', c => { c.tests = []; }, /at least one test/u],
    ['nonobject profiles', c => { c.profiles = []; }, /profiles must be an object/u],
    ['empty profiles', c => { c.profiles = {}; }, /at least one profile/u],
    ['unknown profile category', c => { c.profiles['browser-engine'].requiredCategories = ['other']; }, /references unknown categories/u],
    ['blank profile description', c => { c.profiles['browser-engine'].description = ''; }, /non-empty description/u],
    ['malformed test ID', c => { c.tests[0].id = 'COMPAT-other'; }, /Invalid compat-critical test id/u],
    ['duplicate test ID', c => { c.tests[1].id = c.tests[0].id; }, /Duplicate compat-critical test id/u],
    ['duplicate test title', c => { c.tests[1].title = c.tests[0].title; }, /Duplicate compat-critical test title/u],
    ['unrecognized test category', c => { c.tests[0].categories = ['unknown']; }, /references unknown categories/u],
    ['unrecognized test profile', c => { c.tests[0].profiles = ['unknown']; }, /references unknown profiles/u],
    ['unrecognized test status', c => { c.tests[0].status = 'paused'; }, /unsupported status/u],
    ['nonarray quarantines', c => { c.quarantines = {}; }, /quarantines must be an array/u],
    ['nonobject quarantine', c => { c.quarantines = [null]; }, /quarantines\[0\] must be an object/u],
    ['unknown quarantine identity', c => { c.quarantines = [{ ...quarantine, testId: 'COMPAT-MISSING-001' }]; }, /unknown compat-critical test/u],
    ['duplicate quarantine', c => { c.quarantines = [{ ...quarantine }, { ...quarantine }]; }, /Duplicate quarantine entry/u],
    ['invalid quarantine expiry', c => { c.quarantines = [{ ...quarantine, expiresOn: 'not-a-date' }]; }, /invalid expiresOn date/u],
    ['global missing active category', c => { c.requiredCategories.push('touch'); }, /missing active coverage categories: touch/u],
    ['duplicate required category', c => { c.requiredCategories.push('boot'); }, /contains duplicates/u],
    ['empty profile category', c => { c.profiles['browser-engine'].requiredCategories = []; }, /non-empty array/u],
    ['malformed test object', c => { c.tests[0] = null; }, /tests\[0\] must be an object/u]
  ];
  for (const [label, mutate, expected] of mutations) {
    const candidate = baseContract();
    mutate(candidate);
    assert.throws(
      () => validateCompatCriticalContract(candidate, { now: new Date('2026-09-04T00:00:00Z') }),
      expected,
      label
    );
  }
  assert.throws(
    () => validateCompatCriticalContract(baseContract(), { now: 'not-a-date' }),
    /Invalid validation date/u
  );
});

test('compat-critical selection and discovery refuse unknown, unowned and duplicate results', () => {
  const contract = validateCompatCriticalContract(baseContract());
  assert.throws(
    () => selectCompatCriticalTests(contract, 'unknown-profile'),
    /Unknown compat-critical profile/u
  );
  const rejected = { ...contract, quarantinedIds: new Set(contract.tests.map(entry => entry.id)) };
  assert.throws(
    () => selectCompatCriticalTests(rejected, 'browser-engine'),
    /selects zero compat-critical tests/u
  );
  assert.throws(
    () => verifyCompatCriticalDiscovery(contract, 'browser-engine',
      'Listing tests:\n  [chromium-desktop] › other.spec.ts:1:1 › suite › Foreign\nTotal: 1'),
    /Unexpected or ambiguous compat-critical Playwright discovery line/u
  );
  const duplicate = [
    'Listing tests:',
    '  [chromium-desktop] › smoke.spec.ts:1:1 › suite › Shell boot',
    '  [chromium-desktop] › smoke.spec.ts:2:1 › suite › Shell boot',
    '  [chromium-desktop] › navigation.spec.ts:3:1 › suite › Navigation',
    'Total: 3'
  ].join('\n');
  assert.throws(
    () => verifyCompatCriticalDiscovery(contract, 'browser-engine', duplicate),
    /Expected exactly one discovered compat-critical test titled "Shell boot"; found 2/u
  );
});

test('compat-critical file admission rejects missing and duplicated sources and skipped suites', async () => {
  const root = await mkdtemp(path.join(os.homedir(), '.coglatas-compat-critical-'));
  const sourceFile = path.join(root, 'tests/ui/smoke.spec.ts');
  const contract = validateCompatCriticalContract(baseContract({ singleFile: true }));
  const valid = [
    "test('Shell boot', async () => {});",
    "test('Navigation', async () => {});",
    ''
  ].join('\n');
  try {
    await assert.rejects(
      () => loadCompatCriticalContract(path.join(root, 'missing-contract.json')),
      /Unable to read compat-critical contract/u
    );
    const invalidContract = path.join(root, 'invalid-contract.json');
    await writeFile(invalidContract, '{not valid JSON', 'utf8');
    await assert.rejects(
      () => loadCompatCriticalContract(invalidContract),
      /Unable to read compat-critical contract/u
    );
    await assert.rejects(
      () => verifyCompatCriticalSources(contract, root),
      /compat-critical source file is missing/u
    );
    await mkdir(path.dirname(sourceFile), { recursive: true });
    await writeFile(sourceFile, valid, 'utf8');
    await assert.doesNotReject(() => verifyCompatCriticalSources(contract, root));

    await writeFile(sourceFile, valid.replace("test('Navigation'",
      "test('Shell boot', async () => {});\ntest('Navigation'"), 'utf8');
    await assert.rejects(
      () => verifyCompatCriticalSources(contract, root),
      /must resolve to exactly one Playwright test declaration/u
    );

    const skipped = [
      "test.describe.skip('disabled', () => {",
      "  test('Shell boot', async () => {});",
      "  test('Navigation', async () => {});",
      '});',
      ''
    ].join('\n');
    await writeFile(sourceFile, skipped, 'utf8');
    await assert.rejects(
      () => verifyCompatCriticalSources(contract, root),
      /nested under a skipped\/fixme describe block/u
    );
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
