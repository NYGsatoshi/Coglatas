const assert = require('node:assert/strict'),
  attribution = require('./zap-attribution.cjs'),
  expectedDoubleMatches = 2,
  firstOrdinal = 0,
  fixtureBodyOverflow = 2097153,
  fixtureLocationOverflow = 20,
  reducer = {locateEvidence: attribution.locate, propertyNames: attribution.properties, sqlResponse: attribution.sqlResponse},
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

const sqlAlert = (message, id = 8) => ({getAlertId: () => id, getMessage: () => message,
  getEvidence: () => { throw new Error('Evidence must not be read'); }}),
  sqlMessage = (code, type, body) => ({getResponseHeader: () => ({getStatusCode: () => code, getHeader: () => type}),
    getResponseBody: () => body}),
  safeResponse = alert => JSON.parse(JSON.stringify(reducer.sqlResponse(alert))),
  fallback = JSON.stringify({code: 'NotFound', message: 'Endpoint not found.', traceId: 'hostile-private-trace'});

test('SQL diagnostics use each alert own response and retain no protected strings', () => {
  const secret = 'hostile-cookie-token-email@example.invalid',
    first = safeResponse(sqlAlert(sqlMessage(200, 'application/json; private=' + secret, JSON.stringify({body: secret})))),
    second = safeResponse(sqlAlert(sqlMessage(404, 'application/json', fallback), 9));
  assert.deepEqual(first, {alertId: 8, ruleId: '40018', risk: 'High', status: 'captured', responseStatusCode: 200,
    mediaType: 'json', bodyClassification: 'other-json'});
  assert.deepEqual(second, {alertId: 9, ruleId: '40018', risk: 'High', status: 'captured', responseStatusCode: 404,
    mediaType: 'json', bodyClassification: 'endpoint-not-found-envelope'});
  assert.ok(!JSON.stringify([first, second]).includes(secret));
  assert.ok(!JSON.stringify(second).includes('hostile-private-trace'));
});

test('fallback classification requires fixed fields, status and media without exporting unknown fields', () => {
  const cases = [
    [200, 'application/json', fallback], [404, 'text/html', fallback],
    [404, 'application/json', JSON.stringify({code: 'NotFound', message: 'private message', traceId: 'private'})],
    [404, 'application/json', JSON.stringify({code: 'NotFound', message: 'Endpoint not found.', traceId: 'private', extra: 'private'})],
    [404, 'application/json', '[]'],
  ];
  for (const [code, media, body] of cases) {
    assert.equal(safeResponse(sqlAlert(sqlMessage(code, media, body))).bodyClassification, 'other-json');
  }
});

test('SQL diagnostics preserve unavailable messages and invalid headers as finite entries', () => {
  for (const alert of [sqlAlert(null), {getAlertId: () => 8, getMessage: () => { throw new Error('private'); }}]) {
    assert.deepEqual(safeResponse(alert), {alertId: 8, ruleId: '40018', risk: 'High', status: 'message-unavailable',
      responseStatusCode: null, mediaType: 'unavailable', bodyClassification: 'body-unavailable'});
  }
  for (const code of [0, 99, 600, 200.5, '200', null]) {
    const result = safeResponse(sqlAlert(sqlMessage(code, 'private', 'private')));
    assert.equal(result.status, 'response-header-unavailable');
    assert.equal(result.responseStatusCode, null);
    assert.equal(result.mediaType, 'unavailable');
  }
});

test('SQL response limits, absent bodies and hostile media remain explicit and value-free', () => {
  assert.equal(safeResponse(sqlAlert(sqlMessage(500, 'application/json', 'x'.repeat(fixtureBodyOverflow)))).bodyClassification, 'body-limit');
  assert.equal(safeResponse(sqlAlert(sqlMessage(500, '', null))).bodyClassification, 'body-unavailable');
  const message = sqlMessage(500, 'private-media-value', 'private-body-value');
  message.getResponseBody = () => { throw new Error('private error'); };
  const result = safeResponse(sqlAlert(message));
  assert.equal(result.mediaType, 'other');
  assert.equal(result.bodyClassification, 'body-unavailable');
  assert.ok(!JSON.stringify(result).includes('private'));
});

test('capture inventories only High SQL own messages and preserves the existing PII instance', () => {
  const pii = {...sqlAlert(sqlMessage(200, 'application/json', '{"id":"match"}'), 7),
    getEvidence: () => 'match', getPluginId: () => 10062, getRisk: () => 3},
    sql = {...sqlAlert(sqlMessage(404, 'application/json', fallback)), getPluginId: () => 40018, getRisk: () => 3},
    excluded = {getPluginId: () => 40018, getRisk: () => 2, getMessage: () => { throw new Error('Must not read Medium'); }},
    alerts = [pii, sql, excluded], original = attribution.context;
  let captured;
  attribution.context = () => ({alerts: {size: () => alerts.length, get: i => alerts[i]}, names: schemaNames, role: 'beta-owner',
    api: {Files: {writeString: (_path, value) => { captured = JSON.parse(value); }}, Paths: {get: value => value}, utf8: 'synthetic'}});
  try { attribution.capture(); }
  finally { attribution.context = original; }
  assert.deepEqual(captured.instances, [attribution.instance(pii, schemaNames)]);
  assert.equal(captured.sqlResponses.length, 1);
  assert.equal(captured.sqlResponses[0].bodyClassification, 'endpoint-not-found-envelope');
  assert.equal(captured.schemaVersion, 2);
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
