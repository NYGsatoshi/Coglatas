import { spawn } from 'node:child_process';
import { createHash, randomBytes, randomUUID } from 'node:crypto';
import { mkdtemp, writeFile, rm, mkdir } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { dirname, join, resolve, relative, isAbsolute } from 'node:path';
import { fileURLToPath } from 'node:url';
import { aclInventory, expectedAcls, inventoryMatches, brokerPolicyMatches, processSyntheticEvents, fixtureArguments, syntheticClientPath, clientFileOwnerMatches, denialObservation, kafkaToolArguments } from './sec-arch-kafka-acls.mjs';

// All broker and client traffic stays inside one network-disabled disposable container.
const image = 'apache/kafka@sha256:5cc2a2fd93fa2687b44015eee04fb2c3edd9e526bd64bf8bec5ff1e268772e0e';
let options;
try { options = fixtureArguments(process.argv.slice(2)); }
catch { console.error('Fixture CLI arguments are invalid'); process.exit(2); }
const { development, candidateSha } = options;
const reportPath = resolve(options.report);
const outputRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../../artifacts/sec-arch');
const outputRelative = relative(outputRoot, reportPath);
if (outputRelative.startsWith('..') || isAbsolute(outputRelative) || outputRelative === '') {
  console.error('Fixture report must be a JSON artifact inside artifacts/sec-arch'); process.exit(2);
}
const container = `coglatas-sec-arch-kafka-${randomUUID().replaceAll('-', '')}`;
const cases = [];
const denialObservations = [];
let temporary;
let created = false;
let revision;
let candidateVerified = false;
let environmentFingerprint = null;
let clientFileOwnershipVerified = false;

async function command(executable, commandArgs, input = '', timeoutMs = 45000) {
  return await new Promise((resolveResult, reject) => {
    const child = spawn(executable, commandArgs, { windowsHide: true, stdio: ['pipe', 'pipe', 'pipe'] });
    let output = '';
    let timedOut = false;
    const timer = setTimeout(() => { timedOut = true; child.kill('SIGKILL'); }, timeoutMs);
    const collect = data => { if (output.length < 1000000) output += data.toString(); };
    child.stdout.on('data', collect);
    child.stderr.on('data', collect);
    child.on('error', error => { clearTimeout(timer); reject(new Error(`Fixture executable unavailable: ${error.code}`)); });
    child.on('close', code => { clearTimeout(timer); resolveResult({ code, output, timedOut }); });
    child.stdin.on('error', () => {});
    child.stdin.end(input);
  });
}

const docker = (a, input, timeout) => command('docker', a, input, timeout);
const cli = (name, a, input, timeout = 45000) => docker(['exec', '-i', container, 'timeout',
  String(Math.floor(timeout / 1000) - 2), ...kafkaToolArguments(name, a)], input, timeout);
const broker = ['--bootstrap-server', '127.0.0.1:9092'];
const admin = [...broker, '--command-config', '/tmp/sec-arch-admin.properties'];

async function installClientConfiguration(principal, configuration, identity) {
  const path = syntheticClientPath(principal);
  // docker cp assigns root ownership on Linux. Create 0600 files as the same
  // non-root container user that executes the Kafka CLI, without printing contents.
  requireResult(await docker(['exec', '-i', container, '/bin/sh', '-c', `umask 077; set -C; cat > ${path}`], configuration),
    'private synthetic client configuration');
  const metadata = requireResult(await docker(['exec', container, 'stat', '-c', '%a:%u:%g', path]), 'client file ownership').trim();
  if (!clientFileOwnerMatches(metadata, identity)) throw new Error('Fixture client file ownership mismatch');
}

function requireResult(result, label) {
  if (result.code !== 0 || result.timedOut) throw new Error(`Fixture command failed: ${label}`);
  return result.output;
}

function record(id, passed, positiveControls = 1, negativeControls = 1) {
  cases.push({ verifierId: id, outcome: passed ? 'PASS' : 'FAIL', positiveControls, negativeControls });
  console.log(`${id}: ${passed ? 'PASS' : 'FAIL'}`);
  if (!passed) throw new Error(`Fixture assertion failed: ${id}`);
}

