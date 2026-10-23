export const observationKinds = ['REQUEST', 'RESPONSE', 'NETWORK_FAILURE', 'REQUEST_FINISHED',
  'AUTHORIZED_READ', 'SESSION_REPLACEMENT_STARTED', 'SESSION_REPLACEMENT_COMPLETED', 'SESSION_VERIFIED',
  'OBSERVER_REGISTERED', 'ACTION_STARTED', 'ACTION_COMPLETED',
  'ACTION_FAILED', 'REQUEST_OBSERVED', 'WAITING_RESPONSE', 'RESPONSE_OBSERVED', 'RESPONSE_MISSING',
  'RESPONSE_FAILED', 'FRESH_SELECTED'];
const requestKinds = ['REQUEST', 'RESPONSE', 'NETWORK_FAILURE', 'REQUEST_FINISHED', 'FRESH_SELECTED'];
const actionFailures = ['NONE', 'UNKNOWN', 'WAITING_LOCATOR', 'NOT_VISIBLE', 'DETACHED', 'POINTER_INTERCEPTION'];
const keys = (value, names) => value && Object.keys(value).sort().join() === [...names].sort().join();

/** This is an observation completeness label, never a causal diagnosis. */
export function validateThreadObservation(value) {
  const invalid = () => { throw new Error('Restricted thread observation is invalid.'); };
  if (!keys(value, ['events', 'truncated', 'orphanResponse', 'freshRequest', 'outcome', 'classification', 'actionFailure', 'failureTarget']) ||
      !Array.isArray(value.events) || value.events.length > 24 ||
      typeof value.truncated !== 'boolean' || typeof value.orphanResponse !== 'boolean' ||
      !['COMPLETED', 'FAILED'].includes(value.outcome) || !actionFailures.includes(value.actionFailure)) { invalid(); }
  const starts = new Map();
  let replacementStart = -1, replacementEnd = -1, sessionIndex = -1, observerIndex = -1, selected = null;
  for (const [index, event] of value.events.entries()) {
    if (!keys(event, ['kind', 'request', 'status']) || !observationKinds.includes(event.kind) ||
        !(event.request === null || Number.isInteger(event.request) && event.request >= 1 && event.request <= 24) ||
        !(event.status === null || Number.isInteger(event.status) && event.status >= 100 && event.status <= 599) ||
        (event.kind === 'RESPONSE' ? event.status === null : event.status !== null) ||
        (!requestKinds.includes(event.kind) && event.request !== null)) { invalid(); }
    if (event.kind === 'SESSION_REPLACEMENT_STARTED') {
      if (replacementStart >= 0) { invalid(); } replacementStart = index;
    }
    if (event.kind === 'SESSION_REPLACEMENT_COMPLETED') {
      if (replacementStart < 0 || replacementEnd >= 0) { invalid(); } replacementEnd = index;
    }
    if (event.kind === 'SESSION_VERIFIED') {
      if (replacementEnd < 0 || sessionIndex >= 0) { invalid(); } sessionIndex = index;
    }
    if (event.kind === 'OBSERVER_REGISTERED') {
      if (sessionIndex < 0 || observerIndex >= 0) { invalid(); } observerIndex = index;
    }
    if (event.kind === 'REQUEST') {
      if (event.request === null || starts.has(event.request)) { invalid(); }
      starts.set(event.request, index);
    } else if (requestKinds.includes(event.kind)) {
      if (event.request === null ? !value.orphanResponse : !starts.has(event.request)) { invalid(); }
    }
    if (event.kind === 'FRESH_SELECTED') {
      if (selected !== null || event.request === null || sessionIndex < 0 || observerIndex < sessionIndex ||
          starts.get(event.request) <= observerIndex) { invalid(); }
      selected = event.request;
    }
  }
  if (value.freshRequest !== selected || value.outcome === 'COMPLETED' && value.actionFailure !== 'NONE') { invalid(); }
  if (value.outcome === 'COMPLETED' && value.failureTarget !== null) { invalid(); }
  const flags = ['singleTab', 'visible', 'enabled', 'selected', 'inViewport', 'centerReceivesPointer', 'workspaceExpanded'];
  if (value.failureTarget !== null && (!keys(value.failureTarget, flags) ||
      flags.some((flag) => typeof value.failureTarget[flag] !== 'boolean'))) { invalid(); }
  const incomplete = value.truncated || value.orphanResponse || selected === null ||
    value.outcome === 'FAILED' && value.failureTarget === null;
  if (value.classification !== (incomplete ? 'INCOMPLETE' : 'UNKNOWN')) { invalid(); }
  return structuredClone(value);
}

/** Read error text only in memory; retain one fixed token and no native call log. */
export function actionFailureKind(error) {
  const message = typeof error?.message === 'string' ? error.message : '';
  if (message.includes('intercepts pointer events')) { return 'POINTER_INTERCEPTION'; }
  if (message.includes('element was detached') || message.includes('not attached to the DOM')) { return 'DETACHED'; }
  if (message.includes('element is not visible')) { return 'NOT_VISIBLE'; }
  if (message.includes('waiting for locator')) { return 'WAITING_LOCATOR'; }
  return 'UNKNOWN';
}

/** Matching resource data and Request objects stay private to this closure. */
export function observeThreadRequests(page, matches) {
  const events = [], requests = new WeakMap();
  let next = 0, truncated = false, orphanResponse = false, freshRequest = null, frozen = false, snapshot = null;
  const append = (kind, request = null, status = null) => {
    if (frozen) { return false; }
    if (events.length === 24) { truncated = true; return false; }
    events.push({ kind, request, status }); return true;
  };
  const onRequest = (request) => {
    if (!matches(request) || frozen) { return; }
    if (next === 24) { truncated = true; return; }
    const ordinal = ++next;
    if (append('REQUEST', ordinal)) { requests.set(request, ordinal); }
  };
  const terminal = (kind, request, status = null) => {
    if (!matches(request) || frozen) { return; }
    const ordinal = requests.get(request) ?? null;
    if (ordinal === null) { orphanResponse = true; }
    append(kind, ordinal, status);
  };
  const listeners = {
    request: onRequest,
    response: (response) => terminal('RESPONSE', response.request(), response.status()),
    requestfailed: (request) => terminal('NETWORK_FAILURE', request),
    requestfinished: (request) => terminal('REQUEST_FINISHED', request),
  };
  for (const [name, listener] of Object.entries(listeners)) { page.on(name, listener); }
  return {
    mark: (kind) => { append(kind); },
    select: (request) => {
      const ordinal = requests.get(request) ?? null;
      if (freshRequest === null && ordinal !== null && append('FRESH_SELECTED', ordinal)) { freshRequest = ordinal; }
    },
    finish: (outcome, failureTarget = null, actionFailure = 'NONE') => {
      if (snapshot) { return structuredClone(snapshot); }
      frozen = true;
      for (const [name, listener] of Object.entries(listeners)) { page.off(name, listener); }
      snapshot = validateThreadObservation({ events, truncated, orphanResponse, freshRequest, outcome, actionFailure, failureTarget,
        classification: truncated || orphanResponse || freshRequest === null || outcome === 'FAILED' && failureTarget === null
          ? 'INCOMPLETE' : 'UNKNOWN' });
      return structuredClone(snapshot);
    },
  };
}
