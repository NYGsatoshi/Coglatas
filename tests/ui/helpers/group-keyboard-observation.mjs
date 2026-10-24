import { mkdirSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';

export const groupKeyboardProfiles = ['chromium-desktop', 'chromium-mobile', 'firefox-desktop',
  'webkit-desktop', 'webkit-mobile', 'narrow-320'];
export const groupKeyboardInputDirectory = 'artifacts/group-keyboard-observation';
export const groupKeyboardOutputDirectory = 'artifacts/group-keyboard-observation-validated';
export const groupKeyboardByteLimit = 32768;
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
  if (!keys(value, ['schemaVersion', 'scope', 'profile', 'attempt', 'bodyOutcome', 'captureState', 'classification', 'events', 'truncated']) ||
      value.schemaVersion !== 1 || value.scope !== 'MOCKED_UI_KEYBOARD_OBSERVATION_ONLY' ||
      !['COMPLETED', 'FAILED'].includes(value.bodyOutcome) || !['CAPTURED', 'UNAVAILABLE'].includes(value.captureState) ||
      !Array.isArray(value.events) || value.events.length > 128 || typeof value.truncated !== 'boolean') { invalid(); }
  groupKeyboardFilename(value.profile, value.attempt);
  for (const event of value.events) {
    if (!keys(event, ['kind', 'key', 'target', 'active', 'documentFocused', 'defaultPrevented', 'milliseconds',
      'listTop', 'listMax', 'asideTop', 'asideMax']) ||
      !['KEYDOWN', 'KEYUP', 'POST_KEYDOWN', 'FOCUS_IN', 'FOCUS_OUT', 'SCROLL', 'FINAL'].includes(event.kind) ||
      ![null, 'HOME', 'END', 'TAB', 'SHIFT_TAB'].includes(event.key) ||
      !positions.includes(event.target) || !positions.includes(event.active) ||
      typeof event.documentFocused !== 'boolean' || ![null, true, false].includes(event.defaultPrevented) ||
      ['milliseconds', 'listTop', 'listMax', 'asideTop', 'asideMax'].some((key) =>
        !Number.isSafeInteger(event[key]) || event[key] < 0 || event[key] > 1000000)) { invalid(); }
  }
  if (value.captureState === 'UNAVAILABLE' && (value.events.length !== 0 || value.truncated)) { invalid(); }
  if (value.classification !== (value.captureState === 'UNAVAILABLE' || value.truncated || !value.events.length ? 'INCOMPLETE' : 'UNKNOWN')) { invalid(); }
  if (Buffer.byteLength(JSON.stringify(value)) > groupKeyboardByteLimit) { invalid(); }
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
    const events = [];
    let truncated = false;
    const kindOf = (element) => element?.matches?.('.group-members__list') ? 'LIST'
      : element?.matches?.('[data-testid="group-members-panel"] summary') ? 'SUMMARY'
      : element?.matches?.('[data-testid="group-leave"]') ? 'LEAVE'
      : element?.matches?.('.dm-page__group-details') ? 'ASIDE' : 'OTHER';
    const record = (kind, event = null) => {
      const list = document.querySelector('.group-members__list');
      const aside = document.querySelector('.dm-page__group-details');
      if (!list || !aside) { return; }
      if (events.length === 128) { truncated = true; return; }
      const key = event?.key === 'Home' ? 'HOME' : event?.key === 'End' ? 'END'
        : event?.key === 'Tab' ? (event.shiftKey ? 'SHIFT_TAB' : 'TAB') : null;
      events.push({ kind, key, target: kindOf(event?.target), active: kindOf(document.activeElement),
        documentFocused: document.hasFocus(), defaultPrevented: event ? event.defaultPrevented : null,
        milliseconds: Math.round(performance.now()), listTop: Math.round(list.scrollTop),
        listMax: Math.round(list.scrollHeight - list.clientHeight), asideTop: Math.round(aside.scrollTop),
        asideMax: Math.round(aside.scrollHeight - aside.clientHeight) });
    };
    for (const name of ['keydown', 'keyup']) {
      document.addEventListener(name, (event) => {
        if (!['Home', 'End', 'Tab'].includes(event.key)) { return; }
        record(name === 'keydown' ? 'KEYDOWN' : 'KEYUP', event);
        if (name === 'keydown') { queueMicrotask(() => record('POST_KEYDOWN', event)); }
      }, true);
    }
    for (const name of ['focusin', 'focusout']) {
      document.addEventListener(name, (event) => record(name === 'focusin' ? 'FOCUS_IN' : 'FOCUS_OUT', event), true);
    }
    document.addEventListener('scroll', (event) => {
      if (['LIST', 'ASIDE'].includes(kindOf(event.target))) { record('SCROLL', event); }
    }, true);
    Object.defineProperty(window, '__groupKeyboardObservation', { value: () => {
      record('FINAL'); return { events: [...events], truncated };
    } });
  });
}

export async function observeGroupKeyboardCase(page, testInfo, body) {
  await install(page);
  return withGroupKeyboardObservation(body, async (bodyOutcome) => {
    let snapshot = null;
    try { snapshot = await page.evaluate(() => window['__groupKeyboardObservation']?.() ?? null); } catch { /* Publish explicit unavailability. */ }
    const value = validateGroupKeyboardObservation({ schemaVersion: 1, scope: 'MOCKED_UI_KEYBOARD_OBSERVATION_ONLY',
      profile: testInfo.project.name, attempt: testInfo.retry, bodyOutcome,
      captureState: snapshot ? 'CAPTURED' : 'UNAVAILABLE', events: snapshot?.events ?? [], truncated: snapshot?.truncated ?? false,
      classification: !snapshot || snapshot.truncated || !snapshot.events.length ? 'INCOMPLETE' : 'UNKNOWN' });
    mkdirSync(groupKeyboardInputDirectory, { recursive: true });
    writeFileSync(join(groupKeyboardInputDirectory, groupKeyboardFilename(value.profile, value.attempt)),
      JSON.stringify(value) + '\n', { flag: 'wx' });
    if (!snapshot) { throw new Error('Group keyboard observation is unavailable.'); }
  });
}
