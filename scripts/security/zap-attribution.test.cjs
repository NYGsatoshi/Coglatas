const assert = require('node:assert/strict'),
  engine = require('node:vm'),
  expectedDoubleMatches = 2,
  firstOrdinal = 0,
  fixtureBodyOverflow = 2097153,
  fixtureLocationOverflow = 20,
  fs = require('node:fs'),
  paths = require('node:path'),
  reducer = engine.runInNewContext(`${fs.readFileSync(paths.join(__dirname, 'zap-attribution.js'), 'utf8')}\n({locateEvidence: Attribution.locate, propertyNames: Attribution.properties})`),
  retainedLocationLimit = 16,
  safeLocate = (...args) => JSON.parse(JSON.stringify(reducer.locateEvidence(...args))),
  schemaNames = reducer.propertyNames({components: {schemas: {File: {properties: {id: {}, items: {}, title: {}}}}}}),
  {test} = require('node:test');

test('UUID substring is attributed to its schema path without exporting the UUID or match', () => {
  const fixtureUuid = 'aaaaaaaa-bbbb-cccc-dddd-123456789012',
   result = safeLocate(JSON.stringify({items: [{id: fixtureUuid}]}), '123456789012', schemaNames);
  assert.deepEqual(result, {locations: [{matchKind: 'substring', schemaPath: ['items', '*', 'id'], valueCategory: 'uuid-substring'}], status: 'matched-json-property'});
  assert.ok(!JSON.stringify(result).includes(fixtureUuid));
  assert.ok(!JSON.stringify(result).includes('123456789012'));
});

test('unmodeled dynamic property names and protected values never enter metadata', () => {
  const result = safeLocate(JSON.stringify({'private-user@example.invalid': 'protected text'}), 'protected', schemaNames);
  assert.deepEqual(result.locations[firstOrdinal].schemaPath, ['__unmodeled__']);
  assert.ok(!JSON.stringify(result).includes('private-user'));
  assert.ok(!JSON.stringify(result).includes('protected'));
});

test('ambiguous matching properties are all retained and never classified as false positive', () => {
  const result = safeLocate('{"id":"test","title":"test"}', 'test', schemaNames);
  assert.equal(result.locations.length, expectedDoubleMatches);
  assert.equal(result.status, 'matched-json-property');
  assert.ok(!JSON.stringify(result).includes('false-positive'));
});

test('unavailable attribution remains explicit rather than inventing a field', () => {
  assert.equal(safeLocate('not JSON', 'literal', schemaNames).status, 'non-json-body');
  assert.equal(safeLocate('{}', '', schemaNames).status, 'empty-evidence');
  assert.equal(safeLocate('{"id":"other"}', 'literal', schemaNames).status, 'no-scalar-match');
});

test('invites denial-envelope evidence retains only the traceId schema location', () => {
  const syntheticTrace = '00-abcdef4111111111111111abcdef-0123456789abcdef-00',
    traceNames = reducer.propertyNames({properties: {traceId: {}}}),
    traceResult = safeLocate(JSON.stringify({traceId: syntheticTrace}), '4111111111111111', traceNames);
  assert.deepEqual(traceResult, {locations: [{matchKind: 'substring', schemaPath: ['traceId'], valueCategory: 'text'}], status: 'matched-json-property'});
  assert.ok(!JSON.stringify(traceResult).includes(syntheticTrace));
  assert.ok(!JSON.stringify(traceResult).includes('4111111111111111'));
});

test('bounded traversal records the limit without exporting response data', () => {
  assert.equal(safeLocate('x'.repeat(fixtureBodyOverflow), 'x', schemaNames).status, 'body-limit');
  const result = safeLocate(JSON.stringify(Array(fixtureLocationOverflow).fill({id: 'match'})), 'match', schemaNames);
  assert.equal(result.status, 'location-limit');
  assert.equal(result.locations.length, retainedLocationLimit);
});
