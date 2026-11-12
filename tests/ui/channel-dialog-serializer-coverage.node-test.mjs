import test from 'node:test';
import { strict as assert } from 'node:assert';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const root = fileURLToPath(new URL('../../', import.meta.url));

await test('existing channel-dialog serializer executes in its two original projects', () => {
  const output = execFileSync(process.execPath, [
    '--enable-source-maps',
    fileURLToPath(new URL('../../node_modules/@playwright/test/cli.js', import.meta.url)),
    'test', '--config',
    fileURLToPath(new URL('./channel-dialog-serializer-coverage.config.mjs', import.meta.url)),
  ], { cwd: root, encoding: 'utf8', env: process.env });
  assert.match(output, /\b2 passed\b/);
  assert.match(output, /\[chromium-desktop\]/);
  assert.match(output, /\[chromium-mobile\]/);
});
