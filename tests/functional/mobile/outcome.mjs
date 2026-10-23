import { mobileOwners } from './profile.mjs';
import { failureKind } from '../fixtures/functional-diagnostics.mjs';

export function safeRunIdentity(value) {
  return value === undefined || value === 'local' ? 'local' : /^\d{1,20}$/.test(value) ? value : 'UNAVAILABLE';
}

const sources = [
  ['files/files-fast-journey.spec.ts', 'FILES_OWNER'],
  ['files/files-lifecycle-steps.ts', 'FILES_HELPER'],
  ['messaging/collaboration-owner-journey.spec.ts', 'COLLABORATION_OWNER'],
  ['mobile/channel.browser.ts', 'MOBILE_CHANNEL'],
  ['ui/workspace-channel-directory-real.ts', 'CHANNEL_HELPER'],
];

/** Project errors in memory into fixed enums and approved source line numbers only. */
export function mobileOutcome(test, result) {
  const annotated = test.annotations.find((item) => item.type === 'journey')?.description;
  const id = Object.hasOwn(mobileOwners, annotated ?? '') ? annotated : 'UNKNOWN_OWNER';
  const expected = mobileOwners[id] ?? [];
  const completed = new Set();
  let failure = null;
  let additionalFailure = false;
  const visit = (step, parentId = 'OWNER', depth = 0) => {
    if (depth > 20) { return; }
    const token = typeof step.title === 'string' ? step.title.match(/\/\s+([A-Z0-9-]+)\b/)?.[1] : null;
    const stepId = step.category === 'test.step' && expected.includes(token) ? token : parentId;
    if (step.category === 'test.step' && expected.includes(token) && !step.error) { completed.add(token); }
    if (step.error) {
      // Later finally/cleanup errors must not replace the earlier failed
      // mandatory step. Descend within the selected branch for its source line.
      if (!failure || failure.stepId === stepId || (failure.stepId === 'OWNER' && stepId !== 'OWNER')) {
        failure = { step, stepId };
      } else { additionalFailure = true; }
    }
    for (const child of step.steps ?? []) { visit(child, stepId, depth + 1); }
  };
  for (const step of result?.steps ?? []) { visit(step); }
  const status = ['passed', 'failed', 'timedOut', 'skipped', 'interrupted'].includes(result?.status)
    ? result.status : 'NOT_EXECUTED';
  const location = failure?.step.location;
  const file = typeof location?.file === 'string' ? location.file.replaceAll('\\', '/') : '';
  const source = sources.find(([suffix]) => file.endsWith('/' + suffix));
  return {
    journeyId: id, status, retry: Number.isSafeInteger(result?.retry) && result.retry >= 0 && result.retry <= 10 ? result.retry : null,
    completedStepIds: expected.filter((step) => completed.has(step)),
    failedStepId: status === 'passed' ? null : failure?.stepId ?? 'OWNER',
    failureKind: result ? failureKind({ ...result, errors: failure ? [] : result.errors }, failure?.step) : 'NOT_EXECUTED',
    sourceModule: source?.[1] ?? 'UNKNOWN',
    sourceLine: source && Number.isSafeInteger(location?.line) && location.line > 0 && location.line < 10000 ? location.line : null,
    additionalFailure,
  };
}
