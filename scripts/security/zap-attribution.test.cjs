const {test} = require('node:test');
const assert = require('node:assert/strict');
const {readFileSync} = require('node:fs');
const {join} = require('node:path');
const {propertyNames, locateEvidence} = new Function(readFileSync(join(__dirname, 'zap-attribution.js'), 'utf8') + '\nreturn {propertyNames, locateEvidence};')();
const names = propertyNames({components: {schemas: {File: {properties: {items: {}, id: {}, title: {}}}}}});

test('UUID substring is attributed to its schema path without exporting the UUID or match', () => {
  const uuid = 'aaaaaaaa-bbbb-cccc-dddd-123456789012';
  const result = locateEvidence(JSON.stringify({items: [{id: uuid}]}), '123456789012', names);
  assert.deepEqual(result, {status: 'matched-json-property', locations: [{schemaPath: ['items', '*', 'id'], valueCategory: 'uuid-substring', matchKind: 'substring'}]});
  assert.ok(!JSON.stringify(result).includes(uuid));
  assert.ok(!JSON.stringify(result).includes('123456789012'));
});

test('unmodeled dynamic property names and protected values never enter metadata', () => {
  const result = locateEvidence(JSON.stringify({'private-user@example.invalid': 'protected text'}), 'protected', names);
  assert.deepEqual(result.locations[0].schemaPath, ['__unmodeled__']);
  assert.ok(!JSON.stringify(result).includes('private-user'));
  assert.ok(!JSON.stringify(result).includes('protected'));
});

test('ambiguous matching properties are all retained and never classified as false positive', () => {
  const result = locateEvidence('{"id":"test","title":"test"}', 'test', names);
  assert.equal(result.locations.length, 2);
  assert.equal(result.status, 'matched-json-property');
  assert.ok(!JSON.stringify(result).includes('false-positive'));
});

test('unavailable attribution remains explicit rather than inventing a field', () => {
  assert.equal(locateEvidence('not JSON', 'literal', names).status, 'non-json-body');
  assert.equal(locateEvidence('{}', '', names).status, 'empty-evidence');
  assert.equal(locateEvidence('{"id":"other"}', 'literal', names).status, 'no-scalar-match');
});

test('bounded traversal records the limit without exporting response data', () => {
  assert.equal(locateEvidence('x'.repeat(2097153), 'x', names).status, 'body-limit');
  const result = locateEvidence(JSON.stringify(Array(20).fill({id: 'match'})), 'match', names);
  assert.equal(result.status, 'location-limit');
  assert.equal(result.locations.length, 16);
});
