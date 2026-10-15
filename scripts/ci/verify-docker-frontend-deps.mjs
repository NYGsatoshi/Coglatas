import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { accessSync, constants } from 'node:fs';
import { createRequire } from 'node:module';
import path from 'node:path';

// Run only in the no-network, no-secret Docker smoke stage.
const root = '/src/frontend';
const require = createRequire(path.join(root, 'package.json'));
const bin = name => path.join(root, 'node_modules', '.bin', name);

for (const name of ['ng', 'esbuild', 'syncfusion-license']) {
  accessSync(bin(name), constants.X_OK);
}

const esbuild = require('esbuild');
assert.equal(esbuild.version, '0.28.1');
assert.match(esbuild.transformSync('const value = 42', { loader: 'js' }).code, /value/);
const esbuildCliVersion = execFileSync(bin('esbuild'), ['--version'], {
  encoding: 'utf8', timeout: 20000
}).trim();
assert.equal(esbuildCliVersion, esbuild.version);

// Validate native/optional consumers even though lifecycle scripts never ran.
const watcher = require('@parcel/watcher');
assert.equal(typeof watcher.subscribe, 'function');
const lmdb = require('lmdb');
assert.equal(typeof lmdb.open, 'function');
const msgpackrExtract = require('msgpackr-extract');
assert.ok(msgpackrExtract != null);

const angularVersion = execFileSync(bin('ng'), ['version'], {
  encoding: 'utf8',
  timeout: 30000,
  env: { ...process.env, CI: 'true', NG_CLI_ANALYTICS: 'false', NO_COLOR: '1' },
});
assert.match(angularVersion, /Angular CLI|Angular:/i);

// Do not activate Syncfusion without the protected production build environment.
console.log('Docker script-free dependency smoke passed (Angular CLI, esbuild, Syncfusion CLI presence, native modules).');
