// Audit-only: never opens a DB/socket, reads an archive or runs restore commands.
export function syntheticRestoreOrder(manifest) {
  const invalid=()=>{throw Error("INVALID_SYNTHETIC_RESTORE_MANIFEST")};
  const keys=(o,n)=>o!==null&&typeof o==="object"&&!Array.isArray(o)&&
    Object.keys(o).length===n.length&&n.every(name=>Object.hasOwn(o,name));
  const valid=(v,n)=>typeof v==="string"&&new RegExp(`^[a-f0-9]{${n}}$`).test(v);
  const identifier=v=>typeof v==="string"&&v.length<128&&v.split(".").length===2&&v.split(".").every(s=>/^[a-zA-Z_][a-zA-Z_0-9]{0,62}$/.test(s))&&!/^(pg_catalog|information_schema|pg_toast|pg_temp)/.test(v.split(".")[0]);
  if(!keys(manifest,["version","scope","sourceSha","catalogSha256","archiveTocSha256","counts","completeInventory","targetEmpty","tables","foreignKeys","userTriggers"])||
    manifest.version!==1||manifest.scope!=="ISOLATED_SYNTHETIC_OFFLINE_ONLY"||
    !valid(manifest.sourceSha,40)||!valid(manifest.catalogSha256,64)||!valid(manifest.archiveTocSha256,64)||
    !keys(manifest.counts,["tables","foreignKeys","userTriggers","dataEntries"])||
    !Array.isArray(manifest.tables)||!Array.isArray(manifest.foreignKeys)||!Array.isArray(manifest.userTriggers)||
    !manifest.tables.length||manifest.tables.length>1024||manifest.foreignKeys.length>4096||manifest.userTriggers.length>4096||
    typeof manifest.completeInventory!=="boolean"||typeof manifest.targetEmpty!=="boolean"||
    ["tables","foreignKeys","userTriggers","dataEntries"].some(name=>
      !Number.isSafeInteger(manifest.counts[name])||manifest.counts[name]<0||manifest.counts[name]>4096))invalid();
  const hazards=new Set();
  if(!manifest.completeInventory)hazards.add("INVENTORY_INCOMPLETE");
  if(!manifest.targetEmpty)hazards.add("TARGET_NOT_VERIFIED_EMPTY");
  if(manifest.counts.tables!==manifest.tables.length||manifest.counts.foreignKeys!==manifest.foreignKeys.length||manifest.counts.userTriggers!==manifest.userTriggers.length)hazards.add("CATALOG_COUNT_MISMATCH");
  const tables=new Map();
  for(const row of manifest.tables){
    if(!keys(row,["name","archiveData"])||!identifier(row.name)||typeof row.archiveData!=="boolean"||tables.has(row.name))invalid();
    tables.set(row.name,row.archiveData);
    if(!row.archiveData)hazards.add("TABLE_DATA_MISSING");
  }
  if(manifest.counts.dataEntries!==[...tables.values()].filter(Boolean).length)hazards.add("ARCHIVE_DATA_COUNT_MISMATCH");
  const deps=new Map([...tables.keys()].map(x=>[x,new Set()]));
  const children=new Map([...tables.keys()].map(x=>[x,new Set()]));
  const fks=new Set(),selfReferences=[];
  for(const fk of manifest.foreignKeys){
    if(!keys(fk,["name","child","parent","validated"])||!identifier(fk.child)||!identifier(fk.parent)||
      typeof fk.name!=="string"||!/^[a-zA-Z_][a-zA-Z_0-9]{0,62}$/.test(fk.name)||typeof fk.validated!=="boolean"||fks.has(JSON.stringify([fk.child,fk.name])))invalid();
    fks.add(JSON.stringify([fk.child,fk.name]));
    if(!fk.validated)hazards.add("UNVALIDATED_FOREIGN_KEY");
    if(!tables.has(fk.child)||!tables.has(fk.parent)){hazards.add("FK_TABLE_MISSING_FROM_ARCHIVE");continue}
    if(fk.child===fk.parent){selfReferences.push(fk.name);continue}
    deps.get(fk.child).add(fk.parent);
    children.get(fk.parent).add(fk.child);
  }
  const triggers=new Set();
  for(const t of manifest.userTriggers){
    if(!keys(t,["name","table","timing","enabled"])||typeof t.name!=="string"||!/^[a-zA-Z_][a-zA-Z_0-9]{0,62}$/.test(t.name)||
      !identifier(t.table)||!["BEFORE_INSERT","AFTER_INSERT"].includes(t.timing)||typeof t.enabled!=="boolean"||triggers.has(JSON.stringify([t.table,t.name])))invalid();
    triggers.add(JSON.stringify([t.table,t.name]));
    if(!tables.has(t.table))hazards.add("TRIGGER_TABLE_NOT_ARCHIVED");
    if(t.enabled)hazards.add("ENABLED_USER_TRIGGER_REQUIRES_SIDE_EFFECT_PROOF");
  }
  const ready=[...tables.keys()].filter(x=>deps.get(x).size===0).sort(),order=[];
  while(ready.length){
    const x=ready.shift();
    order.push(x);
    for(const c of children.get(x)){
      deps.get(c).delete(x);
      if(deps.get(c).size===0){ready.push(c);ready.sort()}
    }
  }
  if(order.length!==tables.size)hazards.add("CROSS_TABLE_FOREIGN_KEY_CYCLE");
  const blocked=hazards.size>0;
  return {
    schema:"coglatas-synthetic-restore-data-order-advisory-v1",
    sourceSha:manifest.sourceSha,catalogSha256:manifest.catalogSha256,archiveTocSha256:manifest.archiveTocSha256,
    status:blocked?"BLOCKED":"REVIEW_ONLY_NOT_RESTORE_VERIFIED",
    candidateTableDataOrder:blocked?[]:order,selfForeignKeys:selfReferences.sort(),blockingReasons:[...hazards].sort(),
    additionalRequiredEvidence:["EXACT_ORIGINAL_EF_DDL_AND_PG_ARCHIVE","STRICT_SCHEMA_ROLE_RLS_CATALOG_COMPARISON",
      "REAL_POSTGRESQL_TRIGGER_SEQUENCE_ROW_AND_FK_PROOF","RESTORED_APPLICATION_FILES_KEYS_LOGIN_AUDIT",
      "APPROVED_ISOLATED_HTTPS_RESTART_AND_ROLLBACK"]
  };
}

