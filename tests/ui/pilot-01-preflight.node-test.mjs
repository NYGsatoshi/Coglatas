import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { stripTypeScriptTypes } from 'node:module';
import { test } from 'node:test';

const root = new URL('../../', import.meta.url);
/** Reads repository sources for structural checks that fail closed on missing Pilot-01 wiring. */
const source = (path) => readFileSync(new URL(path, root), 'utf8');

test('Pilot-01 real-backend TypeScript helpers are syntactically parseable', () => {
  for (const path of [
    'tests/ui/pilot-workspace-channel-real.ts',
    'tests/ui/workspace-member-management-real.ts',
    'tests/ui/workspace-channel-directory-real.ts'
  ]) {
    assert.doesNotThrow(() => stripTypeScriptTypes(source(path), { mode: 'strip' }), path);
  }
});

test('Pilot-01 helper-only changes trigger P0 preflight on PR and main', () => {
  const workflow = source('.github/workflows/real-backend-p0-preflight.yml');
  const pullRequestPaths = workflow.match(/^  pull_request:\r?\n([\s\S]*?)(?=^  push:)/m)?.[1];
  const mainPushPaths = workflow.match(/^  push:\r?\n([\s\S]*?)(?=^concurrency:)/m)?.[1];
  for (const [trigger, paths] of [['pull_request', pullRequestPaths], ['main push', mainPushPaths]]) {
    assert.ok(paths, trigger + ' paths section exists');
    for (const helper of [
      'tests/ui/pilot-workspace-channel-real.ts',
      'tests/ui/workspace-member-management-real.ts',
      'tests/ui/workspace-channel-directory-real.ts'
    ]) {
      assert.ok(paths.includes('- "' + helper + '"'), trigger + ' must include ' + helper);
    }
  }
});

test('Pilot-01 waits for authoritative SignalR catch-up before opening its real channel dialog', () => {
  const helper = source('tests/ui/workspace-channel-directory-real.ts');
  const synchronization = helper.indexOf("getByTestId('realtime-connection-state')");
  const click = helper.indexOf('await createAction.click()');
  assert.ok(synchronization >= 0 && synchronization < click,
    'realtime synchronization must be observed before attempting to open the dialog');
  assert.match(helper,
    /toContainText\(\s*'Realtime updates connected\.',\s*\{ timeout: 30_000 \}\s*\)/);
  assert.match(helper, /await expect\(createAction\)\.toBeEnabled\(\)/);
});

test('Pilot-01 is wired into a required real-backend P0 test without adding an ungated title', () => {
  const smoke = source('tests/ui/real-backend-smoke.spec.ts');
  const manifest = source('scripts/ci/real-backend-pr-p0-required-tests.txt');
  assert.match(manifest,
    /^creates and activates a Workspace through the delegated real-backend capability$/m);
  assert.match(smoke, /preparePilotPrivateChannelHistory\(/);
  assert.match(smoke, /createPilotWorkspaceChannelJourney\(/);
  assert.match(smoke, /smokeRecipientName,\s*waitForAuthorizationOutbox\)/);
  assert.match(smoke, /pilotJourney\.hooks\)/);
  assert.match(smoke, /pilotJourney\.evidence\(\)/);
  assert.doesNotMatch(smoke, /page\.route\([^)]*chat-channels/);
});

test('Pilot-01 reauthorizes and bounds candidate selection before channel invite', () => {
  const pilot = source('tests/ui/pilot-workspace-channel-real.ts');
  const settled = pilot.indexOf('await settleAuthorization(manager);');
  const connected = pilot.indexOf("getByTestId('realtime-connection-state')");
  const invite = pilot.indexOf("getByTestId('channel-invite-open').click()");
  assert.ok(settled >= 0 && connected > settled && invite > connected);
  assert.match(pilot, /candidate\.click\(\{ timeout: 15_000 \}\)/);
});

test('Hub-degraded My Tasks uses a valid HTTP scope without changing application authority', () => {
  const smoke = source('tests/ui/real-backend-smoke.spec.ts');
  assert.match(smoke, /name: 'All Workspaces', exact: true/);
  assert.match(smoke, /isPagedResponse\(body\) && body\.scope === 'AllWorkspaces'/);
  assert.match(smoke, /remainingAuthorizedWorkspaceCount/);
  assert.match(smoke, /revoked Workspace must be absent from the authoritative directory/);
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
  assert.match(pilot, /recipient\.request\.get\(thread\), 'uninvited private thread denied'/);
  assert.match(pilot, /const removedPost = await messageCommand\(recipient, history/);
  assert.match(pilot, /await assertMasked\(removedPost, 'removed member cannot post after Workspace removal'/);
  assert.match(pilot, /uninvitedPrivateThreadDenied: true/);
  assert.match(pilot, /readonlyWriteDenied: true/);
  assert.match(pilot, /removedMemberWriteDenied: true/);
  assert.match(pilot, /scopedFilesDeniedAfterRemoval: true/);
});
