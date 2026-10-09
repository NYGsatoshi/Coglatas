export function fixtureArguments(args) {
  let report = 'artifacts/sec-arch/kafka.json';
  let suppliedReport = false;
  let development = false;
  let candidateSha;
  for (let index = 0; index < args.length; index++) {
    const arg = args[index];
    if (arg === '--development' && !development) development = true;
    else if (arg === '--candidate-sha' && candidateSha === undefined && /^[a-f0-9]{40}$/.test(args[index + 1] ?? ''))
      candidateSha = args[++index];
    else if (!arg.startsWith('--') && !suppliedReport && arg.endsWith('.json')) {
      report = arg;
      suppliedReport = true;
    } else throw new Error('Fixture CLI arguments are invalid');
  }
  return { report, development, candidateSha };
}

export function syntheticClientPath(principal) {
  if (!['admin', 'alpha', 'beta', 'unauthorized', 'invalid'].includes(principal))
    throw new Error('Fixture client identity is invalid');
  return `/tmp/sec-arch-${principal}.properties`;
}

export function clientFileOwnerMatches(metadata, identity) {
  return /^[1-9][0-9]*:[0-9]+$/.test(identity) && metadata === `600:${identity}`;
}

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

export function denialObservation(result, exception) {
  if (!['SaslAuthenticationException', 'TopicAuthorizationException', 'AuthorizationException',
    'GroupAuthorizationException', 'ClusterAuthorizationException'].includes(exception))
    throw new Error('Unsupported synthetic denial class');
  const exitCode = Number.isInteger(result.code) ? result.code : null;
  const expectedExceptionObserved = typeof result.output === 'string' && result.output.includes(exception);
  return { expectedException: exception, exitCode, timedOut: result.timedOut === true || exitCode === 124,
    timeoutSource: result.timedOut === true ? 'OUTER_PROCESS' : exitCode === 124 ? 'INNER_CONTAINER' : null,
    expectedExceptionObserved,
    observedFailureClasses: ['ClassNotFoundException', 'NoClassDefFoundError', 'TimeoutException', 'ConfigException']
      .filter(name => typeof result.output === 'string' && result.output.includes(name)),
    qualifiedDenial: result.timedOut === false && [0, 1].includes(exitCode) && expectedExceptionObserved };
}

export function kafkaToolArguments(name, args) {
  const classes = { topics: 'org.apache.kafka.tools.TopicCommand', acls: 'org.apache.kafka.tools.AclCommand',
    'console-producer': 'org.apache.kafka.tools.ConsoleProducer',
    'console-consumer': 'org.apache.kafka.tools.consumer.ConsoleConsumer',
    configs: 'kafka.admin.ConfigCommand',
    'consumer-groups': 'org.apache.kafka.tools.consumer.group.ConsumerGroupCommand' };
  if (!Object.hasOwn(classes, name) || !Array.isArray(args) || args.some(value => typeof value !== 'string'))
    throw new Error('Unsupported pinned Kafka tool');
  return ['java', '-Xmx256m', '-Dlog4j.configurationFile=/opt/kafka/config/tools-log4j2.yaml',
    '-cp', '/opt/kafka/libs/*', classes[name], ...args];
}

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
