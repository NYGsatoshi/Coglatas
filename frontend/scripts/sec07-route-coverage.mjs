// SEC-07 #580: source-derived representative browser route manifest only.
// This module neither starts a scanner nor authenticates actual browser sessions.
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import ts from 'typescript';
import { SourceInventory } from './check-av-mig-source.mjs';

export const ROUTE_SOURCE = fileURLToPath(new URL('../src/app/app.routes.ts', import.meta.url));
export const REQUIRED = Object.freeze({
  workspace: ['/workspaces', '/workspaces/:workspaceId/files'],
  project: ['/projects', '/projects/:projectId'],
  task: ['/tasks', '/projects/:projectId/tasks/:taskId'],
  files: ['/files'],
  messaging: ['/messages', '/conversations/:conversationId'],
  announcement: ['/announcements'],
  audit: ['/admin/audit'],
});
export const ROLES = Object.freeze(['alpha-owner', 'alpha-restricted']);

const fail = () => { throw new Error('SEC-07 source or discovery receipt is invalid'); };
const stringList = (value) => Array.isArray(value) && value.every((item) =>
  typeof item === 'string' && item.length > 0 && item.length <= 160);
const exactKeys = (value, expected) => value !== null && !Array.isArray(value) &&
  typeof value === 'object' &&
  JSON.stringify(Object.keys(value).sort()) === JSON.stringify([...expected].sort());

function shellChildren(source) {
  const declaration = source.statements.filter(ts.isVariableStatement)
    .flatMap((item) => [...item.declarationList.declarations])
    .find((item) => item.name.getText(source) === 'routes');
  assert.ok(declaration && ts.isArrayLiteralExpression(declaration.initializer),
    'SEC-07: routes declaration must be a static array');
  const shells = declaration.initializer.elements.filter(ts.isObjectLiteralExpression)
    .filter((item) => SourceInventory.property(item, 'children'));
  assert.equal(shells.length, 1, 'SEC-07: exactly one protected shell expected');
  const shell = shells[0];
  const guard = SourceInventory.property(shell, 'canActivate');
  assert.ok(guard && ts.isArrayLiteralExpression(guard) && guard.elements.length === 1 &&
    ts.isIdentifier(guard.elements[0]) && guard.elements[0].text === 'authSessionGuard',
  'SEC-07: protected shell must retain the session guard');
  const children = SourceInventory.property(shell, 'children');
  assert.ok(children && ts.isArrayLiteralExpression(children));
  return children;
}

export function manifest(sourceText = readFileSync(ROUTE_SOURCE, 'utf8')) {
  assert.ok(typeof sourceText === 'string' && Buffer.byteLength(sourceText, 'utf8') < 256 * 1024);
  const parsed = ts.createSourceFile('app.routes.ts', sourceText, ts.ScriptTarget.Latest, true);
  assert.equal(parsed.parseDiagnostics.length, 0, 'SEC-07: invalid TypeScript route source');
  const routes = SourceInventory.routeRecords(shellChildren(parsed))
    .filter((item) => item.kind === 'screen')
    .map((item) => item.path);
  assert.equal(new Set(routes).size, routes.length, 'SEC-07: duplicate protected route');
  const present = new Set(routes);
  for (const [name, representativeRoutes] of Object.entries(REQUIRED)) {
    assert.ok(name && representativeRoutes.every((route) => present.has(route)),
      'SEC-07: required route class missing from guarded Angular source');
  }
  return {
    schema: 'coglatas-sec07-angular-route-manifest-v1',
    scope: 'REPRESENTATIVE_SOURCE_ONLY',
    sourceSha256: createHash('sha256').update(sourceText, 'utf8').digest('hex'),
    representativeClasses: REQUIRED,
    protectedSourceRouteCount: routes.length,
    authenticatedBrowserObservation: 'UNVERIFIED',
    scannerExecution: 'NOT_EXECUTED',
    releaseAcceptance: 'BLOCKED',
  };
}

export function verifyDiscovery(receipt, sourceManifest = manifest()) {
  if (!exactKeys(receipt, ['schema', 'scope', 'sourceSha256', 'actors']) ||
      receipt.schema !== 'coglatas-sec07-synthetic-discovery-input-v1' ||
      receipt.scope !== 'UNATTESTED_TEST_INPUT' ||
      receipt.sourceSha256 !== sourceManifest.sourceSha256 ||
      !Array.isArray(receipt.actors) || receipt.actors.length !== ROLES.length) fail();
  if (receipt.actors.some((row, index) =>
    !exactKeys(row, ['role', 'authenticated', 'routes']) ||
    row.role !== ROLES[index] || row.authenticated !== true ||
    !stringList(row.routes) || new Set(row.routes).size !== row.routes.length ||
    row.routes.some((path) => !path.startsWith('/app/') ||
      /[?#\\]/u.test(path) || path.includes('..')))) fail();
  for (const row of receipt.actors) {
    const observed = new Set(row.routes.map((path) => path.slice('/app'.length)));
    for (const paths of Object.values(REQUIRED)) {
      if (!paths.some((path) => observed.has(path))) fail();
    }
  }
  // A synthetically populated JSON file is never proof that ZAP authenticated,
  // crawled a route, followed same-origin links, or tested the actual product.
  return {
    sourceManifest: 'BOUND',
    reportedRepresentativeClassPresence: 'COMPLETE_UNATTESTED',
    authenticatedBrowserObservation: 'UNVERIFIED',
    scannerExecution: 'NOT_EXECUTED',
    releaseAcceptance: 'BLOCKED',
  };
}
