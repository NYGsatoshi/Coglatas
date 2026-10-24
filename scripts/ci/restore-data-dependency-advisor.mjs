// Audit-only: never opens a DB/socket, reads an archive or runs restore commands.
export function syntheticRestoreOrder(manifest) {
  const invalid=()=>{throw Error("INVALID_SYNTHETIC_RESTORE_MANIFEST")};
  const keys=(o,n)=>o!==null&&typeof o==="object"&&!Array.isArray(o)&&Object.keys(o).sort().join("|")===n.sort().join("|");
  const valid=(v,n)=>typeof v==="string"&&new RegExp(`^[a-f0-9]{${n}}$`).test(v);
  const identifier=v=>typeof v==="string"&&v.length<128&&v.split(".").length===2&&v.split(".").every(s=>/^[a-zA-Z_][a-zA-Z_0-9]{0,62}$/.test(s))&&!/^(pg_catalog|information_schema|pg_toast|pg_temp)/.test(v.split(".")[0]);
  if(!keys(manifest,["version","scope","sourceSha","catalogSha256","archiveTocSha256","counts","completeInventory","targetEmpty","tables","foreignKeys","userTriggers"])||
    manifest.version!==1||manifest.scope!=="ISOLATED_SYNTHETIC_OFFLINE_ONLY"||
    !valid(manifest.sourceSha,40)||!valid(manifest.catalogSha256,64)||!valid(manifest.archiveTocSha256,64)||
    !keys(manifest.counts,["tables","foreignKeys","userTriggers","dataEntries"])||
    !Array.isArray(manifest.tables)||!Array.isArray(manifest.foreignKeys)||!Array.isArray(manifest.userTriggers)||
    !manifest.tables.length||manifest.tables.length>1024||manifest.foreignKeys.length>4096||manifest.userTriggers.length>4096||
    typeof manifest.completeInventory!=="boolean"||typeof manifest.targetEmpty!=="boolean"||
    Object.values(manifest.counts).some(x=>!Number.isSafeInteger(x)||x<0||x>4096))invalid();
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
      typeof fk.name!=="string"||!/^[a-zA-Z_][a-zA-Z_0-9]{0,62}$/.test(fk.name)||typeof fk.validated!=="boolean"||fks.has(fk.name))invalid();
    fks.add(fk.name);
    if(!fk.validated)hazards.add("UNVALIDATED_FOREIGN_KEY");
    if(!tables.has(fk.child)||!tables.has(fk.parent)){hazards.add("FK_TABLE_MISSING_FROM_ARCHIVE");continue}
    if(fk.child===fk.parent){selfReferences.push(fk.name);continue}
    deps.get(fk.child).add(fk.parent);
    children.get(fk.parent).add(fk.child);
  }
  const triggers=new Set();
  for(const t of manifest.userTriggers){
    if(!keys(t,["name","table","timing","enabled"])||typeof t.name!=="string"||!/^[a-zA-Z_][a-zA-Z_0-9]{0,62}$/.test(t.name)||
      !identifier(t.table)||!["BEFORE_INSERT","AFTER_INSERT"].includes(t.timing)||typeof t.enabled!=="boolean"||triggers.has(t.name))invalid();
    triggers.add(t.name);
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

import {readFileSync} from 'node:fs';
import {pathToFileURL} from 'node:url';
if(process.argv[1]&&import.meta.url===pathToFileURL(process.argv[1]).href){
  if(process.argv.length!==4||process.argv[2]!=='--synthetic-manifest'){
    process.stderr.write('Usage: node restore-data-dependency-advisor.mjs --synthetic-manifest <offline-json>\n');
    process.exitCode=2;
  }else{
    try{
      const bytes=readFileSync(process.argv[3]);
      if(bytes.length>262144)throw Error('input too large');
      const advice=syntheticRestoreOrder(JSON.parse(bytes.toString('utf8')));
      process.stdout.write(JSON.stringify(advice,null,2)+'\n');
      if(advice.status==='BLOCKED')process.exitCode=1;
    }catch{
      process.stderr.write('INVALID_SYNTHETIC_RESTORE_MANIFEST\n');
      process.exitCode=2;
    }
  }
}
