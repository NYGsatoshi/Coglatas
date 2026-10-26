import assert from 'node:assert/strict';
import { access, readFile } from 'node:fs/promises';
import test from 'node:test';

const angularJson = JSON.parse(await readFile(new URL('../angular.json', import.meta.url), 'utf8')),
  frontendPackageLock = JSON.parse(await readFile(new URL('../package-lock.json', import.meta.url), 'utf8')),
  coglatasPackageJson = JSON.parse(await readFile(new URL('../../coglatas-frontend/package.json', import.meta.url), 'utf8')),
  coglatasPackageLock = JSON.parse(await readFile(new URL('../../coglatas-frontend/package-lock.json', import.meta.url), 'utf8')),
  expectedDependencies = {
    '@lucide/angular': '1.61.0',
    '@microsoft/signalr': '10.0.11',
    '@syncfusion/ej2-angular-gantt': '35.1.41',
    '@syncfusion/ej2-material3-theme': '35.1.37',
    '@syncfusion/ej2-angular-grids': '35.1.41',
    '@syncfusion/ej2-angular-inputs': '35.1.41',
    '@syncfusion/ej2-angular-popups': '34.2.8',
    'ag-grid-angular': '36.2.0',
    'ag-grid-community': '36.2.0',
    rxjs: '7.8.2',
    'zone.js': '0.16.3',
  },
  expectedDevDependencies = {
    '@angular-devkit/build-angular': '22.2.4',
    '@storybook/angular': '10.6.3',
    jsdom: '30.1.3',
    storybook: '10.6.3',
    vitest: '5.0.5',
  },
  packageJson = JSON.parse(await readFile(new URL('../package.json', import.meta.url), 'utf8'));

test('keeps the reviewed Angular 22 third-party versions pinned in manifests and lockfile', () => {
  for (const [dependencyType, expected] of [
    ['dependencies', expectedDependencies],
    ['devDependencies', expectedDevDependencies],
  ]) {
    for (const [name, version] of Object.entries(expected)) {
      assert.equal(packageJson[dependencyType][name], version,
        `${name} drifted from the ANG22-06 reviewed version`);
      assert.equal(frontendPackageLock.packages[''][dependencyType][name], version,
        `${name} lockfile root differs from the ANG22-06 reviewed version`);
      const installed = frontendPackageLock.packages[`node_modules/${name}`];
      assert.equal(installed?.version, version,
        `${name} lockfile node differs from the ANG22-06 reviewed version`);
      assert.match(installed?.integrity ?? '', /^sha512-/u,
        `${name} must retain its immutable registry integrity`);
    }
  }
});

test('requires exact-version deny policy for the new nested Syncfusion 35.1.41 installers', () => {
  for (const dependent of ['@syncfusion/ej2-angular-grids', '@syncfusion/ej2-angular-inputs']) {
    const nested = frontendPackageLock.packages[`node_modules/${dependent}/node_modules/@syncfusion/ej2-angular-base`];
    assert.equal(nested?.version, '35.1.41', `${dependent} must pin the newly proposed base version`);
    assert.equal(nested?.hasInstallScript, true, `${dependent} base script must remain observable`);
  }
  assert.equal(packageJson.allowScripts['@syncfusion/ej2-angular-base@35.1.41'], false,
    'unreviewed Syncfusion 35.1.41 lifecycle scripts must not execute');
  assert.equal(Object.hasOwn(packageJson.allowScripts, '@syncfusion/ej2-angular-base@35.1.37'), false,
    'stale base version must not stay approved');
  assert.equal(Object.hasOwn(packageJson.allowScripts, '@syncfusion/ej2-angular-base@35.1.39'), false,
    'stale base version must not stay classified');
});

