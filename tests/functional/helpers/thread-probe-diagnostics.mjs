export const THREAD_PROBE_ATTACHMENT = 'restricted-thread-probe-v1';
const phases = ['OBSERVER_REGISTERED', 'ACTION_STARTED', 'ACTION_COMPLETED', 'ACTION_FAILED',
  'REQUEST_OBSERVED', 'REQUEST_FAILED', 'WAITING_RESPONSE', 'RESPONSE_OBSERVED', 'RESPONSE_MISSING', 'RESPONSE_FAILED'];
const flags = ['singleTab', 'visible', 'enabled', 'selected', 'inViewport', 'centerReceivesPointer', 'workspaceExpanded'];

/** No locator strings, attributes, identities, text, URLs or errors are retained. */
export function validateThreadProbe(value) {
  if (!value || Object.keys(value).sort().join() !== 'phases,target' ||
      !Array.isArray(value.phases) || value.phases.length > phases.length ||
      new Set(value.phases).size !== value.phases.length || value.phases.some((phase) => !phases.includes(phase)) ||
      !value.target || Object.keys(value.target).sort().join() !== [...flags].sort().join() ||
      flags.some((flag) => typeof value.target[flag] !== 'boolean')) {
    throw new Error('Restricted thread probe diagnostics are invalid.');
  }
  return { phases: [...value.phases], target: Object.fromEntries(flags.map((flag) => [flag, value.target[flag]])) };
}

export function readThreadProbe(attachments) {
  const selected = (attachments ?? []).filter((item) => item.name === THREAD_PROBE_ATTACHMENT);
  if (!selected.length) { return { state: 'MISSING', data: null }; }
  try {
    if (selected.length !== 1 || !selected[0].body || selected[0].body.length > 4096) { throw new Error(); }
    return { state: 'AVAILABLE', data: validateThreadProbe(JSON.parse(selected[0].body.toString('utf8'))) };
  } catch { return { state: 'INVALID', data: null }; }
}
