// PostgreSQL 18 catalog identities are logical; original physical rows remain private evidence.
export const TRIGGER_CATALOG_SQL = `
SELECT coalesce(json_agg(value ORDER BY schema_name COLLATE "C",relation_name COLLATE "C",trigger_name COLLATE "C"),'[]')
FROM (
 SELECT n.nspname AS schema_name,r.relname AS relation_name,t.tgname AS trigger_name,
 json_build_object(
  'oid',t.oid::text,'parentOid',nullif(t.tgparentid,0)::text,
  'schema',n.nspname,'relation',r.relname,'name',t.tgname,
  'definition',pg_get_triggerdef(t.oid,false),
  'type',t.tgtype,'enabled',t.tgenabled,'internal',t.tgisinternal,
  'deferrable',t.tgdeferrable,'initiallyDeferred',t.tginitdeferred,
  'argumentCount',t.tgnargs,'argumentsHex',encode(t.tgargs,'hex'),
  'columns',(SELECT coalesce(json_agg(CASE WHEN NOT a.attisdropped THEN a.attname END ORDER BY v.ordinal),'[]')
    FROM unnest(t.tgattr::smallint[]) WITH ORDINALITY v(number,ordinal)
    LEFT JOIN pg_attribute a ON a.attrelid=t.tgrelid AND a.attnum=v.number),
  'qualificationPresent',t.tgqual IS NOT NULL,
  'oldTable',t.tgoldtable,'newTable',t.tgnewtable,
  'function',json_build_object(
    'schema',fn.nspname,'name',f.proname,'arguments',pg_get_function_identity_arguments(f.oid),
    'result',json_build_array(ftn.nspname,ft.typname),'language',l.lanname,
    'source',f.prosrc,'definition',pg_get_functiondef(f.oid),'builtin',f.oid<16384,
    'owner',pg_get_userbyid(f.proowner),'acl',f.proacl::text,'config',f.proconfig,
    'strict',f.proisstrict,'securityDefiner',f.prosecdef,'leakproof',f.proleakproof,
    'volatility',f.provolatile,'parallel',f.proparallel,'cost',f.procost::text,'rows',f.prorows::text),
  'referencedRelation',CASE WHEN t.tgconstrrelid<>0 THEN json_build_array(rn.nspname,rr.relname) END,
  'supportingIndex',CASE WHEN t.tgconstrindid<>0 THEN json_build_array(ixn.nspname,ix.relname) END,
  'constraint',CASE WHEN t.tgconstraint<>0 THEN json_build_object(
    'schema',cn.nspname,'relation',cr.relname,'name',co.conname,'type',co.contype,
    'definition',pg_get_constraintdef(co.oid,false),'validated',co.convalidated,
    'deferrable',co.condeferrable,'initiallyDeferred',co.condeferred,
    'referencedRelation',CASE WHEN co.confrelid<>0 THEN json_build_array(cfn.nspname,cfr.relname) END,
    'supportingIndex',CASE WHEN co.conindid<>0 THEN json_build_array(cin.nspname,ci.relname) END,
    'parent',CASE WHEN co.conparentid<>0 THEN json_build_array(cpn.nspname,cpr.relname,cp.conname) END) END,
  'partition',json_build_object('isPartition',r.relispartition,'bound',pg_get_expr(r.relpartbound,r.oid,false),
    'parents',(SELECT coalesce(json_agg(json_build_array(pn.nspname,pr.relname,i.inhseqno,i.inhdetachpending)
      ORDER BY i.inhseqno,pn.nspname COLLATE "C",pr.relname COLLATE "C"),'[]')
      FROM pg_inherits i JOIN pg_class pr ON pr.oid=i.inhparent JOIN pg_namespace pn ON pn.oid=pr.relnamespace
      WHERE i.inhrelid=r.oid))
 ) AS value
 FROM pg_trigger t JOIN pg_class r ON r.oid=t.tgrelid JOIN pg_namespace n ON n.oid=r.relnamespace
 JOIN pg_proc f ON f.oid=t.tgfoid JOIN pg_namespace fn ON fn.oid=f.pronamespace
 JOIN pg_language l ON l.oid=f.prolang JOIN pg_type ft ON ft.oid=f.prorettype JOIN pg_namespace ftn ON ftn.oid=ft.typnamespace
 LEFT JOIN pg_class rr ON rr.oid=t.tgconstrrelid LEFT JOIN pg_namespace rn ON rn.oid=rr.relnamespace
 LEFT JOIN pg_class ix ON ix.oid=t.tgconstrindid LEFT JOIN pg_namespace ixn ON ixn.oid=ix.relnamespace
 LEFT JOIN pg_constraint co ON co.oid=t.tgconstraint LEFT JOIN pg_namespace cn ON cn.oid=co.connamespace
 LEFT JOIN pg_class cr ON cr.oid=co.conrelid LEFT JOIN pg_class cfr ON cfr.oid=co.confrelid
 LEFT JOIN pg_namespace cfn ON cfn.oid=cfr.relnamespace LEFT JOIN pg_class ci ON ci.oid=co.conindid
 LEFT JOIN pg_namespace cin ON cin.oid=ci.relnamespace LEFT JOIN pg_constraint cp ON cp.oid=co.conparentid
 LEFT JOIN pg_class cpr ON cpr.oid=cp.conrelid LEFT JOIN pg_namespace cpn ON cpn.oid=cp.connamespace
 WHERE n.nspname !~ '^pg_' AND n.nspname<>'information_schema'
) inventory;`;

