import { mkdtempSync, readFileSync, rmSync, symlinkSync, writeFileSync } from 'node:fs';
import assert from 'node:assert/strict';
import { fileURLToPath } from 'node:url';
import { SourceInventory, inputs } from './check-av-mig-source.mjs';
import { resolve } from 'node:path';
import { spawnSync } from 'node:child_process';
import test from 'node:test';
import { tmpdir } from 'node:os';

const values = { failure: 1, loginIndex: 1, ownerIndex: 0, success: 0 };
{
  const cases = [
      ['baseline', () => inputs(), values.success],
      [
        'missing state owner',
        (inventory) => {
          inventory.routes[values.loginIndex].stateOwners = [];
        },
        values.failure,
      ],
      [
        'unknown owner',
        (inventory) => {
          inventory.routes[values.loginIndex].stateOwners.push('InventedFacade');
        },
        values.failure,
      ],
      [
        'synchronized route deletion',
        (...data) => {
          data.forEach((document) => document.routes.pop());
        },
        values.failure,
      ],
      [
        'synchronized duplicate',
        (...data) => {
          data.forEach((document) => document.routes.push(document.routes[values.loginIndex]));
        },
        values.failure,
      ],
      [
        'freeze disagreement',
        (inventory, freeze) => {
          freeze.routes[values.loginIndex].november = false;
        },
        values.failure,
      ],
      [
        'orphan owner',
        (inventory) => {
          inventory.stateOwners[values.ownerIndex].sourcePath = 'missing.ts';
        },
        values.failure,
      ],
      [
        'synchronized owner removal',
        (inventory, freeze) => {
          inventory.routes[values.loginIndex].stateOwners = ['LoginPageComponent'];
          freeze.routes[values.loginIndex].stateOwner = 'LoginPageComponent';
        },
        values.failure,
      ],
      ['unknown current Web route', (inventory, freeze, target, current) => { current.routes[0].path = '/unknown'; }, values.failure],
      ['Home component role drift', (inventory, freeze, target, current) => { current.routes.find((route) => route.path === '/home').component = 'MessagesPageComponent'; }, values.failure],
      ['Home direct owner removal', (inventory, freeze, target, current) => { current.routes.find((route) => route.path === '/home').stateOwners.pop(); }, values.failure],
      ['missing current Web route', (inventory, freeze, target, current) => { current.routes.pop(); }, values.failure],
      ['duplicate current Web route', (inventory, freeze, target, current) => { current.routes.push(current.routes[0]); }, values.failure],
      ['missing current Web owner', (inventory, freeze, target, current) => { current.routes[2].stateOwners.pop(); }, values.failure],
      ['unknown current Web owner', (inventory, freeze, target, current) => { current.routes[2].stateOwners.push('InventedFacade'); }, values.failure],
      ['synchronized current Web owner removal', (inventory, freeze, target, current) => {
        current.routes[2].stateOwners.pop();
        current.stateOwners = current.stateOwners.filter((owner) => owner.name !== 'LocalDraftStore');
      }, values.failure],
      ['current Web owner source drift', (inventory, freeze, target, current) => { current.stateOwners[1].sourcePath = 'frontend/src/app/features/files/file-stars.service.ts'; }, values.failure],
      ['missing Files owner extension', (inventory, freeze, target, current) => { current.routeOwnerExtensions.pop(); }, values.failure],
      ['synchronized Files owner extension removal', (inventory, freeze, target, current) => {
        current.routeOwnerExtensions = [];
        current.stateOwners = current.stateOwners.filter((owner) => owner.name !== 'LocalFileStarsService');
      }, values.failure],
      ['current Web Avalonia promotion', (inventory, freeze, target, current) => { current.avaloniaPromotion = true; }, values.failure],
      ['current Web target owner injection', (inventory, freeze, target, current) => { current.routes[2].targetOwner = 785; }, values.failure],
      ['local draft persistence promotion', (inventory, freeze, target, current) => { current.stateClassifications[0].storage = 'localStorage'; }, values.failure],
      ['Starred metadata payload promotion', (inventory, freeze, target, current) => { current.stateClassifications[1].payloadClass = 'file-ids-and-labels'; }, values.failure],
      ['local state authorization promotion', (inventory, freeze, target, current) => { current.stateClassifications[1].authorizationAuthority = true; }, values.failure],
      ['current Web adapter source drift', (inventory, freeze, target, current) => { current.stateClassifications[0].adapterSourcePath = 'frontend/src/app/features/files/file-stars.repository.ts'; }, values.failure],
      ['missing current NgRx import fact', (inventory, freeze, target, current) => { current.ngrx.currentProductionImports = []; }, values.failure],
    ],
    runProbe = (directory, mutate) => {
      const data = inputs(),
        path = resolve(directory, 'inputs.json'),
        script = fileURLToPath(new URL('./check-av-mig-source.mjs', import.meta.url));
      mutate(...data);
      writeFileSync(path, JSON.stringify(data));
      return spawnSync(process.execPath, [script, path], { encoding: 'utf8' });
    };
  for (const [name, mutate, expected] of cases) {
    test(`production source inventory CLI: ${name}`, () => {
      const directory = mkdtempSync(resolve(tmpdir(), 'av-mig-'));
      try {
        const result = runProbe(directory, mutate);
        assert.equal(result.status, expected, result.stderr);
      } finally {
        rmSync(directory, { force: true, recursive: true });
      }
    });
  }
}

