import test from 'node:test';
import assert from 'node:assert/strict';
import { assertMobileMvp0Profile, mobileMvp0Device, mobileMvp0Project, validateMobileMvp0Actors, mobileMvp0FailureLocation } from './mobile-mvp0-profile.mjs';
const project = () => ({ name: mobileMvp0Project, use: { browserName: 'chromium', ...structuredClone(mobileMvp0Device) } });
test('explicit first-attempt isolated Mobile profile is accepted', () => assert.doesNotThrow(() => assertMobileMvp0Profile(project(), 0, '1')));
test('ordinary Desktop or canonical owner project cannot claim MVP0 Mobile', () => {
  for (const name of ['chromium-desktop', 'real-backend-mobile-320']) {
    assert.throws(() => assertMobileMvp0Profile({ ...project(), name }, 0, '1'));
  }
});
test('missing opt-in and retry cannot become first-attempt Mobile evidence', () => {
  assert.throws(() => assertMobileMvp0Profile(project(), 0, '0'));
  assert.throws(() => assertMobileMvp0Profile(project(), 1, '1'));
});
test('widened, untouchable, nonmobile or different browser contexts are rejected', () => {
  for (const patch of [{ viewport: { width: 1280, height: 800 } }, { screen: { width: 1280, height: 800 } },
    { hasTouch: false }, { isMobile: false }, { deviceScaleFactor: 2 }, { browserName: 'firefox' }]) {
    const p = project(); Object.assign(p.use, patch);
    assert.throws(() => assertMobileMvp0Profile(p, 0, '1'));
  }
});

test('all four real actor receipts are required, with no Desktop substitute', () => {
  const geometry = { width: 320, height: 800, screenWidth: 320, screenHeight: 800, scale: 1, touchPoints: 1 };
  const receipt = { project: mobileMvp0Project, retry: 0, actors: Object.fromEntries(
    ['manager', 'recipient', 'third', 'newcomer'].map(name => [name, { ...geometry }])) };
  assert.equal(validateMobileMvp0Actors(receipt), 4);
  for (const mutate of [r => delete r.actors.newcomer, r => { r.actors.recipient.width = 1280; },
    r => { r.actors.unexpected = geometry; }, r => { r.retry = 1; }]) {
    const altered = structuredClone(receipt); mutate(altered);
    assert.throws(() => validateMobileMvp0Actors(altered));
  }
});

test('failure correlation exposes only an allowlisted source location, never assertion content', () => {
  const privateText = 'SYNTHETIC_PRIVATE_BODY cookie=SYNTHETIC_SECRET';
  assert.deepEqual(mobileMvp0FailureLocation(privateText + '\n at login (/work/tests/ui/real-backend-smoke.spec.ts:4291:58)'),
    { file: 'tests/ui/real-backend-smoke.spec.ts', line: 4291 });
  assert.equal(mobileMvp0FailureLocation(privateText), null);
  assert.equal(mobileMvp0FailureLocation('/work/arbitrary/private.txt:34:5'), null);
});
