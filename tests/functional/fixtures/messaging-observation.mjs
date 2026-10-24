/** Diagnostic-only state: fixed tokens and booleans, never message text or raw frames. */
export const MESSAGING_OBSERVATION_KEY = '__coglatasFunctionalMessagingObservationV1';
export const MESSAGING_EVENT_LIMIT = 48;
export const MESSAGING_EVENT_KINDS = ['initial', 'structure', 'replacement', 'beforeinput', 'input', 'change', 'focus', 'blur'];
export const MESSAGING_CONNECTIONS = ['Connected', 'Reconnecting', 'Degraded', 'Offline'];
const FLAGS = ['composerPresent', 'draftEnabled', 'sendEnabled', 'draftFocused'];
const CONTROLS = new Set(['AuthorizationInvalidated', 'AuthorizationStateChanged']);
const MAX_TIME = 8640000000000000;

function keys(value, expected) {
  return value && !Array.isArray(value) && typeof value === 'object'
    && Object.keys(value).length === expected.length && expected.every((key) => Object.hasOwn(value, key));
}
const integer = (value, max) => Number.isSafeInteger(value) && value >= 0 && value <= max;

function validateEvent(event) {
  if (!keys(event, ['atMs', 'kind', ...FLAGS, 'connection']) || !integer(event.atMs, MAX_TIME)
    || !MESSAGING_EVENT_KINDS.includes(event.kind) || FLAGS.some((flag) => typeof event[flag] !== 'boolean')
    || (event.connection !== null && !MESSAGING_CONNECTIONS.includes(event.connection))) {
    throw new Error('Invalid structural Messaging event.');
  }
}

function validateControl(control) {
  if (!keys(control, ['atMs', 'openedAtMs', 'kind']) || !integer(control.atMs, MAX_TIME) || !integer(control.openedAtMs, MAX_TIME) || !CONTROLS.has(control.kind)) {
    throw new Error('Invalid Messaging control observation.');
  }
}

export function validateMessagingObservation(value) {
  if (value === null) { return value; }
  if (!keys(value, ['documentStartedAtMs', 'storageReadable', 'storageWritable', 'events', 'eventsTruncated', 'webSocketObserved', 'controls', 'controlsTruncated'])
    || !integer(value.documentStartedAtMs, MAX_TIME)
    || typeof value.storageReadable !== 'boolean' || typeof value.storageWritable !== 'boolean' || typeof value.webSocketObserved !== 'boolean'
    || !Array.isArray(value.events) || value.events.length > MESSAGING_EVENT_LIMIT
    || !integer(value.eventsTruncated, 1000000)
    || !Array.isArray(value.controls) || value.controls.length > MESSAGING_EVENT_LIMIT
    || !integer(value.controlsTruncated, 1000000)) { throw new Error('Invalid bounded Messaging observation.'); }
  value.events.forEach(validateEvent);
  value.controls.forEach(validateControl);
  return value;
}

/** Read only the two known SignalR control discriminants; discard all other frame data. */
export function messagingControlKinds(payload) {
  if (typeof payload !== 'string' || payload.length > 65536) { return []; }
  const kinds = [];
  for (const record of payload.split('\u001e').slice(0, 16)) {
    try {
      const message = JSON.parse(record);
      if (message?.type !== 1) { continue; }
      if (message.target === 'AuthorizationInvalidated') { kinds.push('AuthorizationInvalidated'); }
      else if (message.target === 'DurableEvent' && Array.isArray(message.arguments)
        && message.arguments.length === 1
        && message.arguments[0]?.eventType === 'Security.AuthorizationStateChanged.v1') {
        kinds.push('AuthorizationStateChanged');
      }
    } catch { /* Malformed or non-JSON frames are not diagnostic records. */ }
  }
  return kinds;
}

