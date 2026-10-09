export function aclInventory(text) {
  let resource;
  const entries = [];
  for (const line of text.split(/\r?\n/)) {
    const resourceMatch = /ResourcePattern\(resourceType=(\w+), name=([^,]+), patternType=(\w+)\)/.exec(line);
    if (resourceMatch) resource = resourceMatch.slice(1);
    const entry = /\(principal=([^,]+), host=([^,]+), operation=(\w+), permissionType=(\w+)\)/.exec(line);
    if (entry) {
      if (!resource) throw new Error('Unclassified ACL entry');
      entries.push([...resource, ...entry.slice(1)].join('|'));
    }
  }
  return entries.sort();
}

export const expectedAcls = ['alpha', 'beta'].flatMap(p => [
  ...['READ', 'WRITE', 'DESCRIBE', 'DESCRIBE_CONFIGS'].map(o => `TOPIC|sec-arch-${p}|LITERAL|User:${p}|127.0.0.1|${o}|ALLOW`),
  `GROUP|sec-arch-${p}-group|LITERAL|User:${p}|127.0.0.1|READ|ALLOW`
]).sort();

export const inventoryMatches = entries => JSON.stringify(entries) === JSON.stringify(expectedAcls);

export function brokerPolicyMatches(properties) {
  return properties['authorizer.class.name'] === 'org.apache.kafka.metadata.authorizer.StandardAuthorizer' &&
    properties['allow.everyone.if.no.acl.found'] === 'false' && properties['super.users'] === 'User:admin' &&
    properties['listener.security.protocol.map'] === 'CONTROLLER:SASL_PLAINTEXT,BROKER:SASL_PLAINTEXT' &&
    properties['sasl.enabled.mechanisms'] === 'PLAIN';
}

export function processSyntheticEvents(messages, tenant) {
  const seen = new Set();
  let rejected = 0;
  let duplicates = 0;
  for (const message of messages) {
    const event = JSON.parse(message);
    if (!event || Array.isArray(event) || Object.keys(event).sort().join('|') !== 'eventId|tenant' ||
      typeof event.eventId !== 'string' || !event.eventId.startsWith('synthetic-') || event.tenant !== tenant) {
      rejected++;
      continue;
    }
    if (seen.has(event.eventId)) duplicates++;
    else seen.add(event.eventId);
  }
  return { applied: seen.size, rejected, duplicates };
}
