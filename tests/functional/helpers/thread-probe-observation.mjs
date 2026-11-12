export const observationKinds = ['REQUEST', 'RESPONSE', 'NETWORK_FAILURE', 'REQUEST_FINISHED',
  'AUTHORIZED_OBSERVER_REGISTERED', 'AUTHORIZED_SELECTED',
  'AUTHORIZED_ACTION_STARTED', 'AUTHORIZED_ACTION_COMPLETED',
  'ROOT_ASSERTION_STARTED', 'ROOT_ASSERTION_PASSED', 'ROOT_ASSERTION_FAILED',
  'AUTHORIZED_READ', 'SESSION_REPLACEMENT_STARTED', 'SESSION_REPLACEMENT_COMPLETED', 'SESSION_VERIFIED',
  'OBSERVER_REGISTERED', 'ACTION_STARTED', 'ACTION_COMPLETED',
  'ACTION_FAILED', 'REQUEST_OBSERVED', 'REQUEST_FAILED', 'WAITING_RESPONSE', 'RESPONSE_OBSERVED', 'RESPONSE_MISSING',
  'RESPONSE_FAILED', 'FRESH_SELECTED'];
const requestKinds = ['REQUEST', 'RESPONSE', 'NETWORK_FAILURE', 'REQUEST_FINISHED', 'FRESH_SELECTED', 'AUTHORIZED_SELECTED'];
const actionFailures = ['NONE', 'UNKNOWN', 'WAITING_LOCATOR', 'NOT_VISIBLE', 'DETACHED', 'POINTER_INTERCEPTION'];
const keys = (value, names) => value && Object.keys(value).sort(compareCodeUnits).join() === [...names].sort(compareCodeUnits).join();

