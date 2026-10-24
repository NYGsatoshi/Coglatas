import test from 'node:test';
import assert from 'node:assert/strict';
import {syntheticRestoreOrder as plan} from '../../scripts/ci/restore-data-dependency-advisor.mjs';
const manifest=(tables=['public.users'],foreignKeys=[],userTriggers=[])=>({
  version:1,scope:'ISOLATED_SYNTHETIC_OFFLINE_ONLY',sourceSha:'a'.repeat(40),
  catalogSha256:'b'.repeat(64),archiveTocSha256:'c'.repeat(64),
  counts:{tables:tables.length,foreignKeys:foreignKeys.length,userTriggers:userTriggers.length,dataEntries:tables.length},
  completeInventory:true,targetEmpty:true,tables:tables.map(name=>({name,archiveData:true})),foreignKeys,userTriggers,
});
const fk=(name,child,parent,validated=true)=>({name,child,parent,validated});
test('original-DDL data-only users must load before announcements',()=>{
  const a=plan(manifest(['public.announcements','public.users'],[
    fk('FK_announcements_users_AuthorUserId','public.announcements','public.users')
  ]));
  assert.equal(a.status,'REVIEW_ONLY_NOT_RESTORE_VERIFIED');
  assert.deepEqual(a.candidateTableDataOrder,['public.users','public.announcements']);
  assert.ok(a.additionalRequiredEvidence.includes('STRICT_SCHEMA_ROLE_RLS_CATALOG_COMPARISON'));
});
test('independent rows have deterministic lexical order',()=>{
  assert.deepEqual(plan(manifest(['public.z','public.a','public.m'])).candidateTableDataOrder,
    ['public.a','public.m','public.z']);
});
test('multi-edge DAG parents load before child',()=>{
  const a=plan(manifest(['public.c','public.a','public.b'],[
    fk('f1','public.c','public.a'),fk('f2','public.c','public.b'),fk('f3','public.b','public.a')
  ]));
  assert.deepEqual(a.candidateTableDataOrder,['public.a','public.b','public.c']);
});
test('duplicate dependency edges do not allow premature ready child',()=>{
  const a=plan(manifest(['public.b','public.a'],[fk('f1','public.b','public.a'),fk('f2','public.b','public.a')]));
  assert.deepEqual(a.candidateTableDataOrder,['public.a','public.b']);
});
test('self reference recorded, does not qualify self-FK COPY semantics',()=>{
  const a=plan(manifest(['public.users'],[fk('f_self','public.users','public.users')]));
  assert.deepEqual(a.selfForeignKeys,['f_self']);
  assert.equal(a.status,'REVIEW_ONLY_NOT_RESTORE_VERIFIED');
});
test('FK cycle blocks every proposed load order',()=>{
  const a=plan(manifest(['public.a','public.b'],[fk('f1','public.a','public.b'),fk('f2','public.b','public.a')]));
  assert.equal(a.status,'BLOCKED');assert.deepEqual(a.candidateTableDataOrder,[]);
  assert.ok(a.blockingReasons.includes('CROSS_TABLE_FOREIGN_KEY_CYCLE'));
});
test('unknown parent, missing table-data entry and catalog/TOC mismatch all block',()=>{
  const unknown=plan(manifest(['public.child'],[fk('f','public.child','public.parent')]));
  assert.ok(unknown.blockingReasons.includes('FK_TABLE_MISSING_FROM_ARCHIVE'));
  const incomplete=manifest();incomplete.tables[0].archiveData=false;
  assert.ok(plan(incomplete).blockingReasons.includes('TABLE_DATA_MISSING'));
  const wrong=manifest();wrong.counts.dataEntries+=1;
  assert.ok(plan(wrong).blockingReasons.includes('ARCHIVE_DATA_COUNT_MISMATCH'));
});
test('active BEFORE/AFTER insert user triggers block; disabled triggers are only advisory',()=>{
  for(const timing of ['BEFORE_INSERT','AFTER_INSERT']){
    const x=plan(manifest(['public.files'],[],[{name:'t',table:'public.files',timing,enabled:true}]));
    assert.ok(x.blockingReasons.includes('ENABLED_USER_TRIGGER_REQUIRES_SIDE_EFFECT_PROOF'));
  }
  assert.equal(plan(manifest(['public.files'],[],[
    {name:'t',table:'public.files',timing:'AFTER_INSERT',enabled:false}
  ])).status,'REVIEW_ONLY_NOT_RESTORE_VERIFIED');
});
test('missing trigger table, nonempty target, inventory incomplete and unvalidated FK block',()=>{
  assert.ok(plan(manifest(['public.a'],[],[
    {name:'t',table:'public.b',timing:'AFTER_INSERT',enabled:false}
  ])).blockingReasons.includes('TRIGGER_TABLE_NOT_ARCHIVED'));
  const m=manifest();m.targetEmpty=false;m.completeInventory=false;
  assert.equal(plan(m).status,'BLOCKED');
  const f=manifest(['public.a','public.b'],[fk('f','public.a','public.b',false)]);
  assert.ok(plan(f).blockingReasons.includes('UNVALIDATED_FOREIGN_KEY'));
});
test('malformed or suspicious metadata never emits success',()=>{
  const values=[];
  const a=manifest();a.tables[0].payload='private';values.push(a);
  const b=manifest();b.sourceSha='bad';values.push(b);
  const c=manifest();c.archiveTocSha256='bad';values.push(c);
  const d=manifest(['pg_catalog.pg_class']);values.push(d);
  const e=manifest(['public.a;DROP']);values.push(e);
  const f=manifest(['public.a','public.a']);values.push(f);
  const g=manifest();g.counts.foreignKeys=-1;values.push(g);
  const h=manifest(['public.a','public.b'],[fk('same','public.a','public.b'),fk('same','public.a','public.b')]);values.push(h);
  for(const value of values)assert.throws(()=>plan(value),/INVALID_SYNTHETIC_RESTORE_MANIFEST/);
});