export function createMessagingControlBuffer() {
  let controls = [], truncated = 0, latestSocketOpenedAtMs = -1;
  return {
    observeSocket(openedAtMs = Date.now()) { latestSocketOpenedAtMs = Math.max(latestSocketOpenedAtMs, openedAtMs); },
    append(payload, atMs = Date.now(), openedAtMs = atMs) {
      latestSocketOpenedAtMs = Math.max(latestSocketOpenedAtMs, openedAtMs);
      for (const kind of messagingControlKinds(payload)) {
        controls.push({ atMs, openedAtMs, kind });
        if (controls.length > MESSAGING_EVENT_LIMIT) { controls.shift(); truncated = Math.min(truncated + 1, 1000000); }
      }
    },
    reset() { controls = []; truncated = 0; latestSocketOpenedAtMs = -1; },
    snapshot(documentStartedAtMs = 0) { return { webSocketObserved: latestSocketOpenedAtMs >= documentStartedAtMs, controls: controls.filter((entry) => entry.openedAtMs >= documentStartedAtMs).map((entry) => ({ ...entry })), controlsTruncated: truncated }; },
  };
}

/** Serialized into a test browser document; no product globals or private text are inspected. */
export function installMessagingObservation(key, environment = globalThis) {
  const { window, document, crypto, Date: Clock, MutationObserver } = environment;
  const state = { documentStartedAtMs: Clock.now(), storageReadable: false, storageWritable: false,
    events: [], eventsTruncated: 0, frozen: false };
  let storage, probe;
  try { storage = window.sessionStorage; state.storageReadable = Number.isSafeInteger(storage.length); }
  catch { /* Optional browser storage is unavailable. */ }
  try {
    if (storage) {
      probe = `coglatas.functional.diagnostic-probe.${crypto.randomUUID()}`;
      storage.setItem(probe, '1');
      state.storageWritable = true;
    }
  } catch { /* Retain only whether the optional probe succeeded. */ }
  finally { if (storage && probe) { try { storage.removeItem(probe); } catch { /* Best effort. */ } } }
  Object.defineProperty(window, key, { value: state, configurable: true });
  let previous, lastComposer = null;
  const record = (kind) => {
    if (state.frozen) { return; }
    const draft = document.querySelector('[data-testid="message-draft"]');
    // A newly mounted textarea can have identical UI flags to the replaced
    // node. Track DOM identity only in memory; never export elements or values.
    const replaced = !!draft && lastComposer !== null && draft !== lastComposer;
    if (draft) { lastComposer = draft; }
    const send = document.querySelector('[data-testid="send-message"]');
    const indicator = document.querySelector('[data-testid="realtime-connection-state"]');
    // These are fixed presentation classes. Do not read indicator text or draft values.
    let connection = null;
    if (indicator?.classList.contains('realtime-indicator--quiet')) { connection = 'Connected'; }
    else if (indicator?.classList.contains('realtime-indicator--attention')) { connection = 'Reconnecting'; }
    const snapshot = { composerPresent: !!draft, draftEnabled: !!draft && !draft.disabled,
      sendEnabled: !!send && !send.disabled, draftFocused: !!draft && document.activeElement === draft,
      connection };
    const signature = JSON.stringify(snapshot);
    if (kind === 'structure' && signature === previous && !replaced) { return; }
    previous = signature;
    state.events.push({ atMs: Clock.now(), kind: replaced ? 'replacement' : kind, ...snapshot });
    if (state.events.length > 48) { state.events.shift(); state.eventsTruncated = Math.min(state.eventsTruncated + 1, 1000000); }
  };
  for (const kind of ['beforeinput', 'input', 'change']) {
    document.addEventListener(kind, (event) => {
      if (event.target?.matches?.('[data-testid="message-draft"]')) { record(kind); }
    }, true);
  }
  document.addEventListener('focusin', (event) => {
    if (event.target?.matches?.('[data-testid="message-draft"]')) { record('focus'); }
  }, true);
  document.addEventListener('focusout', (event) => {
    if (event.target?.matches?.('[data-testid="message-draft"]')) { record('blur'); }
  }, true);
  new MutationObserver(() => record('structure')).observe(document, {
    subtree: true, childList: true, attributes: true, attributeFilter: ['disabled', 'class'],
  });
  record('initial');
}