/** This is an observation completeness label, never a causal diagnosis. */
export function validateThreadObservation(value) {
  const invalid = () => { throw new Error('Restricted thread observation is invalid.'); };
  const timed = Object.hasOwn(value, 'elapsedMs');
  const versioned = Object.hasOwn(value, 'schemaVersion');
  if (versioned && (value.schemaVersion !== 3 || !timed)) { invalid(); }
  const eventLimit = versioned ? 32 : 24;
  if (!keys(value, ['events', ...(versioned ? ['schemaVersion'] : []), ...(timed ? ['elapsedMs'] : []), 'truncated', 'orphanResponse', 'freshRequest', 'outcome', 'classification', 'actionFailure', 'failureTarget']) ||
      !Array.isArray(value.events) || value.events.length > eventLimit ||
      typeof value.truncated !== 'boolean' || typeof value.orphanResponse !== 'boolean' ||
      !['COMPLETED', 'FAILED'].includes(value.outcome) || !actionFailures.includes(value.actionFailure)) { invalid(); }
  if (timed && (!Array.isArray(value.elapsedMs) || value.elapsedMs.length !== value.events.length ||
      value.elapsedMs.some((time) => time !== null && (!Number.isSafeInteger(time) || time < 0 || time > 3600000)))) { invalid(); }
  let previousTime = -1;
  for (const time of timed ? value.elapsedMs : []) {
    if (time !== null) { if (time < previousTime) { invalid(); } previousTime = time; }
  }
  const markers = new Set();
  const starts = new Map();
  let replacementStart = -1, replacementEnd = -1, sessionIndex = -1, observerIndex = -1, selected = null;
  let authorizedObserver = -1, authorizedSelected = null;
  for (const [index, event] of value.events.entries()) {
    if (!keys(event, ['kind', 'request', 'status']) || !observationKinds.includes(event.kind) ||
        !(event.request === null || Number.isInteger(event.request) && event.request >= 1 && event.request <= 24) ||
        !(event.status === null || Number.isInteger(event.status) && event.status >= 100 && event.status <= 599) ||
        (event.kind === 'RESPONSE' ? event.status === null : event.status !== null) ||
        (!requestKinds.includes(event.kind) && event.request !== null)) { invalid(); }
    if (['AUTHORIZED_ACTION_STARTED', 'AUTHORIZED_ACTION_COMPLETED', 'ROOT_ASSERTION_STARTED', 'ROOT_ASSERTION_PASSED', 'ROOT_ASSERTION_FAILED'].includes(event.kind)) {
      if (markers.has(event.kind) || authorizedObserver < 0) { invalid(); }
      if (event.kind === 'AUTHORIZED_ACTION_COMPLETED' && !markers.has('AUTHORIZED_ACTION_STARTED') ||
          event.kind === 'ROOT_ASSERTION_STARTED' && !value.events.slice(0, index).some((item) => item.kind === 'AUTHORIZED_READ') ||
          ['ROOT_ASSERTION_PASSED', 'ROOT_ASSERTION_FAILED'].includes(event.kind) &&
          (!markers.has('ROOT_ASSERTION_STARTED') || markers.has('ROOT_ASSERTION_PASSED') || markers.has('ROOT_ASSERTION_FAILED'))) { invalid(); }
      markers.add(event.kind);
    }
    if (event.kind === 'SESSION_REPLACEMENT_STARTED') {
      if (replacementStart >= 0) { invalid(); } replacementStart = index;
    }
    if (event.kind === 'AUTHORIZED_OBSERVER_REGISTERED') {
      if (authorizedObserver >= 0 || replacementStart >= 0) { invalid(); } authorizedObserver = index;
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
    if (event.kind === 'AUTHORIZED_SELECTED') {
      if (authorizedSelected !== null || authorizedObserver < 0 || replacementStart >= 0 ||
          event.request === null || starts.get(event.request) <= authorizedObserver) { invalid(); }
      authorizedSelected = event.request;
    }
  }
  if (value.freshRequest !== selected || value.outcome === 'COMPLETED' && value.actionFailure !== 'NONE') { invalid(); }
  if (value.outcome === 'COMPLETED' && value.failureTarget !== null) { invalid(); }
  const flags = ['singleTab', 'visible', 'enabled', 'selected', 'inViewport', 'centerReceivesPointer', 'workspaceExpanded'];
  if (value.failureTarget !== null && (!keys(value.failureTarget, flags) ||
      flags.some((flag) => typeof value.failureTarget[flag] !== 'boolean'))) { invalid(); }
  if (value.outcome === 'COMPLETED' && markers.has('ROOT_ASSERTION_FAILED')) { invalid(); }
  const incomplete = timed && value.elapsedMs.includes(null) || value.truncated || value.orphanResponse || selected === null ||
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
export function observeThreadRequests(page, matches, clock = () => performance.now()) {
  const events = [], elapsedMs = [], requests = new WeakMap();
  let origin = null, previousTime = -1;
  try { const value = clock(); if (Number.isFinite(value)) { origin = value; } } catch { /* Unavailable clock stays unknown. */ }
  const sampleTime = () => {
    try {
      const value = clock(), elapsed = value - origin;
      if (origin !== null && Number.isFinite(value) && Number.isFinite(elapsed) && elapsed >= previousTime && elapsed >= 0 && elapsed <= 3600000) {
        previousTime = elapsed; return Math.floor(elapsed);
      }
    } catch { /* Clock failures cannot replace a mandatory test result. */ }
    return null;
  };
  let next = 0, truncated = false, orphanResponse = false, freshRequest = null, frozen = false, snapshot = null;
  let preActionDeniedCallback = null, preActionDeniedRecorded = false;
  const append = (kind, request = null, status = null) => {
    if (frozen) { return false; }
    if (events.length === 32) { truncated = true; return false; }
    events.push({ kind, request, status }); elapsedMs.push(sampleTime()); return true;
  };
  const onRequest = (request) => {
    if (!matches(request) || frozen) { return; }
    if (next === 24) { truncated = true; return; }
    const ordinal = ++next;
    if (append('REQUEST', ordinal)) { requests.set(request, ordinal); }
  };
  const terminal = (kind, request, status = null) => {
    if (!matches(request) || frozen) { return false; }
    const ordinal = requests.get(request) ?? null;
    if (ordinal === null) { orphanResponse = true; }
    return append(kind, ordinal, status);
  };
  const listeners = {
    request: onRequest,
    response: (response) => {
      const request = response.request(), status = response.status(), ordinal = requests.get(request) ?? null;
      // A real 400 may belong to an already-in-flight request, not the new
      // action. Observe that specific interleaving once without supplying a
      // response, changing an action, reading an HTTP body or awaiting work.
      const start = events.findIndex((event) => event.kind === 'REQUEST' && event.request === ordinal);
      const observer = events.findIndex((event) => event.kind === 'OBSERVER_REGISTERED');
      const action = events.findIndex((event) => event.kind === 'ACTION_STARTED');
      const interleaving = !frozen && !preActionDeniedRecorded && ordinal !== null && status === 400 &&
        observer > start && start >= 0 && action > observer && observer >= 0;
      const retained = terminal('RESPONSE', request, status);
      // The finite cap can discard this response. A DOM sample would then lack
      // its required retained provenance; leave it unavailable/INCOMPLETE.
      if (interleaving && retained) {
        preActionDeniedRecorded = true;
        try { preActionDeniedCallback?.(); } catch { /* Auxiliary diagnostics never mask the response. */ }
      }
    },
    requestfailed: (request) => terminal('NETWORK_FAILURE', request),
    requestfinished: (request) => terminal('REQUEST_FINISHED', request),
  };
  for (const [name, listener] of Object.entries(listeners)) { page.on(name, listener); }
  return {
    onPreActionDenied: (callback) => { preActionDeniedCallback = callback; },
    mark: (kind) => { append(kind); },
    select: (request) => {
      const ordinal = requests.get(request) ?? null;
      if (freshRequest === null && ordinal !== null && append('FRESH_SELECTED', ordinal)) { freshRequest = ordinal; }
    },
    selectAuthorized: (request) => {
      const ordinal = requests.get(request) ?? null;
      if (ordinal === null) { orphanResponse = true; }
      append('AUTHORIZED_SELECTED', ordinal);
    },
    finish: (outcome, failureTarget = null, actionFailure = 'NONE') => {
      if (snapshot) { return structuredClone(snapshot); }
      frozen = true;
      for (const [name, listener] of Object.entries(listeners)) { page.off(name, listener); }
      snapshot = validateThreadObservation({ schemaVersion: 3, events, elapsedMs, truncated, orphanResponse, freshRequest, outcome, actionFailure, failureTarget,
        classification: elapsedMs.includes(null) || truncated || orphanResponse || freshRequest === null || outcome === 'FAILED' && failureTarget === null
          ? 'INCOMPLETE' : 'UNKNOWN' });
      return structuredClone(snapshot);
    },
  };
}

/** Secondary diagnostics/cleanup can fail a success, but cannot replace the primary owner failure. */
export async function withThreadProbeLifecycle(run, capture, publish, restore, reportSecondary) {
  let primary, secondary, hasPrimary = false, hasSecondary = false, result;
  const attempt = async (stage, action) => {
    try { await action(); }
    catch (error) {
      if (!hasSecondary) { secondary = error; hasSecondary = true; }
      // Only a fixed stage is offered to the observer. A broken observer cannot mask either error.
      try { reportSecondary(stage); } catch { /* The operation already remains failed. */ }
    }
  };
  try { result = await run(); }
  catch (error) {
    primary = error; hasPrimary = true;
    await attempt('CAPTURE', () => capture(error));
  }
  await attempt('PUBLISH', publish);
  await attempt('RESTORE', restore);
  if (hasPrimary) { throw primary; }
  if (hasSecondary) { throw secondary; }
  return result;
}

// Match default UTF-16 code-unit ordering, including native string coercion.
function compareCodeUnits(left, right) {
  const leftString = `${left}`;
  const rightString = `${right}`;
  if (leftString === rightString) return 0;
  return leftString < rightString ? -1 : 1;
}
