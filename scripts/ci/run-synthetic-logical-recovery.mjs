import { spawnSync } from 'node:child_process';
import { createHash, randomBytes } from 'node:crypto';
import { existsSync, lstatSync, mkdirSync, mkdtempSync, readFileSync, realpathSync, writeFileSync, chmodSync } from 'node:fs';
import { dirname, isAbsolute, join, relative, resolve, sep, posix } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const ROOT = realpathSync(resolve(dirname(fileURLToPath(import.meta.url)), '../..'));
const DOCKER = '/usr/bin/docker', GIT = '/usr/bin/git', SOCKET = '/var/run/docker.sock';
const NODE_IMAGE = 'node@sha256:e7add2a490bb17d97b485b8641bd88455fff7839ade0f5eba493c3508124d413';
const SDK_IMAGE = 'mcr.microsoft.com/dotnet/sdk:10.0.403@sha256:afd0ccff9d306812a69ef0f47458376b6acb544ae866cfc4b8d28ff0eef35ea7';
const BASE = 'infra/compose/test/real-backend-smoke.yml';
const OVERLAY = 'infra/compose/test/acceptance/logical-recovery-source.yml';
const TARGET = 'infra/compose/test/logical-recovery-target.yml';
const MAX_BYTES = 128 * 1024 * 1024;
const sha256 = value => createHash('sha256').update(value).digest('hex');
const reject = code => { const error = new Error('Synthetic logical recovery failed.'); error.code = code; throw error; };
const require = (condition, code) => { if (!condition) reject(code); };

export function hostedContract(env, platform = process.platform, args = process.argv.slice(2)) {
  require(platform === 'linux' && args.length === 0 && env.GITHUB_ACTIONS === 'true' &&
    env.GITHUB_REPOSITORY === 'NYGsatoshi/Coglatas' && env.GITHUB_REPOSITORY_ID === '1261244608' &&
    env.GITHUB_ACTOR === 'NYGsatoshi' && env.GITHUB_EVENT_NAME === 'workflow_dispatch' &&
    env.COGLATAS_LOGICAL_RECOVERY_SELECTED === '1' && env.GITHUB_RUN_ATTEMPT === '1' &&
    /^[1-9][0-9]{0,19}$/.test(env.GITHUB_RUN_ID ?? '') && /^[a-f0-9]{40}$/.test(env.GITHUB_SHA ?? '') &&
    /^NYGsatoshi\/Coglatas\/\.github\/workflows\/licensed-real-backend-acceptance\.yml@refs\/heads\/[A-Za-z0-9_./-]+$/.test(env.GITHUB_WORKFLOW_REF ?? ''), 'HOST_CONTRACT');
  require((!env.DOCKER_HOST || env.DOCKER_HOST === 'unix://' + SOCKET) &&
    (!env.DOCKER_CONTEXT || env.DOCKER_CONTEXT === 'default') && !env.DOCKER_TLS_VERIFY && !env.DOCKER_CERT_PATH, 'LOCAL_DOCKER');
  return { runId: env.GITHUB_RUN_ID, attempt: 1, sourceSha: env.GITHUB_SHA };
}

export function resourceNames(runId, nonce) {
  require(/^[1-9][0-9]{0,19}$/.test(runId) && /^[a-f0-9]{12}$/.test(nonce), 'RESOURCE_ID');
  const prefix = `coglatas-recovery-${runId}-1-${nonce}`;
  const names = { source: prefix + '-source', target: prefix + '-target', fault: prefix + '-fault' };
  require(Object.values(names).every(name => name.length <= 63), 'RESOURCE_ID');
  return names;
}

export function beneath(root, path) {
  const part = relative(root, path);
  require(part && part !== '..' && !part.startsWith('..' + sep) && !isAbsolute(part), 'PRIVATE_PATH');
  return path;
}

export function validateComposition(config, { target, image, project }) {
  const services = config?.services;
  require(services && typeof services === 'object' && services.app && services.postgres, 'COMPOSE');
  if (target) require(JSON.stringify(Object.keys(services).sort()) === JSON.stringify(['app', 'postgres']), 'TARGET_SERVICES');
  if (!target) require(JSON.stringify(Object.keys(services).sort()) ===
    JSON.stringify(['app', 'migrate', 'postgres']), 'SOURCE_SERVICES');
  // The inherited browser is explicitly profiled out and is never selected by
  // this orchestrator. Only these three source services can execute.
  for (const service of target ? Object.values(services) : ['app', 'postgres', 'migrate'].map(key => services[key])) {
    require(!service.ports?.length && !service.privileged && !service.network_mode && !service.pid && !service.ipc &&
      !service.container_name && !service.volumes_from && !service.devices?.length && !service.cap_add?.length, 'COMPOSE_SCOPE');
  }
  require(Object.entries(config.networks ?? {}).length === 1 &&
    Object.entries(config.networks).every(([key, network]) => key === 'default' && network.name === project + '_default' &&
      network.internal === true && !network.external && !network.driver_opts && (!network.driver || network.driver === 'bridge')), 'COMPOSE_NETWORK');
  require(Object.entries(config.volumes ?? {}).every(([key, volume]) => volume?.name === project + '_' + key &&
    !volume.external && !volume.driver_opts && (!volume.driver || volume.driver === 'local')), 'COMPOSE_VOLUME');
  const app = services.app, settings = app.environment ?? {};
  require(app.volumes?.length === 2 && app.volumes.every(mount => mount.type === 'volume') &&
    new Set(app.volumes.map(mount => mount.target)).size === 2 &&
    app.volumes.every(mount => ['/app/storage/uploads', '/app/data/protection-keys'].includes(mount.target)), 'APP_MOUNT');
  require(settings.ASPNETCORE_ENVIRONMENT === 'Test' && settings.Tenancy__AppMode === 'SaaS' &&
    settings.Tenancy__TenantResolutionStrategy === 'HeaderForDevelopmentOnly' &&
    String(settings.Tenancy__AllowDevelopmentHeaderTenantResolution) === 'true' &&
    String(settings.Security__EnableCsrfProtection) === 'true', 'TEST_PROFILE');
  for (const key of ['Tenancy__SeedOnStartup', 'UiShell__SeedOnStartup', 'COGLATAS_SEED_ADMIN_ENABLED',
    'COGLATAS_BROWSER_SMOKE_SEED_ENABLED', 'COGLATAS_DEMO_DATASET_ENABLED'])
    require(String(settings[key]) === 'false', 'SEED_PROFILE');
  require(!settings.COGLATAS_BOOTSTRAP_ADMIN_EMAIL && !settings.BootstrapAdmin__Email, 'SEED_PROFILE');
  if (target) {
    require(!app.build && !services.postgres.build && app.image === image &&
      /^sha256:[a-f0-9]{64}$/.test(services.postgres.image ?? '') &&
      String(settings.SecurityCiFixture__Enabled) === 'false' && String(settings.LocalAdmin__SeedOnStartup) === 'false', 'TARGET_PROFILE');
    require(Object.values(services).flatMap(service => service.volumes ?? []).every(mount => mount.type === 'volume'), 'TARGET_MOUNT');
  } else {
    require(app.build?.context === ROOT && app.build.dockerfile === 'Dockerfile' &&
      ['true', 'false'].includes(String(settings.SecurityCiFixture__Enabled)), 'SOURCE_BUILD');
  }
  return true;
}

