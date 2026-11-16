import { realpathSync } from 'node:fs';
import { pathToFileURL } from 'node:url';
import { CATALOG_SQL } from './run-synthetic-logical-recovery.mjs';
import { TRIGGER_CATALOG_SQL, logicalTriggerCatalog } from './synthetic-trigger-catalog.mjs';
import { planRestoreList } from './synthetic-restore-list.mjs';

// Reuse Main's original logical schema and fully validated trigger/firing-order
// projection for the bounded MAIN_PORTABLE_LOGICAL_V1 synthetic acceptance gate.
// Raw PostgreSQL catalog-byte differences remain separately recorded; this
// projection does not authorize production restore, backup strategy or release.
const FIELDS = Object.freeze(['relations', 'constraints', 'indexes', 'triggers',
  'functions', 'policies', 'schemas', 'defaults', 'roles', 'memberships', 'migrations']);
const reject = () => {
  const error = new Error('Private portable catalog rejected.');
  error.code = 'PORTABLE_CATALOG'; throw error;
};
const require = value => { if (!value) reject(); };

export function portableCatalog(catalog, triggers) {
  require(catalog && typeof catalog === 'object' && !Array.isArray(catalog));
  require(Object.keys(catalog).length === FIELDS.length &&
    FIELDS.every(key => Object.hasOwn(catalog, key) && Array.isArray(catalog[key])));
  require(Array.isArray(triggers) && catalog.triggers.length === triggers.length);
  // The original raw trigger tuple inventory is fully reconciled with the
  // PostgreSQL 18 native projection before any physical RI name is abstracted.
  const projectedTriggers = logicalTriggerCatalog(triggers, catalog.triggers);
  return { version: 1, catalog: { ...catalog, triggers: projectedTriggers } };
}

async function stdinBounded() {
  const chunks = []; let length = 0;
  for await (const chunk of process.stdin) {
    length += chunk.length;
    require(length > 0 && length <= 16 * 1024 * 1024);
    chunks.push(chunk);
  }
  require(length > 0);
  return Buffer.concat(chunks);
}

async function main() {
  require(process.argv.length === 3 && ['queries', 'project', 'plan'].includes(process.argv[2]));
  if (process.argv[2] === 'queries') {
    process.stdout.write(JSON.stringify({ catalog: CATALOG_SQL, triggers: TRIGGER_CATALOG_SQL }));
    return;
  }
  if (process.argv[2] === 'plan') {
    // Only the source-owned Main TOC planner may reorder original FK slots.
    process.stdout.write(planRestoreList(await stdinBounded()).bytes);
    return;
  }
  const source = JSON.parse((await stdinBounded()).toString('utf8'));
  require(source && typeof source === 'object' && !Array.isArray(source) &&
    Object.keys(source).length === 2 && Object.hasOwn(source, 'catalog') &&
    Object.hasOwn(source, 'triggers'));
  const result = Buffer.from(JSON.stringify(portableCatalog(source.catalog, source.triggers)));
  require(result.length > 0 && result.length <= 16 * 1024 * 1024);
  process.stdout.write(result);
}

if (process.argv[1] && pathToFileURL(realpathSync(process.argv[1])).href === import.meta.url) {
  try { await main(); } catch {
    // No raw catalog values or SQL diagnostics may enter public job logs.
    process.stderr.write('PORTABLE_CATALOG_REJECTED\n');
    process.exitCode = 1;
  }
}