const checkFunctions = ['RI_FKey_check_ins', 'RI_FKey_check_upd'];
const actionFunctions = ['RI_FKey_noaction_del', 'RI_FKey_noaction_upd', 'RI_FKey_restrict_del', 'RI_FKey_restrict_upd',
  'RI_FKey_cascade_del', 'RI_FKey_cascade_upd', 'RI_FKey_setnull_del', 'RI_FKey_setnull_upd',
  'RI_FKey_setdefault_del', 'RI_FKey_setdefault_upd'];
const fields = ['oid', 'parentOid', 'schema', 'relation', 'name', 'definition', 'type', 'enabled', 'internal',
  'deferrable', 'initiallyDeferred', 'argumentCount', 'argumentsHex', 'columns', 'qualificationPresent',
  'oldTable', 'newTable', 'function', 'referencedRelation', 'supportingIndex', 'constraint', 'partition'];
const functionFields = ['schema', 'name', 'arguments', 'result', 'language', 'source', 'definition', 'builtin',
  'owner', 'acl', 'config', 'strict', 'securityDefiner', 'leakproof', 'volatility', 'parallel', 'cost', 'rows'];
const constraintFields = ['schema', 'relation', 'name', 'type', 'definition', 'validated', 'deferrable',
  'initiallyDeferred', 'referencedRelation', 'supportingIndex', 'parent'];
const reject = () => { const error = new Error('Synthetic trigger inventory rejected.'); error.code = 'TRIGGER_INVENTORY'; throw error; };
const require = condition => { if (!condition) reject(); };
const same = (a, b) => JSON.stringify(a) === JSON.stringify(b);
const text = value => typeof value === 'string';
const identity = (value, length = 2) => Array.isArray(value) && value.length === length && value.every(text);
const keys = (value, expected) => value && typeof value === 'object' && !Array.isArray(value) &&
  Object.keys(value).length === expected.length && expected.every(key => Object.hasOwn(value, key));
const compareCodeUnits = (a, b) => {
  if (a < b) {
    return -1;
  }
  if (a > b) {
    return 1;
  }
  return 0;
};
const decimalText = value => text(value) && /^(?:0|[1-9]\d*)(?:\.\d+)?(?:e[+-]?\d+)?$/.test(value) &&
  Number.isFinite(Number(value));

