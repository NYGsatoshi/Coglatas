import assert from 'node:assert/strict';
import test from 'node:test';
import { spawnSync } from 'node:child_process';
import { existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';

function fixtureSource(fixture, { journeyId, stepId, nestedCleanup, captureBeforeCleanup }) {
  return `
      import { EventEmitter } from 'node:events';
      import { test as observedTest, diagnosticStep, captureFunctionalFailure } from ${JSON.stringify(fixture)};
      const protectedValue = 'PRIVATE_BODY_COOKIE_TOKEN_PASSWORD';
      let beforeCleanup = true;
      class Locator {
        first() { return this; }
        locator() { return this; }
        getByRole() { return this; }
        getByTestId() { return this; }
        async isVisible() { return beforeCleanup; }
        async isEnabled() { return beforeCleanup; }
        async count() { return beforeCleanup ? 1 : 0; }
      }
      const page = Object.assign(new EventEmitter(), {isClosed:()=>false,getByTestId:()=>new Locator()});
      const context = Object.assign(new EventEmitter(), {pages:()=>[page]});
      const browser = {newContext:async()=>{throw new Error('synthetic screenshot unavailable');}};
      const test = observedTest.extend({
        browser:[async({},use)=>use(browser),{scope:'worker'}],
        context:async({},use)=>use(context),page:async({},use)=>use(page),
      });
      test('synthetic fixture lifecycle only; no product execution credit', {
        annotation:[{type:'journey',description:${JSON.stringify(journeyId)}},{type:'backend',description:'real'}]
      }, async({page})=>{
        const request = {url:()=> ${JSON.stringify(journeyId === 'FUNC-MSG-001' ? 'http://127.0.0.1:5080/api/conversations/private-resource?token=' : 'http://127.0.0.1:5080/api/files?token=')}+protectedValue,method:()=> 'GET'};
        try {
          await diagnosticStep(${JSON.stringify(journeyId + ' / ' + stepId + ' ')}+protectedValue,page,async()=>{
            ${nestedCleanup ? 'try {' : ''}
            context.emit('request',request);
            page.emit('console',{type:()=> 'error',text:()=>{throw new Error('Never read console text');}});
            throw new Error('expect(locator) '+protectedValue);
            ${nestedCleanup ? `${captureBeforeCleanup ? '} catch (error) { await captureFunctionalFailure(page); throw error;' : ''}} finally {
              beforeCleanup = false;
              context.emit('request',request);
            }` : ''}
          });
        } finally {
          beforeCleanup = false;
          context.emit('request',request);
        }
      });
    `;
}

function runFixture({ domain = 'files', journeyId = 'FUNC-FILE-002', stepId = 'F05-FAST-04', nestedCleanup = false, captureBeforeCleanup = false } = {}) {
  const directory = mkdtempSync(join(tmpdir(), 'functional-diagnostic-fixture-'));
  try {
    const cli = fileURLToPath(new URL('../../node_modules/@playwright/test/cli.js', import.meta.url));
    const fixture = fileURLToPath(new URL('./fixtures/diagnostic-test.ts', import.meta.url));
    const reporter = fileURLToPath(new URL('./fixtures/functional-evidence-reporter.mjs', import.meta.url));
    mkdirSync(join(directory, 'artifacts/functional'), { recursive: true });
    mkdirSync(join(directory, 'tests/functional'), { recursive: true });
    writeFileSync(join(directory, `artifacts/functional/setup-${domain}.json`), '{"setupSeconds":1}');
    writeFileSync(join(directory, 'tests/functional/quarantine.json'), '{"entries":[]}');
    writeFileSync(join(directory, 'package.json'), '{"type":"module"}');
    writeFileSync(join(directory, 'playwright.config.ts'), `export default {testDir:'.',workers:1,retries:0,reporter:[[${JSON.stringify(reporter)}]]};`);
    writeFileSync(join(directory, 'fixture.spec.ts'), fixtureSource(fixture, { journeyId, stepId, nestedCleanup, captureBeforeCleanup }));
    const result = spawnSync(process.execPath, [cli, 'test'], { cwd: directory, encoding: 'utf8', timeout: 30000,
      env: { ...process.env, COGLATAS_FUNCTIONAL_EVIDENCE: '1', COGLATAS_FUNCTIONAL_DOMAIN: domain, COGLATAS_FUNCTIONAL_SELECTED_GATES: 'functional-full',
        TARGET_SHA: 'a'.repeat(40), GITHUB_RUN_ID: '100', GITHUB_RUN_ATTEMPT: '1' } });
    assert.ifError(result.error);
    assert.notEqual(result.status, 0);
    const diagnosticPath = join(directory, `artifacts/functional/diagnostics-${domain}.json`);
    assert.ok(existsSync(diagnosticPath), result.stdout + result.stderr);
    const diagnostics = JSON.parse(readFileSync(diagnosticPath, 'utf8'));
    const attempt = diagnostics.journeys[0].attempts[0];
    assert.equal(diagnostics.journeys[0].status, 'FAIL', result.stdout + result.stderr);
    assert.equal(attempt.failedStepId, stepId);
    assert.equal(attempt.failureKind, 'ASSERTION');
    assert.equal(attempt.browserEvidenceState, 'COMPLETE');
    assert.equal(attempt.browser.projectionCheckpoint, 'failed-step');
    assert.equal(attempt.browser.structuralSnapshotState, 'UNAVAILABLE');
    assert.ok(!JSON.stringify(diagnostics).includes('PRIVATE_BODY_COOKIE_TOKEN_PASSWORD'));
    return attempt.browser;
  } finally {
    rmSync(directory, { recursive: true, force: true });
  }
}

test('actual diagnostic fixture freezes structural state and network before owner cleanup without raw content', () => {
  const browser = runFixture();
  assert.equal(browser.projection.filesPage, true);
  assert.equal(browser.projection.detailHeading, true);
  assert.equal(browser.network.length, 1);
});

test('messaging failure checkpoint survives inner cleanup and repeated outer capture without leaking protected content', () => {
  const browser = runFixture({ domain: 'collaboration', journeyId: 'FUNC-MSG-001', stepId: 'MSG-THREAD',
    nestedCleanup: true, captureBeforeCleanup: true });
  assert.equal(browser.projection.filesPage, true);
  // Controlled fixture booleans witness capture timing; this does not model the Messaging DOM.
  assert.equal(browser.consoleCounts.error, 1);
  assert.equal(browser.network[0].operation, 'message');
  assert.equal(browser.network.length, 1, 'cleanup events must not overwrite the first failure checkpoint');
});

test('negative control exposes cleanup-altered diagnostics when only the outer messaging step captures', () => {
  const browser = runFixture({ domain: 'collaboration', journeyId: 'FUNC-MSG-001', stepId: 'MSG-THREAD',
    nestedCleanup: true, captureBeforeCleanup: false });
  assert.equal(browser.projection.filesPage, false);
  assert.equal(browser.network.length, 2, 'the inner cleanup event arrives before the outer step can capture');
});