import {mkdtempSync, writeFileSync, readFileSync, rmSync, symlinkSync} from 'node:fs';
import {tmpdir} from 'node:os';
import {join} from 'node:path';
import {fileURLToPath} from 'node:url';
import {spawnSync} from 'node:child_process';

const advisorPath = fileURLToPath(new URL('../../scripts/ci/restore-data-dependency-advisor.mjs', import.meta.url));
const inputLimit = 262144;

function cliFixture(action) {
  const directory = mkdtempSync(join(tmpdir(), 'coglatas-restore-advisor-'));
  const input = join(directory, 'manifest.json');
  writeFileSync(input, JSON.stringify(manifest()));
  try { action({directory, input}); }
  finally { rmSync(directory, {recursive:true, force:true}); }
}

function runCli(input, directory, mutation = '') {
  const receipt = join(directory, 'io-receipt.json');
  const probe = `
    import fs from 'node:fs';
    import {syncBuiltinESMExports} from 'node:module';
    import {execFileSync} from 'node:child_process';
    const input = ${JSON.stringify(input)};
    const receipt = ${JSON.stringify(receipt)};
    const originalOpen = fs.openSync, originalRead = fs.readSync, originalClose = fs.closeSync;
    const stages = {opens:0, closes:0, reads:0, bytes:0, nonblocking:false};
    let active = null;
    fs.openSync = (path, flags, ...rest) => {
      if (path !== input) return originalOpen(path, flags, ...rest);
      stages.opens++;
      stages.nonblocking = (flags & fs.constants.O_NONBLOCK) !== 0;
      ${mutation}
      active = originalOpen(path, flags, ...rest);
      return active;
    };
    fs.readSync = (fd, buffer, offset, length, position) => {
      if (fd === active) stages.reads++;
      const count = originalRead(fd, buffer, offset, length, position);
      if (fd === active) stages.bytes += count;
      return count;
    };
    fs.closeSync = fd => {
      if (fd === active) { stages.closes++; active = null; }
      return originalClose(fd);
    };
    syncBuiltinESMExports();
    process.on('exit', () => fs.writeFileSync(receipt, JSON.stringify(stages)));
  `;
  const result = spawnSync(process.execPath,
    ['--import', `data:text/javascript,${encodeURIComponent(probe)}`, advisorPath, '--synthetic-manifest', input],
    {encoding:'utf8', timeout:3000, maxBuffer:16384});
  assert.equal(result.error, undefined, 'CLI must finish without a timeout or loader failure');
  assert.equal(result.signal, null);
  return {...result, stages:JSON.parse(readFileSync(receipt, 'utf8'))};
}

function assertInvalidCli(result) {
  assert.equal(result.status, 2);
  assert.equal(result.stdout, '');
  assert.equal(result.stderr, 'INVALID_SYNTHETIC_RESTORE_MANIFEST\n');
}