function validateFunction(value) {
  require(keys(value, functionFields) && identity(value.result));
  require(['schema', 'name', 'arguments', 'language', 'source', 'definition', 'owner', 'volatility', 'parallel'].every(key => text(value[key])));
  require(['builtin', 'strict', 'securityDefiner', 'leakproof'].every(key => typeof value[key] === 'boolean'));
  require([value.cost, value.rows].every(decimalText));
  require((value.acl === null || text(value.acl)) &&
    (value.config === null || (Array.isArray(value.config) && value.config.every(text))));
}

function validateConstraint(value) {
  if (value === null) return;
  require(keys(value, constraintFields));
  require(['schema', 'relation', 'name', 'type', 'definition'].every(key => text(value[key])));
  require(['validated', 'deferrable', 'initiallyDeferred'].every(key => typeof value[key] === 'boolean'));
  require([value.referencedRelation, value.supportingIndex].every(item => item === null || identity(item)));
  require(value.parent === null || identity(value.parent, 3));
}

function validatePartition(value) {
  require(keys(value, ['isPartition', 'bound', 'parents']) && typeof value.isPartition === 'boolean');
  require(value.bound === null || text(value.bound));
  require(Array.isArray(value.parents) && value.parents.every(parent => Array.isArray(parent) &&
    parent.length === 4 && text(parent[0]) && text(parent[1]) && Number.isInteger(parent[2]) && typeof parent[3] === 'boolean'));
}

function validateRow(row) {
  require(keys(row, fields) && text(row.oid) && /^[1-9]\d*$/.test(row.oid) &&
    (row.parentOid === null || (text(row.parentOid) && /^[1-9]\d*$/.test(row.parentOid))));
  require(['schema', 'relation', 'name', 'definition'].every(key => text(row[key])));
  require(Number.isInteger(row.type) && row.type > 0 && row.type < 128 &&
    (row.type & 60) !== 0 && !(row.type & 2 && row.type & 64));
  require(text(row.enabled) && 'ODRA'.includes(row.enabled) && row.enabled.length === 1);
  require(['internal', 'deferrable', 'initiallyDeferred', 'qualificationPresent'].every(key => typeof row[key] === 'boolean'));
  require(Number.isInteger(row.argumentCount) && row.argumentCount >= 0 && /^[\da-f]*$/.test(row.argumentsHex) && row.argumentsHex.length % 2 === 0);
  require(Array.isArray(row.columns) && row.columns.every(text));
  require([row.oldTable, row.newTable].every(value => value === null || text(value)));
  require([row.referencedRelation, row.supportingIndex].every(value => value === null || identity(value)));
  validateFunction(row.function); validateConstraint(row.constraint); validatePartition(row.partition);
}

function generatedRiSide(row) {
  const fn = row.function, constraint = row.constraint;
  if (!row.internal || !constraint || constraint.type !== 'f' || !fn.builtin || fn.schema !== 'pg_catalog' ||
      fn.language !== 'internal' || fn.arguments !== '' || !same(fn.result, ['pg_catalog', 'trigger']) ||
      fn.source !== fn.name || row.argumentCount !== 0 || row.argumentsHex !== '' || row.columns.length !== 0 ||
      row.qualificationPresent || row.oldTable !== null || row.newTable !== null) return null;
  if (!row.supportingIndex || !same(row.supportingIndex, constraint.supportingIndex)) return null;
  if (row.type !== riEventType(fn.name)) return null;
  const ownRelation = [row.schema, row.relation], child = [constraint.schema, constraint.relation];
  if (checkFunctions.includes(fn.name) && same(ownRelation, child) &&
      same(row.referencedRelation, constraint.referencedRelation) && row.name === 'RI_ConstraintTrigger_c_' + row.oid) return 'c';
  if (actionFunctions.includes(fn.name) && same(ownRelation, constraint.referencedRelation) &&
      same(row.referencedRelation, child) && row.name === 'RI_ConstraintTrigger_a_' + row.oid) return 'a';
  return null;
}

function riEventType(name) {
  if (name.endsWith('_ins')) return 5;
  if (name.endsWith('_del')) return 9;
  return 17;
}

