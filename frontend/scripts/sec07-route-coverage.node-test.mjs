import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { ROUTE_SOURCE, REQUIRED, ROLES, manifest, verifyDiscovery } from './sec07-route-coverage.mjs';

const source = readFileSync(ROUTE_SOURCE, 'utf8');
const original = manifest(source);
function synthetic() {
  return {
    schema: 'coglatas-sec07-synthetic-discovery-input-v1',
    scope: 'UNATTESTED_TEST_INPUT',
    sourceSha256: original.sourceSha256,
    actors: ROLES.map((role) => ({
      role, authenticated: true,
      routes: Object.values(REQUIRED).flatMap((values) => values.map((path) => '/app' + path)),
    })),
  };
}

test('actual Angular route declaration has guarded representative route classes', () => {
  assert.ok(original.protectedSourceRouteCount >= 20);
  assert.equal(original.authenticatedBrowserObservation, 'UNVERIFIED');
  assert.equal(original.scannerExecution, 'NOT_EXECUTED');
  assert.equal(original.releaseAcceptance, 'BLOCKED');
});

test('synthetic recorded route presence never becomes authenticated scanner evidence', () => {
  const result = verifyDiscovery(synthetic(), original);
  assert.equal(result.reportedRepresentativeClassPresence, 'COMPLETE_UNATTESTED');
  assert.equal(result.authenticatedBrowserObservation, 'UNVERIFIED');
  assert.equal(result.releaseAcceptance, 'BLOCKED');
});

test('protected shell session guard and representative routes cannot be removed', () => {
  for (const mutated of [
    source.replace('canActivate: [authSessionGuard]', 'canActivate: []'),
    source.replace("path: 'files'", "path: 'files-missing'"),
    source.replace("path: 'admin/audit'", "path: 'admin/not-audit'"),
  ]) {
    assert.notEqual(mutated, source);
    assert.throws(() => manifest(mutated), /SEC-07/u);
  }
});

test('missing role, unbound source and self-claimed scan authorization fail closed', () => {
  for (const change of [
    (report) => { report.sourceSha256 = 'a'.repeat(64); },
    (report) => { report.actors.pop(); },
    (report) => { report.actors[0].role = 'beta-owner'; },
    (report) => { report.actors[0].authenticated = false; },
    (report) => { report.scope = 'NATIVE_SCAN_APPROVED'; },
    (report) => { report.ownerApproval = 'APPROVED'; },
    (report) => { report.actors[1].routes = []; },
  ]) {
    const sample = synthetic();
    change(sample);
    assert.throws(() => verifyDiscovery(sample, original), /SEC-07/u);
  }
});

test('external, malformed and duplicated route observations never count', () => {
  for (const path of [
    'https://other.invalid/app/files', '//other.invalid/app/files',
    '/app/files?session=secret', '/app/files/../admin', '/app/files\\raw',
  ]) {
    const report = synthetic();
    report.actors[0].routes.push(path);
    assert.throws(() => verifyDiscovery(report, original), /SEC-07/u);
  }
  const duplicate = synthetic();
  duplicate.actors[0].routes.push('/app/files');
  assert.throws(() => verifyDiscovery(duplicate, original), /SEC-07/u);
});

test('representative classes cannot be credited with only the login page', () => {
  const report = synthetic();
  report.actors[1].routes = ['/app/login'];
  assert.throws(() => verifyDiscovery(report, original), /SEC-07/u);
});