test('same catalog names on different tables remain distinct identities', () => {
  const advice = plan(manifest(['public.a','public.b','public.parent'], [
    fk('same', 'public.a', 'public.parent'), fk('same', 'public.b', 'public.parent')
  ], [
    {name:'same_trigger',table:'public.a',timing:'AFTER_INSERT',enabled:false},
    {name:'same_trigger',table:'public.b',timing:'AFTER_INSERT',enabled:false}
  ]));
  assert.equal(advice.status, 'REVIEW_ONLY_NOT_RESTORE_VERIFIED');
  assert.deepEqual(advice.candidateTableDataOrder, ['public.parent','public.a','public.b']);
});

test('same-table duplicate catalog identities still reject different payloads', () => {
  assert.throws(() => plan(manifest(['public.a','public.b','public.c'], [
    fk('same', 'public.a', 'public.b'), fk('same', 'public.a', 'public.c')
  ])), /INVALID_SYNTHETIC_RESTORE_MANIFEST/);
  assert.throws(() => plan(manifest(['public.a'], [], [
    {name:'same',table:'public.a',timing:'BEFORE_INSERT',enabled:false},
    {name:'same',table:'public.a',timing:'AFTER_INSERT',enabled:false}
  ])), /INVALID_SYNTHETIC_RESTORE_MANIFEST/);
});

test('every adjacent joined-key collision in each closed-shape partition rejects', () => {
  const partitions = [
    {keys:['version','scope','sourceSha','catalogSha256','archiveTocSha256','counts','completeInventory','targetEmpty','tables','foreignKeys','userTriggers'], select:m=>m},
    {keys:['tables','foreignKeys','userTriggers','dataEntries'], select:m=>m.counts},
    {keys:['name','archiveData'], select:m=>m.tables[0]},
    {keys:['name','child','parent','validated'], select:m=>m.foreignKeys[0]},
    {keys:['name','table','timing','enabled'], select:m=>m.userTriggers[0]}
  ];
  for (const partition of partitions) {
    const keys = [...partition.keys].sort();
    for (let index=0; index<keys.length-1; index++) {
      const value = manifest(['public.a','public.b'], [fk('fk', 'public.a', 'public.b')], [
        {name:'trigger',table:'public.a',timing:'AFTER_INSERT',enabled:false}
      ]);
      const row = partition.select(value), first=keys[index], second=keys[index+1];
      row[`${first}|${second}`] = row[first];
      delete row[first]; delete row[second];
      assert.throws(() => plan(value), /INVALID_SYNTHETIC_RESTORE_MANIFEST/);
    }
  }
});

test('every expected count is a bounded safe integer', () => {
  for (const key of ['tables','foreignKeys','userTriggers','dataEntries']) {
    for (const invalid of [undefined, null, '1', true, 0.5, NaN, Infinity, -1, 4097]) {
      const value = manifest(); value.counts[key] = invalid;
      assert.throws(() => plan(value), /INVALID_SYNTHETIC_RESTORE_MANIFEST/);
    }
  }
});

test('real CLI accepts small and exact-limit regular manifests with advisory output only', () => {
  cliFixture(({directory,input}) => {
    for (const exactLimit of [false,true]) {
      const value = JSON.stringify(manifest());
      writeFileSync(input, exactLimit ? value.padEnd(inputLimit, ' ') : value);
      const result = runCli(input, directory);
      assert.equal(result.status, 0); assert.equal(result.stderr, '');
      const advice = JSON.parse(result.stdout);
      assert.equal(advice.status, 'REVIEW_ONLY_NOT_RESTORE_VERIFIED');
      assert.deepEqual(advice.candidateTableDataOrder, ['public.users']);
      assert.equal(result.stages.opens, 1); assert.equal(result.stages.closes, 1);
      assert.ok(result.stages.bytes > 0 && result.stages.bytes <= inputLimit);
    }
  });
});

test('real CLI retains blocked exit1 and usage exit2 without raw input output', () => {
  cliFixture(({directory,input}) => {
    const value = manifest(); value.targetEmpty = false;
    writeFileSync(input, JSON.stringify(value));
    const result = runCli(input, directory);
    assert.equal(result.status, 1); assert.equal(result.stderr, '');
    const advice = JSON.parse(result.stdout);
    assert.equal(advice.status, 'BLOCKED'); assert.deepEqual(advice.candidateTableDataOrder, []);
    const usage = spawnSync(process.execPath, [advisorPath], {encoding:'utf8', timeout:3000});
    assert.equal(usage.error, undefined); assert.equal(usage.status, 2); assert.equal(usage.stdout, '');
    assert.match(usage.stderr, /^Usage: node restore-data-dependency-advisor\.mjs --synthetic-manifest <offline-json>\n$/);
  });
});

