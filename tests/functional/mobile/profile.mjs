export const mobileProject = 'real-backend-mobile-320';

/** Device geometry only: never inherit another actor's authentication state. */
export function mobileDeviceOptions(projectName) {
  return projectName === mobileProject ? {
    viewport: { width: 320, height: 800 }, screen: { width: 320, height: 800 },
    isMobile: true, hasTouch: true, deviceScaleFactor: 1,
  } : {};
}

export function assertMobileProfile(browserName, project, enabled = process.env.COGLATAS_REAL_BACKEND_MOBILE) {
  const use = project.use;
  if (enabled !== '1' || browserName !== 'chromium' || use.browserName !== 'chromium' || project.name !== mobileProject ||
      use.viewport?.width !== 320 || use.viewport?.height !== 800 ||
      use.screen?.width !== 320 || use.screen?.height !== 800 ||
      use.isMobile !== true || use.hasTouch !== true || use.deviceScaleFactor !== 1) {
    throw new Error('The isolated real-backend mobile 320 touch profile is required.');
  }
}

export const mobileOwners = Object.freeze({
  'FUNC-MSG-001': ['MSG-01', 'MSG-02', 'MSG-03', 'MSG-04', 'MSG-THREAD', 'MSG-THREAD-DENIAL', 'MSG-RECOVERY'],
  'FUNC-NOTIF-001': ['NOTIF-01', 'NOTIF-02', 'NOTIF-03', 'NOTIF-04'],
  'FUNC-FILE-002': Array.from({ length: 7 }, (_, index) => `F05-FAST-0${index + 1}`),
  'FUNC-MOBILE-001': ['MOB-01', 'MOB-02', 'MOB-03', 'MOB-04', 'MOB-05'],
});

/** Reject duplicate, absent, skipped, retried, incomplete or wrongly profiled owners. */
export function validateMobileOwners(records) {
  if (records.length !== 4) { throw new Error('Mobile acceptance requires exactly four owners.'); }
  return Object.entries(mobileOwners).map(([id, steps]) => {
    const matches = records.filter((record) => record.id === id);
    const owner = matches[0];
    const attempt = owner?.attempts?.[0];
    if (matches.length !== 1 || owner.project !== mobileProject || owner.expectedStatus !== 'passed' ||
        owner.backend !== 'real' || owner.attempts.length !== 1 || attempt.status !== 'passed' || attempt.retry !== 0 ||
        steps.some((step) => !attempt.steps.includes(step))) {
      throw new Error('Mobile acceptance owner execution is incomplete or unsuccessful.');
    }
    return { journeyId: id, status: 'PASS', attempts: 1, retries: 0, completedStepIds: steps };
  });
}
