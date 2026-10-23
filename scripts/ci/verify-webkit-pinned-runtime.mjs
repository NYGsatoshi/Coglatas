import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { access, readFile } from 'node:fs/promises';
import { createRequire } from 'node:module';
import path from 'node:path';
import { webkit } from 'playwright';

const require = createRequire(import.meta.url);
assert.equal(process.version, 'v24.21.0', 'Node runtime drift');
assert.equal(execFileSync('npm', ['--version'], { encoding: 'utf8' }).trim(), '11.19.0', 'npm runtime drift');
const lock = JSON.parse(await readFile('package-lock.json', 'utf8'));
for (const name of ['@playwright/test', 'playwright', 'playwright-core']) {
  assert.equal(lock.packages[`node_modules/${name}`].version, '1.64.1', `${name} lock drift`);
  assert.equal(require(`${name}/package.json`).version, '1.64.1', `${name} installed version drift`);
}
const metadata = JSON.parse(await readFile(path.join(path.dirname(require.resolve('playwright-core/package.json')), 'browsers.json'), 'utf8'));
const expected = metadata.browsers.find((item) => item.name === 'webkit');
assert.equal(expected.revision, '2370', 'Locked WebKit revision drift');
assert.equal(expected.browserVersion, '27.2', 'Locked WebKit version drift');
assert.equal(webkit.executablePath(), '/ms-playwright/webkit-2370/pw_run.sh', 'Installed WebKit path drift');
await access(webkit.executablePath());
const browser = await webkit.launch({ headless: true });
try {
  assert.equal(browser.version(), '27.2', 'Running WebKit version drift');
  const page = await browser.newPage();
  await page.setContent('<title>Coglatas WebKit runtime control</title>');
  assert.equal(await page.title(), 'Coglatas WebKit runtime control');
} finally {
  await browser.close();
}
console.log('Pinned WebKit launch control PASS: Node24.21.0/npm11.19.0/Playwright1.64.1/WebKit2370/27.2');
