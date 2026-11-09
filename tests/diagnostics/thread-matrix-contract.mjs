export const CASES = ['TD-01', 'TD-02', 'TD-03'];
export const REGRESSIONS = [
  'uses a dedicated 320px thread pane with keyboard focus, an isolated draft, and no overflow',
  'purges protected tabs, bodies and drafts after an authoritative 403 at 320px',
  'purges protected tabs, bodies and drafts after an authoritative 403 at 1366px',
];
const keys = ['caseId', 'classification', 'phases', 'backgroundStatus', 'freshStatus', 'earlyRedaction', 'clickPerformed', 'oldResponseExcluded', 'uiCleared'];
const phases = ['OBSERVER_REGISTERED', 'ACTION_STARTED', 'REQUEST_OBSERVED', 'ACTION_COMPLETED', 'WAITING_RESPONSE', 'RESPONSE_OBSERVED'];

export function validateMatrixObservation(value) {
  if (!value || Object.keys(value).sort(compareCodeUnits).join() !== [...keys].sort(compareCodeUnits).join() || !CASES.includes(value.caseId)) { throw new Error('Invalid matrix observation.'); }
  const expected = {
    'TD-01': ['ORDINARY_FRESH_DENIAL', null, 400, false, true, null],
    'TD-02': ['LEGITIMATE_EARLY_REDACTION', 400, null, true, false, null],
    'TD-03': ['OLD_RESPONSE_EXCLUDED', 400, 400, false, true, true],
  }[value.caseId];
  if (value.classification !== expected[0] || value.backgroundStatus !== expected[1] || value.freshStatus !== expected[2] ||
      value.earlyRedaction !== expected[3] || value.clickPerformed !== expected[4] || value.oldResponseExcluded !== expected[5] || value.uiCleared !== true ||
      !Array.isArray(value.phases) || value.phases.length !== (value.caseId === 'TD-02' ? 0 : phases.length) ||
      new Set(value.phases).size !== value.phases.length || value.phases.some((phase) => !phases.includes(phase)) ||
      (value.caseId !== 'TD-02' && (value.phases[0] !== 'OBSERVER_REGISTERED' || value.phases[1] !== 'ACTION_STARTED' ||
        !['REQUEST_OBSERVED', 'ACTION_COMPLETED'].every((phase) => value.phases.slice(2, 4).includes(phase)) ||
        value.phases[4] !== 'WAITING_RESPONSE' || value.phases[5] !== 'RESPONSE_OBSERVED'))) {
    throw new Error('Incomplete matrix observation.');
  }
  return Object.fromEntries(keys.map((key) => [key, key === 'phases' ? [...value.phases] : value[key]]));
}

export function validateExecutedCases(rows, mode) {
  const expected = mode === 'matrix' ? CASES : mode === 'default-fixture' ? REGRESSIONS.map((_, index) => `EXISTING-0${index + 1}`) : [];
  if (!expected.length || rows.length !== expected.length) { throw new Error('Incomplete diagnostic selection.'); }
  return expected.map((id) => {
    const matches = rows.filter((row) => row.id === id), row = matches[0];
    if (matches.length !== 1 || row.expectedStatus !== 'passed' || row.attempts.length !== 1 ||
        row.attempts[0].status !== 'passed' || row.attempts[0].retry !== 0) { throw new Error('Unsuccessful diagnostic execution.'); }
    return { id, attempts: 1, retries: 0, ...(mode === 'matrix' ? { observation: validateMatrixObservation(row.observation) } : {}) };
  });
}

// Match default UTF-16 code-unit ordering, including native string coercion.
function compareCodeUnits(left, right) {
  const leftString = `${left}`;
  const rightString = `${right}`;
  if (leftString === rightString) return 0;
  return leftString < rightString ? -1 : 1;
}
