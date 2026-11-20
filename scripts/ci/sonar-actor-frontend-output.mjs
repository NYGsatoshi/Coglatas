import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { posix, resolve } from 'node:path';
import { pathToFileURL } from 'node:url';

/** Couple the measurement copy to actual Angular production configuration and canonical producer source. */
export function measurementFrontendOutput(angular, canonicalProducer) {
  const project = angular.projects.frontend, build = project.architect.build;
  assert.equal(project.root, '');
  assert.equal(build.builder, '@angular/build:application');
  assert.equal(build.configurations.production.outputPath, undefined);
  const output = build.options.outputPath;
  assert.ok(output && typeof output === 'object' && !Array.isArray(output));
  assert.equal(typeof output.base, 'string'); assert.equal(typeof output.browser, 'string');
  assert.match(output.base, /^dist\/[A-Za-z0-9_-]+(?:\/[A-Za-z0-9_-]+)*$/);
  assert.match(output.browser, /^(?:[A-Za-z0-9_-]+(?:\/[A-Za-z0-9_-]+)*)?$/);
  const directory = posix.join('frontend', output.base, output.browser);
  // Strict coupling to the existing, tracked shell producer. Unsupported source
  // formatting or additional copy commands require review rather than a guessed path.
  const copies = [...canonicalProducer.matchAll(/^ +cp -a (\S+)\/\. artifacts\/main-runtime\/frontend\/\r?$/gm)];
  assert.equal(copies.length, 1); assert.equal(copies[0][1], directory);
  const checks = [...canonicalProducer.matchAll(/^ +test -f (frontend\/dist\/\S+)\/index\.html\r?$/gm)];
  assert.ok(checks.length > 0);
  for (const check of checks) assert.equal(check[1], directory);
  return directory;
}

if (process.argv[1] && pathToFileURL(resolve(process.argv[1])).href === import.meta.url) {
  try {
    assert.equal(process.argv.length, 2);
    console.log(measurementFrontendOutput(JSON.parse(readFileSync('frontend/angular.json', 'utf8')),
      readFileSync('.github/workflows/main-build-artifacts.yml', 'utf8')));
  } catch { console.error('Measurement frontend output source contract differs.'); process.exitCode = 1; }
}
