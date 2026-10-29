import MobileMvp0Reporter from './mobile-mvp0-reporter.mjs';
import test from 'node:test';
import assert from 'node:assert/strict';
import { assertMobileMvp0Profile, mobileMvp0Device, mobileMvp0Project, validateMobileMvp0Actors, mobileMvp0FailureLocation } from './mobile-mvp0-profile.mjs';
const project = () => ({ name: mobileMvp0Project, use: { browserName: 'chromium', ...structuredClone(mobileMvp0Device) } });
await test('explicit first-attempt isolated Mobile profile is accepted', () => assert.doesNotThrow(() => assertMobileMvp0Profile(project(), 0, '1')));
await test('ordinary Desktop or canonical owner project cannot claim MVP0 Mobile', () => {
  for (const name of ['chromium-desktop', 'real-backend-mobile-320']) {
    assert.throws(() => assertMobileMvp0Profile({ ...project(), name }, 0, '1'));
  }
});
await test('missing opt-in and retry cannot become first-attempt Mobile evidence', () => {
  assert.throws(() => assertMobileMvp0Profile(project(), 0, '0'));
  assert.throws(() => assertMobileMvp0Profile(project(), 1, '1'));
});
await test('widened, untouchable, nonmobile or different browser contexts are rejected', () => {
  for (const patch of [{ viewport: { width: 1280, height: 800 } }, { screen: { width: 1280, height: 800 } },
    { hasTouch: false }, { isMobile: false }, { deviceScaleFactor: 2 }, { browserName: 'firefox' }]) {
    const p = project(); Object.assign(p.use, patch);
    assert.throws(() => assertMobileMvp0Profile(p, 0, '1'));
  }
});

await test('all four real actor receipts are required, with no Desktop substitute', () => {
  const geometry = { width: 320, height: 800, screenWidth: 320, screenHeight: 800, scale: 1, touchPoints: 1 };
  const receipt = { project: mobileMvp0Project, retry: 0, actors: Object.fromEntries(
    ['manager', 'recipient', 'third', 'newcomer'].map(name => [name, { ...geometry }])) };
  assert.equal(validateMobileMvp0Actors(receipt), 4);
  for (const mutate of [r => delete r.actors.newcomer, r => { r.actors.recipient.width = 1280; },
    r => { r.actors.unexpected = geometry; }, r => { r.actors.unexpected = r.actors.newcomer; delete r.actors.newcomer; },
    r => { r.actors = Object.create(r.actors); }, r => { r.retry = 1; }]) {
    const altered = structuredClone(receipt); mutate(altered);
    assert.throws(() => validateMobileMvp0Actors(altered));
  }
});

await test('failure correlation exposes only an allowlisted source location, never assertion content', () => {
  const privateText = 'SYNTHETIC_PRIVATE_BODY cookie=SYNTHETIC_SECRET';
  assert.deepEqual(mobileMvp0FailureLocation(privateText + '\n at login (/work/tests/ui/real-backend-smoke.spec.ts:4291:58)'),
    { file: 'tests/ui/real-backend-smoke.spec.ts', line: 4291 });
  assert.equal(mobileMvp0FailureLocation(privateText), null);
  assert.equal(mobileMvp0FailureLocation('/work/arbitrary/private.txt:34:5'), null);
});

await test('closed reporter owns stdout and never mirrors raw worker/global error values', () => {
  const output = [], reporter = new MobileMvp0Reporter({ write: text => output.push(text) });
  assert.equal(reporter.printsToStdio(), true);
  reporter.onBegin();
  reporter.onStdOut('SYNTHETIC_PRIVATE_MESSAGE'); reporter.onStdErr('SYNTHETIC_COOKIE');
  reporter.onError({ message: 'SYNTHETIC_SECRET' }); reporter.onEnd({ status: 'SYNTHETIC_PRIVATE' });
  assert.equal(output.some(text => text.includes('SYNTHETIC_')), false);
  assert.deepEqual(JSON.parse(output.at(-1)), { mobileMvp0Run: 'UNKNOWN' });
});
await test('failure reporter preserves bounded caller chronology without assertion or path contents', () => {
  const output = [], reporter = new MobileMvp0Reporter({ write: text => output.push(text) });
  reporter.onTestEnd({ title: 'SYNTHETIC_PRIVATE_TITLE' }, { status: 'timedOut', retry: 0,
    errors: [{ stack: 'SYNTHETIC_PRIVATE_MESSAGE\n at wait (/work/tests/ui/real-backend-smoke.spec.ts:5377:15)\n at caller (/work/tests/ui/real-backend-smoke.spec.ts:5212:5)\n at private (/arbitrary/private.txt:55:1)' }] });
  assert.deepEqual(JSON.parse(output[0]), { mobileMvp0Case: 'timedOut', retry: 0, locations: [
    { file: 'tests/ui/real-backend-smoke.spec.ts', line: 5377 }, { file: 'tests/ui/real-backend-smoke.spec.ts', line: 5212 }] });
  assert.equal(output[0].includes('SYNTHETIC_'), false);
  assert.equal(output[0].includes('private.txt'), false);
});