/* eslint-disable one-var, sort-vars */
{
  const routeSourcePath = fileURLToPath(new URL('../src/app/app.routes.ts', import.meta.url)),
    script = fileURLToPath(new URL('./check-av-mig-source.mjs', import.meta.url)),
    cases = [
      [
        'component binding drift',
        (source) => source.replace('m.LoginPageComponent', 'm.SessionExpiredPageComponent'),
      ],
      [
        'redirect target drift',
        (source) => source.replace("redirectTo: 'login'", "redirectTo: 'workspaces'"),
      ],
      [
        'fallback kind drift',
        (source) => source.replace('component: PagePlaceholderComponent,', "redirectTo: 'login',"),
      ],
      ['current Web component binding drift', (source) => source.replace('m.DraftsPageComponent', 'm.AccountPageComponent')],
      ['current Web component module drift', (source) => source.replace("import('./features/drafts/drafts-page/drafts-page.component')", "import('./features/account/account-page/account-page.component')")],
      ['current Web directory binding drift', (source) => source.replace("conversationDirectory: 'dm'", "conversationDirectory: 'teams'")],
      ['undeclared current Web route', (source) => source.replace("path: 'drafts'", "path: 'unregistered-drafts'")],
      ['synchronized current Web route deletion',
        (source) => source.replace(/      \{\r?\n        path: 'drafts',[\s\S]*?\r?\n      \},\r?\n/u, ''),
        (data) => { data[3].routes = data[3].routes.filter((route) => route.path !== '/drafts'); }],
    ];

  for (const [name, mutate, mutateData] of cases) {
// Node's test runner owns completion and reports callback failures.
    void test(`production source inventory CLI rejects ${name}`, () => {
      const directory = mkdtempSync(resolve(tmpdir(), 'av-mig-route-'));
      try {
        const dataPath = resolve(directory, 'inputs.json'),
          routePath = resolve(directory, 'app.routes.ts'),
          original = readFileSync(routeSourcePath, 'utf8'),
          mutated = mutate(original);
        assert.notEqual(mutated, original, `${name}: mutation must alter route source`);
        const data = inputs();
        mutateData?.(data);
        writeFileSync(dataPath, JSON.stringify(data));
        writeFileSync(routePath, mutated);
        const result = spawnSync(process.execPath, [script, dataPath, routePath], { encoding: 'utf8' });
        assert.equal(result.status, values.failure, result.stderr);
      } finally {
        rmSync(directory, { force: true, recursive: true });
      }
    });
  }
}
/* eslint-enable one-var, sort-vars */

void test('production source inventory CLI rejects an absent current Web addendum', () => {
  const directory = mkdtempSync(resolve(tmpdir(), 'av-mig-no-web-'));
  try {
    const dataPath = resolve(directory, 'inputs.json');
    writeFileSync(dataPath, JSON.stringify(inputs().slice(0, 3)));
    const script = fileURLToPath(new URL('./check-av-mig-source.mjs', import.meta.url));
    const result = spawnSync(process.execPath, [script, dataPath], { encoding: 'utf8' });
    assert.equal(result.status, values.failure);
    assert.match(result.stderr, /Missing current Web addendum/u);
  } finally {
    rmSync(directory, { force: true, recursive: true });
  }
});

void test('current Web direct owner bindings reject a different imported source and a missing injection', () => {
  const previousRoot = SourceInventory.root;
  const root = mkdtempSync(resolve(tmpdir(), 'av-mig-owner-binding-'));
  try {
    SourceInventory.root = root;
    const componentPath = resolve(root, 'example.ts');
    const ownerPath = resolve(root, 'owner.ts');
    writeFileSync(ownerPath, 'export class LocalDraftStore {}');
    const context = {
      owners: new Map([['LocalDraftStore', {}]]),
      sources: new Map([['ExampleComponent', componentPath], ['LocalDraftStore', ownerPath]])
    };
    const route = { path: '/drafts', component: 'ExampleComponent', stateOwners: ['ExampleComponent', 'LocalDraftStore'] };
    writeFileSync(componentPath, "import { LocalDraftStore } from './other'; export class ExampleComponent { state = inject(LocalDraftStore); }");
    assert.throws(() => SourceInventory.verifyScreen(route, { ownerPath: 'example.ts' }, context), /Injected owner import mismatch/u);
    writeFileSync(componentPath, 'export class ExampleComponent {}');
    assert.throws(() => SourceInventory.verifyScreen(route, { ownerPath: 'example.ts' }, context), /directly injected state owner drift/u);
  } finally {
    SourceInventory.root = previousRoot;
    rmSync(root, { force: true, recursive: true });
  }
});


test('source inventory denies lexical and symlink escapes for data-driven source paths', () => {
  const previousRoot = SourceInventory.root,
    root = mkdtempSync(resolve(tmpdir(), 'av-mig-source-root-')),
    outside = mkdtempSync(resolve(tmpdir(), 'av-mig-source-external-'));
  try {
    SourceInventory.root = root;
    const outsideFile = resolve(outside, 'external.ts'),
      alias = resolve(root, 'alias.ts'),
      route = { component: 'ExampleComponent', path: '/example', stateOwners: ['ExampleComponent'] };
    writeFileSync(outsideFile, 'export class ExampleComponent {}');
    symlinkSync(outsideFile, alias);
    for (const ownerPath of ['../other/external.ts', 'alias.ts']) {
      assert.throws(
        () => SourceInventory.verifyScreen(route, { ownerPath }, { owners: new Map(), sources: new Map() }),
        /Invalid file path/u,
      );
    }
    const inventory = { routes: [], stateOwners: [] },
      target = { routes: [] };
    for (const routeDefinition of ['../other/external.ts', 'alias.ts']) {
      const freeze = { routes: [], routeDefinition };
      assert.throws(() => SourceInventory.verify([inventory, freeze, target]), /Invalid file path/u);
    }
  } finally {
    SourceInventory.root = previousRoot;
    rmSync(root, { force: true, recursive: true });
    rmSync(outside, { force: true, recursive: true });
  }
});
