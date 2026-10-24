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