function triggerIdentity(row) {
  const side = generatedRiSide(row);
  const name = side === null ? ['exact-name', row.name] :
    ['generated-ri', side, row.constraint.schema, row.constraint.relation, row.constraint.name,
      row.function.schema, row.function.name, row.function.arguments];
  return [row.schema, row.relation, name];
}

function verifyInventory(rows, rawTriggers) {
  require(Array.isArray(rows) && Array.isArray(rawTriggers) && rows.length === rawTriggers.length);
  const raw = new Map();
  for (const row of rawTriggers) {
    require(Array.isArray(row) && row.length === 6);
    const key = JSON.stringify(row.slice(0, 3)); require(!raw.has(key)); raw.set(key, row);
  }
  const oids = new Map(), names = new Set();
  for (const row of rows) {
    validateRow(row);
    const name = JSON.stringify([row.schema, row.relation, row.name]);
    require(!oids.has(row.oid) && !names.has(name)); names.add(name); oids.set(row.oid, row);
    require(same(raw.get(name), [row.schema, row.relation, row.name, row.enabled, row.internal, row.definition]));
  }
  for (const row of rows) {
    const seen = new Set([row.oid]); let current = row;
    while (current.parentOid !== null) {
      require(oids.has(current.parentOid) && !seen.has(current.parentOid)); seen.add(current.parentOid);
      current = oids.get(current.parentOid);
    }
  }
  return oids;
}

function timing(type) {
  if (type & 64) return 'INSTEAD';
  if (type & 2) return 'BEFORE';
  return 'AFTER';
}

function roles(enabled) {
  if (enabled === 'A') return ['origin', 'replica'];
  if (enabled === 'O') return ['origin'];
  if (enabled === 'R') return ['replica'];
  return [];
}

function groupKeys(row) {
  const prefix = [row.schema, row.relation, timing(row.type), row.type & 1 ? 'ROW' : 'STATEMENT'];
  return [[4, 'INSERT'], [8, 'DELETE'], [16, 'UPDATE'], [32, 'TRUNCATE']]
    .filter(([bit]) => row.type & bit)
    .flatMap(([, event]) => roles(row.enabled).map(role => JSON.stringify([...prefix, event, role])));
}

function firingOrders(projected, rawTriggers) {
  const groups = new Map();
  const byName = new Map(projected.map(value =>
    [JSON.stringify([value.row.schema, value.row.relation, value.row.name]), value]));
  for (const raw of rawTriggers) {
    const { row, value } = byName.get(JSON.stringify(raw.slice(0, 3)));
    for (const key of groupKeys(row)) {
      if (!groups.has(key)) groups.set(key, []);
      groups.get(key).push(value.identity);
    }
  }
  return [...groups.entries()].sort((a, b) => compareCodeUnits(a[0], b[0])).map(([key, order]) => [JSON.parse(key), order]);
}

/** Preserve every logical trigger field and actual potential co-firing order. */
export function logicalTriggerCatalog(rows, rawTriggers) {
  const oids = verifyInventory(rows, rawTriggers), identities = new Set();
  const projected = rows.map(row => {
    const id = triggerIdentity(row), key = JSON.stringify(id); require(!identities.has(key)); identities.add(key);
    const value = { identity: id, type: row.type, enabled: row.enabled, internal: row.internal,
      deferrable: row.deferrable, initiallyDeferred: row.initiallyDeferred,
      argumentCount: row.argumentCount, argumentsHex: row.argumentsHex, columns: row.columns,
      qualificationPresent: row.qualificationPresent, oldTable: row.oldTable, newTable: row.newTable,
      function: row.function, referencedRelation: row.referencedRelation, supportingIndex: row.supportingIndex,
      constraint: row.constraint, partition: row.partition,
      parent: row.parentOid === null ? null : triggerIdentity(oids.get(row.parentOid)),
      exactDefinition: generatedRiSide(row) === null ? row.definition : null };
    return { row, key, value };
  });
  return { version: 1, rowCount: projected.length,
    rows: projected.sort((a, b) => compareCodeUnits(a.key, b.key)).map(value => value.value),
    firingOrders: firingOrders(projected, rawTriggers) };
}
