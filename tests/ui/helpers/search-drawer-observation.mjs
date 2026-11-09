import { mkdirSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';

export const searchDrawerInputDirectory = 'artifacts/search-drawer-observation';
export const searchDrawerOutputDirectory = 'artifacts/search-drawer-observation-validated';
export const searchDrawerByteLimit = 65536;
const positions = ['TOGGLE', 'OPENER', 'DRAWER', 'AUTHOR', 'OTHER'];
const exactKeys = (value, expected) => value && typeof value === 'object' && !Array.isArray(value) &&
  Object.keys(value).sort(compareCodeUnits).join() === [...expected].sort(compareCodeUnits).join();
const eventKeys = ['kind', 'key', 'target', 'targetNode', 'targetConnected', 'active', 'activeNode',
  'documentFocused', 'defaultPrevented', 'milliseconds', 'hostNode', 'trackedHostNode', 'trackedHostConnected',
  'openerNode', 'drawerNode', 'panelExpanded', 'drawerOpen', 'parentLoading', 'parentDenied'];

export function searchDrawerFilename(profile, attempt) {
  if (profile !== 'chromium-mobile' || !Number.isInteger(attempt) || attempt < 0 || attempt > 2) {
    throw new Error('Search drawer observation identity is invalid.');
  }
  return `${profile}-attempt-${attempt}.json`;
}

/** No browser text, URL, HTTP payload, identity string or arbitrary attribute is publishable. */
export function validateSearchDrawerObservation(value) {
  const invalid = () => { throw new Error('Search drawer observation is invalid.'); };
  if (!exactKeys(value, ['schemaVersion', 'scope', 'profile', 'attempt', 'bodyOutcome', 'captureState',
    'classification', 'events', 'truncated']) || value.schemaVersion !== 1 ||
    value.scope !== 'MOCKED_UI_SEARCH_LIFECYCLE_ONLY' ||
    !['COMPLETED', 'FAILED'].includes(value.bodyOutcome) ||
    !['CAPTURED', 'UNAVAILABLE'].includes(value.captureState) || !Array.isArray(value.events) ||
    value.events.length > 128 || typeof value.truncated !== 'boolean') { invalid(); }
  searchDrawerFilename(value.profile, value.attempt);
  for (const event of value.events) {
    if (!exactKeys(event, eventKeys) ||
      !['LIFECYCLE', 'KEYDOWN', 'CAPTURE_MICROTASK', 'KEYUP', 'CLICK', 'FOCUS_IN', 'FOCUS_OUT', 'FINAL'].includes(event.kind) ||
      ![null, 'ENTER', 'TAB', 'SHIFT_TAB', 'ESCAPE'].includes(event.key) ||
      !positions.includes(event.target) || !positions.includes(event.active) ||
      ![null, true, false].includes(event.defaultPrevented) ||
      ['targetConnected', 'documentFocused', 'trackedHostConnected', 'panelExpanded', 'drawerOpen',
        'parentLoading', 'parentDenied'].some((key) => typeof event[key] !== 'boolean') ||
      ['targetNode', 'activeNode', 'hostNode', 'trackedHostNode', 'openerNode', 'drawerNode'].some((key) =>
        !Number.isSafeInteger(event[key]) || event[key] < 0 || event[key] > 4096) ||
      !Number.isSafeInteger(event.milliseconds) || event.milliseconds < 0 || event.milliseconds > 1000000 ||
      event.drawerOpen !== (event.drawerNode > 0)) { invalid(); }
  }
  if (value.captureState === 'UNAVAILABLE' && (value.events.length !== 0 || value.truncated)) { invalid(); }
  const incomplete = value.captureState === 'UNAVAILABLE' || value.truncated || !value.events.length;
  if (value.classification !== (incomplete ? 'INCOMPLETE' : 'UNKNOWN') ||
    Buffer.byteLength(JSON.stringify(value)) > searchDrawerByteLimit) { invalid(); }
  return structuredClone(value);
}

export async function withSearchDrawerObservation(body, finalize) {
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
    let truncated = false, nextIdentity = 1, trackedHost = null, previousLifecycle = '';
    const select = (id) => document.querySelector(`[data-testid="${id}"]`);
    const identify = (node) => {
      if (!node || node.nodeType !== 1) { return 0; }
      if (!identities.has(node)) {
        if (nextIdentity > 4096) { truncated = true; return 0; }
        identities.set(node, nextIdentity++);
      }
      return identities.get(node);
    };
    const position = (node) => node?.matches?.('[data-testid="message-filter-drawer-toggle"]') ? 'TOGGLE'
      : node?.matches?.('[data-testid="message-advanced-filters-open"]') ? 'OPENER'
      : node?.matches?.('[data-testid="message-advanced-author"]') ? 'AUTHOR'
      : node?.closest?.('[data-testid="message-advanced-filters-drawer"]') ? 'DRAWER' : 'OTHER';
    const structural = () => {
      const host = document.querySelector('app-message-search-filters');
      if (host) { trackedHost = host; }
      const drawer = select('message-advanced-filters-drawer');
      return { hostNode: identify(host), trackedHostNode: identify(trackedHost),
        trackedHostConnected: !!trackedHost?.isConnected,
        openerNode: identify(select('message-advanced-filters-open')), drawerNode: identify(drawer),
        panelExpanded: select('message-filter-drawer-toggle')?.getAttribute('aria-expanded') === 'true',
        drawerOpen: !!drawer, parentLoading: !!select('messages-list-loading'),
        parentDenied: !!select('messages-permission-denied') };
    };
    const record = (kind, event = null, state = structural()) => {
      if (events.length === 128) { truncated = true; return; }
      const key = event?.key === 'Enter' ? 'ENTER' : event?.key === 'Escape' ? 'ESCAPE'
        : event?.key === 'Tab' ? (event.shiftKey ? 'SHIFT_TAB' : 'TAB') : null;
      events.push({ kind, key, target: position(event?.target), targetNode: identify(event?.target),
        targetConnected: !!event?.target?.isConnected, active: position(document.activeElement),
        activeNode: identify(document.activeElement), documentFocused: document.hasFocus(),
        defaultPrevented: event ? event.defaultPrevented : null,
        milliseconds: Math.round(performance.now()), ...state });
    };
    const lifecycle = () => {
      const state = structural(), signature = JSON.stringify(state);
      if (signature !== previousLifecycle) { previousLifecycle = signature; record('LIFECYCLE', null, state); }
    };
    const observer = new MutationObserver(lifecycle), listeners = new AbortController();
    observer.observe(document, { childList: true, subtree: true, attributes: true, attributeFilter: ['aria-expanded'] });
    for (const name of ['keydown', 'keyup']) {
      document.addEventListener(name, (event) => {
        if (!['Enter', 'Tab', 'Escape'].includes(event.key) || !trackedHost) { return; }
        record(name === 'keydown' ? 'KEYDOWN' : 'KEYUP', event);
        if (name === 'keydown') { queueMicrotask(() => record('CAPTURE_MICROTASK', event)); }
      }, { capture: true, signal: listeners.signal });
    }
    for (const name of ['click', 'focusin', 'focusout']) {
      document.addEventListener(name, (event) => {
        if (!trackedHost) { return; }
        record(name === 'click' ? 'CLICK' : name === 'focusin' ? 'FOCUS_IN' : 'FOCUS_OUT', event);
      }, { capture: true, signal: listeners.signal });
    }
    Object.defineProperty(window, '__searchDrawerObservation', { value: () => {
      lifecycle(); record('FINAL'); observer.disconnect(); listeners.abort();
      return { events: [...events], truncated };
    } });
  });
}

