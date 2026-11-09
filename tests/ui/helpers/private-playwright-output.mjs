import { lstatSync, mkdirSync, realpathSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { homedir } from 'node:os';
import { dirname, isAbsolute, join, resolve } from 'node:path';
import { createRequire } from 'node:module';
import { fileURLToPath } from 'node:url';

const require = createRequire(import.meta.url);
const outputOwners = new Set(['mobile', 'thread']);

function privateDirectory(path, uid) {
  const target = resolve(path), chain = [];
  for (let current = target;; current = dirname(current)) {
    chain.unshift(current);
    if (dirname(current) === current) break;
  }
  for (const current of chain) {
    try { mkdirSync(current, { mode: 0o700 }); }
    catch (error) { if (error.code !== 'EEXIST') throw error; }
    const stat = lstatSync(current);
    if (unsafeAncestor(stat, uid)) throw new Error('Unsafe directory.');
    if (current === target && !ownedPrivatePermissions(stat, uid)) throw new Error('Unsafe private directory.');
  }
}

function reloadBootstrap() {
  try {
    const library = join(dirname(require.resolve('playwright/package.json')), 'lib');
    const entry = realpathSync(process.argv[1]);
    return entry === realpathSync(join(library, 'loader/loaderProcessEntry.js')) ||
      (process.env.TEST_WORKER_INDEX !== undefined && entry === realpathSync(join(library, 'worker/workerProcessEntry.js')));
  } catch { return false; }
}

export function privatePlaywrightOutput(kind) {
  if (!outputOwners.has(kind)) throw new Error('Unknown Playwright output owner.');
  const options = require('playwright/lib/program').program.commands.find(command => command.name() === 'test').opts();
  const history = options.lastFailedFile ?? process.env.PLAYWRIGHT_LAST_RUN_OUTPUT_FILE;
  // These CLI modes never consume the configured default output directory.
  if (options.output || (options.list && (!options.lastFailed || history))) return undefined;
  if (process.platform !== 'linux') {
    // SDK reloads source before reapplying the parent's serialized CLI overrides.
    if (reloadBootstrap()) return undefined;
    throw new Error('Native non-Linux Playwright runs require an explicit trusted --output directory.');
  }
  const configured = process.env.XDG_CACHE_HOME;
  const cache = configured && isAbsolute(configured) ? configured : join(homedir(), '.cache');
  const repository = realpathSync(fileURLToPath(new URL('../../../', import.meta.url)));
  const fingerprint = createHash('sha256').update(repository).digest('hex');
  const parent = join(cache, 'coglatas', 'playwright-output', fingerprint, kind);
  // Workers reload source before the SDK reapplies serialized CLI overrides.
  // Main has already initialized its private default; explicit CLI output needs no default IO.
  if (!reloadBootstrap()) {
    try { privateDirectory(parent, process.getuid()); }
    catch { throw new Error('Private Playwright output is unavailable. Select a writable private XDG_CACHE_HOME or an explicit trusted --output directory.'); }
  }
  return join(parent, 'playwright-output');
}

function ownedPrivatePermissions(stat, uid) {
  return stat.uid === uid && (stat.mode & 0o777) === 0o700;
}

function unsafeAncestor(stat, uid) {
  return !stat.isDirectory() || stat.isSymbolicLink() || (stat.uid !== 0 && stat.uid !== uid) ||
        ((stat.mode & 0o022) !== 0 && (stat.mode & 0o1000) === 0);
}
