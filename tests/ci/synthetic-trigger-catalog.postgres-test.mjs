// Explicit opt-in: creates only one fresh owned network-none PostgreSQL cluster.
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { createHash, randomBytes } from 'node:crypto';
import { existsSync, lstatSync, mkdirSync, readFileSync, realpathSync, writeFileSync } from 'node:fs';
import { isAbsolute, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { test } from 'node:test';
import { logicalTriggerCatalog, TRIGGER_CATALOG_SQL } from '../../scripts/ci/synthetic-trigger-catalog.mjs';
import { assertSameSnapshot, assertTruncatedArchiveFailure, corruptCopy } from '../../scripts/ci/run-synthetic-logical-recovery.mjs';
import { installRestoreList, planRestoreList } from '../../scripts/ci/synthetic-restore-list.mjs';

if (process.env.COGLATAS_RI_NATIVE_TEST !== '1') throw new Error('Explicit isolated native RI test opt-in is required.');
const root = fileURLToPath(new URL('../..', import.meta.url));
const dockerPath = process.env.COGLATAS_RI_DOCKER ?? '/usr/bin/docker';
if (!isAbsolute(dockerPath) || !existsSync(dockerPath) || !lstatSync(dockerPath).isFile() ||
    realpathSync(dockerPath) !== resolve(dockerPath)) throw new Error('A regular absolute Docker executable is required.');
const endpoint = process.platform === 'win32' ? 'npipe:////./pipe/dockerDesktopLinuxEngine' : 'unix:///var/run/docker.sock';
const image = 'postgres@sha256:77f585114c32fbca283dc835b0596f4e52b51b4c6662d7810b2f4084f60a1873';
const hash = value => createHash('sha256').update(value).digest('hex');

const FIXTURE_SQL = `
CREATE TABLE public."__EFMigrationsHistory"("MigrationId" varchar(150) PRIMARY KEY,"ProductVersion" varchar(32) NOT NULL);
INSERT INTO public."__EFMigrationsHistory" VALUES('synthetic_original','10.0.0');
CREATE TABLE public.parents(id integer PRIMARY KEY);
CREATE TABLE public.children(id integer PRIMARY KEY,retired text,parent_id integer NOT NULL,other_parent_id integer NOT NULL,
 CONSTRAINT aa_parent_fk FOREIGN KEY(parent_id) REFERENCES public.parents(id) DEFERRABLE INITIALLY IMMEDIATE,
 CONSTRAINT bb_parent_fk FOREIGN KEY(other_parent_id) REFERENCES public.parents(id));
ALTER TABLE public.children DROP COLUMN retired;
CREATE FUNCTION public.user_trigger() RETURNS trigger LANGUAGE plpgsql COST 100.5 AS $body$
BEGIN IF TG_OP='DELETE' THEN RETURN OLD; END IF; RETURN NEW; END;$body$;
CREATE TRIGGER zz_multi AFTER INSERT OR UPDATE ON public.children FOR EACH ROW EXECUTE FUNCTION public.user_trigger('original');
CREATE TRIGGER yy_columns AFTER UPDATE OF parent_id ON public.children FOR EACH ROW
 WHEN(OLD.parent_id IS DISTINCT FROM NEW.parent_id) EXECUTE FUNCTION public.user_trigger('column');
CREATE TRIGGER tt_transition AFTER UPDATE ON public.children REFERENCING OLD TABLE AS old_rows NEW TABLE AS new_rows
 FOR EACH STATEMENT EXECUTE FUNCTION public.user_trigger();
CREATE TRIGGER placeholder BEFORE INSERT ON public.children FOR EACH ROW EXECUTE FUNCTION public.user_trigger();
DO $rename$ DECLARE trigger_id oid; BEGIN
 SELECT oid INTO trigger_id FROM pg_trigger WHERE tgrelid='public.children'::regclass AND tgname='placeholder';
 EXECUTE format('ALTER TRIGGER placeholder ON public.children RENAME TO %I','RI_ConstraintTrigger_c_'||trigger_id::text);
END;$rename$;
CREATE TABLE public.partition_parent(id integer PRIMARY KEY) PARTITION BY RANGE(id);
CREATE TABLE public.partition_zero PARTITION OF public.partition_parent FOR VALUES FROM(0) TO(100);
CREATE TRIGGER zz_partition AFTER INSERT OR UPDATE ON public.partition_parent FOR EACH ROW EXECUTE FUNCTION public.user_trigger('partition');
CREATE TABLE public.z_first_child(id integer PRIMARY KEY,parent_id integer NOT NULL,
 CONSTRAINT z_first_fk FOREIGN KEY(parent_id) REFERENCES public.parents(id) ON DELETE RESTRICT);
CREATE TABLE public.a_later_child(id integer PRIMARY KEY,parent_id integer NOT NULL,
 CONSTRAINT a_later_fk FOREIGN KEY(parent_id) REFERENCES public.parents(id) ON DELETE CASCADE);
CREATE SCHEMA pgx;
CREATE TABLE pgx.inventory_control(id integer PRIMARY KEY,value text NOT NULL);
CREATE SEQUENCE pgx.sequence_control START 42;
CREATE TRIGGER namespace_trigger BEFORE INSERT OR UPDATE ON pgx.inventory_control FOR EACH ROW EXECUTE FUNCTION public.user_trigger('namespace');
INSERT INTO pgx.inventory_control VALUES(1,'original');
SELECT nextval('pgx.sequence_control');
CREATE ROLE pgfixture NOLOGIN;
INSERT INTO public.parents VALUES(1);
INSERT INTO public.children(id,parent_id,other_parent_id) VALUES(1,1,1);
INSERT INTO public.z_first_child VALUES(1,1);
INSERT INTO public.a_later_child VALUES(1,1);
INSERT INTO public.partition_parent VALUES(1);`;

test('native PostgreSQL 18.6 logical RI identities preserve exact schema and reject semantic/order changes', async t => {
  const nonce = randomBytes(6).toString('hex'), name = 'coglatas-ri-native-' + nonce;
  const directory = join(root, 'artifacts', 'recovery-ri-native', nonce);
  mkdirSync(directory, { recursive: true, mode: 0o700 });
  const config = join(directory, 'docker'); mkdirSync(config, { mode: 0o700 });
  const receipt = { schemaVersion: 1, scope: 'ISOLATED_SYNTHETIC_RI_CATALOG_ONLY', image,
    sourceSqlSha256: hash(Buffer.from(TRIGGER_CATALOG_SQL)), network: 'none', publicPorts: false,
    sourceFiles: Object.fromEntries(['scripts/ci/synthetic-trigger-catalog.mjs',
      'scripts/ci/run-synthetic-logical-recovery.mjs', 'scripts/ci/synthetic-restore-list.mjs',
      'tests/ci/synthetic-trigger-catalog.postgres-test.mjs']
      .map(file => [file, hash(readFileSync(join(root, file)))])),
    production: false, originalFailureErased: false, controls: [], cleanup: false };
  let created = false, originalFailure;
  const save = (file, bytes) => writeFileSync(join(directory, file), bytes, { mode: 0o600 });
  const docker = (args, input, allowed = false) => {
    const result = spawnSync(dockerPath, ['--config', config, '--host', endpoint, ...args], {
      input, timeout: 30_000, maxBuffer: 16 * 1024 * 1024, windowsHide: true,
    });
    if (result.error || result.signal || !Number.isInteger(result.status) || (!allowed && result.status !== 0)) {
      if (!existsSync(join(directory, 'first-command.stderr')))
        save('first-command.stderr', result.stderr ?? Buffer.alloc(0));
      throw new Error('Owned native PostgreSQL command failed.');
    }
    return result;
  };
  const exec = (args, input) => docker(['exec', ...(input === undefined ? [] : ['--interactive']), name, ...args], input);
  const psql = (database, sql) => exec(['psql', '-X', '-q', '-A', '-t', '-v', 'ON_ERROR_STOP=1', '-U', 'postgres', '-d', database], Buffer.from(sql)).stdout;
  const schema = database => exec(['pg_dump', '-U', 'postgres', '-d', database, '--schema-only', '--no-owner', '--no-privileges', '--restrict-key=' + nonce]).stdout;
  const driver = readFileSync(join(root, 'scripts/ci/run-synthetic-logical-recovery.mjs'), 'utf8');
  const rawSql = driver.match(/const CATALOG_SQL = String.raw`([\s\S]*?)`;/)?.[1];
  const rowsSql = driver.match(/const ROWS_SQL = String.raw`([\s\S]*?)`;/)?.[1];
  const sequencesSql = driver.match(/const SEQUENCES_SQL = String.raw`([\s\S]*?)`;/)?.[1];
  assert.ok(rawSql && rowsSql && sequencesSql);
  receipt.originalCatalogSqlSha256 = hash(Buffer.from(rawSql));
  receipt.originalRowsSqlSha256 = hash(Buffer.from(rowsSql));
  receipt.sequencesSqlSha256 = hash(Buffer.from(sequencesSql));
  const capture = (database, label) => {
    const rawBytes = psql(database, rawSql), triggerBytes = psql(database, TRIGGER_CATALOG_SQL), dataBytes = psql(database, rowsSql);
    const sequenceBytes = psql(database, sequencesSql);
    save(label + '.catalog.raw.json', rawBytes); save(label + '.triggers.raw.json', triggerBytes);
    save(label + '.rows.raw.json', dataBytes);
    save(label + '.sequences.raw.json', sequenceBytes);
    const raw = JSON.parse(rawBytes), rows = JSON.parse(triggerBytes);
    return { raw, rows, dataRows: JSON.parse(dataBytes), sequences: JSON.parse(sequenceBytes),
      logical: { ...raw, triggers: logicalTriggerCatalog(rows, raw.triggers) } };
  };
  const control = async (id, verify) => {
    await t.test(id, () => {
      try { verify(); receipt.controls.push({ id, status: 'PASS' }); }
      catch (error) {
        originalFailure ??= error; receipt.controls.push({ id, status: 'FAIL' });
        throw new Error('Isolated native RI control failed: ' + id);
      }
    });
    if (originalFailure) throw originalFailure;
  };
  const changed = async (id, sql, verify) => control(id, () => {
    psql('ri_target', 'BEGIN;\n' + sql + '\nCOMMIT;');
    const actual = capture('ri_target', id);
    verify(actual);
  });
  // Each negative uses a fresh transaction rolled back by the same psql session.
  const negative = async (id, sql, expected, extra) => control(id, () => {
    const captured = psql('ri_target', 'BEGIN;\n' + sql + '\n' + TRIGGER_CATALOG_SQL + '\nROLLBACK;');
    const rows = JSON.parse(captured); save(id + '.triggers.raw.json', captured);
    const raw = rows.map(row => [row.schema, row.relation, row.name, row.enabled, row.internal, row.definition]);
    // Query output retains the same PostgreSQL C-collated order as the full raw catalog.
    const actual = logicalTriggerCatalog(rows, raw);
    assert.notDeepEqual(actual, expected.logical.triggers);
    assert.throws(() => assertSameSnapshot(expected.logical, { ...expected.logical, triggers: actual }),
      { code: 'STRICT_SNAPSHOT' });
    if (extra) extra(actual, rows);
  });
  try {
    assert.equal(docker(['ps', '-aq', '--filter', 'name=^/' + name + '$']).stdout.toString().trim(), '');
    assert.equal(docker(['info', '--format', '{{.OSType}}']).stdout.toString().trim(), 'linux');
    docker(['run', '--pull', 'never', '--detach', '--rm', '--name', name, '--label', 'coglatas.ri-native=' + nonce, '--network', 'none', '--read-only',
      '--tmpfs', '/tmp:rw,size=33554432', '--tmpfs', '/var/run/postgresql:rw,size=16777216',
      '--tmpfs', '/var/lib/postgresql:rw,size=268435456', '--env', 'POSTGRES_HOST_AUTH_METHOD=trust', image]);
    created = true;
    let ready = false;
    for (let attempt = 0; attempt < 60; attempt++) {
      // pg_isready can accept the image's temporary initialization server.
      const finalServer = docker(['exec', name, '/bin/cat', '/proc/1/comm'], undefined, true).stdout.toString().trim() === 'postgres';
      if (finalServer && docker(['exec', name, 'pg_isready', '-U', 'postgres'], undefined, true).status === 0) {
        ready = true; receipt.readyPhase = 'FINAL_POSTGRES_PID1'; break;
      }
      await new Promise(done => setTimeout(done, 200));
    }
    assert.ok(ready);
    receipt.postgresVersionNum = Number(psql('postgres', "SELECT current_setting('server_version_num');").toString().trim());
    assert.equal(receipt.postgresVersionNum, 180006);
    exec(['createdb', '-U', 'postgres', 'ri_source']); exec(['createdb', '-U', 'postgres', 'ri_target']);
    psql('ri_source', FIXTURE_SQL);
    const before = capture('ri_source', 'source'), beforeSchema = schema('ri_source'); save('source.schema.sql', beforeSchema);
    const dump = exec(['pg_dump', '-U', 'postgres', '-d', 'ri_source', '--format=custom', '--no-owner', '--no-privileges']).stdout;
    save('database.dump', dump); receipt.dumpSha256 = hash(dump);
    const originalList = exec(['pg_restore', '--list'], dump).stdout, restoreList = planRestoreList(originalList);
    save('restore-original.list', originalList); save('restore-planned.list', restoreList.bytes);
    receipt.restoreList = { entryCount: restoreList.entryCount, foreignKeyCount: restoreList.foreignKeyCount,
      originalSha256: hash(originalList), plannedSha256: hash(restoreList.bytes) };
    installRestoreList((database, args, options) => {
      assert.equal(database, 'ri_target'); assert.deepEqual(args.slice(0, 3), ['exec', '-T', 'postgres']);
      return exec(args.slice(3), options.input);
    }, 'ri_target', restoreList.bytes);
    exec(['pg_restore', '-U', 'postgres', '-d', 'ri_target', '--no-owner', '--no-privileges', '--exit-on-error',
      '--use-list=/tmp/coglatas-recovery.list'], dump);
    receipt.restoreCount = 1;
    const restored = capture('ri_target', 'target'), afterSchema = schema('ri_target'); save('target.schema.sql', afterSchema);
    await control('one original custom dump/restore preserves raw schema and complete logical catalog', () => {
      assert.deepEqual(beforeSchema, afterSchema);
      assert.deepEqual(before.dataRows, restored.dataRows);
      assert.deepEqual(before.sequences, restored.sequences);
      assert.notDeepEqual(before.raw.triggers, restored.raw.triggers);
      assert.deepEqual(before.logical, restored.logical);
      const snapshot = (value, schemaBytes) => ({ schema: hash(schemaBytes), rows: value.dataRows,
        catalog: value.logical, sequences: value.sequences, uploads: [], keys: [] });
      assertSameSnapshot(snapshot(before, beforeSchema), snapshot(restored, afterSchema));
      assert.ok(restored.logical.triggers.rows.some(row => row.function.cost === '100.5'));
      assert.equal(before.logical.triggers.rowCount, before.raw.triggers.length);
      assert.equal(before.logical.triggers.rows.filter(row => row.identity[2][0] === 'generated-ri' &&
        row.constraint.relation === 'children').length, 8);
      assert.equal(before.logical.triggers.rows.filter(row => row.identity[2][0] === 'generated-ri').length, 16);
      assert.ok(before.logical.triggers.rows.some(row => row.parent !== null));
      assert.ok(before.logical.triggers.rows.some(row => row.identity[2][0] === 'exact-name' && row.identity[2][1].startsWith('RI_ConstraintTrigger_c_')));
      receipt.rawSchemaEqual = true; receipt.logicalCatalogEqual = true;
      receipt.schemaSha256 = hash(beforeSchema); receipt.triggerCount = before.raw.triggers.length;
    });
    await control('native TOC reverses cross-table FK creation order while complete plan preserves co-firing order', () => {
      const actionNames = before.rows.filter(row => row.relation === 'parents' && row.function.name.endsWith('_del'))
        .map(row => row.constraint.name);
      assert.ok(actionNames.indexOf('z_first_fk') < actionNames.indexOf('a_later_fk'));
      const a = ' FK CONSTRAINT public a_later_child ', z = ' FK CONSTRAINT public z_first_child ';
      assert.ok(originalList.includes(a) && originalList.includes(z));
      assert.ok(originalList.indexOf(a) < originalList.indexOf(z));
      assert.ok(restoreList.bytes.indexOf(z) < restoreList.bytes.indexOf(a));
      assert.equal(restoreList.foreignKeyCount, 4);
    });
    await control('same complete list preserves the original corrupt-copy rejection and a ready fault database', () => {
      exec(['createdb', '-U', 'postgres', 'ri_fault']);
      const rejected = docker(['exec', '--interactive', name, 'pg_restore', '-U', 'postgres', '-d', 'ri_fault',
        '--no-owner', '--no-privileges', '--exit-on-error', '--use-list=/tmp/coglatas-recovery.list'], corruptCopy(dump), true);
      save('fault-restore.stderr', rejected.stderr); receipt.faultRestoreCount = 1;
      assertTruncatedArchiveFailure(rejected);
      assert.equal(psql('ri_fault', 'SELECT 1;').toString().trim(), '1');
    });
    await control('complete user inventory includes pgx data sequence trigger and nonreserved pgfixture role', () => {
      assert.ok(restored.dataRows.some(row => row[0] === 'pgx' && row[1] === 'inventory_control' && row[2] === 1));
      assert.ok(restored.sequences.some(row => row[0] === 'pgx' && row[1] === 'sequence_control' && row[3] === '42'));
      assert.ok(restored.raw.schemas.some(row => row[0] === 'pgx'));
      assert.ok(restored.rows.some(row => row.schema === 'pgx' && row.name === 'namespace_trigger'));
      assert.ok(restored.raw.roles.some(row => row[0] === 'pgfixture'));
      assert.ok(!restored.raw.schemas.some(row => row[0].startsWith('pg_')));
      assert.ok(!restored.raw.roles.some(row => row[0].startsWith('pg_')));
      const temporary = JSON.parse(psql('ri_target', 'CREATE TEMP TABLE temp_scope_control(id integer);\n' + rowsSql));
      assert.ok(!temporary.some(row => row[0].startsWith('pg_') || row[1] === 'temp_scope_control'));
      assert.deepEqual(temporary, restored.dataRows);
    });
    await control('pgx-only restored row mutation must not pass the complete row oracle', () => {
      const original = psql('ri_target', rowsSql);
      psql('ri_target', "UPDATE pgx.inventory_control SET value='tampered' WHERE id=1;");
      try {
        assert.equal(psql('ri_target', 'SELECT value FROM pgx.inventory_control WHERE id=1;').toString().trim(), 'tampered');
        const altered = psql('ri_target', rowsSql);
        save('pgx-original.rows.json', original); save('pgx-tampered.rows.json', altered);
        assert.deepEqual(schema('ri_target'), afterSchema);
        assert.notDeepEqual(JSON.parse(altered), JSON.parse(original));
        assert.throws(() => assertSameSnapshot(JSON.parse(original), JSON.parse(altered)), { code: 'STRICT_SNAPSHOT' });
      } finally {
        psql('ri_target', "UPDATE pgx.inventory_control SET value='original' WHERE id=1;");
      }
    });
    await control('restored foreign keys reject a genuine missing parent', () => {
      const sql = `DO $check$ BEGIN
BEGIN INSERT INTO public.children(id,parent_id,other_parent_id) VALUES(2,999,999);
RAISE EXCEPTION 'Unexpected accepted foreign key'; EXCEPTION WHEN foreign_key_violation THEN NULL; END;
END;$check$;`;
      psql('ri_source', sql); psql('ri_target', sql);
    });
    await negative('missing-internal', 'ALTER TABLE public.children DROP CONSTRAINT aa_parent_fk;', restored);
    await negative('custom-internal-name', `DO $rename$ DECLARE original_name name; BEGIN
SELECT t.tgname INTO STRICT original_name FROM pg_trigger t JOIN pg_constraint c ON c.oid=t.tgconstraint
JOIN pg_proc f ON f.oid=t.tgfoid WHERE t.tgrelid='public.children'::regclass
AND c.conname='aa_parent_fk' AND f.proname='RI_FKey_check_ins';
EXECUTE format('ALTER TRIGGER %I ON public.children RENAME TO custom_internal_trigger',original_name);
END;$rename$;`, restored);
    await negative('disabled-internal', 'ALTER TABLE public.children DISABLE TRIGGER ALL;', restored);
    await negative('replica-state', 'ALTER TABLE public.children ENABLE REPLICA TRIGGER zz_multi;', restored);
    await negative('always-state', 'ALTER TABLE public.children ENABLE ALWAYS TRIGGER zz_multi;', restored);
    await negative('fk-action', 'ALTER TABLE public.children DROP CONSTRAINT aa_parent_fk; ALTER TABLE public.children ADD CONSTRAINT aa_parent_fk FOREIGN KEY(parent_id) REFERENCES public.parents(id) ON DELETE CASCADE DEFERRABLE;', restored);
    await negative('fk-deferrability', 'ALTER TABLE public.children ALTER CONSTRAINT aa_parent_fk NOT DEFERRABLE;', restored);
    await negative('fk-initially-deferred', 'ALTER TABLE public.children ALTER CONSTRAINT aa_parent_fk INITIALLY DEFERRED;', restored);
    await negative('user-name', 'ALTER TRIGGER zz_multi ON public.children RENAME TO zz_changed;', restored);
    await negative('user-function', "CREATE OR REPLACE FUNCTION public.user_trigger() RETURNS trigger LANGUAGE plpgsql AS $body$BEGIN RETURN NULL; END;$body$;", restored);
    await negative('fractional-function-cost', 'ALTER FUNCTION public.user_trigger() COST 100.75;', restored);
    await negative('update-columns', "DROP TRIGGER yy_columns ON public.children; CREATE TRIGGER yy_columns AFTER UPDATE OF id ON public.children FOR EACH ROW WHEN(OLD.parent_id IS DISTINCT FROM NEW.parent_id) EXECUTE FUNCTION public.user_trigger('column');", restored);
    await negative('arguments', "DROP TRIGGER zz_multi ON public.children; CREATE TRIGGER zz_multi AFTER INSERT OR UPDATE ON public.children FOR EACH ROW EXECUTE FUNCTION public.user_trigger('changed');", restored);
    await negative('when-condition', "DROP TRIGGER yy_columns ON public.children; CREATE TRIGGER yy_columns AFTER UPDATE OF parent_id ON public.children FOR EACH ROW WHEN(OLD.parent_id>0) EXECUTE FUNCTION public.user_trigger('column');", restored);
    await negative('timing', "DROP TRIGGER zz_multi ON public.children; CREATE TRIGGER zz_multi BEFORE INSERT OR UPDATE ON public.children FOR EACH ROW EXECUTE FUNCTION public.user_trigger('original');", restored);
    await negative('overlapping-event-mask', "DROP TRIGGER zz_multi ON public.children; CREATE TRIGGER zz_multi AFTER INSERT ON public.children FOR EACH ROW EXECUTE FUNCTION public.user_trigger('original');", restored);
    await negative('transition-alias', 'DROP TRIGGER tt_transition ON public.children; CREATE TRIGGER tt_transition AFTER UPDATE ON public.children REFERENCING OLD TABLE AS changed_old NEW TABLE AS new_rows FOR EACH STATEMENT EXECUTE FUNCTION public.user_trigger();', restored);
    await negative('partition-parent', 'ALTER TABLE public.partition_parent DETACH PARTITION public.partition_zero; CREATE TRIGGER zz_partition AFTER INSERT OR UPDATE ON public.partition_zero FOR EACH ROW EXECUTE FUNCTION public.user_trigger(\'partition\');', restored);
    await negative('ri-cofiring-order', `ALTER TABLE public.children DROP CONSTRAINT aa_parent_fk; ALTER TABLE public.children DROP CONSTRAINT bb_parent_fk;
ALTER TABLE public.children ADD CONSTRAINT bb_parent_fk FOREIGN KEY(other_parent_id) REFERENCES public.parents(id);
ALTER TABLE public.children ADD CONSTRAINT aa_parent_fk FOREIGN KEY(parent_id) REFERENCES public.parents(id) DEFERRABLE INITIALLY IMMEDIATE;`, restored,
    actual => { assert.deepEqual(actual.rows, restored.logical.triggers.rows); assert.notDeepEqual(actual.firingOrders, restored.logical.triggers.firingOrders); });
    await changed('negative-transactions-restore-the-original-target', 'SELECT 1;', actual => assert.deepEqual(actual.logical, restored.logical));
    receipt.status = 'PASS';
  } catch (error) {
    originalFailure ??= error;
    save('first-failure.private.txt', String(error.stack ?? error)); receipt.status = 'ERROR';
  } finally {
    try {
      if (created) {
        const inspected = JSON.parse(docker(['inspect', name]).stdout)[0];
        assert.equal(inspected.Config.Labels['coglatas.ri-native'], nonce);
        docker(['rm', '--force', name]);
      }
      assert.equal(docker(['ps', '-aq', '--filter', 'label=coglatas.ri-native=' + nonce]).stdout.toString().trim(), '');
      assert.equal(docker(['volume', 'ls', '-q', '--filter', 'label=coglatas.ri-native=' + nonce]).stdout.toString().trim(), '');
      receipt.cleanup = true;
    } catch (error) {
      save('cleanup-failure.private.txt', String(error)); receipt.cleanup = false;
      originalFailure ??= error; receipt.status = 'ERROR';
    }
    save('receipt.json', JSON.stringify(receipt, null, 2) + '\n');
  }
  if (originalFailure) throw new Error('Isolated native RI verification failed; original private evidence retained.');
});
