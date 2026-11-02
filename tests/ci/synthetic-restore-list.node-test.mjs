import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { test } from 'node:test';
import { installRestoreList, planRestoreList, verifyRestoreList } from '../../scripts/ci/synthetic-restore-list.mjs';

const source = Buffer.from('; TOC Entries: 9\n; native header counts implicit entries too\n' +
  '1; 2615 2200 SCHEMA - public postgres\n' +
  '2; 1259 200 TABLE public earlier postgres\n' +
  '7; 2606 900 FK CONSTRAINT public earlier a_constraint postgres\n' +
  '; preserve this comment position\n' +
  '9; 2606 100 FK CONSTRAINT public later z_constraint postgres\n' +
  '12; 0 0 COMMENT public TABLE earlier postgres\n');

test('complete list swaps only FK slots using original numeric OIDs, not names or header count', () => {
  const original = Buffer.from(source), planned = planRestoreList(source);
  assert.equal(planned.entryCount, 5); assert.equal(planned.foreignKeyCount, 2);
  assert.deepEqual(source, original);
  const before = source.toString().split('\n'), after = planned.bytes.toString().split('\n');
  assert.equal(after[4], before[6]); assert.equal(after[6], before[4]);
  before.forEach((line, index) => { if (![4, 6].includes(index)) assert.equal(after[index], line); });
  assert.deepEqual([...after].sort(), [...before].sort());
  assert.deepEqual(planRestoreList(planned.bytes).bytes, planned.bytes);
});

test('zero/one FK, non-ASCII names and CRLF line bytes remain complete', () => {
  for (const value of ['1; 1259 5 TABLE public sample postgres\n',
    '; header\r\n1; 2606 5 FK CONSTRAINT public café foreign_key postgres\r\n']) {
    const bytes = Buffer.from(value); assert.deepEqual(planRestoreList(bytes).bytes, bytes);
  }
});

test('malformed UTF8, byte limits, missing final newline and nonnative lines fail closed', () => {
  for (const value of [null, 'text', Buffer.alloc(0), Buffer.alloc(16 * 1024 * 1024 + 1),
    Buffer.from([0xc3, 0x28, 10]), Buffer.concat([Buffer.from([0xef, 0xbb, 0xbf]), source]),
    Buffer.from('1; 0 0 TABLE public sample postgres'), Buffer.from('; only comments\n'),
    Buffer.from('1; 0 0 TABLE public\0sample postgres\n'), Buffer.from('unexpected line\n'),
    Buffer.from('1; 0 0 TABLE public\rsample postgres\n'), Buffer.from('\n'.repeat(400_001))])
    assert.throws(() => planRestoreList(value), { code: 'RESTORE_LIST' });
});

test('duplicate IDs, FK OIDs and malformed/out-of-range numeric identities are rejected', () => {
  for (const value of [source.toString().replace('9; 2606', '7; 2606'),
    source.toString().replace('2606 100', '2606 900'),
    source.toString().replace('9; 2606 100', '0; 2606 100'),
    source.toString().replace('9; 2606 100', '2147483648; 2606 100'),
    source.toString().replace('2606 100', '2606 4294967296'),
    source.toString().replace('2606 100', '2606 0'),
    source.toString().replace('2606 100', '2606 0100'),
    source.toString().replace('2606 100', '1259 100'),
    source.toString().replace('FK CONSTRAINT public later z_constraint postgres', 'FK CONSTRAINT')])
    assert.throws(() => planRestoreList(Buffer.from(value)), { code: 'RESTORE_LIST' });
});

test('entry inventory count is bounded independently from native header metadata', () => {
  const bytes = Buffer.from(Array.from({ length: 100_001 }, (_, index) =>
    (index + 1) + '; 0 0 COMMENT public sample postgres\n').join(''));
  assert.throws(() => planRestoreList(bytes), { code: 'RESTORE_LIST' });
});

test('independent verifier rejects dropped/altered entries, comments, non-FK moves and wrong order', () => {
  const planned = planRestoreList(source).bytes.toString(), lines = planned.split('\n');
  const moved = [...lines]; [moved[3], moved[4]] = [moved[4], moved[3]];
  for (const value of [planned.replace(lines[4] + '\n', ''), planned.replace('TABLE public earlier', 'TABLE public changed'),
    planned.replace('preserve this comment', 'changed comment'), moved.join('\n'),
    planned.replace(lines[6], lines[4]), source.toString(), planned + '; injected comment\n'])
    assert.throws(() => verifyRestoreList(source, Buffer.from(value)), { code: 'RESTORE_LIST' });
});

test('installer sends exact same complete private bytes to each owned target without interpolation', () => {
  const { bytes } = planRestoreList(source), calls = [];
  const compose = (project, args, options) => {
    calls.push({ project, args, options });
    return { status: 0, stdout: Buffer.from(createHash('sha256').update(options.input).digest('hex') +
      '  /tmp/coglatas-recovery.list\n') };
  };
  installRestoreList(compose, 'owned-target', bytes); installRestoreList(compose, 'owned-fault', bytes);
  assert.equal(calls.length, 2);
  for (const call of calls) {
    assert.deepEqual(call.args.slice(0, 5), ['exec', '-T', 'postgres', '/bin/sh', '-c']);
    assert.match(call.args[5], /umask 077/); assert.match(call.args[5], /set -C/);
    assert.match(call.args[5], /test ! -L/); assert.match(call.args[5], /stat -c %a/);
    assert.equal(call.options.input, bytes); assert.equal(call.args.length, 6);
    assert.ok(!call.args[5].includes('FK CONSTRAINT'));
  }
});

test('write failures and mismatched byte verification reject before restore; malformed input never writes', () => {
  const { bytes } = planRestoreList(source);
  for (const result of [{ status: 1, stdout: Buffer.alloc(0) }, { status: 0, stdout: Buffer.from('incorrect hash') }])
    assert.throws(() => installRestoreList(() => result, 'owned-target', bytes), { code: 'RESTORE_LIST' });
  let calls = 0;
  assert.throws(() => installRestoreList(() => { calls++; }, 'owned-target', Buffer.from('bad\n')), { code: 'RESTORE_LIST' });
  assert.equal(calls, 0);
});
