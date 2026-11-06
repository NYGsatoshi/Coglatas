import { createHash } from 'node:crypto';
import { readFileSync } from 'node:fs';
import assert from 'node:assert/strict';
import test from 'node:test';

const fixture = JSON.parse(readFileSync(new URL('../Coglatas.Tests/ProjectIde/Fixtures/artifact-hash-vectors-v1.json', import.meta.url), 'utf8'));
assert.equal(fixture.status, 'PROTOTYPE_ONLY');
assert.equal(fixture.vectors.length, 4);
for (const vector of fixture.vectors) {
  test(`independent UTF-8 SHA-256 framing: ${vector.domain}`, () => {
    const hash = (separator, body) => createHash('sha256').update(vector.domain, 'utf8').update(separator).update(body, 'utf8').digest('hex');
    assert.equal(hash(Buffer.from([0]), vector.canonical), vector.sha256);
    assert.notEqual(hash(Buffer.alloc(0), vector.canonical), vector.sha256);
    assert.notEqual(hash(Buffer.from('\n'), vector.canonical), vector.sha256);
    assert.notEqual(hash(Buffer.from([0]), ''), vector.sha256);
    assert.notEqual(hash(Buffer.from([0]), `${vector.canonical}\n`), vector.sha256);
  });
}
test('type substitution changes full digest', () => {
  assert.equal(new Set(fixture.vectors.map(v => v.sha256)).size, 4);
});
