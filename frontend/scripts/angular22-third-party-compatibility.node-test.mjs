import assert from 'node:assert/strict';
import { access, readFile } from 'node:fs/promises';
import test from 'node:test';

const angularJson = JSON.parse(await readFile(new URL('../angular.json', import.meta.url), 'utf8')),
  frontendPackageLock = JSON.parse(await readFile(new URL('../package-lock.json', import.meta.url), 'utf8')),
  coglatasPackageJson = JSON.parse(await readFile(new URL('../../coglatas-frontend/package.json', import.meta.url), 'utf8')),
  coglatasPackageLock = JSON.parse(await readFile(new URL('../../coglatas-frontend/package-lock.json', import.meta.url), 'utf8')),
  expectedDependencies = {
    '@lucide/angular': '1.63.0',
    '@microsoft/signalr': '10.0.11',
    '@syncfusion/ej2-angular-gantt': '34.2.8',
    '@syncfusion/ej2-angular-grids': '35.1.42',
    '@syncfusion/ej2-angular-inputs': '35.1.42',
    '@syncfusion/ej2-angular-popups': '35.1.42',
    'ag-grid-angular': '36.2.0',
    'ag-grid-community': '36.2.0',
    rxjs: '7.8.2',
    'zone.js': '0.16.3',
  },
  expectedDevDependencies = {
    '@angular-devkit/build-angular': '22.2.4',
    '@storybook/angular': '10.6.5',
    jsdom: '30.1.3',
    storybook: '10.6.5',
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

test('denies unreviewed nested Syncfusion 35.1.41 installers with a package-wide fallback', () => {
  for (const dependent of ['@syncfusion/ej2-angular-grids', '@syncfusion/ej2-angular-inputs']) {
    const nested = frontendPackageLock.packages[`node_modules/${dependent}/node_modules/@syncfusion/ej2-angular-base`];
    assert.equal(nested?.version, '35.1.41', `${dependent} must pin the newly proposed base version`);
    assert.equal(nested?.hasInstallScript, true, `${dependent} base script must remain observable`);
  }
  assert.equal(packageJson.allowScripts['@syncfusion/ej2-angular-base'], false,
    'unreviewed Syncfusion base lifecycle scripts must not execute');
  assert.equal(packageJson.allowScripts['@syncfusion/ej2-angular-base@34.2.8'], true,
    'the existing reviewed v34 script must remain explicitly version-approved');
  assert.equal(Object.hasOwn(packageJson.allowScripts, '@syncfusion/ej2-angular-base@35.1.41'), false,
    'new v35 versions must not gain an independent exact-version approval');
  assert.equal(Object.hasOwn(packageJson.allowScripts, '@syncfusion/ej2-angular-base@35.1.37'), false,
    'stale base version must not stay approved');
  assert.equal(Object.hasOwn(packageJson.allowScripts, '@syncfusion/ej2-angular-base@35.1.39'), false,
    'stale base version must not stay classified');
});

test('pins the qualified Angular 22 CDK and sibling CLI/DevKit package integrity', () => {
  assert.equal(packageJson.dependencies['@angular/cdk'], '22.2.5');
  assert.equal(frontendPackageLock.packages[''].dependencies['@angular/cdk'], '22.2.5');
  assert.equal(frontendPackageLock.packages['node_modules/@angular/cdk'].version, '22.2.5');
  for (const [name, version] of Object.entries({
    '@angular-devkit/architect': '0.2202.5',
    '@angular-devkit/build-angular': '22.2.4',
    '@angular-devkit/core': '22.2.5',
    '@angular/build': '22.2.4',
    '@angular/cli': '22.2.5',
  })) {
    assert.equal(coglatasPackageJson.devDependencies[name], version);
    assert.equal(coglatasPackageLock.packages[''].devDependencies[name], version);
    const installed = coglatasPackageLock.packages[`node_modules/${name}`];
    assert.equal(installed?.version, version);
    assert.match(installed?.integrity ?? '', /^sha512-/u);
  }
});

test('denies the Popups nested Syncfusion 35.1.41 base install script', () => {
  const node = frontendPackageLock.packages[
    'node_modules/@syncfusion/ej2-angular-popups/node_modules/@syncfusion/ej2-angular-base'
  ];
  assert.equal(node?.version, '35.1.41');
  assert.equal(node?.hasInstallScript, true);
  assert.equal(packageJson.allowScripts['@syncfusion/ej2-angular-base'], false);
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

  const { assets } = angularJson.projects.frontend.architect.build.options,
    assetInputs = assets.map((asset) => typeof asset === 'string' ? asset : asset.input),
    requiredInputs = [
    'node_modules/@syncfusion/ej2-base/styles',
    'node_modules/@syncfusion/ej2-grids/styles',
    'node_modules/@syncfusion/ej2-treegrid/styles',
    'node_modules/@syncfusion/ej2-layouts/styles',
    'node_modules/@syncfusion/ej2-popups/styles',
    'node_modules/@syncfusion/ej2-gantt/styles',
  ];
  for (const requiredInput of requiredInputs) {
    assert.equal(assetInputs.includes(requiredInput), true, `${requiredInput} is missing from the production asset contract`);
  }
  await Promise.all(requiredInputs.map((requiredInput) =>
    assert.doesNotReject(
      access(new URL(`../${requiredInput}/material3.css`, import.meta.url)),
      `${requiredInput}/material3.css is missing from the installed Syncfusion package`,
    )
  ));
});

test('keeps the Angular 22 Vitest runner contract on the reviewed jsdom toolchain', () => {
  const testTarget = angularJson.projects.frontend.architect.test;

  assert.equal(testTarget.builder, '@angular/build:unit-test');
  assert.equal(testTarget.options.runnerConfig, 'vitest.config.ts');
});
