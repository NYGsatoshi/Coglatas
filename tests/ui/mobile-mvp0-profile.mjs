export const mobileMvp0Project = 'real-backend-mobile-mvp0-320';
export const mobileMvp0Title = 'exercises mandatory authenticated MVP0 flows through ASP.NET Core backend';
export const mobileMvp0Device = Object.freeze({
  viewport: Object.freeze({ width: 320, height: 800 }),
  screen: Object.freeze({ width: 320, height: 800 }),
  isMobile: true, hasTouch: true, deviceScaleFactor: 1,
});

export function assertMobileMvp0Profile(project, retry, enabled = process.env.COGLATAS_REAL_BACKEND_MVP0_MOBILE) {
  const use = project.use;
  if (enabled !== '1' || retry !== 0 || project.name !== mobileMvp0Project || use.browserName !== 'chromium' ||
      use.viewport?.width !== 320 || use.viewport?.height !== 800 ||
      use.screen?.width !== 320 || use.screen?.height !== 800 ||
      use.isMobile !== true || use.hasTouch !== true || use.deviceScaleFactor !== 1) {
    throw new Error('MVP0 Mobile requires its explicit isolated Chromium 320 touch profile and first attempt.');
  }
}

export function validateMobileMvp0Actors(receipt) {
  if (receipt.project !== mobileMvp0Project || receipt.retry !== 0 || !receipt.actors ||
      Object.keys(receipt.actors).sort().join(',') !== 'manager,newcomer,recipient,third') {
    throw new Error('MVP0 Mobile requires exactly four verified actor profiles.');
  }
  for (const actual of Object.values(receipt.actors)) {
    if (actual.width !== 320 || actual.height !== 800 || actual.screenWidth !== 320 ||
        actual.screenHeight !== 800 || actual.scale !== 1 || actual.touchPoints !== 1) {
      throw new Error('An actor was not verified at the native narrow touch profile.');
    }
  }
  return 4;
}

export function mobileMvp0FailureLocations(text) {
  const pattern = /\/work\/(tests\/ui\/(?:real-backend-smoke\.spec\.ts|group-dm-real\.ts)):(\d{1,5}):/g;
  const locations = [], seen = new Set();
  for (const match of String(text).slice(0, 32768).matchAll(pattern)) {
    const key = match[1] + ':' + match[2];
    if (!seen.has(key)) { locations.push({ file: match[1], line: Number(match[2]) }); seen.add(key); }
    if (locations.length === 8) break;
  }
  return locations;
}

export function mobileMvp0FailureLocation(junit) {
  return mobileMvp0FailureLocations(junit)[0] ?? null;
}
