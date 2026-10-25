import { mkdirSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';

export const groupKeyboardProfiles = ['chromium-desktop', 'chromium-mobile', 'firefox-desktop',
  'webkit-desktop', 'webkit-mobile', 'narrow-320'];
export const groupKeyboardInputDirectory = 'artifacts/group-keyboard-observation';
export const groupKeyboardOutputDirectory = 'artifacts/group-keyboard-observation-validated';
export const groupKeyboardV1ByteLimit = 32768;
export const groupKeyboardByteLimit = 65536;
const keys = (value, expected) => value && Object.keys(value).sort().join() === [...expected].sort().join();
const positions = ['LIST', 'SUMMARY', 'LEAVE', 'ASIDE', 'OTHER'];

export function groupKeyboardFilename(profile, attempt) {
  if (!groupKeyboardProfiles.includes(profile) || !Number.isInteger(attempt) || attempt < 0 || attempt > 2) {
    throw new Error('Group keyboard observation identity is invalid.');
  }
  return `${profile}-attempt-${attempt}.json`;
}

/** Only closed structural metadata can cross the CI publication boundary. */
export function validateGroupKeyboardObservation(value) {
  const invalid = () => { throw new Error('Group keyboard observation is invalid.'); };
  const version = value?.schemaVersion;
  if (!keys(value, ['schemaVersion', 'scope', 'profile', 'attempt', 'bodyOutcome', 'captureState', 'classification', 'events', 'truncated']) ||
      ![1, 2].includes(version) || value.scope !== 'MOCKED_UI_KEYBOARD_OBSERVATION_ONLY' ||
      !['COMPLETED', 'FAILED'].includes(value.bodyOutcome) || !['CAPTURED', 'UNAVAILABLE'].includes(value.captureState) ||
      !Array.isArray(value.events) || value.events.length > 128 || typeof value.truncated !== 'boolean') { invalid(); }
  groupKeyboardFilename(value.profile, value.attempt);
  for (const event of value.events) {
    const fields = ['kind', 'key', 'target', 'active', 'documentFocused', 'defaultPrevented', 'milliseconds',
      'listTop', 'listMax', 'asideTop', 'asideMax'];
    const kinds = version === 1 ? ['KEYDOWN', 'KEYUP', 'POST_KEYDOWN', 'FOCUS_IN', 'FOCUS_OUT', 'SCROLL', 'FINAL']
      : ['KEYDOWN', 'KEYUP', 'CAPTURE_MICROTASK', 'FOCUS_IN', 'FOCUS_OUT', 'SCROLL', 'SCROLL_END', 'LIFECYCLE', 'FINAL'];
    if (version === 2) fields.push('listNode', 'listConnected', 'trackedListNode', 'trackedListConnected',
      'targetNode', 'targetConnected', 'activeNode', 'activeConnected', 'trusted');
    if (!keys(event, fields) || !kinds.includes(event.kind) ||
      ![null, 'HOME', 'END', 'TAB', 'SHIFT_TAB'].includes(event.key) ||
      !positions.includes(event.target) || !positions.includes(event.active) ||
      typeof event.documentFocused !== 'boolean' || ![null, true, false].includes(event.defaultPrevented) ||
      ['milliseconds', 'listTop', 'listMax', 'asideTop', 'asideMax'].some((key) =>
        !Number.isSafeInteger(event[key]) || event[key] < 0 || event[key] > 1000000)) { invalid(); }
    if (version === 2) {
      if (['listConnected', 'trackedListConnected', 'targetConnected', 'activeConnected'].some((key) =>
        typeof event[key] !== 'boolean') || ![null, true, false].includes(event.trusted)) { invalid(); }
      for (const prefix of ['list', 'trackedList', 'target', 'active']) {
        const identity = event[`${prefix}Node`];
        if (!Number.isSafeInteger(identity) || identity < 0 || identity > 4096 ||
          (identity === 0 && event[`${prefix}Connected`])) { invalid(); }
      }
    }
  }
  if (value.captureState === 'UNAVAILABLE' && (value.events.length !== 0 || value.truncated)) { invalid(); }
  const incomplete = value.captureState === 'UNAVAILABLE' || value.truncated || !value.events.length ||
    (version === 2 && !value.events.some((event) => event.listNode > 0));
  if (value.classification !== (incomplete ? 'INCOMPLETE' : 'UNKNOWN')) { invalid(); }
  if (Buffer.byteLength(JSON.stringify(value)) > (version === 1 ? groupKeyboardV1ByteLimit : groupKeyboardByteLimit)) { invalid(); }
  return structuredClone(value);
}

/** Diagnostic publication cannot replace a mandatory primary error or turn its failure green. */
export async function withGroupKeyboardObservation(body, finalize) {
  let primary, failed = false, result;
  try { result = await body(); } catch (error) { primary = error; failed = true; }
  try { await finalize(failed ? 'FAILED' : 'COMPLETED'); }
  catch (error) { if (!failed) { throw error; } }
  if (failed) { throw primary; }
  return result;
}

async function install(page) {
  await page.addInitScript(() => {
    const events = [], identities = new WeakMap();
    let truncated = false, nextIdentity = 1, trackedList = null, previousLifecycle = '';
    const kindOf = (element) => element?.matches?.('.group-members__list') ? 'LIST'
      : element?.matches?.('[data-testid="group-members-panel"] summary') ? 'SUMMARY'
      : element?.matches?.('[data-testid="group-leave"]') ? 'LEAVE'
      : element?.matches?.('.dm-page__group-details') ? 'ASIDE' : 'OTHER';
    const identify = (element) => {
      if (!element || element.nodeType !== 1) { return 0; }
      if (!identities.has(element)) {
        if (nextIdentity > 4096) { truncated = true; return 0; }
        identities.set(element, nextIdentity++);
      }
      return identities.get(element);
    };
    const listState = () => {
      const list = document.querySelector('.group-members__list');
      if (list) { trackedList = list; }
      return { list, listNode: identify(list), listConnected: !!list?.isConnected,
        trackedListNode: identify(trackedList), trackedListConnected: !!trackedList?.isConnected };
    };
    const record = (kind, event = null, state = listState()) => {
      if (!trackedList && kind !== 'FINAL') { return; }
      if (events.length === 128) { truncated = true; return; }
      const aside = document.querySelector('.dm-page__group-details');
      const key = event?.key === 'Home' ? 'HOME' : event?.key === 'End' ? 'END'
        : event?.key === 'Tab' ? (event.shiftKey ? 'SHIFT_TAB' : 'TAB') : null;
      events.push({ kind, key, target: kindOf(event?.target), active: kindOf(document.activeElement),
        documentFocused: document.hasFocus(), defaultPrevented: event ? event.defaultPrevented : null,
        milliseconds: Math.round(performance.now()), listTop: Math.round(state.list?.scrollTop ?? 0),
        listMax: Math.round(state.list ? state.list.scrollHeight - state.list.clientHeight : 0),
        asideTop: Math.round(aside?.scrollTop ?? 0),
        asideMax: Math.round(aside ? aside.scrollHeight - aside.clientHeight : 0),
        listNode: state.listNode, listConnected: state.listConnected,
        trackedListNode: state.trackedListNode, trackedListConnected: state.trackedListConnected,
        targetNode: identify(event?.target), targetConnected: !!event?.target?.isConnected,
        activeNode: identify(document.activeElement), activeConnected: !!document.activeElement?.isConnected,
        trusted: event ? event.isTrusted : null });
    };
    const lifecycle = () => {
      const state = listState();
      const signature = JSON.stringify([state.listNode, state.listConnected, state.trackedListNode, state.trackedListConnected]);
      if (signature !== previousLifecycle) { previousLifecycle = signature; record('LIFECYCLE', null, state); }
    };
    const observer = new MutationObserver(lifecycle), listeners = new AbortController();
    observer.observe(document, { childList: true, subtree: true });
    const options = { capture: true, signal: listeners.signal };
    for (const name of ['keydown', 'keyup']) {
      document.addEventListener(name, (event) => {
        if (!['Home', 'End', 'Tab'].includes(event.key)) { return; }
        record(name === 'keydown' ? 'KEYDOWN' : 'KEYUP', event);
        if (name === 'keydown') { queueMicrotask(() => record('CAPTURE_MICROTASK', event)); }
      }, options);
    }
    for (const name of ['focusin', 'focusout']) {
      document.addEventListener(name, (event) => record(name === 'focusin' ? 'FOCUS_IN' : 'FOCUS_OUT', event), options);
    }
    for (const name of ['scroll', 'scrollend']) {
      document.addEventListener(name, (event) => {
        if (['LIST', 'ASIDE'].includes(kindOf(event.target))) { record(name === 'scroll' ? 'SCROLL' : 'SCROLL_END', event); }
      }, options);
    }
    Object.defineProperty(window, '__groupKeyboardObservation', { value: () => {
      lifecycle(); record('FINAL'); observer.disconnect(); listeners.abort();
      return { events: [...events], truncated };
    } });
  });
}

export async function observeGroupKeyboardCase(page, testInfo, body) {
  await install(page);
  return withGroupKeyboardObservation(body, async (bodyOutcome) => {
    let snapshot = null;
    try { snapshot = await page.evaluate(() => window['__groupKeyboardObservation']?.() ?? null); } catch { /* Publish explicit unavailability. */ }
    const value = validateGroupKeyboardObservation({ schemaVersion: 2, scope: 'MOCKED_UI_KEYBOARD_OBSERVATION_ONLY',
      profile: testInfo.project.name, attempt: testInfo.retry, bodyOutcome,
      captureState: snapshot ? 'CAPTURED' : 'UNAVAILABLE', events: snapshot?.events ?? [], truncated: snapshot?.truncated ?? false,
      classification: !snapshot || snapshot.truncated || !snapshot.events.some((event) => event.listNode > 0) ? 'INCOMPLETE' : 'UNKNOWN' });
    mkdirSync(groupKeyboardInputDirectory, { recursive: true });
    writeFileSync(join(groupKeyboardInputDirectory, groupKeyboardFilename(value.profile, value.attempt)),
      JSON.stringify(value) + '\n', { flag: 'wx' });
    if (!snapshot) { throw new Error('Group keyboard observation is unavailable.'); }
  });
}
