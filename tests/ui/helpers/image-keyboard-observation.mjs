import { mkdirSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';

export const imageKeyboardInputDirectory = 'artifacts/image-keyboard-observation';
export const imageKeyboardOutputDirectory = 'artifacts/image-keyboard-observation-validated';
export const imageKeyboardByteLimit = 65536;
const profiles = ['chromium-desktop', 'chromium-mobile', 'firefox-desktop', 'webkit-desktop', 'webkit-mobile', 'narrow-320'];
const widths = [320, 390, 768, 1366, 1536];
const keys = (value, expected) => value && Object.keys(value).sort().join() === [...expected].sort().join();

export function imageKeyboardFilename(profile, width, image, attempt) {
  if (!profiles.includes(profile) || !widths.includes(width) || !['WIDE', 'PORTRAIT'].includes(image) ||
      !Number.isInteger(attempt) || attempt < 0 || attempt > 2) {
    throw new Error('Image keyboard observation identity is invalid.');
  }
  return `${profile}-${width}-${image.toLowerCase()}-attempt-${attempt}.json`;
}

/** Only fixed identities, booleans and bounded numeric geometry can be published. */
export function validateImageKeyboardObservation(value) {
  const invalid = () => { throw new Error('Image keyboard observation is invalid.'); };
  if (!keys(value, ['schemaVersion', 'scope', 'profile', 'width', 'image', 'attempt', 'bodyOutcome',
    'captureState', 'classification', 'events', 'truncated']) || value.schemaVersion !== 1 ||
      value.scope !== 'MOCKED_UI_IMAGE_SCROLL_ONLY' || !['COMPLETED', 'FAILED'].includes(value.bodyOutcome) ||
      !['CAPTURED', 'UNAVAILABLE'].includes(value.captureState) || !Array.isArray(value.events) ||
      value.events.length > 64 || typeof value.truncated !== 'boolean') { invalid(); }
  imageKeyboardFilename(value.profile, value.width, value.image, value.attempt);
  for (const event of value.events) {
    const booleans = ['viewportConnected', 'sameViewport', 'targetIsViewport', 'isFocused', 'focusWithin',
      'documentFocused', 'imageComplete', 'imageConnected'];
    const numbers = ['milliseconds', 'scrollLeft', 'scrollTop', 'scrollWidth', 'scrollHeight', 'clientWidth',
      'clientHeight', 'naturalWidth', 'naturalHeight', 'displayedWidth', 'displayedHeight'];
    if (!keys(event, ['kind', 'key', 'defaultPrevented', 'trusted', 'overflowX', 'overflowY', ...booleans, ...numbers]) ||
        !['INITIAL', 'KEYDOWN', 'KEYUP', 'FOCUS_IN', 'FOCUS_OUT', 'SCROLL', 'SCROLL_END', 'FINAL'].includes(event.kind) ||
        ![null, 'RIGHT', 'DOWN'].includes(event.key) || ![null, true, false].includes(event.defaultPrevented) ||
        ![null, true, false].includes(event.trusted) ||
        !['AUTO', 'SCROLL', 'HIDDEN', 'CLIP', 'VISIBLE', 'OTHER'].includes(event.overflowX) ||
        !['AUTO', 'SCROLL', 'HIDDEN', 'CLIP', 'VISIBLE', 'OTHER'].includes(event.overflowY) ||
        booleans.some((key) => typeof event[key] !== 'boolean') ||
        numbers.some((key) => !Number.isSafeInteger(event[key]) || event[key] < 0 || event[key] > 1000000)) { invalid(); }
  }
  if (value.captureState === 'UNAVAILABLE' && (value.events.length !== 0 || value.truncated)) { invalid(); }
  const incomplete = value.captureState === 'UNAVAILABLE' || value.truncated || !value.events.length ||
    value.events[0].kind !== 'INITIAL' || value.events.at(-1).kind !== 'FINAL';
  if (value.classification !== (incomplete ? 'INCOMPLETE' : 'UNKNOWN') ||
      Buffer.byteLength(JSON.stringify(value)) > imageKeyboardByteLimit) { invalid(); }
  return structuredClone(value);
}

/** A diagnostic failure must never replace the original mandatory assertion error. */
export async function withImageKeyboardObservation(body, finalize) {
  let primary, failed = false, result;
  try { result = await body(); } catch (error) { primary = error; failed = true; }
  try { await finalize(failed ? 'FAILED' : 'COMPLETED'); }
  catch (error) { if (!failed) { throw error; } }
  if (failed) { throw primary; }
  return result;
}

export async function observeImageKeyboardScroll(page, testInfo, width, image, body) {
  let installed = false;
  try {
    installed = await page.evaluate(() => {
      const viewport = document.querySelector('[data-testid="files-image-viewport"]');
      const image = document.querySelector('[data-testid="files-preview-image"]');
      if (!viewport || !image) { return false; }
      const events = [], listeners = new AbortController(), start = performance.now();
      let truncated = false;
      const overflow = (value) => ['auto', 'scroll', 'hidden', 'clip', 'visible'].includes(value) ? value.toUpperCase() : 'OTHER';
      const record = (kind, event = null) => {
        if (events.length === 64) { truncated = true; return; }
        const style = getComputedStyle(viewport), bounds = image.getBoundingClientRect();
        events.push({ kind, key: event?.key === 'ArrowRight' ? 'RIGHT' : event?.key === 'ArrowDown' ? 'DOWN' : null,
          defaultPrevented: event ? event.defaultPrevented : null, trusted: event ? event.isTrusted : null,
          viewportConnected: viewport.isConnected,
          sameViewport: document.querySelector('[data-testid="files-image-viewport"]') === viewport,
          targetIsViewport: event?.target === viewport, isFocused: document.activeElement === viewport,
          focusWithin: viewport.contains(document.activeElement), documentFocused: document.hasFocus(),
          overflowX: overflow(style.overflowX), overflowY: overflow(style.overflowY),
          milliseconds: Math.round(performance.now() - start), scrollLeft: Math.round(viewport.scrollLeft),
          scrollTop: Math.round(viewport.scrollTop), scrollWidth: viewport.scrollWidth, scrollHeight: viewport.scrollHeight,
          clientWidth: viewport.clientWidth, clientHeight: viewport.clientHeight,
          imageComplete: image.complete, imageConnected: image.isConnected,
          naturalWidth: image.naturalWidth, naturalHeight: image.naturalHeight,
          displayedWidth: Math.round(bounds.width), displayedHeight: Math.round(bounds.height) });
      };
      const options = { capture: true, signal: listeners.signal };
      for (const name of ['keydown', 'keyup']) {
        document.addEventListener(name, (event) => {
          if (['ArrowRight', 'ArrowDown'].includes(event.key)) { record(name === 'keydown' ? 'KEYDOWN' : 'KEYUP', event); }
        }, options);
      }
      for (const name of ['focusin', 'focusout', 'scroll', 'scrollend']) {
        document.addEventListener(name, (event) => {
          if (event.target !== viewport) { return; }
          const kind = name === 'focusin' ? 'FOCUS_IN' : name === 'focusout' ? 'FOCUS_OUT' : name === 'scroll' ? 'SCROLL' : 'SCROLL_END';
          record(kind, event);
        }, options);
      }
      window['__imageKeyboardScrollObservation'] = () => {
        record('FINAL'); listeners.abort(); delete window['__imageKeyboardScrollObservation'];
        return { events, truncated };
      };
      record('INITIAL');
      return true;
    });
  } catch { /* Run the original assertions even when observation is unavailable. */ }
  return withImageKeyboardObservation(body, async (bodyOutcome) => {
    let snapshot = null;
    if (installed) {
      try { snapshot = await page.evaluate(() => window['__imageKeyboardScrollObservation']?.() ?? null); }
      catch { /* Preserve explicit unavailability without disclosing browser errors. */ }
    }
    const value = validateImageKeyboardObservation({ schemaVersion: 1, scope: 'MOCKED_UI_IMAGE_SCROLL_ONLY',
      profile: testInfo.project.name, width, image, attempt: testInfo.retry, bodyOutcome,
      captureState: snapshot ? 'CAPTURED' : 'UNAVAILABLE', events: snapshot?.events ?? [], truncated: snapshot?.truncated ?? false,
      classification: !snapshot || snapshot.truncated || !snapshot.events.length ||
        snapshot.events[0].kind !== 'INITIAL' || snapshot.events.at(-1).kind !== 'FINAL' ? 'INCOMPLETE' : 'UNKNOWN' });
    mkdirSync(imageKeyboardInputDirectory, { recursive: true });
    writeFileSync(join(imageKeyboardInputDirectory, imageKeyboardFilename(value.profile, width, image, value.attempt)),
      JSON.stringify(value) + '\n', { flag: 'wx' });
    if (!snapshot) { throw new Error('Image keyboard observation is unavailable.'); }
  });
}
