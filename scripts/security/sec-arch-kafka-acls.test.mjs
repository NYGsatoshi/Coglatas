import { strict as assert } from 'node:assert';
import { test } from 'node:test';
import { aclInventory, expectedAcls, inventoryMatches, brokerPolicyMatches, processSyntheticEvents, fixtureArguments } from './sec-arch-kafka-acls.mjs';

test('fixture CLI accepts an exact candidate and rejects ignored, malformed or duplicate arguments', () => {
  assert.deepEqual(fixtureArguments(['artifacts/sec-arch/result.json', '--candidate-sha', 'a'.repeat(40)]),
    { report: 'artifacts/sec-arch/result.json', development: false, candidateSha: 'a'.repeat(40) });
  assert.equal(fixtureArguments(['--development']).development, true);
  for (const args of [['--output', 'result.json'], ['--candidate-sha'], ['--candidate-sha', 'bad'],
    ['one.json', 'two.json'], ['--development', '--development'],
    ['--candidate-sha', 'a'.repeat(40), '--candidate-sha', 'b'.repeat(40)]])
    assert.throws(() => fixtureArguments(args));
});

test('actual Kafka ACL text is parsed into exact sorted resources, principals, hosts and operations', () => {
  const fixture = expectedAcls.map(entry => {
    const [type, name, pattern, principal, host, operation, permission] = entry.split('|');
    return `Current ACLs for resource ResourcePattern(resourceType=${type}, name=${name}, patternType=${pattern}):\n` +
      ` (principal=${principal}, host=${host}, operation=${operation}, permissionType=${permission})`;
  }).join('\n');
  assert.equal(inventoryMatches(aclInventory(fixture)), true);
  assert.throws(() => aclInventory('(principal=User:alpha, host=127.0.0.1, operation=READ, permissionType=ALLOW)'));
});

test('synthetic consumer rejects a foreign tenant and deduplicates a replay without suppressing its valid control', () => {
  const event = JSON.stringify({ eventId: 'synthetic-event', tenant: 'alpha' });
  const foreign = JSON.stringify({ eventId: 'synthetic-foreign', tenant: 'beta' });
  assert.deepEqual(processSyntheticEvents([event, event, foreign], 'alpha'), { applied: 1, rejected: 1, duplicates: 1 });
  assert.deepEqual(processSyntheticEvents([event], 'beta'), { applied: 0, rejected: 1, duplicates: 0 });
});

for (const mutation of ['missing', 'excess', 'wildcard', 'prefix', 'broad-host', 'superuser', 'duplicate', 'disabled']) {
  test(`deliberate ${mutation} ACL fixture cannot pass`, () => {
    const entries = [...expectedAcls];
    if (mutation === 'missing') entries.pop();
    else if (mutation === 'excess') entries.push('CLUSTER|kafka-cluster|LITERAL|User:alpha|127.0.0.1|ALTER|ALLOW');
    else if (mutation === 'duplicate') entries.push(entries[0]);
    else if (mutation === 'disabled') entries.length = 0;
    else entries[0] = entries[0].replace(
      mutation === 'wildcard' ? 'sec-arch-alpha' : mutation === 'prefix' ? 'LITERAL' : mutation === 'broad-host' ? '127.0.0.1' : 'User:alpha',
      mutation === 'wildcard' ? '*' : mutation === 'prefix' ? 'PREFIXED' : mutation === 'broad-host' ? '*' : 'User:admin');
    assert.equal(inventoryMatches(entries.sort()), false);
  });
}

test('effective fixture broker policy rejects fallback, disabled authorizer, superuser and unauthenticated listener mutations', () => {
  const baseline = { 'authorizer.class.name': 'org.apache.kafka.metadata.authorizer.StandardAuthorizer',
    'allow.everyone.if.no.acl.found': 'false', 'super.users': 'User:admin',
    'listener.security.protocol.map': 'CONTROLLER:SASL_PLAINTEXT,BROKER:SASL_PLAINTEXT', 'sasl.enabled.mechanisms': 'PLAIN' };
  assert.equal(brokerPolicyMatches(baseline), true);
  for (const [key, value] of [['authorizer.class.name', ''], ['allow.everyone.if.no.acl.found', 'true'],
    ['super.users', 'User:admin;User:alpha'], ['listener.security.protocol.map', 'BROKER:PLAINTEXT'], ['sasl.enabled.mechanisms', '']])
    assert.equal(brokerPolicyMatches({ ...baseline, [key]: value }), false);
});
