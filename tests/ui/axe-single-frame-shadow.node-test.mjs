import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import test from 'node:test';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const workflow = readFileSync(path.join(root, '.github/workflows/ci.yml'), 'utf8');
const spec = readFileSync(path.join(root, 'tests/ui/penpot-responsive.spec.ts'), 'utf8');
const a11y = readFileSync(path.join(root, 'tests/ui/a11y.ts'), 'utf8');

test('Axe experiment requires independent PR/base/label opt-in', () => {
  const line = workflow.split('\n').find(x =>
    x.trimStart().startsWith('COGLATAS_AXE_SINGLE_FRAME_SHADOW:'));
  assert.ok(line, 'No experiment flag');
  for (const guard of [
    "github.event_name == 'pull_request'",
    "github.base_ref == 'SecurityTestImplimation'",
    "contains(github.event.pull_request.labels.*.name, 'ci-axe-single-frame-shadow')",
    "&& '1' || '0'"
  ]) assert.ok(line.includes(guard), 'Missing independent eligibility: ' + guard);
  assert.match(spec, /process\.env\['COGLATAS_AXE_SINGLE_FRAME_SHADOW'\] === '1'/);
});

test('The full canonical Axe result is authoritative before guarded legacy comparison', () => {
  const method = spec.slice(spec.indexOf('async analyze(page: Page, selector?: string)'));
  const canonical = method.indexOf('const standard = await expectNoAccessibilityViolations(page, selector)');
  const frames = method.indexOf('expect(page.frames()).toHaveLength(1)');
  const noIframe = method.indexOf("page.locator('iframe, frame').count()");
  const legacy = method.indexOf('new AxeBuilder({ page }).setLegacyMode()');
  const parity = method.indexOf('expect(comparableAxeResults(legacy)).toEqual(comparableAxeResults(standard))');
  assert.ok(canonical >= 0 && frames > canonical && noIframe > canonical &&
    legacy > frames && parity > legacy, 'Canonical / frame scope / parity order drift');
  assert.equal((spec.match(/axeProbe\.analyze\(page/g) || []).length, 3);
  for (const category of ["'violations'", "'incomplete'", "'passes'", "'inapplicable'"]) {
    assert.ok(spec.includes(category), 'Missing canonical ruleset category');
  }
  assert.match(a11y, /expect\(results\.violations, formatViolations\(results\.violations\)\)\.toEqual\(\[\]\)/);
  assert.match(a11y, /return results;/);
  assert.match(spec, /axeProbe\.report\(testInfo\.retry, completed\)/);
});

test('Real Compose browser forwards the experiment and defaults it OFF', {
  skip: process.env.GITHUB_ACTIONS !== 'true'
}, () => {
  function read(flag) {
    const env = { ...process.env };
    delete env.COGLATAS_AXE_SINGLE_FRAME_SHADOW;
    if (flag !== undefined) env.COGLATAS_AXE_SINGLE_FRAME_SHADOW = flag;
    const result = spawnSync('docker', [
      'compose', '-f', 'infra/compose/dev/playwright.yml', 'config', '--format', 'json'
    ], { cwd: root, env, encoding: 'utf8', timeout: 20000 });
    assert.equal(result.status, 0, (result.stderr || '').slice(0, 1000));
    return JSON.parse(result.stdout).services['angular-playwright'];
  }
  const enabled = read('1'), disabled = read(undefined), invalid = read('false');
  assert.equal(enabled.environment.COGLATAS_AXE_SINGLE_FRAME_SHADOW, '1');
  assert.equal(disabled.environment.COGLATAS_AXE_SINGLE_FRAME_SHADOW, '0');
  assert.equal(invalid.environment.COGLATAS_AXE_SINGLE_FRAME_SHADOW, 'false');
  assert.deepEqual(Object.keys(enabled.environment).sort(),
    Object.keys(disabled.environment).sort());
  assert.equal(enabled.network_mode, disabled.network_mode);
});
