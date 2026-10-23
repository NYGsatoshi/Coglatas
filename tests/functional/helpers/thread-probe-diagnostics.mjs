import { validateThreadObservation } from './thread-probe-observation.mjs';
import { validateInitialThreadObservation } from './thread-initial-observation.mjs';
export const THREAD_PROBE_ATTACHMENT = 'restricted-thread-probe-v1';
export const THREAD_PROBE_SECONDARY_ANNOTATION = 'restricted-thread-secondary-v1';
const phases = ['OBSERVER_REGISTERED', 'ACTION_STARTED', 'ACTION_COMPLETED', 'ACTION_FAILED',
  'REQUEST_OBSERVED', 'REQUEST_FAILED', 'WAITING_RESPONSE', 'RESPONSE_OBSERVED', 'RESPONSE_MISSING', 'RESPONSE_FAILED'];
const flags = ['singleTab', 'visible', 'enabled', 'selected', 'inViewport', 'centerReceivesPointer', 'workspaceExpanded'];

/** No locator strings, attributes, identities, text, URLs or errors are retained. */
export function validateThreadProbe(value) {
  const extended = value && Object.hasOwn(value, 'observation');
  const initial = value && Object.hasOwn(value, 'initial');
  const fields = ['phases', 'target', ...(extended ? ['observation'] : []), ...(initial ? ['initial'] : [])];
  if (!value || Object.keys(value).sort().join() !== fields.sort().join() || initial && !extended ||
      !Array.isArray(value.phases) || value.phases.length > phases.length ||
      new Set(value.phases).size !== value.phases.length || value.phases.some((phase) => !phases.includes(phase)) ||
      !(extended && value.target === null) && (!value.target || Object.keys(value.target).sort().join() !== [...flags].sort().join() ||
      flags.some((flag) => typeof value.target[flag] !== 'boolean'))) {
    throw new Error('Restricted thread probe diagnostics are invalid.');
  }
  const result = { phases: [...value.phases], target: value.target === null ? null : Object.fromEntries(flags.map((flag) => [flag, value.target[flag]])),
    ...(extended ? { observation: validateThreadObservation(value.observation) } : {}),
    ...(initial ? { initial: validateInitialThreadObservation(value.initial) } : {}) };
  if (initial) {
    const selected = result.observation.events.find((event) => event.kind === 'AUTHORIZED_SELECTED');
    if (result.initial.responseShape !== null && (!selected || !result.observation.events.some((event) =>
      event.kind === 'RESPONSE' && event.request === selected.request && event.status === 200) ||
      !result.observation.events.some((event) => event.kind === 'AUTHORIZED_READ')) ||
      result.initial.failureDom !== null && result.observation.outcome !== 'FAILED') {
      throw new Error('Initial Thread diagnostic provenance is invalid.');
    }
  }
  if (Buffer.byteLength(JSON.stringify(result)) > 4096) { throw new Error('Restricted thread probe exceeds its size limit.'); }
  return result;
}

export function readThreadProbe(attachments) {
  const selected = (attachments ?? []).filter((item) => item.name === THREAD_PROBE_ATTACHMENT);
  if (!selected.length) { return { state: 'MISSING', data: null }; }
  try {
    if (selected.length !== 1 || !selected[0].body || selected[0].body.length > 4096) { throw new Error(); }
    return { state: 'AVAILABLE', data: validateThreadProbe(JSON.parse(selected[0].body.toString('utf8'))) };
  } catch { return { state: 'INVALID', data: null }; }
}

export function readThreadProbeSecondary(annotations) {
  const selected = (annotations ?? []).filter((item) => item.type === THREAD_PROBE_SECONDARY_ANNOTATION);
  if (!selected.length) { return { state: 'MISSING', stages: [] }; }
  const stages = selected.map((item) => item.description);
  if (stages.length > 3 || new Set(stages).size !== stages.length ||
      stages.some((stage) => !['CAPTURE', 'PUBLISH', 'RESTORE'].includes(stage))) {
    return { state: 'INVALID', stages: [] };
  }
  return { state: 'AVAILABLE', stages };
}