async function acl(a) { return requireResult(await cli('acls', [...admin, ...a]), 'ACL administration'); }
async function add(principal, topic, operations, group = false, pattern = 'literal') {
  return acl(['--add', '--allow-principal', `User:${principal}`, '--allow-host', '127.0.0.1',
    ...operations.flatMap(o => ['--operation', o]), group ? '--group' : '--topic', topic, '--resource-pattern-type', pattern]);
}
async function remove(principal, topic, operations, group = false, pattern = 'literal') {
  return acl(['--remove', '--force', '--allow-principal', `User:${principal}`, '--allow-host', '127.0.0.1',
    ...operations.flatMap(o => ['--operation', o]), group ? '--group' : '--topic', topic, '--resource-pattern-type', pattern]);
}
async function produce(principal, topic, message, timeout = 25000) {
  return cli('console-producer', [...broker, '--producer.config', `/tmp/sec-arch-${principal}.properties`, '--topic', topic,
    '--sync', '--producer-property', 'max.block.ms=5000', '--producer-property', 'request.timeout.ms=3000',
    '--producer-property', 'delivery.timeout.ms=6000', '--producer-property', 'retries=0'], `${message}\n`, timeout);
}
async function consume(principal, topic, group, maximum = 1) {
  return cli('console-consumer', [...broker, '--consumer.config', `/tmp/sec-arch-${principal}.properties`,
    '--topic', topic, '--group', group, '--from-beginning', '--max-messages', String(maximum), '--timeout-ms', '10000'], '', 25000);
}
const denied = (result, exception) => {
  const observation = denialObservation(result, exception);
  denialObservations.push(observation);
  return observation.qualifiedDenial;
};