import {constants, lstatSync, openSync, fstatSync, readSync, closeSync} from 'node:fs';

const maxManifestBytes = 262144;

// Open nonblocking before inspecting the descriptor: a checked regular path can
// be replaced with a FIFO between lstat and open. Never read an unbounded path.
export function readSyntheticManifest(filePath) {
  const invalid = () => { throw Error('INVALID_SYNTHETIC_RESTORE_MANIFEST'); };
  const sameFile = (left, right) => left.dev === right.dev && left.ino === right.ino;
  const boundedRegular = stat => stat.isFile() && Number.isSafeInteger(stat.size) &&
    stat.size >= 0 && stat.size <= maxManifestBytes;
  const initial = lstatSync(filePath);
  if (!boundedRegular(initial)) invalid();
  const descriptor = openSync(filePath,
    constants.O_RDONLY | (constants.O_NONBLOCK ?? 0) | (constants.O_NOFOLLOW ?? 0));
  try {
    const opened = fstatSync(descriptor);
    if (!boundedRegular(opened) || !sameFile(initial, opened) || initial.size !== opened.size) invalid();
    const bytes = Buffer.alloc(maxManifestBytes + 1);
    let length = 0;
    while (length < bytes.length) {
      const count = readSync(descriptor, bytes, length, bytes.length - length, length);
      if (count === 0) break;
      length += count;
    }
    const final = fstatSync(descriptor);
    if (length > maxManifestBytes || !boundedRegular(final) || !sameFile(opened, final) ||
        length !== opened.size || final.size !== opened.size ||
        final.mtimeMs !== opened.mtimeMs || final.ctimeMs !== opened.ctimeMs) invalid();
    return JSON.parse(bytes.subarray(0, length).toString('utf8'));
  } finally {
    closeSync(descriptor);
  }
}

import {pathToFileURL} from 'node:url';
if(process.argv[1]&&import.meta.url===pathToFileURL(process.argv[1]).href){
  if(process.argv.length!==4||process.argv[2]!=='--synthetic-manifest'){
    process.stderr.write('Usage: node restore-data-dependency-advisor.mjs --synthetic-manifest <offline-json>\n');
    process.exitCode=2;
  }else{
    try{
      const advice=syntheticRestoreOrder(readSyntheticManifest(process.argv[3]));
      process.stdout.write(JSON.stringify(advice,null,2)+'\n');
      if(advice.status==='BLOCKED')process.exitCode=1;
    }catch{
      process.stderr.write('INVALID_SYNTHETIC_RESTORE_MANIFEST\n');
      process.exitCode=2;
    }
  }
}
