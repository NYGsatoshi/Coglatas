import assert from 'node:assert/strict';
import { mkdtempSync, readFileSync, realpathSync, rmSync } from 'node:fs';
import { createRequire } from 'node:module';
import { tmpdir } from 'node:os';
import { basename, dirname, join } from 'node:path';
import test from 'node:test';
import { groupKeyboardFilename, groupKeyboardInputDirectory, observeGroupKeyboardCase } from './helpers/group-keyboard-observation.mjs';
import { observeSearchDrawerCase, searchDrawerFilename, searchDrawerInputDirectory } from './helpers/search-drawer-observation.mjs';

// Use the existing locked frontend DOM implementation. These are Node DOM
// controls, not browser/device acceptance or native keyboard-scroll evidence.
const requireFrontend = createRequire(new URL('../../frontend/package.json', import.meta.url));
const { JSDOM } = requireFrontend('jsdom');
const profile = { project: { name: 'chromium-mobile' }, retry: 0 };
const marker = 'synthetic-private-observer-canary';

async function withDom(control) {
  const root = mkdtempSync(join(tmpdir(), 'council-observer-'));
  const previousDirectory = process.cwd();
  const dom = new JSDOM('<!doctype html><html><body></body></html>', { url: 'https://observer.example.test/' });
  const names = ['window', 'document', 'MutationObserver', 'AbortController'];
  const previous = new Map(names.map(name => [name, Object.getOwnPropertyDescriptor(globalThis, name)]));
  try {
    process.chdir(root);
    for (const name of names) Object.defineProperty(globalThis, name, {
      value: name === 'window' ? dom.window : dom.window[name], configurable: true, writable: true,
    });
    // Invoke the original callback object in its original module. No source
    // extraction, eval, generated script, counter rewrite or browser substitute
    // is used to claim that Node executed a serialized browser callback.
    const page = { addInitScript: async callback => { callback(); }, evaluate: async callback => callback() };
    await control({ root, page, window: dom.window, document: dom.window.document });
  } finally {
    for (const name of names) {
      const descriptor = previous.get(name);
      if (descriptor) Object.defineProperty(globalThis, name, descriptor);
      else delete globalThis[name];
    }
    dom.window.close();
    process.chdir(previousDirectory);
    const owned = realpathSync(root);
    assert.equal(dirname(owned), realpathSync(tmpdir()));
    assert.ok(basename(owned).startsWith('council-observer-'));
    rmSync(owned, { recursive: true, force: true });
  }
}

function receipt(root, directory, filename) {
  const text = readFileSync(join(root, directory, filename), 'utf8');
  assert.equal(text.includes(marker), false, 'Private DOM text and values must not enter structural evidence.');
  return JSON.parse(text);
}

function roster(document) {
  document.body.innerHTML = `<aside class="dm-page__group-details"><details data-testid="group-members-panel">
    <summary>${marker}</summary><ul class="group-members__list" tabindex="0"><li>${marker}</li></ul>
    <button data-testid="group-leave">${marker}</button></details></aside>`;
  const list = document.querySelector('ul');
  Object.defineProperties(list, { scrollHeight: { value: 100 }, clientHeight: { value: 20 } });
  list.scrollTop = 32;
  return list;
}

void test('Node DOM roster observer records capture order, cancellation and real scroll lifecycle without private text', async () => {
  await withDom(async ({ root, page, window, document }) => {
    await observeGroupKeyboardCase(page, profile, async () => {
      const list = roster(document);
      await Promise.resolve();
      list.focus();
      list.addEventListener('keydown', event => event.preventDefault());
      list.dispatchEvent(new window.KeyboardEvent('keydown', { key: 'Home', bubbles: true, cancelable: true }));
      await Promise.resolve();
      list.dispatchEvent(new window.KeyboardEvent('keyup', { key: 'Home', bubbles: true }));
      list.dispatchEvent(new window.KeyboardEvent('keydown', { key: marker, bubbles: true }));
      list.dispatchEvent(new window.Event('scroll'));
      list.dispatchEvent(new window.Event('scrollend'));
    });
    const value = receipt(root, groupKeyboardInputDirectory, groupKeyboardFilename(profile.project.name, 0));
    assert.equal(value.classification, 'UNKNOWN');
    assert.equal(value.bodyOutcome, 'COMPLETED');
    const down = value.events.find(event => event.kind === 'KEYDOWN');
    const microtask = value.events.find(event => event.kind === 'CAPTURE_MICROTASK');
    assert.equal(down.key, 'HOME'); assert.equal(down.target, 'LIST');
    assert.equal(down.defaultPrevented, false); assert.equal(microtask.defaultPrevented, true);
    assert.equal(down.listTop, 32); assert.equal(down.listMax, 80);
    assert.ok(down.listNode > 0); assert.equal(down.listNode, microtask.listNode);
    assert.equal(down.trusted, false, 'Synthetic DOM events must not masquerade as native device input.');
    assert.equal(value.events.filter(event => event.kind === 'KEYDOWN').length, 1);
    assert.ok(value.events.some(event => event.kind === 'SCROLL'));
    assert.ok(value.events.some(event => event.kind === 'SCROLL_END'));
  });
});