try {
  const git = await command('git', ['rev-parse', 'HEAD']);
  if (resolve('scripts/security/run-sec-arch-kafka.mjs') !== fileURLToPath(import.meta.url))
    throw new Error('Fixture must run from its repository root');
  revision = requireResult(git, 'candidate revision').trim();
  if (!/^[a-f0-9]{40}$/.test(revision)) throw new Error('Exact candidate revision required');
  if (candidateSha !== undefined && candidateSha !== revision) throw new Error('Exact candidate revision required');
  const status = requireResult(await command('git', ['status', '--porcelain']), 'candidate worktree');
  candidateVerified = status.trim() === '';
  if (!development) requireResult(await command('git', ['ls-files', '--error-unmatch', 'scripts/security/run-sec-arch-kafka.mjs']), 'tracked fixture source');
  if (!candidateVerified && !development) throw new Error('Dirty worktree cannot produce candidate-bound evidence');
  const dockerVersion = requireResult(await docker(['version', '--format', '{{.Server.Version}}|{{.Server.Os}}|{{.Server.Arch}}']), 'Docker runtime fingerprint').trim();
  environmentFingerprint = createHash('sha256').update(`${image}|${dockerVersion}|${process.version}|${process.platform}|${process.arch}|network=none|client-files=owner0600|launcher=pinned-java-tools|fixture=v3`).digest('hex');
  temporary = await mkdtemp(join(tmpdir(), 'coglatas-sec-arch-kafka-'));
  const passwords = Object.fromEntries(['admin', 'alpha', 'beta', 'unauthorized'].map(p => [p, randomBytes(24).toString('hex')]));
  const jaas = `org.apache.kafka.common.security.plain.PlainLoginModule required username="admin" password="${passwords.admin}" ` +
    Object.entries(passwords).map(([p, password]) => `user_${p}="${password}"`).join(' ') + ';';
  const environment = {
    CLUSTER_ID: 'MkU3OEVBNTcwNTJENDM2Qk', KAFKA_NODE_ID: '1', KAFKA_PROCESS_ROLES: 'broker,controller',
    KAFKA_LISTENERS: 'BROKER://127.0.0.1:9092,CONTROLLER://127.0.0.1:9093',
    KAFKA_ADVERTISED_LISTENERS: 'BROKER://127.0.0.1:9092',
    KAFKA_CONTROLLER_LISTENER_NAMES: 'CONTROLLER',
    KAFKA_LISTENER_SECURITY_PROTOCOL_MAP: 'CONTROLLER:SASL_PLAINTEXT,BROKER:SASL_PLAINTEXT',
    KAFKA_CONTROLLER_QUORUM_VOTERS: '1@127.0.0.1:9093',
    KAFKA_INTER_BROKER_LISTENER_NAME: 'BROKER', KAFKA_SASL_MECHANISM_INTER_BROKER_PROTOCOL: 'PLAIN',
    KAFKA_SASL_MECHANISM_CONTROLLER_PROTOCOL: 'PLAIN', KAFKA_SASL_ENABLED_MECHANISMS: 'PLAIN',
    KAFKA_LISTENER_NAME_BROKER_PLAIN_SASL_JAAS_CONFIG: jaas,
    KAFKA_LISTENER_NAME_CONTROLLER_PLAIN_SASL_JAAS_CONFIG: jaas,
    KAFKA_AUTHORIZER_CLASS_NAME: 'org.apache.kafka.metadata.authorizer.StandardAuthorizer',
    KAFKA_SUPER_USERS: 'User:admin', KAFKA_ALLOW_EVERYONE_IF_NO_ACL_FOUND: 'false',
    KAFKA_AUTO_CREATE_TOPICS_ENABLE: 'false', KAFKA_OFFSETS_TOPIC_REPLICATION_FACTOR: '1',
    KAFKA_TRANSACTION_STATE_LOG_REPLICATION_FACTOR: '1', KAFKA_TRANSACTION_STATE_LOG_MIN_ISR: '1',
    KAFKA_GROUP_INITIAL_REBALANCE_DELAY_MS: '0'
  };
  const envFile = join(temporary, 'broker.env');
  await writeFile(envFile, Object.entries(environment).map(([key, value]) => `${key}=${value}`).join('\n'), { mode: 0o600 });
  requireResult(await docker(['run', '-d', '--network', 'none', '--label', 'coglatas.fixture=sec-arch-synthetic',
    '--name', container, '--env-file', envFile, image], '', 120000), 'isolated broker startup');
  created = true;
  const isolation = requireResult(await docker(['inspect', '--format', '{{.HostConfig.NetworkMode}}|{{json .HostConfig.PortBindings}}', container]), 'network isolation');
  if (!/^none\|(null|\{\})\s*$/.test(isolation)) throw new Error('Fixture network isolation failed');
  const uid = requireResult(await docker(['exec', container, 'id', '-u']), 'fixture client user').trim();
  const gid = requireResult(await docker(['exec', container, 'id', '-g']), 'fixture client group').trim();
  const identity = `${uid}:${gid}`;
  for (const [principal, password] of Object.entries(passwords)) {
    await installClientConfiguration(principal,
      `security.protocol=SASL_PLAINTEXT\nsasl.mechanism=PLAIN\nsasl.jaas.config=org.apache.kafka.common.security.plain.PlainLoginModule required username="${principal}" password="${password}";\nrequest.timeout.ms=5000\ndefault.api.timeout.ms=10000\n`, identity);
  }
  await installClientConfiguration('invalid',
    `security.protocol=SASL_PLAINTEXT\nsasl.mechanism=PLAIN\nsasl.jaas.config=org.apache.kafka.common.security.plain.PlainLoginModule required username="alpha" password="${randomBytes(24).toString('hex')}";\nrequest.timeout.ms=3000\ndefault.api.timeout.ms=5000\n`, identity);
  clientFileOwnershipVerified = true;
  let ready = false;
  for (let attempt = 0; attempt < 18; attempt++) {
    const result = await cli('topics', [...admin, '--list'], '', 15000);
    if (result.code === 0 && !result.timedOut) { ready = true; break; }
    await new Promise(r => setTimeout(r, 1000));
  }
  if (!ready) throw new Error('Authenticated broker readiness failed');
  const effective = requireResult(await docker(['exec', container, 'cat', '/opt/kafka/config/server.properties']), 'effective fixture configuration');
  const safeProperties = {};
  for (const line of effective.split(/\r?\n/)) {
    const separator = line.indexOf('=');
    const key = line.slice(0, separator);
    if (['authorizer.class.name', 'allow.everyone.if.no.acl.found', 'super.users',
      'listener.security.protocol.map', 'sasl.enabled.mechanisms'].includes(key)) {
      if (Object.hasOwn(safeProperties, key)) throw new Error('Fixture duplicate security configuration');
      safeProperties[key] = line.slice(separator + 1);
    }
  }
  if (!brokerPolicyMatches(safeProperties)) throw new Error('Fixture effective broker policy mismatch');
  for (const topic of ['sec-arch-alpha', 'sec-arch-beta', 'sec-arch-no-acl'])
    requireResult(await cli('topics', [...admin, '--create', '--topic', topic, '--partitions', '1', '--replication-factor', '1']), 'synthetic topic creation');
  await add('alpha', 'sec-arch-alpha', ['Read', 'Write', 'Describe', 'DescribeConfigs']);
  await add('alpha', 'sec-arch-alpha-group', ['Read'], true);
  await add('beta', 'sec-arch-beta', ['Read', 'Write', 'Describe', 'DescribeConfigs']);
  await add('beta', 'sec-arch-beta-group', ['Read'], true);
  const baseline = aclInventory(await acl(['--list']));
  if (!inventoryMatches(baseline)) throw new Error('Incomplete ACL inventory');
  requireResult(await produce('alpha', 'sec-arch-alpha', 'synthetic-alpha-control'), 'Alpha producer control');
  requireResult(await produce('beta', 'sec-arch-beta', 'synthetic-beta-control'), 'Beta producer control');
  const alpha = await consume('alpha', 'sec-arch-alpha', 'sec-arch-alpha-group');
  const beta = await consume('beta', 'sec-arch-beta', 'sec-arch-beta-group');
  record('SEC-ARCH-KAFKA-POSITIVE-IDENTITIES', alpha.code === 0 && alpha.output.includes('synthetic-alpha-control') &&
    beta.code === 0 && beta.output.includes('synthetic-beta-control'), 2, 0);
  // Exercise the same bounded write as the valid Alpha positive. TopicCommand's
  // Admin.close() can outlive the failed request; harness timeout is never denial evidence.
  const authentication = await produce('invalid', 'sec-arch-alpha', 'synthetic-authentication-denied', 15000);
  record('SEC-ARCH-KAFKA-AUTHENTICATION', denied(authentication, 'SaslAuthenticationException'));
  record('SEC-ARCH-KAFKA-CROSS-TENANT-WRITE', denied(await produce('alpha', 'sec-arch-beta', 'synthetic-denied'), 'TopicAuthorizationException'));
  record('SEC-ARCH-KAFKA-CROSS-TENANT-READ', denied(await consume('alpha', 'sec-arch-beta', 'sec-arch-alpha-group'), 'TopicAuthorizationException'));
  record('SEC-ARCH-KAFKA-UNAUTHORIZED-PRODUCER', denied(await produce('unauthorized', 'sec-arch-alpha', 'synthetic-denied'), 'TopicAuthorizationException'));
  record('SEC-ARCH-KAFKA-UNAUTHORIZED-CONSUMER', denied(await consume('unauthorized', 'sec-arch-alpha', 'sec-arch-alpha-group'), 'AuthorizationException'));
  record('SEC-ARCH-KAFKA-GROUP-JOIN', denied(await consume('alpha', 'sec-arch-alpha', 'sec-arch-beta-group'), 'GroupAuthorizationException'));
  record('SEC-ARCH-KAFKA-NO-ACL-FAIL-CLOSED', denied(await produce('alpha', 'sec-arch-no-acl', 'synthetic-denied'), 'TopicAuthorizationException'));
  const describe = await cli('topics', [...broker, '--command-config', '/tmp/sec-arch-alpha.properties', '--describe', '--topic', 'sec-arch-beta']);
  const ownDescribe = await cli('topics', [...broker, '--command-config', '/tmp/sec-arch-alpha.properties', '--describe', '--topic', 'sec-arch-alpha']);
  const adminDescribe = await cli('topics', [...admin, '--describe', '--topic', 'sec-arch-beta']);
  const describeObservation = {
    ownExit: ownDescribe.code, ownNamed: ownDescribe.output.includes('sec-arch-alpha'),
    adminExit: adminDescribe.code, adminNamed: adminDescribe.output.includes('sec-arch-beta'),
    deniedExit: describe.code, deniedTimedOut: describe.timedOut,
    authorizationError: describe.output.includes('TopicAuthorizationException'),
    maskedAbsent: describe.output.includes('sec-arch-beta') && describe.output.includes('does not exist')
  };
  console.log(`SEC-ARCH-KAFKA-DESCRIBE-OBSERVATION: ${JSON.stringify(describeObservation)}`);
  // TopicCommand may mask a non-describable topic as absent. Both live controls must first prove
  // the caller can describe its own topic and the same broker exposes Beta to fixture admin.
  record('SEC-ARCH-KAFKA-DESCRIBE-BOUNDARY', ownDescribe.code === 0 && ownDescribe.output.includes('sec-arch-alpha') &&
    adminDescribe.code === 0 && adminDescribe.output.includes('sec-arch-beta') && !describe.timedOut &&
    (denied(describe, 'TopicAuthorizationException') ||
      (describe.code !== 0 && describe.output.includes("Topic 'sec-arch-beta' does not exist as expected"))));
  const cluster = await cli('configs', [...broker, '--command-config', '/tmp/sec-arch-alpha.properties', '--entity-type', 'brokers', '--entity-default', '--describe']);
  record('SEC-ARCH-KAFKA-CLUSTER-BOUNDARY', denied(cluster, 'ClusterAuthorizationException'));

  await add('alpha', 'sec-arch-beta', ['Read']);
  record('SEC-ARCH-KAFKA-EXCESS-ACL-MUTATION', !inventoryMatches(aclInventory(await acl(['--list']))));
  await remove('alpha', 'sec-arch-beta', ['Read']);
  await add('alpha', 'sec-arch-', ['Read'], false, 'prefixed');
  record('SEC-ARCH-KAFKA-PATTERN-MUTATION', !inventoryMatches(aclInventory(await acl(['--list']))));
  await remove('alpha', 'sec-arch-', ['Read'], false, 'prefixed');
  await remove('alpha', 'sec-arch-alpha', ['Write']);
  record('SEC-ARCH-KAFKA-MISSING-PRIVILEGE', !inventoryMatches(aclInventory(await acl(['--list']))) &&
    denied(await produce('alpha', 'sec-arch-alpha', 'synthetic-denied'), 'TopicAuthorizationException'));
  await add('alpha', 'sec-arch-alpha', ['Write']);
  record('SEC-ARCH-KAFKA-ACL-RESTORED', inventoryMatches(aclInventory(await acl(['--list']))), 1, 0);

  const replay = JSON.stringify({ eventId: 'synthetic-replay', tenant: 'alpha' });
  const foreign = JSON.stringify({ eventId: 'synthetic-foreign', tenant: 'beta' });
  for (const message of [replay, replay, foreign])
    requireResult(await produce('alpha', 'sec-arch-alpha', message), 'synthetic event producer control');
  requireResult(await cli('consumer-groups', [...admin, '--group', 'sec-arch-alpha-group', '--topic', 'sec-arch-alpha',
    '--reset-offsets', '--to-earliest', '--execute']), 'inactive synthetic consumer offset reset');
  const events = await consume('alpha', 'sec-arch-alpha', 'sec-arch-alpha-group', 4);
  const messages = events.output.split(/\r?\n/).filter(line => line.startsWith('{'));
  const processed = processSyntheticEvents(messages, 'alpha');
  record('SEC-ARCH-KAFKA-REPLAY-TENANT-PAYLOAD', events.code === 0 && !events.timedOut && messages.length === 3 &&
    processed.applied === 1 && processed.rejected === 1 && processed.duplicates === 1);

  // A fixture administrator intentionally bypasses ACLs; never count it as a tenant principal.
  requireResult(await produce('admin', 'sec-arch-no-acl', 'synthetic-admin-bypass-control'), 'superuser risk control');
  record('SEC-ARCH-KAFKA-SUPERUSER-RISK', brokerPolicyMatches(safeProperties) && !expectedAcls.some(e => e.includes('User:admin')));
} catch (error) {
  // Never print broker configuration, command outputs, file paths or synthetic credentials.
  cases.push({ verifierId: 'SEC-ARCH-KAFKA-HARNESS', outcome: 'ERROR', reason: 'Fixture setup or execution failed; inspect locally without publishing raw broker logs.' });
  console.error(error.message.startsWith('Fixture') || error.message.startsWith('Dirty') || error.message.startsWith('Incomplete') ||
    ['Unclassified ACL entry', 'Authenticated broker readiness failed', 'Exact candidate revision required'].includes(error.message)
    ? error.message : 'Kafka fixture failed');
} finally {
  if (created) {
    const label = await docker(['inspect', '--format', '{{index .Config.Labels "coglatas.fixture"}}', container]);
    if (label.code === 0 && label.output.trim() === 'sec-arch-synthetic') {
      const cleanup = await docker(['rm', '-f', '-v', container]);
      if (cleanup.code !== 0) cases.push({ verifierId: 'SEC-ARCH-KAFKA-CLEANUP', outcome: 'ERROR' });
    } else cases.push({ verifierId: 'SEC-ARCH-KAFKA-CLEANUP', outcome: 'ERROR' });
  }
  if (temporary) {
    const prefix = resolve(tmpdir()) + '/';
    const normalized = resolve(temporary).replaceAll('\\', '/');
    if (normalized.startsWith(prefix.replaceAll('\\', '/')) && normalized.split('/').at(-1).startsWith('coglatas-sec-arch-kafka-'))
      await rm(temporary, { recursive: true, force: true });
  }
  const finalRevision = await command('git', ['rev-parse', 'HEAD']);
  const finalStatus = await command('git', ['status', '--porcelain']);
  if (finalRevision.code !== 0 || finalStatus.code !== 0 || finalRevision.output.trim() !== revision || finalStatus.output.trim() !== '')
    candidateVerified = false;
  const runtimePassed = cases.length === 16 && cases.every(c => c.outcome === 'PASS');
  const report = {
    schemaVersion: 1, verifierId: 'SEC-ARCH-KAFKA-ISOLATED', verifierVersion: '3', candidateSha: revision ?? null,
    candidateVerified, environmentFingerprint, clientFileOwnershipVerified,
    imageDigest: image, executedAtUtc: new Date().toISOString(), executionScope: 'ISOLATED_SYNTHETIC_ONLY',
    productActivation: 'INACTIVE_CONDITIONAL', ownerApproval: null,
    outcome: runtimePassed ? candidateVerified ? 'PASS' : 'UNVERIFIED' : 'ERROR', cases, denialObservations,
    blindSpots: ['Synthetic broker ACL checks do not certify product Kafka activation/compliance.',
      'Synthetic replay/tenant payload checks do not establish product Outbox/consumer integration or network-policy enforcement.',
      'Report provenance requires trusted CI digest reconciliation; a local report is not server attestation.']
  };
  await mkdir(dirname(reportPath), { recursive: true });
  await writeFile(reportPath, JSON.stringify(report, null, 2) + '\n', { flag: 'wx' });
  console.log(`SEC-ARCH isolated Kafka: ${report.outcome}`);
  process.exitCode = runtimePassed && (candidateVerified || development) ? 0 : 1;
}