export async function observeSearchDrawerCase(page, testInfo, body) {
  await install(page);
  return withSearchDrawerObservation(body, async (bodyOutcome) => {
    let snapshot = null;
    try { snapshot = await page.evaluate(() => window['__searchDrawerObservation']?.() ?? null); }
    catch { /* Publish explicit unavailability without browser exception details. */ }
    const value = validateSearchDrawerObservation({ schemaVersion: 1, scope: 'MOCKED_UI_SEARCH_LIFECYCLE_ONLY',
      profile: testInfo.project.name, attempt: testInfo.retry, bodyOutcome,
      captureState: snapshot ? 'CAPTURED' : 'UNAVAILABLE', events: snapshot?.events ?? [], truncated: snapshot?.truncated ?? false,
      classification: !snapshot || snapshot.truncated || !snapshot.events.length ? 'INCOMPLETE' : 'UNKNOWN' });
    mkdirSync(searchDrawerInputDirectory, { recursive: true });
    writeFileSync(join(searchDrawerInputDirectory, searchDrawerFilename(value.profile, value.attempt)),
      JSON.stringify(value) + '\n', { flag: 'wx' });
    if (!snapshot) { throw new Error('Search drawer observation is unavailable.'); }
  });
}

// Match default UTF-16 code-unit ordering, including native string coercion.
function compareCodeUnits(left, right) {
  const leftString = `${left}`;
  const rightString = `${right}`;
  if (leftString === rightString) return 0;
  return leftString < rightString ? -1 : 1;
}