export function assertDisjoint(source, target) {
  require(source.length === 3 && target.length === 3 && new Set([...source, ...target]).size === 6, 'VOLUME_ALIAS');
}

export function assertSameSnapshot(expected, actual, code = 'STRICT_SNAPSHOT') {
  const exactNumbers = value => {
    if (typeof value === 'number') require(Number.isSafeInteger(value), 'SNAPSHOT_NUMBER_PRECISION');
    else if (value && typeof value === 'object') for (const child of Object.values(value)) exactNumbers(child);
  };
  exactNumbers(expected); exactNumbers(actual);
  require(JSON.stringify(expected) === JSON.stringify(actual), code);
}

export function corruptCopy(original) {
  require(Buffer.isBuffer(original) && original.length > 512 && original.subarray(0, 5).equals(Buffer.from('PGDMP')), 'DUMP_FORMAT');
  return Buffer.from(original.subarray(0, 128));
}

export function assertTruncatedArchiveFailure(result) {
  // A failed Docker exec is not evidence that pg_restore read the archive.
  // Match the native client failure produced by this bounded truncation only.
  require(result.status === 1 && !result.signal && !result.error &&
    Buffer.isBuffer(result.stderr) &&
    /^pg_restore: error: could not read from input file: end of file\r?\n$/.test(result.stderr.toString('utf8')),
  'FAULT_NOT_ARCHIVE_REJECTION');
}

export async function awaitOutboxIdle(read, pause = () => new Promise(done => setTimeout(done, 1000))) {
  for (let sample = 0; sample < 30; sample++) {
    const count = read(); require(/^[0-9]+$/.test(count), 'OUTBOX_SCHEMA');
    if (count === '0') return;
    await pause();
  }
  reject('OUTBOX_NOT_IDLE');
}