test('pins Angular 22.2.4 CDK and sibling CLI/DevKit package integrity', () => {
  assert.equal(packageJson.dependencies['@angular/cdk'], '22.2.4');
  assert.equal(frontendPackageLock.packages[''].dependencies['@angular/cdk'], '22.2.4');
  assert.equal(frontendPackageLock.packages['node_modules/@angular/cdk'].version, '22.2.4');
  for (const [name, version] of Object.entries({
    '@angular-devkit/architect': '0.2202.4',
    '@angular-devkit/build-angular': '22.2.4',
    '@angular-devkit/core': '22.2.4',
    '@angular/build': '22.2.4',
    '@angular/cli': '22.2.4',
  })) {
    assert.equal(coglatasPackageJson.devDependencies[name], version);
    assert.equal(coglatasPackageLock.packages[''].devDependencies[name], version);
    const installed = coglatasPackageLock.packages[`node_modules/${name}`];
    assert.equal(installed?.version, version);
    assert.match(installed?.integrity ?? '', /^sha512-/u);
  }
});

test('denies unreviewed Gantt 35.1.41 nested Syncfusion installer', () => {
  const nested = frontendPackageLock.packages[
    'node_modules/@syncfusion/ej2-angular-gantt/node_modules/@syncfusion/ej2-angular-base'
  ];
  assert.equal(nested?.version, '35.1.41');
  assert.equal(nested?.hasInstallScript, true);
  assert.equal(packageJson.allowScripts['@syncfusion/ej2-angular-base@35.1.41'], false);
});

test('keeps the sibling Storybook documentation workspace on Compodoc 2.x', () => {
  assert.equal(coglatasPackageJson.devDependencies['@compodoc/compodoc'], '^2.0.0');
  assert.equal(coglatasPackageLock.packages['node_modules/@compodoc/compodoc'].version, '2.0.0');
});

test('retains the Angular Storybook browser builder and zone.js runtime', () => {
  const { architect } = angularJson.projects.frontend;
  const browserTarget = architect['storybook-browser'];
  const storybookTarget = architect.storybook;
  const buildStorybookTarget = architect['build-storybook'];

  assert.equal(browserTarget.builder, '@angular-devkit/build-angular:browser');
  assert.deepEqual(browserTarget.options.polyfills, ['zone.js']);
  assert.equal(storybookTarget.options.browserTarget, 'frontend:storybook-browser');
  assert.equal(buildStorybookTarget.options.browserTarget, 'frontend:storybook-browser');
  assert.equal(storybookTarget.options.compodoc, false);
  assert.equal(buildStorybookTarget.options.compodoc, false);
});

test('retains Syncfusion license and theme sanitation gates', async () => {
  assert.match(packageJson.scripts['syncfusion:activate'], /require-syncfusion-license\.mjs/u);
  assert.match(packageJson.scripts['build-storybook'], /sanitize-syncfusion-theme-css\.mjs/u);

  const expectedThemeAsset = {
    glob: 'material3.css',
    input: 'node_modules/@syncfusion/ej2-material3-theme/styles',
    output: 'assets/vendor/syncfusion'
  };
  for (const target of ['build', 'storybook-browser']) {
    const assets = angularJson.projects.frontend.architect[target].options.assets;
    const vendorAssets = assets.filter((entry) =>
      typeof entry === 'object' && entry.input?.startsWith('node_modules/@syncfusion/'));
    assert.deepEqual(vendorAssets, [expectedThemeAsset],
      `Production and Storybook must copy the same reviewed, consolidated Material 3 CSS (${target}).`);
  }
  await assert.doesNotReject(() => access(
    new URL('../node_modules/@syncfusion/ej2-material3-theme/styles/material3.css', import.meta.url)
  ), 'The reviewed consolidated Material 3 CSS must exist in the installed theme package.');
});

test('keeps the Angular 22 Vitest runner contract on the reviewed jsdom toolchain', () => {
  const testTarget = angularJson.projects.frontend.architect.test;

  assert.equal(testTarget.builder, '@angular/build:unit-test');
  assert.equal(testTarget.options.runnerConfig, 'vitest.config.ts');
});
