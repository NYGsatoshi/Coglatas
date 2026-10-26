import { execFileSync } from 'node:child_process';
import { createHash, randomUUID } from 'node:crypto';
import { lstatSync, mkdirSync, readFileSync, readdirSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { pathToFileURL } from 'node:url';

const baseCompose = 'infra/compose/test/real-backend-smoke.yml';
const runLabel = 'coglatas.ui07-roster.run';
const services = ['postgres', 'migrate', 'app', 'real-backend-playwright'];
const volumes = ['real_backend_smoke_pgdata', 'real_backend_smoke_nuget', 'real_backend_smoke_uploads',
  'real_backend_smoke_data_protection', 'real_backend_smoke_node_modules'];
const shaPattern = /^[a-f0-9]{40}$/;
const imagePattern = /^sha256:[a-f0-9]{64}$/;

function require(condition) {
  if (!condition) throw new Error('UI07_ROSTER_CONTRACT_REJECTED');
}

export function readBrowserReceipts(directory, sourceSha) {
  require(/^artifacts\/roster-private\/[a-f0-9]{32}$/.test(directory));
  for (const parent of ['artifacts', 'artifacts/roster-private', directory]) {
    const info = lstatSync(parent); require(info.isDirectory() && !info.isSymbolicLink());
  }
  const found = [];
  let entries = 0;
  function visit(path, depth) {
    require(depth <= 5);
    for (const entry of readdirSync(path, { withFileTypes: true })) {
      require(++entries <= 256 && !entry.isSymbolicLink());
      const target = join(path, entry.name);
      if (entry.isDirectory()) visit(target, depth + 1);
      else if (/^readonly-roster-structural-receipt-json-[a-f0-9]{40}\.json$/.test(entry.name)) {
        const info = lstatSync(target); require(info.isFile() && info.size > 0 && info.size <= 16384);
        const value = JSON.parse(readFileSync(target, 'utf8'));
        const keys = ['sourceSha', 'browserProject', 'phase', 'status', 'memberCount', 'readOnly', 'canPost',
          'canManageMembers', 'ownerPostStatus', 'readerPostStatus', 'readerManageStatus', 'ownerContextClosed',
          'metrics', 'outer', 'end', 'home', 'viewport', 'retry'];
        require(value && Object.keys(value).length === keys.length && keys.every(key => Object.hasOwn(value, key)));
        require(value.sourceSha === sourceSha && ['readonly-roster-desktop', 'readonly-roster-mobile'].includes(value.browserProject) &&
          ['ADMISSION', 'AUTHENTICATION', 'SERVER_AUTHORITY', 'BROWSER_READONLY_PROJECTION', 'END_BOUNDARY',
            'HOME_BOUNDARY', 'ASSERTIONS_COMPLETE'].includes(value.phase) && ['NOT_VERIFIED', 'PASS'].includes(value.status));
        require(value.retry === 0);
        const number = value => typeof value === 'number' && Number.isFinite(value) && value >= 0 && value <= 10_000_000;
        for (const key of ['memberCount', 'home']) require(value[key] === null || number(value[key]));
        for (const key of ['readOnly', 'canPost', 'canManageMembers', 'ownerContextClosed']) require(value[key] === null || typeof value[key] === 'boolean');
        for (const key of ['ownerPostStatus', 'readerPostStatus', 'readerManageStatus']) {
          require(value[key] === null || Number.isInteger(value[key]) && value[key] >= 100 && value[key] <= 599);
        }
        const fields = (object, names) => object && Object.keys(object).length === names.length && names.every(name => Object.hasOwn(object, name));
        require(value.metrics === null || fields(value.metrics, ['height', 'scrollHeight', 'tabIndex', 'focusableChildren']) && Object.values(value.metrics).every(number));
        require(value.outer === null || fields(value.outer, ['isAside', 'height', 'scrollHeight']) &&
          typeof value.outer.isAside === 'boolean' && number(value.outer.height) && number(value.outer.scrollHeight));
        require(value.end === null || fields(value.end, ['top', 'max']) && Object.values(value.end).every(number));
        require(fields(value.viewport, ['width', 'height']) &&
          (value.browserProject === 'readonly-roster-desktop' ? value.viewport.width === 1536 && value.viewport.height === 600 :
            value.viewport.width === 320 && value.viewport.height === 420));
        if (value.status === 'PASS') require(value.phase === 'ASSERTIONS_COMPLETE' && value.memberCount === 43 &&
          value.readOnly === true && value.canPost === false && value.canManageMembers === false &&
          value.ownerPostStatus === 200 && value.readerPostStatus === 403 && value.readerManageStatus === 400 &&
          value.ownerContextClosed === true && value.metrics && value.metrics.scrollHeight > value.metrics.height &&
          value.metrics.tabIndex === 0 && value.metrics.focusableChildren === 0 && value.outer &&
          value.outer.scrollHeight > value.outer.height && value.end && value.end.max > 0 &&
          Math.abs(value.end.top - value.end.max) <= 1 && value.home === 0 &&
          (value.browserProject !== 'readonly-roster-desktop' || value.outer.isAside === true));
        found.push(value);
      }
    }
  }
  visit(directory, 0);
  require(found.length <= 2 && new Set(found.map(value => value.browserProject)).size === found.length);
  return found.sort((a, b) => a.browserProject.localeCompare(b.browserProject));
}

export function validateRosterInvocation(environment, args, git) {
  require(args.length === 0);
  const sha = git(['rev-parse', 'HEAD']).trim();
  require(shaPattern.test(sha) && environment.TARGET_SHA === sha &&
    git(['status', '--porcelain', '--untracked-files=normal']).trim() === '');
  for (const name of ['REAL_BACKEND_SMOKE_COMPOSE_PROJECT_NAME', 'COGLATAS_REAL_BACKEND_SMOKE_GREP',
    'COGLATAS_FUNCTIONAL_FILES_GATE', 'COGLATAS_FCI04_GATES', 'COGLATAS_FCI04_ONLY',
    'COGLATAS_ISSUE_683_EVIDENCE', 'COGLATAS_REAL_BACKEND_P0_SETUP', 'PLAYWRIGHT_BASE_URL',
    'FUNCTIONAL_COMPOSE_FILES', 'PLAYWRIGHT_GREP', 'PLAYWRIGHT_PROJECT']) {
    require(!environment[name]);
  }
  require(!Object.keys(environment).some(name => name.startsWith('COGLATAS_UI07_ROSTER_')));
  require(environment.COGLATAS_REUSE_PREBUILT_APP_IMAGE === '1' ||
    environment.COGLATAS_REUSE_PREBUILT_DOTNET_BUILD !== 'true');
  return sha;
}

export function createRosterControl({ sourceSha, projectName, token, directory, reuse, fixture }) {
  require(shaPattern.test(sourceSha) && /^coglatas-ui07-roster-[a-f0-9]{32}$/.test(projectName) &&
    /^[a-f0-9]{32}$/.test(token));
  mkdirSync(directory, { recursive: true, mode: 0o700 });
  const overlay = join(directory, 'ownership.compose.json');
  writeFileSync(overlay, JSON.stringify({
    services: Object.fromEntries(services.map(name => [name, { labels: { [runLabel]: token } }])),
    volumes: Object.fromEntries(volumes.map(name => [name, { labels: { [runLabel]: token } }])),
    networks: { default: { labels: { [runLabel]: token } } },
  }), { mode: 0o600 });
  let admitted = false;
  let imageId = null;
  let sourceBindingVerified = false;
  let servedManifest = null;
  let browserReceipts = [];
  let fixtureReceipt = null;
  let cleanupState = 'NOT_ADMITTED';
  let initialCaptured = false;
  let browserStartedAt = null;
  let browserRegistered = false;
  let admittedAt = null;
  const browserName = `${projectName}-browser`;
  const privateOutput = `artifacts/roster-private/${token}`;
  const owned = { container: new Map(), volume: new Map(), network: new Map() };

  async function command(io, args, options) {
    const result = await io.docker(args, options);
    require(result.exitCode === 0 && typeof result.output === 'string');
    return result.output.trim();
  }

  async function inventory(io, kind) {
    const commandName = kind === 'container' ? 'ps' : `${kind}`;
    const args = kind === 'container' ? [commandName, '-aq'] : [commandName, 'ls', '-q'];
    return (await command(io, [...args, '--filter', `label=com.docker.compose.project=${projectName}`]))
      .split(/\s+/).filter(Boolean);
  }

  async function metadata(io, kind, id) {
    const format = kind === 'container'
      ? '{"id":{{json .Id}},"name":{{json .Name}},"created":{{json .Created}},"labels":{{json .Config.Labels}},"image":{{json .Image}},"status":{{json .State.Status}},"health":{{if .State.Health}}{{json .State.Health.Status}}{{else}}null{{end}},"exitCode":{{json .State.ExitCode}}}'
      : '{"id":{{json .Id}},"name":{{json .Name}},"created":{{json .Created}},"driver":{{json .Driver}},"scope":{{json .Scope}},"labels":{{json .Labels}}}';
    const selectedFormat = kind === 'volume'
      ? '{"name":{{json .Name}},"created":{{json .CreatedAt}},"driver":{{json .Driver}},"scope":{{json .Scope}},"mountpoint":{{json .Mountpoint}},"labels":{{json .Labels}}}' : format;
    const value = JSON.parse(await command(io, [kind, 'inspect', '--format', selectedFormat, id]));
    require(value.labels?.['com.docker.compose.project'] === projectName && value.labels?.[runLabel] === token);
    require(Number.isFinite(Date.parse(value.created)) && Date.parse(value.created) >= admittedAt);
    if (kind === 'container') {
      require(/^[a-f0-9]{64}$/.test(value.id) && services.includes(value.labels['com.docker.compose.service']));
    } else {
      const label = value.labels[`com.docker.compose.${kind}`];
      require(value.name.startsWith(`${projectName}_`) &&
        (kind === 'volume' ? volumes.includes(label) : label === 'default'));
      if (kind === 'network') require(/^[a-f0-9]{64}$/.test(value.id));
    }
    return value;
  }

  async function capture(io) {
    require(admitted && !initialCaptured && browserStartedAt === null);
    for (const kind of ['container', 'volume', 'network']) {
      const seen = new Set();
      for (const id of await inventory(io, kind)) {
        const value = await metadata(io, kind, id);
        const identity = value.labels[`com.docker.compose.${kind === 'container' ? 'service' : kind}`];
        require(!seen.has(identity)); seen.add(identity);
        if (kind === 'container') {
          require(identity !== 'real-backend-playwright');
          require([`/${projectName}-${identity}-1`, `/${projectName}_${identity}_1`].includes(value.name));
        }
        owned[kind].set(kind === 'volume' ? value.name : value.id, value);
      }
    }
    initialCaptured = true;
  }

  function resourceIdentity(value) {
    return JSON.stringify(Object.fromEntries(Object.entries(value)
      .filter(([name]) => !['status', 'health', 'exitCode'].includes(name))));
  }

  async function assertKnown(io) {
    require(initialCaptured);
    for (const kind of ['container', 'volume', 'network']) {
      const current = new Map();
      for (const id of await inventory(io, kind)) {
        const value = await metadata(io, kind, id);
        current.set(kind === 'volume' ? value.name : value.id, value);
      }
      require(current.size === owned[kind].size);
      for (const [id, original] of owned[kind]) {
        require(current.has(id) && resourceIdentity(original) === resourceIdentity(current.get(id)));
      }
    }
  }

  async function verifyServedAssets(io, appId) {
    const html = await io.docker(['exec', appId, 'cat', '/app/wwwroot/index.html']);
    require(html.exitCode === 0 && html.stdout && html.stdout.length < 1024 * 1024);
    const references = [...html.stdout.matchAll(/(?:src|href)="([^"]+)"/g)]
      .map(match => match[1]).filter(path => /\.(?:js|css)$/.test(path) && path !== '/api/ui/runtime-config.js');
    require(references.length > 0 && references.length <= 32 && references.some(path => /(?:^|\/)main[^/]*\.js$/.test(path)));
    const paths = [...new Set(['index.html', ...references.map(path => path.replace(/^\//, ''))])].sort();
    const manifest = [];
    for (const path of paths) {
      require(/^[a-zA-Z0-9_./-]+$/.test(path) && !path.split('/').includes('..') && !path.includes('//'));
      const hash = await command(io, ['exec', appId, 'sh', '-c',
        'set -eu; test -s "/app/wwwroot/$1"; expected=$(sha256sum "/app/wwwroot/$1"); actual=$(curl --fail --silent --show-error --max-time 15 "http://127.0.0.1:8080/$1" | sha256sum); test "${expected%% *}" = "${actual%% *}"; printf "%s\\n" "${expected%% *}"',
        'sh', path]);
      require(/^[a-f0-9]{64}$/.test(hash));
      manifest.push({ path, sha256: hash });
    }
    servedManifest = { assetCount: manifest.length,
      sha256: createHash('sha256').update(JSON.stringify(manifest)).digest('hex'),
      containerPayloadMatchedHttp: true, dynamicRuntimeConfigExcluded: true };
    sourceBindingVerified = true;
  }

  function saveReceipt(succeeded) {
    writeFileSync(join(directory, 'execution.json'), JSON.stringify({
      schemaVersion: 1, sourceSha, projectName,
      outcome: succeeded && cleanupState === 'REMOVED_OWNED_COMPUTE_RETAINED_VOLUMES' &&
        browserReceipts.length === 2 && browserReceipts.every(value => value.status === 'PASS') ? 'PASS' : 'FAIL',
      scope: 'ISOLATED_SYNTHETIC_UI07_ONLY', sourceBindingVerified,
      expectedEngineImageId: imageId, servedFrontend: servedManifest,
      browserReceipts, fixture: fixtureReceipt, cleanupState,
      ownedResources: Object.fromEntries(Object.entries(owned).map(([kind, values]) => [kind, values.size])),
      publicGo: false,
    }) + '\n', { mode: 0o600 });
  }

  async function verifyImage(io) {
    const environment = io.environment;
    const image = JSON.parse(await command(io, ['image', 'inspect', '--format',
      '{"id":{{json .Id}},"labels":{{json .Config.Labels}}}', environment.COGLATAS_APP_IMAGE]));
    require(imagePattern.test(image.id));
    if (reuse) {
      require(environment.COGLATAS_REUSE_PREBUILT_APP_SOURCE_SHA === sourceSha);
      const producer = environment.COGLATAS_REUSE_PREBUILT_APP_IMAGE_ID;
      const engine = environment.COGLATAS_REUSE_PREBUILT_APP_ENGINE_ID || producer;
      require(imagePattern.test(producer) && image.id === engine);
      const archive = environment.COGLATAS_REUSE_PREBUILT_APP_IMAGE_ARCHIVE;
      const stamp = environment.COGLATAS_REUSE_PREBUILT_DOTNET_STAMP;
      require(archive && stamp);
      const archiveInfo = lstatSync(archive);
      require(archiveInfo.isFile() && !archiveInfo.isSymbolicLink());
      const archiveDirectory = join(archive, '..');
      for (const [path, expected] of [[join(archiveDirectory, 'source-sha'), sourceSha],
        [join(archiveDirectory, 'runtime-image-name'), environment.COGLATAS_APP_IMAGE],
        [join(archiveDirectory, 'runtime-image-id'), producer], [stamp, sourceSha]]) {
        const info = lstatSync(path);
        require(info.isFile() && !info.isSymbolicLink() && info.size <= 1024 &&
          readFileSync(path, 'utf8').trim() === expected);
      }
      if (engine !== producer) {
        // Reuse the existing finite OCI/content verifier; never equate a tag to provenance.
        const proof = execFileSync('python3', ['scripts/ci/runtime_image_identity.py', 'verify',
          '--archive', archive, '--source-sha', sourceSha, '--image-name', environment.COGLATAS_APP_IMAGE,
          '--producer-id', producer, '--engine-id', engine,
          '--binding', environment.COGLATAS_REUSE_PREBUILT_APP_IMAGE_BINDING || '', '--dotnet-stamp', stamp],
        { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] });
        require(!proof.trim());
      }
    } else require(image.labels?.['org.opencontainers.image.revision'] === sourceSha);
    imageId = image.id;
  }

  return {
    kind: 'UI07_READONLY_ROSTER', overlay,
    async admit(io) {
      require(io.projectName === projectName && !admitted);
      for (const kind of ['container', 'volume', 'network']) {
        require((await inventory(io, kind)).length === 0);
        // Also reject unlabeled name collisions that Compose could otherwise adopt.
        const args = kind === 'container' ? ['ps', '-aq'] : [kind, 'ls', '-q'];
        require((await command(io, [...args, '--filter', `name=${projectName}`])) === '');
      }
      if (!reuse) {
        require(io.environment.SYNCFUSION_LICENSE && io.environment.SYNCFUSION_LICENSE !== 'artifact-reuse-no-build');
        require((await io.docker(['build', '--label', `org.opencontainers.image.revision=${sourceSha}`,
          '--secret', 'id=syncfusion_license,env=SYNCFUSION_LICENSE', '--tag', io.environment.COGLATAS_APP_IMAGE,
          '--file', 'Dockerfile', '.'])).exitCode === 0);
      }
      await verifyImage(io);
      admitted = true;
      admittedAt = Math.floor(Date.now() / 1000) * 1000;
      cleanupState = 'OWNED_PENDING';
    },
    capture,
    async prepare(io) {
      await assertKnown(io);
      const containers = await Promise.all([...owned.container.keys()].map(id => metadata(io, 'container', id)));
      const postgres = containers.filter(x => x.labels['com.docker.compose.service'] === 'postgres');
      const app = containers.filter(x => x.labels['com.docker.compose.service'] === 'app');
      const migrate = containers.filter(x => x.labels['com.docker.compose.service'] === 'migrate');
      require(postgres.length === 1 && postgres[0].health === 'healthy' && app.length === 1 &&
        app[0].health === 'healthy' && app[0].image === imageId && migrate.length === 1 &&
        migrate[0].status === 'exited' && migrate[0].exitCode === 0);
      await verifyServedAssets(io, app[0].id);
      const result = await fixture({ sourceSha, projectName, ownership: {
        projectName, composeFile: baseCompose, preexistingContainers: 0, preexistingVolumes: 0,
        preexistingNetworks: 0, createdByCurrentRun: true, postgresProjectLabel: projectName,
        appProjectLabel: projectName, migrationExitCode: 0, appHealthy: true,
        imageSourceSha: sourceSha, sourceBindingVerified, database: 'coglatas_smoke',
      }, executeSql: async sql => {
        const response = await io.docker(['exec', '-i', postgres[0].id, 'psql', '-X', '-qAt',
          '-v', 'ON_ERROR_STOP=1', '-v', 'VERBOSITY=sqlstate', '-U', 'coglatas_smoke', '-d', 'coglatas_smoke'], { input: sql });
        return { exitCode: response.exitCode, stdout: response.stdout || '', stderr: response.stderr || '' };
      } });
      const receiptKeys = ['kind', 'workspaceId', 'conversationId', 'ownerId', 'readerId', 'memberCount',
        'workspaceMemberCount', 'tenantMemberCount', 'readOnlyCount', 'existingAuthorityUnchanged',
        'sourceSha', 'projectName', 'scope'];
      require(result.receipt && Object.keys(result.receipt).length === receiptKeys.length &&
        receiptKeys.every(key => Object.hasOwn(result.receipt, key)) &&
        result.receipt.kind === 'UI07_READONLY_ROSTER' && result.receipt.sourceSha === sourceSha &&
        result.receipt.projectName === projectName && result.receipt.memberCount === 43 &&
        result.receipt.workspaceMemberCount === 43 && result.receipt.tenantMemberCount === 43 &&
        result.receipt.readOnlyCount === 42 && result.receipt.existingAuthorityUnchanged === true &&
        result.receipt.scope === 'DEFENSIVE_PERSISTED_ROLE_FIXTURE_NOT_ROLE_UPDATE_API_PROOF');
      for (const name of ['workspaceId', 'conversationId', 'ownerId', 'readerId']) {
        require(/^[a-f0-9]{8}-(?:[a-f0-9]{4}-){3}[a-f0-9]{12}$/.test(result.receipt[name]));
      }
      const environmentKeys = ['SOURCE_SHA', 'WORKSPACE_ID', 'CONVERSATION_ID', 'MEMBER_COUNT',
        'OWNER_ID', 'OWNER_EMAIL', 'OWNER_PASSWORD', 'READER_ID', 'READER_EMAIL', 'READER_PASSWORD']
        .map(name => `COGLATAS_UI07_ROSTER_${name}`);
      require(result.environment && Object.keys(result.environment).length === environmentKeys.length &&
        environmentKeys.every(name => typeof result.environment[name] === 'string' && result.environment[name].length > 0) &&
        result.environment.COGLATAS_UI07_ROSTER_SOURCE_SHA === sourceSha &&
        result.environment.COGLATAS_UI07_ROSTER_MEMBER_COUNT === '43');
      Object.assign(io.environment, result.environment);
      fixtureReceipt = result.receipt;
      saveReceipt(false);
    },
    playwrightArgs(args, environment) {
      const names = Object.keys(environment).filter(name => name.startsWith('COGLATAS_UI07_ROSTER_'));
      require(names.length > 0 && fixtureReceipt);
      require(browserStartedAt === null);
      browserStartedAt = Math.floor(Date.now() / 1000) * 1000;
      environment.COGLATAS_UI07_ROSTER_PRIVATE_OUTPUT_DIR = privateOutput;
      args.push('--no-deps', '--name', browserName);
      for (const name of [...names, 'COGLATAS_REAL_BACKEND_UI07_READONLY_ROSTER', 'COGLATAS_UI07_ROSTER_PRIVATE_OUTPUT_DIR']) args.push('--env', name);
      args.push('--entrypoint', 'bash', 'real-backend-playwright', '-lc',
        'npm ci && node node_modules/@playwright/test/cli.js test --config playwright.readonly-roster-real.config.ts');
      return args;
    },
    async browserFinished(io) {
      require(initialCaptured && browserStartedAt !== null && !browserRegistered);
      const value = await metadata(io, 'container', browserName);
      require(value.name === `/${browserName}` && value.labels['com.docker.compose.service'] === 'real-backend-playwright' &&
        Date.parse(value.created) >= browserStartedAt && !owned.container.has(value.id));
      owned.container.set(value.id, value);
      const volumeName = `${projectName}_real_backend_smoke_node_modules`;
      const volume = await metadata(io, 'volume', volumeName);
      if (owned.volume.has(volumeName)) require(resourceIdentity(owned.volume.get(volumeName)) === resourceIdentity(volume));
      else {
        require(Date.parse(volume.created) >= browserStartedAt);
        owned.volume.set(volumeName, volume);
      }
      browserRegistered = true;
      await assertKnown(io);
      browserReceipts = readBrowserReceipts(privateOutput, sourceSha);
      saveReceipt(false);
      require(browserReceipts.length === 2 && browserReceipts.every(value => value.status === 'PASS'));
    },
    async cleanup(io, succeeded) {
      if (!admitted) {
        saveReceipt(false);
        return { exitCode: 1 };
      }
      try {
        await assertKnown(io);
        require(owned.container.size > 0);
        // Revalidate every immutable ID/name immediately before a scoped operation.
        for (const kind of ['container', 'volume', 'network']) {
          for (const id of owned[kind].keys()) await metadata(io, kind, id);
        }
        if (!succeeded) {
          for (const id of owned.container.keys()) await command(io, ['container', 'stop', id]);
          cleanupState = 'STOPPED_OWNED_RETAINED_FAILURE';
        } else {
          for (const id of owned.container.keys()) await command(io, ['container', 'rm', '-f', id]);
          for (const id of owned.network.keys()) await command(io, ['network', 'rm', id]);
          // Docker volumes have no immutable ID. Retain them even on success;
          // matching name/birth metadata is not proof against same-name reuse.
          cleanupState = 'REMOVED_OWNED_COMPUTE_RETAINED_VOLUMES';
        }
      } catch {
        cleanupState = 'PARTIAL_OR_UNVERIFIED_RETAINED';
        saveReceipt(false);
        return { exitCode: 1 };
      }
      saveReceipt(succeeded);
      return { exitCode: succeeded ? 0 : 1 };
    },
  };
}

async function main() {
  try {
    const sourceSha = validateRosterInvocation(process.env, process.argv.slice(2),
      args => execFileSync('git', args, { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] }));
    const token = randomUUID().replaceAll('-', '');
    const projectName = `coglatas-ui07-roster-${token}`;
    const directory = join(process.cwd(), 'artifacts', 'roster-real', token);
    const reuse = process.env.COGLATAS_REUSE_PREBUILT_APP_IMAGE === '1';
    const environment = process.env;
    for (const name of ['DEBUG', 'PWDEBUG', 'NODE_OPTIONS', 'PLAYWRIGHT_JSON_OUTPUT_NAME', 'PLAYWRIGHT_JUNIT_OUTPUT_NAME']) delete environment[name];
    environment.REAL_BACKEND_SMOKE_COMPOSE_PROJECT_NAME = projectName;
    environment.COGLATAS_REAL_BACKEND_UI07_READONLY_ROSTER = '1';
    if (!reuse) environment.COGLATAS_APP_IMAGE = `coglatas-ui07-roster:${token}`;
    const { prepareReadOnlyRosterFixture } = await import('./readonly-roster-fixture.mjs');
    const control = createRosterControl({ sourceSha, projectName, token, directory,
      reuse, fixture: prepareReadOnlyRosterFixture });
    const { runRealBackendSmokeCompose } = await import('./run-real-backend-smoke-compose.mjs');
    process.exitCode = await runRealBackendSmokeCompose(control);
    console.log(`UI07 ReadOnly roster ${process.exitCode === 0 ? 'PASS' : 'FAIL'}; source=${sourceSha}; structural receipt retained.`);
  } catch {
    console.error('UI07 ReadOnly roster failed before qualification; no target or release credit.');
    process.exitCode = 1;
  }
}

if (process.argv[1] && pathToFileURL(process.argv[1]).href === import.meta.url) await main();