export function validateTar(bytes) {
  require(Buffer.isBuffer(bytes) && bytes.length <= MAX_BYTES && bytes.length % 512 === 0, 'TAR');
  const paths = new Set(); let position = 0, files = 0, expanded = 0;
  const text = part => part.toString('utf8').split('\0', 1)[0];
  const octal = part => { const value = text(part).trim(); require(/^[0-7]+$/.test(value), 'TAR'); return parseInt(value, 8); };
  while (position + 512 <= bytes.length) {
    const header = bytes.subarray(position, position + 512);
    if (header.every(byte => byte === 0)) {
      require(bytes.length - position >= 1024 && bytes.subarray(position).every(byte => byte === 0), 'TAR'); return { files, expanded };
    }
    const checksum = [...header].reduce((sum, byte, index) => sum + (index >= 148 && index < 156 ? 32 : byte), 0);
    require(checksum === octal(header.subarray(148, 156)), 'TAR');
    const prefix = text(header.subarray(345, 500));
    const name = (prefix ? prefix + '/' : '') + text(header.subarray(0, 100));
    const type = header[156], directory = type === 53;
    require([0, 48, 53].includes(type) && !posix.isAbsolute(name) && !name.includes('\\'), 'TAR_PATH');
    let path = name.replace(/^\.\//, ''); if (directory) path = path.replace(/\/$/, '');
    require((!path && directory) || path.split('/').every(part => part && part !== '.' && part !== '..'), 'TAR_PATH');
    require(!paths.has(path), 'TAR_PATH'); paths.add(path);
    const size = octal(header.subarray(124, 136));
    require(size <= 16 * 1024 * 1024 && (!directory || size === 0) && ++files <= 2048, 'TAR');
    expanded += size; require(expanded <= 64 * 1024 * 1024, 'TAR');
    position += 512 + Math.ceil(size / 512) * 512; require(position <= bytes.length, 'TAR');
  }
  reject('TAR');
}

const ROWS_SQL = String.raw`
SET timezone='UTC'; SET datestyle='ISO,YMD';
BEGIN ISOLATION LEVEL REPEATABLE READ;
CREATE TEMP TABLE recovery_rows(schema_name text,table_name text,row_count bigint,data_digest text);
DO $capture$ DECLARE item record; value_count bigint; value_digest text; BEGIN
 FOR item IN SELECT n.nspname,c.relname FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
 WHERE n.nspname NOT LIKE 'pg_%' AND n.nspname<>'information_schema' AND c.relkind IN ('r','p')
 ORDER BY n.nspname COLLATE "C",c.relname COLLATE "C" LOOP
 EXECUTE format('LOCK TABLE %I.%I IN SHARE MODE',item.nspname,item.relname);
 EXECUTE format('SELECT count(*),encode(sha256(convert_to(coalesce(jsonb_agg(to_jsonb(r) ORDER BY to_jsonb(r)::text COLLATE "C")::text,''[]''),''UTF8'')),''hex'') FROM %I.%I r',item.nspname,item.relname)
 INTO value_count,value_digest;
 INSERT INTO recovery_rows VALUES(item.nspname,item.relname,value_count,value_digest);
 END LOOP; END $capture$;
SELECT coalesce(json_agg(json_build_array(schema_name,table_name,row_count,data_digest) ORDER BY schema_name COLLATE "C",table_name COLLATE "C"),'[]') FROM recovery_rows;
COMMIT;`;

const CATALOG_SQL = String.raw`
SELECT json_build_object(
 'relations',(SELECT coalesce(json_agg(json_build_array(n.nspname,c.relname,c.relkind,c.relrowsecurity,c.relforcerowsecurity,c.relreplident,c.reloptions,c.relacl::text,pg_get_userbyid(c.relowner)) ORDER BY n.nspname COLLATE "C",c.relname COLLATE "C"),'[]')
 FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname NOT LIKE 'pg_%' AND n.nspname<>'information_schema'),
 'constraints',(SELECT coalesce(json_agg(json_build_array(n.nspname,r.relname,c.conname,c.contype,c.convalidated,c.condeferrable,c.condeferred,pg_get_constraintdef(c.oid,false)) ORDER BY n.nspname COLLATE "C",r.relname COLLATE "C",c.conname COLLATE "C"),'[]')
 FROM pg_constraint c JOIN pg_class r ON r.oid=c.conrelid JOIN pg_namespace n ON n.oid=r.relnamespace WHERE n.nspname NOT LIKE 'pg_%' AND n.nspname<>'information_schema'),
 'indexes',(SELECT coalesce(json_agg(json_build_array(n.nspname,r.relname,c.relname,i.indisunique,i.indisvalid,i.indisready,pg_get_indexdef(i.indexrelid)) ORDER BY n.nspname COLLATE "C",r.relname COLLATE "C",c.relname COLLATE "C"),'[]')
 FROM pg_index i JOIN pg_class c ON c.oid=i.indexrelid JOIN pg_class r ON r.oid=i.indrelid JOIN pg_namespace n ON n.oid=r.relnamespace WHERE n.nspname NOT LIKE 'pg_%' AND n.nspname<>'information_schema'),
 'triggers',(SELECT coalesce(json_agg(json_build_array(n.nspname,c.relname,t.tgname,t.tgenabled,t.tgisinternal,pg_get_triggerdef(t.oid,false)) ORDER BY n.nspname COLLATE "C",c.relname COLLATE "C",t.tgname COLLATE "C"),'[]')
 FROM pg_trigger t JOIN pg_class c ON c.oid=t.tgrelid JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname NOT LIKE 'pg_%' AND n.nspname<>'information_schema'),
 'functions',(SELECT coalesce(json_agg(json_build_array(n.nspname,p.proname,pg_get_function_identity_arguments(p.oid),p.prosecdef,p.proconfig,p.proacl::text,pg_get_functiondef(p.oid)) ORDER BY n.nspname COLLATE "C",p.proname COLLATE "C",pg_get_function_identity_arguments(p.oid) COLLATE "C"),'[]')
 FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace WHERE n.nspname NOT LIKE 'pg_%' AND n.nspname<>'information_schema' AND p.prokind IN ('f','p')),
 'policies',(SELECT coalesce(json_agg(json_build_array(n.nspname,c.relname,p.polname,p.polcmd,p.polpermissive,pg_get_expr(p.polqual,p.polrelid),pg_get_expr(p.polwithcheck,p.polrelid)) ORDER BY n.nspname COLLATE "C",c.relname COLLATE "C",p.polname COLLATE "C"),'[]')
 FROM pg_policy p JOIN pg_class c ON c.oid=p.polrelid JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname NOT LIKE 'pg_%' AND n.nspname<>'information_schema'),
 'schemas',(SELECT coalesce(json_agg(json_build_array(nspname,pg_get_userbyid(nspowner),nspacl::text) ORDER BY nspname COLLATE "C"),'[]') FROM pg_namespace WHERE nspname NOT LIKE 'pg_%' AND nspname<>'information_schema'),
 'defaults',(SELECT coalesce(json_agg(json_build_array(pg_get_userbyid(d.defaclrole),n.nspname,d.defaclobjtype,d.defaclacl::text) ORDER BY pg_get_userbyid(d.defaclrole) COLLATE "C",n.nspname COLLATE "C",d.defaclobjtype),'[]') FROM pg_default_acl d LEFT JOIN pg_namespace n ON n.oid=d.defaclnamespace),
 'roles',(SELECT coalesce(json_agg(json_build_array(rolname,rolsuper,rolinherit,rolcreaterole,rolcreatedb,rolcanlogin,rolreplication,rolbypassrls,rolconnlimit,rolvaliduntil,rolconfig) ORDER BY rolname COLLATE "C"),'[]') FROM pg_roles WHERE rolname NOT LIKE 'pg_%'),
 'memberships',(SELECT coalesce(json_agg(json_build_array(pg_get_userbyid(roleid),pg_get_userbyid(member),admin_option,inherit_option,set_option) ORDER BY pg_get_userbyid(roleid) COLLATE "C",pg_get_userbyid(member) COLLATE "C"),'[]') FROM pg_auth_members),
 'migrations',(SELECT coalesce(json_agg(json_build_array("MigrationId","ProductVersion") ORDER BY "MigrationId" COLLATE "C"),'[]') FROM public."__EFMigrationsHistory"));`;

const SEQUENCES_SQL = String.raw`
CREATE TEMP TABLE recovery_sequences(schema_name text,sequence_name text,parameters jsonb,last_value bigint,is_called boolean);
DO $capture$ DECLARE item record; value_last bigint; value_called boolean; BEGIN
 FOR item IN SELECT n.nspname,c.relname,jsonb_build_array(format_type(s.seqtypid,NULL),s.seqstart::text,s.seqincrement::text,s.seqmax::text,s.seqmin::text,s.seqcache::text,s.seqcycle) parameters
 FROM pg_sequence s JOIN pg_class c ON c.oid=s.seqrelid JOIN pg_namespace n ON n.oid=c.relnamespace
 WHERE n.nspname NOT LIKE 'pg_%' AND n.nspname<>'information_schema' LOOP
 EXECUTE format('SELECT last_value,is_called FROM %I.%I',item.nspname,item.relname) INTO value_last,value_called;
 INSERT INTO recovery_sequences VALUES(item.nspname,item.relname,item.parameters,value_last,value_called);
 END LOOP; END $capture$;
SELECT coalesce(json_agg(json_build_array(schema_name,sequence_name,parameters,last_value::text,is_called) ORDER BY schema_name COLLATE "C",sequence_name COLLATE "C"),'[]') FROM recovery_sequences;`;

const OUTBOX_IDLE_SQL = String.raw`SELECT count(*) FROM public.outbox_events
WHERE "Status"::text NOT IN ('Delivered','Cancelled') OR "LockedAt" IS NOT NULL OR "LockOwner" IS NOT NULL OR "LockToken" IS NOT NULL;`;

const FILE_MANIFEST = String.raw`
const fs=require('node:fs'),crypto=require('node:crypto');const rows=[];let total=0;
function walk(directory,prefix=''){for(const name of fs.readdirSync(directory).sort()){
 if(name.includes('/')||name.includes('\\'))throw Error();const path=directory+'/'+name,relative=prefix+name;
 const before=fs.lstatSync(path,{bigint:true});if(before.isSymbolicLink()||before.nlink>1n&&before.isFile())throw Error();
 if(before.isDirectory()){rows.push([relative+'/',Number(before.mode&4095n),Number(before.uid),Number(before.gid)]);walk(path,relative+'/');}
 else{if(!before.isFile()||before.size>16777216n)throw Error();const fd=fs.openSync(path,fs.constants.O_RDONLY|fs.constants.O_NOFOLLOW);let count=0;const hash=crypto.createHash('sha256');
 try{const opened=fs.fstatSync(fd,{bigint:true});if(opened.ino!==before.ino||opened.dev!==before.dev)throw Error();const chunk=Buffer.alloc(65536);let n;
 while((n=fs.readSync(fd,chunk,0,chunk.length,null))){count+=n;total+=n;if(count>16777216||total>67108864)throw Error();hash.update(chunk.subarray(0,n));}
 const after=fs.fstatSync(fd,{bigint:true});if(count!==Number(before.size)||after.size!==before.size||after.mtimeNs!==before.mtimeNs||after.ctimeNs!==before.ctimeNs)throw Error();
 rows.push([relative,Number(before.mode&4095n),Number(before.uid),Number(before.gid),count,hash.digest('hex')]);}finally{fs.closeSync(fd);}}
 if(rows.length>2048)throw Error();}}
walk('/capture');process.stdout.write(JSON.stringify(rows));`;

function trustedExecutable(path) {
  require(realpathSync(path) === path, 'EXECUTABLE');
  const info = lstatSync(path);
  require(info.isFile() && info.uid === 0 && (info.mode & 0o022) === 0, 'EXECUTABLE');
  for (let parent = dirname(path); parent !== '/'; parent = dirname(parent)) {
    const state = lstatSync(parent);
    require(state.isDirectory() && state.uid === 0 && (state.mode & 0o022) === 0, 'EXECUTABLE');
  }
}

function privateFile(root, name, bytes) {
  require(/^[a-z0-9.-]+$/.test(name), 'PRIVATE_PATH');
  const path = beneath(root, join(root, name));
  writeFileSync(path, bytes, { flag: 'wx', mode: 0o600 }); return path;
}

function runnerDirectory(path) {
  require(isAbsolute(path ?? '') && realpathSync(path) === path, 'PRIVATE_ROOT');
  const info = lstatSync(path);
  require(info.isDirectory() && !info.isSymbolicLink() && info.uid === process.getuid() && (info.mode & 0o022) === 0, 'PRIVATE_ROOT');
}

export async function runRecovery() {
  const identity = hostedContract(process.env);
  trustedExecutable(DOCKER); trustedExecutable(GIT);
  const socketPath = realpathSync(SOCKET);
  require(['/var/run/docker.sock', '/run/docker.sock'].includes(socketPath), 'LOCAL_DOCKER');
  const socket = lstatSync(socketPath); require(socket.isSocket() && socket.uid === 0, 'LOCAL_DOCKER');
  runnerDirectory(process.env.RUNNER_TEMP);
  require(realpathSync(process.env.GITHUB_WORKSPACE ?? '') === ROOT, 'SOURCE');
  const nonce = randomBytes(6).toString('hex'), names = resourceNames(identity.runId, nonce);
  const privateRoot = mkdtempSync(join(process.env.RUNNER_TEMP, 'coglatas-logical-recovery-')); chmodSync(privateRoot, 0o700);
  const configRoot = join(privateRoot, 'docker-config'); mkdirSync(configRoot, { mode: 0o700 });
  writeFileSync(join(configRoot, 'config.json'), '{}', { flag: 'wx', mode: 0o600 });
  const password = 'Synthetic!Aa1' + randomBytes(32).toString('base64url'); process.stdout.write('::add-mask::' + password + '\n');
  const env = { PATH: '/usr/local/bin:/usr/bin:/bin', HOME: process.env.HOME,
    COGLATAS_RECOVERY_PRIVATE: privateRoot, COGLATAS_RECOVERY_FIXTURE_PASSWORD: password,
    COGLATAS_RECOVERY_SOURCE_SEED: 'true', COGLATAS_APP_IMAGE: 'coglatas-logical-recovery:' + nonce,
    SYNCFUSION_LICENSE: 'not-used-without-build' };
  const created = new Set(); let stage = 'source', sourceApp, sourceVolumes, sourceSnapshot;
  const receipt = { schemaVersion: 1, kind: 'SYNTHETIC_LOGICAL_RECOVERY_MECHANISM', ...identity,
    githubJob: process.env.GITHUB_JOB, jobIdReconciliation: 'REQUIRES_PROVIDER_METADATA', trustedAttestation: 'UNVERIFIED',
    status: 'ERROR', testedCombinedTree: null, specificationBaseline: '850a5d9376db11e75235cf55e03ebed4de9c0510',
    firstAttemptOnly: true, runtimeMode: 'Test/SaaS', publicPorts: false, restoredTargetMigrations: false,
    postgresVersionNum: null, appImage: null, postgresImage: null, restoreCalls: { target: 0, fault: 0 },
    helperImage: NODE_IMAGE, migrationSdkImage: SDK_IMAGE,
    strictCheckpoint: 'NOT_EVALUATED', startupDelta: 'NOT_EVALUATED', api: 'NOT_EVALUATED',
    faultSourcePreserved: false, originalSourceRollback: false, cleanup: false,
    rolesAndRlsQualification: 'NOT_EVALUATED', productionHttps: 'NOT_EVALUATED',
    technicalReleaseReady: false, studentCouncilPilot: 'RELEASE_NO_GO', seedFlags: {},
    privateEvidenceUploaded: false, corruptTargetQuarantined: false };
  const command = (executable, args, { input, allowFailure = false, timeout = 240000, commandEnv = env } = {}) => {
    const result = spawnSync(executable, args, { cwd: ROOT, env: commandEnv, input,
      encoding: null, maxBuffer: MAX_BYTES, timeout, windowsHide: true });
    require(!result.error && !result.signal && Number.isInteger(result.status), 'COMMAND_UNAVAILABLE');
    if (!allowFailure) require(result.status === 0, 'COMMAND_FAILED');
    return result;
  };
  const docker = (args, options) => command(DOCKER,
    ['--config', configRoot, '--host', 'unix://' + SOCKET, ...args], options);
  const helperLabels = ['--label', 'com.docker.compose.project=' + names.source,
    '--label', 'com.docker.compose.service=mechanism-helper'];
  const git = args => command(GIT, args).stdout.toString('utf8').trim();
  const compose = (project, args, options) => docker(['compose', '--project-name', project,
    ...(project === names.source ? ['-f', BASE, '-f', OVERLAY] : ['-f', TARGET]), ...args], options);
  const json = result => JSON.parse(result.stdout.toString('utf8'));
  const inspect = id => json(docker(['inspect', id]))[0];
  const appId = project => compose(project, ['ps', '--all', '-q', 'app']).stdout.toString('utf8').trim();
  const psql = (project, sql) => compose(project, ['exec', '-T', 'postgres', '/usr/local/bin/psql',
    '-X', '-q', '-A', '-t', '-v', 'ON_ERROR_STOP=1', '-U', 'coglatas_smoke', '-d', 'coglatas_smoke'], { input: sql }).stdout.toString('utf8').trim();
  const configuration = project => {
    const config = json(compose(project, ['config', '--format', 'json']));
    validateComposition(config, { target: project !== names.source, image: env.COGLATAS_APP_IMAGE, project }); return config;
  };
  const initialAbsence = project => {
    require(!docker(['ps', '-aq', '--filter', 'label=com.docker.compose.project=' + project]).stdout.toString().trim() &&
      !docker(['volume', 'ls', '-q', '--filter', 'label=com.docker.compose.project=' + project]).stdout.toString().trim() &&
      !docker(['network', 'ls', '-q', '--filter', 'label=com.docker.compose.project=' + project]).stdout.toString().trim(), 'PREEXISTING_PROJECT');
    const config = configuration(project);
    for (const volume of Object.values(config.volumes ?? {}))
      require(docker(['volume', 'inspect', volume.name], { allowFailure: true }).status !== 0, 'PREEXISTING_VOLUME');
    require(docker(['network', 'inspect', project + '_default'], { allowFailure: true }).status !== 0, 'PREEXISTING_NETWORK');
    created.add(project);
  };
  const mounts = (project, id) => {
    const app = inspect(id);
    require(app.Config.Labels['com.docker.compose.project'] === project && app.Config.Labels['com.docker.compose.service'] === 'app' && app.Image === env.COGLATAS_APP_IMAGE, 'APP_IDENTITY');
    const settings = Object.fromEntries(app.Config.Env.map(entry => {
      const position = entry.indexOf('='); return [entry.slice(0, position), entry.slice(position + 1)];
    }));
    require(settings.ASPNETCORE_ENVIRONMENT === 'Test' && settings.Tenancy__AppMode === 'SaaS' &&
      settings.SecurityCiFixture__Enabled === 'false' && settings.Tenancy__SeedOnStartup === 'false' &&
      settings.UiShell__SeedOnStartup === 'false' && settings.COGLATAS_SEED_ADMIN_ENABLED === 'false' &&
      settings.COGLATAS_BROWSER_SMOKE_SEED_ENABLED === 'false' && settings.COGLATAS_DEMO_DATASET_ENABLED === 'false', 'RUNTIME_SEED');
    require(!app.HostConfig.Privileged && !Object.values(app.HostConfig.PortBindings ?? {}).some(Boolean), 'RUNTIME_PORT');
    const network = json(docker(['network', 'inspect', project + '_default']))[0];
    require(network.Internal === true && network.Driver === 'bridge' &&
      network.Labels?.['com.docker.compose.project'] === project, 'RUNTIME_NETWORK');
    receipt.seedFlags[project === names.source ? 'sourceCheckpoint' : project === names.target ? 'target' : 'fault'] = {
      runtimeEnvironment: 'Test', appMode: 'SaaS', fixture: false, tenancy: false, uiShell: false, administrator: false, browser: false, demo: false,
    };
    const selected = ['/app/storage/uploads', '/app/data/protection-keys'].map(destination => {
      const matches = app.Mounts.filter(mount => mount.Destination === destination);
      require(matches.length === 1 && matches[0].Type === 'volume', 'VOLUME');
      const volume = json(docker(['volume', 'inspect', matches[0].Name]))[0];
      require(volume.Labels?.['com.docker.compose.project'] === project, 'VOLUME'); return matches[0].Name;
    });
    const pg = compose(project, ['ps', '--all', '-q', 'postgres']).stdout.toString().trim();
    const database = inspect(pg);
    require(database.Config.Labels['com.docker.compose.project'] === project && database.Image === env.COGLATAS_RECOVERY_POSTGRES_IMAGE, 'PG_IDENTITY');
    const data = database.Mounts.filter(mount => mount.Type === 'volume' && mount.Destination === '/var/lib/postgresql');
    require(data.length === 1, 'VOLUME');
    require(json(docker(['volume', 'inspect', data[0].Name]))[0].Labels?.['com.docker.compose.project'] === project, 'VOLUME');
    return [...selected, data[0].Name];
  };
  const manifest = volume => json(docker(['run', '--rm', ...helperLabels, '--network', 'none', '--read-only',
    '--mount', `type=volume,source=${volume},target=/capture,readonly`, '--entrypoint', '/usr/local/bin/node', NODE_IMAGE,
    '-e', FILE_MANIFEST]));
  const snapshot = (project, volumes, name) => {
    require(inspect(appId(project)).State.Running === false, 'APP_NOT_QUIESCED');
    require(psql(project, "SELECT count(*) FROM pg_stat_activity WHERE datname=current_database() AND backend_type='client backend' AND pid<>pg_backend_pid();") === '0', 'DB_WRITER');
    const dump = compose(project, ['exec', '-T', 'postgres', '/usr/local/bin/pg_dump', '-U', 'coglatas_smoke', '-d', 'coglatas_smoke',
      '--schema-only', '--no-owner', '--no-privileges', '--restrict-key=' + nonce]).stdout;
    privateFile(privateRoot, name + '.schema.sql', dump);
    const rows = JSON.parse(psql(project, ROWS_SQL)), catalog = JSON.parse(psql(project, CATALOG_SQL));
    const sequences = JSON.parse(psql(project, SEQUENCES_SQL));
    const value = { schema: sha256(dump), rows, catalog, sequences,
      uploads: manifest(volumes[0]), keys: manifest(volumes[1]) };
    require(value.keys.some(row => row[0].endsWith('.xml')), 'KEYS_ABSENT');
    privateFile(privateRoot, name + '.json', JSON.stringify(value)); return value;
  };
  const audit = project => JSON.parse(psql(project, String.raw`SELECT coalesce(json_agg(json_build_array("Id",encode(sha256(convert_to(to_jsonb(a)::text,'UTF8')),'hex')) ORDER BY "Id"),'[]') FROM public.audit_logs a;`));
  const probe = (project, phase) => {
    const id = appId(project); require(inspect(id).Image === receipt.appImage, 'APP_IDENTITY');
    docker(['run', '--rm', ...helperLabels, '--network', 'container:' + id, '--read-only', '--user', `${process.getuid()}:${process.getgid()}`,
      '--mount', `type=bind,source=${ROOT},target=/work,readonly`,
      '--mount', `type=bind,source=${privateRoot},target=/private`, '--entrypoint', '/usr/local/bin/node', NODE_IMAGE,
      '/work/scripts/ci/synthetic-logical-recovery-probe.mjs', phase]);
    return JSON.parse(readFileSync(join(privateRoot, 'probe-' + phase + '.json'), 'utf8'));
  };
  const stopped = project => { compose(project, ['stop', 'app']); require(!inspect(appId(project)).State.Running, 'APP_NOT_QUIESCED'); };
  const started = async id => {
    docker(['start', id]);
    for (let sample = 0; sample < 180; sample++) {
      const state = inspect(id).State;
      require(state.Running, 'APP_EXITED');
      if (state.Health?.Status === 'healthy') return;
      await new Promise(done => setTimeout(done, 1000));
    }
    reject('APP_NOT_READY');
  };
  const prepareTarget = project => {
    initialAbsence(project); configuration(project); compose(project, ['create', 'postgres', 'app']);
    compose(project, ['up', '-d', '--wait', '--wait-timeout', '180', 'postgres']);
    const volumes = mounts(project, appId(project)); assertDisjoint(sourceVolumes, volumes);
    return volumes;
  };
  const restoreOnce = (project, bytes, expectedFailure) => {
    const kind = project === names.target ? 'target' : 'fault'; require(receipt.restoreCalls[kind]++ === 0, 'RESTORE_RETRY');
    const result = compose(project, ['exec', '-T', 'postgres', '/usr/local/bin/pg_restore', '-U', 'coglatas_smoke', '-d', 'coglatas_smoke',
      '--no-owner', '--no-privileges', '--exit-on-error'], { input: bytes, allowFailure: expectedFailure });
    if (expectedFailure) {
      assertTruncatedArchiveFailure(result);
      require(psql(project, 'SELECT 1;') === '1', 'FAULT_DB_UNAVAILABLE');
      receipt.corruptArchiveRejectedByPgRestore = true;
    } else require(result.status === 0, 'RESTORE_OUTCOME');
  };
  let failure;
  try {
    require(Boolean(docker(['info', '--format', '{{.ID}}']).stdout.toString().trim()), 'LOCAL_DOCKER');
    require(git(['rev-parse', '--show-toplevel']) === ROOT && git(['rev-parse', 'HEAD']) === identity.sourceSha &&
      !git(['status', '--porcelain', '--untracked-files=all']), 'SOURCE');
    receipt.testedCombinedTree = git(['rev-parse', 'HEAD^{tree}']);
    require(/^[a-f0-9]{40}$/.test(receipt.testedCombinedTree), 'SOURCE');
    initialAbsence(names.source); configuration(names.source);
    stage = 'build'; require(Boolean(process.env.SYNCFUSION_LICENSE), 'BUILD_LICENSE');
    compose(names.source, ['build', 'app'], { timeout: 1800000, commandEnv: { ...env, SYNCFUSION_LICENSE: process.env.SYNCFUSION_LICENSE } });
    const built = inspect(env.COGLATAS_APP_IMAGE).Id;
    require(/^sha256:[a-f0-9]{64}$/.test(built), 'APP_IDENTITY');
    env.COGLATAS_APP_IMAGE = built; receipt.appImage = built;
    mkdirSync(join(privateRoot, 'tools'), { mode: 0o700 });
    // Dependency preparation is separate from the subsequently internal-only
    // runtime network. It uses the existing pinned SDK and no production DSN.
    const bundleArguments = ['run', '--rm', ...helperLabels, '--user', `${process.getuid()}:${process.getgid()}`,
      '--mount', `type=bind,source=${ROOT},target=/src`, '--mount', `type=bind,source=${privateRoot}/tools,target=/tools`,
      '--workdir', '/src', '--env', 'DOTNET_CLI_HOME=/tools/home', '--env', 'NUGET_PACKAGES=/tools/packages',
      '--env', 'ASPNETCORE_ENVIRONMENT=Test', '--env', 'ConnectionStrings__DefaultConnection=Host=127.0.0.1;Database=unused;Username=unused;Password=unused',
      '--entrypoint', '/usr/share/dotnet/dotnet', SDK_IMAGE];
    for (const args of [['tool', 'restore'], ['restore', 'src/Coglatas.Web/Coglatas.Web.csproj'],
      ['ef', 'migrations', 'bundle', '--project', 'src/Coglatas.Infrastructure', '--startup-project', 'src/Coglatas.Web',
        '--configuration', 'Release', '--output', '/tools/migrate']]) docker([...bundleArguments, ...args], { timeout: 1800000 });
    receipt.migrationBundleSha256 = sha256(readFileSync(join(privateRoot, 'tools/migrate')));
    stage = 'source-seed'; configuration(names.source);
    compose(names.source, ['up', '-d', '--wait', '--wait-timeout', '180', 'postgres']);
    env.COGLATAS_RECOVERY_POSTGRES_IMAGE = inspect(compose(names.source, ['ps', '-q', 'postgres']).stdout.toString().trim()).Image;
    receipt.postgresImage = env.COGLATAS_RECOVERY_POSTGRES_IMAGE;
    receipt.postgresVersionNum = Number(psql(names.source, "SELECT current_setting('server_version_num');"));
    require(Math.trunc(receipt.postgresVersionNum / 10000) === 18, 'PG_VERSION');
    compose(names.source, ['run', '--rm', '--no-deps', 'migrate']);
    compose(names.source, ['up', '-d', '--no-build', '--no-deps', '--wait', '--wait-timeout', '180', 'app']);
    const seededEnvironment = inspect(appId(names.source)).Config.Env;
    require(seededEnvironment.includes('SecurityCiFixture__Enabled=true'), 'SOURCE_SEED');
    receipt.seedFlags.initialSourceFixture = true;
    // Freeze seed behavior before the original checkpoint/rollback container.
    stopped(names.source); env.COGLATAS_RECOVERY_SOURCE_SEED = 'false';
    env.COGLATAS_RECOVERY_FIXTURE_PASSWORD = 'fixture-seed-disabled';
    compose(names.source, ['up', '-d', '--no-build', '--no-deps', '--wait', '--wait-timeout', '180', 'app']);
    sourceApp = appId(names.source); sourceVolumes = mounts(names.source, sourceApp);
    const actors = JSON.parse(psql(names.source, String.raw`SELECT json_agg(json_build_object('tenant',t."Slug",'email',u."Email",'userId',u."Id",'fileId',f."Id",'fileBytes',f."SizeBytes",'fileSha256',f."HashSha256",'denialMarkers',json_build_array(f."Id"::text,f."OriginalFileName",f."StorageKey",u."Email")) ORDER BY t."Slug") FROM public.file_objects f JOIN public.tenants t ON t."Id"=f."TenantId" JOIN public.users u ON u."Id"=f."UploadedByUserId" WHERE t."Slug" IN ('security-alpha','security-beta') AND f."StorageKey" LIKE '%/security-ci/%';`));
    const revoked = JSON.parse(psql(names.source, String.raw`SELECT json_build_object('tenant','security-alpha','email',"Email",'userId',"Id") FROM public.users WHERE "Email"='security-alpha-member@example.test';`));
    privateFile(privateRoot, 'probe-input.json', JSON.stringify({ password, actors, revoked }));
    probe(names.source, 'checkpoint');
    await awaitOutboxIdle(() => psql(names.source, OUTBOX_IDLE_SQL));
    stopped(names.source);
    stage = 'checkpoint';
    // No active Outbox work may be hidden by app shutdown.
    const outboxColumns = psql(names.source, "SELECT string_agg(column_name,',' ORDER BY ordinal_position) FROM information_schema.columns WHERE table_schema='public' AND table_name='outbox_events';");
    require(outboxColumns.includes('Status'), 'OUTBOX_SCHEMA');
    require(psql(names.source, OUTBOX_IDLE_SQL) === '0', 'OUTBOX_NOT_IDLE');
    sourceSnapshot = snapshot(names.source, sourceVolumes, 'source-checkpoint');
    const originalAudit = audit(names.source); require(originalAudit.length > 0, 'AUDIT_ABSENT');
    const dump = compose(names.source, ['exec', '-T', 'postgres', '/usr/local/bin/pg_dump', '-U', 'coglatas_smoke', '-d', 'coglatas_smoke',
      '--format=custom', '--no-owner', '--no-privileges']).stdout;
    privateFile(privateRoot, 'database.dump', dump); receipt.databaseDumpSha256 = sha256(dump);
    const archives = sourceVolumes.slice(0, 2).map((volume, index) => {
      const bytes = docker(['run', '--rm', ...helperLabels, '--network', 'none', '--read-only', '--mount', `type=volume,source=${volume},target=/capture,readonly`,
        '--entrypoint', '/bin/tar', NODE_IMAGE, '-cpf', '-', '-C', '/capture', '.']).stdout;
      validateTar(bytes); privateFile(privateRoot, index ? 'keys.tar' : 'uploads.tar', bytes); return bytes;
    });
    stage = 'restore'; const targetVolumes = prepareTarget(names.target); restoreOnce(names.target, dump, false);
    require(Number(psql(names.target, "SELECT current_setting('server_version_num');")) === receipt.postgresVersionNum, 'PG_VERSION');
    for (let index = 0; index < 2; index++) docker(['run', '--rm', ...helperLabels, '--network', 'none', '--read-only',
      '--mount', `type=volume,source=${targetVolumes[index]},target=/restore`, '--entrypoint', '/bin/tar', NODE_IMAGE,
      '-xpf', '-', '-C', '/restore'], { input: archives[index] });
    const beforeStart = snapshot(names.target, targetVolumes, 'target-before-start');
    assertSameSnapshot(sourceSnapshot, beforeStart); receipt.strictCheckpoint = 'PASS';
    stage = 'startup'; compose(names.target, ['up', '-d', '--no-build', '--no-deps', '--wait', '--wait-timeout', '180', 'app']);
    stopped(names.target); const afterStart = snapshot(names.target, targetVolumes, 'target-after-start');
    receipt.startupDelta = { equalToCheckpoint: JSON.stringify(beforeStart) === JSON.stringify(afterStart),
      changedTables: beforeStart.rows.filter((row, index) => JSON.stringify(row) !== JSON.stringify(afterStart.rows[index])).length,
      schemaUnchanged: beforeStart.schema === afterStart.schema };
    assertSameSnapshot(beforeStart, afterStart, 'STARTUP_DATA_CHANGED');
    await started(appId(names.target));
    stage = 'target-probes'; receipt.api = probe(names.target, 'verify'); stopped(names.target);
    const targetAudit = new Map(audit(names.target));
    require(originalAudit.every(([id, digest]) => targetAudit.get(id) === digest) && targetAudit.size > originalAudit.length, 'AUDIT_CONTINUITY');
    stage = 'fault'; prepareTarget(names.fault); restoreOnce(names.fault, corruptCopy(dump), true);
    receipt.corruptTargetQuarantined = true;
    assertSameSnapshot(sourceSnapshot, snapshot(names.source, sourceVolumes, 'source-after-fault'), 'FAULT_SOURCE_CHANGED');
    require(sha256(readFileSync(join(privateRoot, 'database.dump'))) === receipt.databaseDumpSha256, 'BACKUP_CHANGED');
    receipt.faultSourcePreserved = true;
    stage = 'rollback';
    require(inspect(sourceApp).Image === receipt.appImage, 'ROLLBACK_IMAGE');
    await started(sourceApp); probe(names.source, 'rollback'); receipt.originalSourceRollback = true;
    require(git(['rev-parse', 'HEAD']) === identity.sourceSha && git(['rev-parse', 'HEAD^{tree}']) === receipt.testedCombinedTree &&
      !git(['status', '--porcelain', '--untracked-files=all']), 'SOURCE_CHANGED');
    receipt.checkpointDigest = sha256(JSON.stringify(sourceSnapshot));
    receipt.componentDigests = Object.fromEntries(Object.entries(sourceSnapshot).map(([key, value]) =>
      [key, key === 'schema' ? value : sha256(JSON.stringify(value))]));
    receipt.tableCount = sourceSnapshot.rows.length; receipt.status = 'PASS';
  } catch (error) {
    failure = error; receipt.failedStage = stage;
    receipt.failureCode = /^[A-Z_]+$/.test(error?.code ?? '') ? error.code : 'UNAVAILABLE';
  } finally {
    // A failed target never replaces or writes into the original source.
    // Cleanup only projects first observed absent and subsequently owned here.
    try {
      for (const project of created) {
        const containers = docker(['ps', '-aq', '--filter', 'label=com.docker.compose.project=' + project]).stdout.toString().trim().split(/\s+/).filter(Boolean);
        for (const id of containers) {
          const info = inspect(id), service = info.Config.Labels['com.docker.compose.service'];
          require(info.Config.Labels['com.docker.compose.project'] === project &&
            ['postgres', 'app', 'migrate', 'mechanism-helper'].includes(service), 'CLEANUP_OWNER');
          if (service === 'mechanism-helper') {
            require(project === names.source, 'CLEANUP_OWNER'); docker(['rm', '--force', id]);
          }
        }
        compose(project, ['down', '--volumes', '--remove-orphans']);
        require(!docker(['ps', '-aq', '--filter', 'label=com.docker.compose.project=' + project]).stdout.toString().trim() &&
          !docker(['volume', 'ls', '-q', '--filter', 'label=com.docker.compose.project=' + project]).stdout.toString().trim() &&
          !docker(['network', 'ls', '-q', '--filter', 'label=com.docker.compose.project=' + project]).stdout.toString().trim(), 'CLEANUP_REMAINS');
      }
      receipt.cleanup = true;
    } catch { receipt.cleanup = false; receipt.status = 'ERROR'; receipt.cleanupError = 'OWNED_CLEANUP_FAILED'; }
    const artifacts = join(ROOT, 'artifacts');
    if (!existsSync(artifacts)) mkdirSync(artifacts, { mode: 0o700 });
    require(lstatSync(artifacts).isDirectory() && realpathSync(artifacts) === artifacts, 'RECEIPT_PATH');
    const parent = join(artifacts, 'synthetic-logical-recovery');
    if (!existsSync(parent)) mkdirSync(parent, { mode: 0o700 });
    runnerDirectory(parent);
    const output = join(parent, identity.runId + '-1-' + nonce);
    mkdirSync(output, { mode: 0o700 });
    require(realpathSync(output) === output, 'RECEIPT_PATH');
    writeFileSync(join(output, 'receipt.json'), JSON.stringify(receipt, null, 2) + '\n', { flag: 'wx', mode: 0o600 });
  }
  return !failure && receipt.status === 'PASS' && receipt.cleanup ? 0 : 1;
}

if (process.argv[1] && pathToFileURL(realpathSync(process.argv[1])).href === import.meta.url) {
  runRecovery().then(code => { process.exitCode = code; }).catch(() => {
    process.stderr.write('Synthetic logical recovery rejected before qualification.\n'); process.exitCode = 1;
  });
}