test('oversize regular input is rejected before opening or reading it', () => {
  cliFixture(({directory,input}) => {
    writeFileSync(input, Buffer.alloc(inputLimit+1, 32));
    const result = runCli(input, directory);
    assertInvalidCli(result);
    assert.equal(result.stages.opens, 0); assert.equal(result.stages.reads, 0);
  });
});

test('directory and symlink inputs never reach a data read', () => {
  cliFixture(({directory,input}) => {
    const link = join(directory, 'link.json'); symlinkSync(input, link);
    for (const path of [directory,link]) {
      const result = runCli(path, directory);
      assertInvalidCli(result);
      assert.equal(result.stages.opens, 0); assert.equal(result.stages.reads, 0);
    }
  });
});

test('regular-path replacement by a FIFO rejects without blocking or reading', () => {
  cliFixture(({directory,input}) => {
    const result = runCli(input, directory, `
      fs.renameSync(input, input+'.original');
      execFileSync('mkfifo', [input]);
    `);
    assertInvalidCli(result);
    assert.equal(result.stages.nonblocking, true);
    assert.equal(result.stages.opens, 1); assert.equal(result.stages.closes, 1);
    assert.equal(result.stages.reads, 0);
  });
});

test('replacement regular inode and growth before descriptor validation reject before read', () => {
  for (const mutation of [
    `fs.renameSync(input, input+'.original'); fs.copyFileSync(input+'.original', input);`,
    `const fd = originalOpen(input, 'a'); try { fs.writeSync(fd, ' '); } finally { originalClose(fd); }`
  ]) cliFixture(({directory,input}) => {
    const result = runCli(input, directory, mutation);
    assertInvalidCli(result);
    assert.equal(result.stages.closes, 1); assert.equal(result.stages.reads, 0);
  });
});

test('growth or shrink during a read cannot escape cap or publish an advisory', () => {
  for (const change of [
    `const writer = originalOpen(input, 'a'); try { fs.writeSync(writer, Buffer.alloc(${inputLimit}, 32)); } finally { originalClose(writer); }`,
    `const writer = originalOpen(input, 'r+'); try { fs.ftruncateSync(writer, 0); } finally { originalClose(writer); }`
  ]) cliFixture(({directory,input}) => {
    const result = runCli(input, directory, `
      fs.readSync = (fd, buffer, offset, length, position) => {
        if (fd === active && stages.reads === 0) { ${change} }
        if (fd === active) stages.reads++;
        const count = originalRead(fd, buffer, offset, length, position);
        if (fd === active) stages.bytes += count;
        return count;
      };
      syncBuiltinESMExports();
    `);
    assertInvalidCli(result);
    assert.equal(result.stages.closes, 1);
    assert.ok(result.stages.reads > 0 && result.stages.bytes <= inputLimit+1);
  });
});

test('malformed JSON and read errors close descriptors and emit only the fixed error', () => {
  cliFixture(({directory,input}) => {
    writeFileSync(input, '{synthetic-invalid-json');
    const malformed = runCli(input, directory);
    assertInvalidCli(malformed); assert.equal(malformed.stages.closes, 1);
    const result = runCli(input, directory, `
      fs.readSync = () => { throw Error('SYNTHETIC_RAW_ERROR_MUST_NOT_ESCAPE'); };
      syncBuiltinESMExports();
    `);
    assertInvalidCli(result); assert.equal(result.stages.closes, 1);
  });
});


test('partial regular-file reads retain byte bounds and the complete advisory', () => {
  cliFixture(({directory,input}) => {
    const result = runCli(input, directory, `
      fs.readSync = (fd, buffer, offset, length, position) => {
        if (fd === active) stages.reads++;
        const count = originalRead(fd, buffer, offset, Math.min(length, 17), position);
        if (fd === active) stages.bytes += count;
        return count;
      };
      syncBuiltinESMExports();
    `);
    assert.equal(result.status, 0); assert.equal(result.stderr, '');
    assert.equal(JSON.parse(result.stdout).status, 'REVIEW_ONLY_NOT_RESTORE_VERIFIED');
    assert.ok(result.stages.reads > 2 && result.stages.bytes > 0 && result.stages.bytes <= inputLimit);
    assert.equal(result.stages.closes, 1);
  });
});
