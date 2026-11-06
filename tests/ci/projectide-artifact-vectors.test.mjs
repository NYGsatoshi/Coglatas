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

const readerCompatibility = JSON.parse(readFileSync(
  new URL('../Coglatas.Tests/ProjectIde/Fixtures/artifact-reader-compat-vectors-v1.json', import.meta.url),
  'utf8',
));
test('independent reader Unicode and ordered-array digest vectors remain distinct', () => {
  assert.equal(readerCompatibility.status, 'PROTOTYPE_ONLY');
  assert.equal(readerCompatibility.vectors.length, 4);
  assert.equal(readerCompatibility.domain, fixture.vectors[0].domain);
  const hashes = new Map();
  for (const vector of readerCompatibility.vectors) {
    const value = createHash('sha256')
      .update(readerCompatibility.domain, 'utf8')
      .update(Buffer.from([0]))
      .update(vector.canonical, 'utf8')
      .digest('hex');
    assert.equal(value, vector.sha256, vector.id);
    assert.equal(hashes.has(vector.id), false, vector.id);
    hashes.set(vector.id, value);
    assert.notEqual(
      createHash('sha256').update(readerCompatibility.domain, 'utf8').update(Buffer.from([0]))
        .update(vector.canonical + '\\n', 'utf8').digest('hex'),
      value,
    );
  }
  assert.equal(hashes.size, 4);
  for (const [left, right] of readerCompatibility.casePairs) {
    assert.notEqual(hashes.get(left), hashes.get(right), left + ' / ' + right);
  }
});
