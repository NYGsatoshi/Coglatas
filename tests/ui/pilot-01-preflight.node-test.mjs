import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { stripTypeScriptTypes } from 'node:module';
import { test } from 'node:test';

const root = new URL('../../', import.meta.url);
const source = (path) => readFileSync(new URL(path, root), 'utf8');

test('Pilot-01 real-backend TypeScript helpers are syntactically parseable', () => {
  for (const path of [
    'tests/ui/pilot-workspace-channel-real.ts',
    'tests/ui/workspace-member-management-real.ts'
  ]) {
    assert.doesNotThrow(() => stripTypeScriptTypes(source(path), { mode: 'strip' }), path);
  }
});

test('Pilot-01 is wired into a required real-backend P0 test without adding an ungated title', () => {
  const smoke = source('tests/ui/real-backend-smoke.spec.ts');
  const manifest = source('scripts/ci/real-backend-pr-p0-required-tests.txt');
  assert.match(manifest,
    /^creates and activates a Workspace through the delegated real-backend capability$/m);
  assert.match(smoke, /preparePilotPrivateChannelHistory\(/);
  assert.match(smoke, /createPilotWorkspaceChannelJourney\(/);
  assert.match(smoke, /pilotJourney\.hooks\)/);
  assert.match(smoke, /pilotJourney\.evidence\(\)/);
  assert.doesNotMatch(smoke, /page\.route\([^)]*chat-channels/);
});

test('Pilot-01 checks every Workspace mutation phase and never treats server errors as denial', () => {
  const member = source('tests/ui/workspace-member-management-real.ts');
  const pilot = source('tests/ui/pilot-workspace-channel-real.ts');
  for (const stage of ['beforeAdmission', 'afterAdmission', 'afterReadOnly', 'afterRestored', 'afterRemoval']) {
    assert.match(member, new RegExp('await hooks\\.' + stage + '\\?\\.\\('));
    assert.match(pilot, new RegExp(stage + ': async'));
  }
  assert.match(pilot, /\[400, 401, 403, 404\]/);
  assert.match(pilot, /expect\(stages, 'all five cross-session authorization phases/);
  assert.match(pilot, /readonlyWriteDenied: true/);
  assert.match(pilot, /scopedFilesDeniedAfterRemoval: true/);
});