void test('Node DOM roster replacement retains distinct element identities and detached-state evidence', async () => {
  await withDom(async ({ root, page, window, document }) => {
    await observeGroupKeyboardCase(page, profile, async () => {
      const first = roster(document);
      await Promise.resolve();
      first.dispatchEvent(new window.KeyboardEvent('keydown', { key: 'End', bubbles: true }));
      await Promise.resolve();
      first.remove();
      await Promise.resolve();
      const replacement = document.createElement('ul');
      replacement.className = 'group-members__list'; replacement.tabIndex = 0;
      document.querySelector('details').append(replacement);
      await Promise.resolve();
      replacement.dispatchEvent(new window.KeyboardEvent('keydown', { key: 'Tab', shiftKey: true, bubbles: true }));
      await Promise.resolve();
    });
    const value = receipt(root, groupKeyboardInputDirectory, groupKeyboardFilename(profile.project.name, 0));
    const keys = value.events.filter(event => event.kind === 'KEYDOWN');
    assert.equal(keys.length, 2); assert.equal(keys[1].key, 'SHIFT_TAB');
    assert.notEqual(keys[0].listNode, keys[1].listNode);
    assert.ok(value.events.some(event => event.listNode === 0 && event.trackedListNode === keys[0].listNode && !event.trackedListConnected));
  });
});

void test('Node DOM empty and overflowing roster observations remain explicitly incomplete', async () => {
  for (const overflow of [false, true]) {
    await withDom(async ({ root, page, window, document }) => {
      await observeGroupKeyboardCase(page, profile, async () => {
        if (!overflow) return;
        const list = roster(document);
        await Promise.resolve();
        for (let index = 0; index < 150; index++) list.dispatchEvent(new window.KeyboardEvent('keydown', { key: 'Home', bubbles: true }));
        await Promise.resolve();
      });
      const value = receipt(root, groupKeyboardInputDirectory, groupKeyboardFilename(profile.project.name, 0));
      assert.equal(value.classification, 'INCOMPLETE');
      assert.equal(value.truncated, overflow);
      if (overflow) assert.equal(value.events.length, 128);
      else assert.equal(value.events.some(event => event.listNode > 0), false);
    });
  }
});

void test('Node DOM roster capture preserves the original failing body and publishes no exception text', async () => {
  await withDom(async ({ root, page, document }) => {
    const primary = new Error(marker);
    await assert.rejects(observeGroupKeyboardCase(page, profile, async () => {
      roster(document); await Promise.resolve(); throw primary;
    }), error => error === primary);
    assert.equal(receipt(root, groupKeyboardInputDirectory, groupKeyboardFilename(profile.project.name, 0)).bodyOutcome, 'FAILED');
  });
});

void test('Node DOM search observer records drawer opening, key cancellation and host invalidation without content', async () => {
  await withDom(async ({ root, page, window, document }) => {
    await observeSearchDrawerCase(page, profile, async () => {
      document.body.innerHTML = `<app-message-search-filters><button data-testid="message-filter-drawer-toggle" aria-expanded="false">${marker}</button>
        <button data-testid="message-advanced-filters-open">${marker}</button></app-message-search-filters>`;
      await Promise.resolve();
      const host = document.querySelector('app-message-search-filters');
      const opener = document.querySelector('[data-testid="message-advanced-filters-open"]');
      opener.focus();
      opener.addEventListener('click', () => {
        document.querySelector('[data-testid="message-filter-drawer-toggle"]').setAttribute('aria-expanded', 'true');
        host.insertAdjacentHTML('beforeend', `<section data-testid="message-advanced-filters-drawer"><input data-testid="message-advanced-author" value="${marker}"></section>`);
      });
      opener.click();
      await Promise.resolve();
      const author = document.querySelector('input'); author.focus();
      author.addEventListener('keydown', event => event.preventDefault());
      author.dispatchEvent(new window.KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true }));
      await Promise.resolve();
      author.dispatchEvent(new window.KeyboardEvent('keyup', { key: 'Enter', bubbles: true }));
      author.dispatchEvent(new window.KeyboardEvent('keydown', { key: marker, bubbles: true }));
      host.remove();
      await Promise.resolve();
    });
    const value = receipt(root, searchDrawerInputDirectory, searchDrawerFilename(profile.project.name, 0));
    assert.equal(value.classification, 'UNKNOWN');
    assert.ok(value.events.some(event => event.kind === 'CLICK' && event.target === 'OPENER' && !event.drawerOpen));
    assert.ok(value.events.some(event => event.kind === 'LIFECYCLE' && event.drawerOpen && event.panelExpanded));
    const down = value.events.find(event => event.kind === 'KEYDOWN');
    const microtask = value.events.find(event => event.kind === 'CAPTURE_MICROTASK');
    assert.equal(down.key, 'ENTER'); assert.equal(down.target, 'AUTHOR');
    assert.equal(down.defaultPrevented, false); assert.equal(microtask.defaultPrevented, true);
    assert.equal(value.events.filter(event => event.kind === 'KEYDOWN').length, 1);
    assert.ok(value.events.some(event => event.hostNode === 0 && event.trackedHostNode > 0 && !event.trackedHostConnected));
  });
});

void test('Node DOM search capture preserves overflow and the original body failure as incomplete evidence', async () => {
  await withDom(async ({ root, page, window, document }) => {
    const primary = new Error(marker);
    await assert.rejects(observeSearchDrawerCase(page, profile, async () => {
      document.body.innerHTML = '<app-message-search-filters><button data-testid="message-advanced-filters-open">Open</button></app-message-search-filters>';
      await Promise.resolve();
      const opener = document.querySelector('button');
      for (let index = 0; index < 150; index++) opener.dispatchEvent(new window.KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
      await Promise.resolve();
      throw primary;
    }), error => error === primary);
    const value = receipt(root, searchDrawerInputDirectory, searchDrawerFilename(profile.project.name, 0));
    assert.equal(value.bodyOutcome, 'FAILED'); assert.equal(value.classification, 'INCOMPLETE');
    assert.equal(value.truncated, true); assert.equal(value.events.length, 128);
  });
});
