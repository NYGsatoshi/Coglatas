const HTTP_BAD_REQUEST = 400;
const HTTP_UNAUTHORIZED = 401;
const HTTP_FORBIDDEN = 403;
const HTTP_NOT_FOUND = 404;
const HTTP_CONFLICT = 409;
const NO_REMAINING_FAILURES = 0;
const FIRST_CAPTURE_GROUP = 1;
const COUNT_INCREMENT = 1;

const sameFailure = (left, right) =>
  left.method === right.method && left.path === right.path && left.status === right.status;

export const isExpectedFailure = (failure) => (
  (failure.method === 'POST' && failure.path === '/api/auth/change-password' && failure.status === HTTP_FORBIDDEN) ||
  (failure.method === 'POST' && failure.path === '/api/auth/change-password' && failure.status === HTTP_BAD_REQUEST) ||
  (failure.method === 'GET' && failure.path === '/api/auth/me' && failure.status === HTTP_UNAUTHORIZED) ||
  (failure.method === 'GET' && failure.path === '/api/projects' && failure.status === HTTP_UNAUTHORIZED) ||
  (failure.method === 'GET' && failure.path === '/api/me/tasks' && failure.status === HTTP_BAD_REQUEST) ||
  (failure.method === 'GET' && failure.path === '/api/me/tasks' && failure.status === HTTP_FORBIDDEN) ||
  (failure.method === 'GET' && failure.path === '/api/me/tasks/counts' && failure.status === HTTP_FORBIDDEN) ||
  (failure.method === 'POST' && /^\/api\/tasks\/[0-9a-f-]+\/kanban-move$/i.test(failure.path) && failure.status === HTTP_CONFLICT) ||
  (failure.method === 'GET' && /^\/api\/projects\/[0-9a-f-]+\/kanban$/i.test(failure.path) && failure.status === HTTP_NOT_FOUND) ||
  (failure.method === 'PATCH' && /^\/api\/tasks\/[0-9a-f-]+\/(?:schedule|progress)$/i.test(failure.path) && failure.status === HTTP_CONFLICT) ||
  (failure.method === 'GET' && /^\/api\/projects\/[0-9a-f-]+\/gantt$/i.test(failure.path) && failure.status === HTTP_NOT_FOUND) ||
  (failure.method === 'GET' && /^\/api\/tasks\/[0-9a-f-]+$/i.test(failure.path) && failure.status === HTTP_NOT_FOUND) ||
  (failure.method === 'POST' && /^\/api\/attachments\/[0-9a-f-]+\/download-grants$/i.test(failure.path) && failure.status === HTTP_NOT_FOUND) ||
  (failure.method === 'GET' && /^\/api\/attachments\/[0-9a-f-]+\/download$/i.test(failure.path) && failure.status === HTTP_BAD_REQUEST) ||
  (failure.method === 'POST' && /^\/api\/attachment-download-grants\/[0-9a-f-]+\/download$/i.test(failure.path) && failure.status === HTTP_BAD_REQUEST)
);

export const selectPr03cExpectedRevocationRefreshFailures = (
  evidence,
  postRevocationFailureStart,
  taskId,
) => evidence.failedApiResponses
  .slice(postRevocationFailureStart)
  .filter((failure) => {
    const method = failure.method.toUpperCase();
    const { pathname } = new URL(failure.path, 'http://localhost');
    const staleProjectTaskList =
      failure.status === HTTP_BAD_REQUEST &&
      method === 'GET' &&
      /^\/api\/projects\/[^/]+\/tasks$/u.test(pathname);
    const revokedTaskExecutionScope =
      failure.status === HTTP_NOT_FOUND &&
      method === 'GET' &&
      pathname === `/api/tasks/${taskId}/execution-scope`;
    return staleProjectTaskList || revokedTaskExecutionScope;
  });

export const classifyUnexpectedApiFailures = (
  evidence,
  scenarioExpectedFailures = [],
) => {
  const remainingScenarioExpected = [...scenarioExpectedFailures];
  const unexpected = evidence.failedApiResponses.filter((failure) => {
    if (isExpectedFailure(failure)) {
      return false;
    }
    const expectedIndex = remainingScenarioExpected.findIndex((expected) =>
      sameFailure(failure, expected),
    );
    if (expectedIndex < NO_REMAINING_FAILURES) {
      return true;
    }
    remainingScenarioExpected.splice(expectedIndex, COUNT_INCREMENT);
    return false;
  });

  return { unexpected, remainingScenarioExpected };
};

export const classifyUnexpectedConsoleErrors = (
  evidence,
  scenarioExpectedFailures = [],
) => {
  const expectedNetworkFailures = new Map();
  const remainingScenarioExpected = [...scenarioExpectedFailures];
  for (const failure of evidence.failedApiResponses) {
    let expected = isExpectedFailure(failure);
    if (!expected) {
      const expectedIndex = remainingScenarioExpected.findIndex((candidate) =>
        sameFailure(failure, candidate),
      );
      if (expectedIndex >= NO_REMAINING_FAILURES) {
        remainingScenarioExpected.splice(expectedIndex, COUNT_INCREMENT);
        expected = true;
      }
    }
    if (!expected) {
      continue;
    }
    expectedNetworkFailures.set(
      failure.status,
      (expectedNetworkFailures.get(failure.status) ?? NO_REMAINING_FAILURES) + COUNT_INCREMENT,
    );
  }

  return evidence.consoleErrors.filter((message) => {
    const match = /Failed to load resource:.*status of (\d{3})/i.exec(message);
    if (!match) {
      return true;
    }
    const status = Number(match[FIRST_CAPTURE_GROUP]);
    const remaining = expectedNetworkFailures.get(status) ?? NO_REMAINING_FAILURES;
    if (remaining === NO_REMAINING_FAILURES) {
      return true;
    }
    expectedNetworkFailures.set(status, remaining - COUNT_INCREMENT);
    return false;
  });
};
