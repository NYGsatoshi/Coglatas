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
    ];

  for (const [name, mutate] of cases) {
    test(`production source inventory CLI rejects ${name}`, () => {
      const directory = mkdtempSync(resolve(tmpdir(), 'av-mig-route-'));
      try {
        const dataPath = resolve(directory, 'inputs.json'),
          routePath = resolve(directory, 'app.routes.ts'),
          original = readFileSync(routeSourcePath, 'utf8'),
          mutated = mutate(original);
        assert.notEqual(mutated, original, `${name}: mutation must alter route source`);
        writeFileSync(dataPath, JSON.stringify(inputs()));
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
