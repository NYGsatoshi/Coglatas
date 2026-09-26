import assert from 'node:assert/strict';
import { mkdtemp, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import test from 'node:test';

import { verifyPlaywrightRequiredTests } from '../../scripts/ci/verify-playwright-required-tests.mjs';

test('accepts required tests when each has a passing JUnit case', async () => {
  const directory = await mkdtemp(join(tmpdir(), 'coglatas-playwright-required-'));
  try {
    const manifestPath = join(directory, 'required.txt');
    const junitPath = join(directory, 'results.xml');
    await writeFile(manifestPath, 'first test\nsecond test\n', 'utf8');
    await writeFile(
      junitPath,
      '<testsuites><testsuite><testcase name="first test"></testcase><testcase name="suite › second test"></testcase></testsuite></testsuites>',
      'utf8'
    );

    const result = await verifyPlaywrightRequiredTests(manifestPath, junitPath);
    assert.equal(result.requiredCount, 2);
    assert.equal(result.discoveredCaseCount, 2);
  } finally {
    await rm(directory, { recursive: true, force: true });
  }
});

test('rejects a required test that is skipped', async () => {
  const directory = await mkdtemp(join(tmpdir(), 'coglatas-playwright-required-'));
  try {
    const manifestPath = join(directory, 'required.txt');
    const junitPath = join(directory, 'results.xml');
    await writeFile(manifestPath, 'required test\n', 'utf8');
    await writeFile(
      junitPath,
      '<testsuites><testsuite><testcase name="required test"><skipped /></testcase></testsuite></testsuites>',
      'utf8'
    );

    await assert.rejects(
      () => verifyPlaywrightRequiredTests(manifestPath, junitPath),
      /Required test did not pass: required test \(skipped\)/u
    );
  } finally {
    await rm(directory, { recursive: true, force: true });
  }
});

test('rejects a required test missing from JUnit results', async () => {
  const directory = await mkdtemp(join(tmpdir(), 'coglatas-playwright-required-'));
  try {
    const manifestPath = join(directory, 'required.txt');
    const junitPath = join(directory, 'results.xml');
    await writeFile(manifestPath, 'required test\n', 'utf8');
    await writeFile(
      junitPath,
      '<testsuites><testsuite><testcase name="different test"></testcase></testsuite></testsuites>',
      'utf8'
    );

    await assert.rejects(
      () => verifyPlaywrightRequiredTests(manifestPath, junitPath),
      /Required test is missing from JUnit results: required test/u
    );
  } finally {
    await rm(directory, { recursive: true, force: true });
  }
});
