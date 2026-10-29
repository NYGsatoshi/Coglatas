import assert from 'node:assert/strict';
import { mkdtemp, readFile, rm, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';

import { attachPenpotEvidence } from './penpot-attachment.mjs';

const png = Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]);
const evidence = Object.freeze({
  documentOverflow: 0,
  mainOverflow: 0,
  theme: 'dark',
  main: { x: 0, width: 1536 },
});
const name = 'chromium-desktop-1536-dark-login';

function recorder() {
  const screenshotCalls = [];
  const attachments = [];
  const page = {
    screenshot: async (options) => {
      screenshotCalls.push(options);
      if (options.path) await writeFile(options.path, png);
      return png;
    },
  };
  const testInfo = {
    attach: async (attachmentName, options) => {
      attachments.push({ name: attachmentName, options });
      if (options.path) {
        assert.deepEqual(await readFile(options.path), png);
      }
    },
  };
  return { page, testInfo, screenshotCalls, attachments };
}

function assertAttachments(attachments, imageOptions) {
  assert.deepEqual(attachments.map((item) => item.name), [name, `${name}-layout`]);
  assert.deepEqual(attachments[0].options, { ...imageOptions, contentType: 'image/png' });
  assert.equal(attachments[1].options.contentType, 'application/json');
  assert.deepEqual(JSON.parse(attachments[1].options.body), evidence);
}

test('unset capture directory attaches the original PNG buffer and exact layout JSON', async () => {
  const original = process.env.PENPOT_CAPTURE_DIRECTORY;
  delete process.env.PENPOT_CAPTURE_DIRECTORY;
  try {
    const capture = recorder();
    await attachPenpotEvidence(capture.page, capture.testInfo, name, evidence);
    assert.deepEqual(capture.screenshotCalls, [
      { fullPage: false, animations: 'disabled' },
    ]);
    assertAttachments(capture.attachments, { body: png });
    assert.strictEqual(capture.attachments[0].options.body, png);
    assert.equal(Object.hasOwn(capture.attachments[0].options, 'path'), false);
  } finally {
    if (original === undefined) delete process.env.PENPOT_CAPTURE_DIRECTORY;
    else process.env.PENPOT_CAPTURE_DIRECTORY = original;
  }
});

test('configured directory retains the PNG pathname and full screenshot evidence', async () => {
  const directory = await mkdtemp(path.join(os.tmpdir(), 'coglatas-penpot-capture-'));
  try {
    const capture = recorder();
    await attachPenpotEvidence(capture.page, capture.testInfo, name, evidence, directory);
    const imagePath = path.join(directory, `${name}.png`);
    assert.deepEqual(capture.screenshotCalls, [
      { path: imagePath, fullPage: false, animations: 'disabled' },
    ]);
    assert.deepEqual(await readFile(imagePath), png);
    assertAttachments(capture.attachments, { path: imagePath });
    assert.equal(Object.hasOwn(capture.attachments[0].options, 'body'), false);
  } finally {
    await rm(directory, { recursive: true, force: true });
  }
});

test('a screenshot failure propagates instead of publishing incomplete evidence', async () => {
  const attachments = [];
  await assert.rejects(
    attachPenpotEvidence(
      { screenshot: async () => { throw new Error('synthetic capture failed'); } },
      { attach: async (...args) => attachments.push(args) },
      name,
      evidence,
      '',
    ),
    /synthetic capture failed/,
  );
  assert.deepEqual(attachments, []);
});

test('a rejected PNG attachment is not silently replaced with layout-only evidence', async () => {
  const calls = [];
  await assert.rejects(
    attachPenpotEvidence(
      { screenshot: async () => png },
      { attach: async (attachmentName) => {
        calls.push(attachmentName);
        throw new Error('synthetic attachment failed');
      } },
      name,
      evidence,
      '',
    ),
    /synthetic attachment failed/,
  );
  assert.deepEqual(calls, [name]);
});
