import assert from 'node:assert/strict';
import { test } from 'node:test';
import { logicalTriggerCatalog } from '../../scripts/ci/synthetic-trigger-catalog.mjs';

const fixture = () => ({
  oid: '10', parentOid: null, schema: 'public', relation: 'children', name: 'RI_ConstraintTrigger_c_10',
  definition: 'original generated trigger definition', type: 5, enabled: 'O', internal: true,
  deferrable: false, initiallyDeferred: false, argumentCount: 0, argumentsHex: '', columns: [],
  qualificationPresent: false, oldTable: null, newTable: null,
  function: { schema: 'pg_catalog', name: 'RI_FKey_check_ins', arguments: '', result: ['pg_catalog', 'trigger'],
    language: 'internal', source: 'RI_FKey_check_ins', definition: 'original built-in definition', builtin: true,
    owner: 'postgres', acl: null, config: null, strict: true, securityDefiner: false, leakproof: false,
    volatility: 'v', parallel: 'u', cost: '1', rows: '0' },
  referencedRelation: ['public', 'parents'], supportingIndex: ['public', 'parents_pkey'],
  constraint: { schema: 'public', relation: 'children', name: 'children_parent_fk', type: 'f',
    definition: 'FOREIGN KEY (parent_id) REFERENCES parents(id)', validated: true,
    deferrable: false, initiallyDeferred: false, referencedRelation: ['public', 'parents'],
    supportingIndex: ['public', 'parents_pkey'], parent: null },
  partition: { isPartition: false, bound: null, parents: [] },
});
const raw = rows => [...rows].sort((a, b) => Buffer.compare(Buffer.from([a.schema, a.relation, a.name].join('\0')),
  Buffer.from([b.schema, b.relation, b.name].join('\0'))))
  .map(row => [row.schema, row.relation, row.name, row.enabled, row.internal, row.definition]);
const project = rows => logicalTriggerCatalog(rows, raw(rows));
const renamedPhysical = (row, oid) => ({ ...structuredClone(row), oid, name: 'RI_ConstraintTrigger_c_' + oid,
  definition: 'new physical trigger definition' });

await test('only proven generated RI names resolve across changed physical identities', () => {
  const original = fixture(), restored = renamedPhysical(original, '20');
  assert.deepEqual(project([original]), project([restored]));
  assert.equal(project([original]).rowCount, 1);
  assert.equal(project([original]).rows[0].identity[2][0], 'generated-ri');
  assert.equal(original.name, 'RI_ConstraintTrigger_c_10');
});

await test('function float metadata remains exact decimal text and rejects malformed or numeric inputs', () => {
  for (const value of ['0', '100.5', '1e-07', '3.4028235e+38']) {
    const row = fixture(); row.function.cost = value;
    assert.equal(project([row]).rows[0].function.cost, value);
  }
  for (const value of [100.5, null, '', 'NaN', 'Infinity', '-1', ' 1', '1 ', '01', '1e999']) {
    for (const field of ['cost', 'rows']) {
      const row = fixture(); row.function[field] = value;
      assert.throws(() => project([row]), { code: 'TRIGGER_INVENTORY' });
    }
  }
});

await test('unknown internal names, unknown functions and user-created RI-looking names remain exact', () => {
  for (const mutate of [row => { row.internal = false; }, row => { row.name = 'custom_internal_name'; },
    row => { row.function.schema = 'public'; }, row => { row.function.builtin = false; },
    row => { row.function.language = 'plpgsql'; }, row => { row.function.source = 'custom'; },
    row => { row.function.name = 'RI_FKey_unknown'; }, row => { row.name = 'RI_ConstraintTrigger_c_11'; },
    row => { row.constraint.type = 'u'; }, row => { row.constraint.relation = 'other'; },
    row => { row.supportingIndex = null; }, row => { row.referencedRelation = ['public', 'other']; }]) {
    const row = fixture(); mutate(row);
    assert.equal(project([row]).rows[0].identity[2][0], 'exact-name');
    const changed = structuredClone(row); changed.name += '_changed';
    assert.notDeepEqual(project([row]), project([changed]));
  }
});

await test('every supported built-in action function requires the matching FK side and generated own-OID name', () => {
  const functions = ['RI_FKey_noaction_del', 'RI_FKey_noaction_upd', 'RI_FKey_restrict_del', 'RI_FKey_restrict_upd',
    'RI_FKey_cascade_del', 'RI_FKey_cascade_upd', 'RI_FKey_setnull_del', 'RI_FKey_setnull_upd',
    'RI_FKey_setdefault_del', 'RI_FKey_setdefault_upd'];
  for (const name of functions) {
    const row = fixture(); row.relation = 'parents'; row.referencedRelation = ['public', 'children'];
    row.name = 'RI_ConstraintTrigger_a_' + row.oid; row.function.name = name; row.function.source = name;
    row.type = name.endsWith('_del') ? 9 : 17;
    assert.equal(project([row]).rows[0].identity[2][0], 'generated-ri');
    const restored = structuredClone(row); restored.oid = '20'; restored.name = 'RI_ConstraintTrigger_a_20';
    restored.definition = 'new physical definition';
    assert.deepEqual(project([row]), project([restored]));
    const wrong = structuredClone(row); wrong.referencedRelation = ['public', 'other'];
    assert.equal(project([wrong]).rows[0].identity[2][0], 'exact-name');
  }
});

await test('every logical trigger and function property remains compared', () => {
  const original = fixture(), expected = project([original]);
  const mutations = [row => { row.type = 17; }, row => { row.enabled = 'D'; }, row => { row.internal = false; },
    row => { row.deferrable = true; }, row => { row.initiallyDeferred = true; },
    row => { row.argumentCount = 1; row.argumentsHex = '6100'; }, row => { row.columns = ['parent_id']; },
    row => { row.qualificationPresent = true; row.definition = 'changed WHEN'; },
    row => { row.oldTable = 'old_rows'; }, row => { row.newTable = 'new_rows'; },
    row => { row.function.arguments = 'changed'; }, row => { row.function.result = ['public', 'changed']; },
    row => { row.function.definition += 'changed'; }, row => { row.function.owner = 'other'; },
    row => { row.function.acl = '{}'; }, row => { row.function.config = ['search_path=public']; },
    row => { row.function.strict = false; }, row => { row.function.securityDefiner = true; },
    row => { row.function.leakproof = true; }, row => { row.function.volatility = 's'; },
    row => { row.function.parallel = 's'; }, row => { row.function.cost = '2'; }, row => { row.function.rows = '1'; },
    row => { row.constraint.name = 'other_fk'; }, row => { row.constraint.definition += ' ON DELETE CASCADE'; },
    row => { row.constraint.validated = false; }, row => { row.constraint.deferrable = true; },
    row => { row.constraint.initiallyDeferred = true; }, row => { row.constraint.parent = ['public', 'ancestor', 'parent_fk']; },
    row => { row.constraint.referencedRelation = ['public', 'other']; },
    row => { row.constraint.supportingIndex = ['public', 'other_idx']; },
    row => { row.partition.isPartition = true; }, row => { row.partition.bound = 'FOR VALUES FROM (0) TO (10)'; },
    row => { row.partition.parents = [['public', 'parent_partition', 1, false]]; }];
  for (const mutate of mutations) {
    const changed = structuredClone(original); mutate(changed);
    assert.notDeepEqual(project([changed]), expected);
  }
});

await test('full inventory rejects omitted, duplicated, malformed and inconsistent raw rows', () => {
  const row = fixture();
  for (const invoke of [() => logicalTriggerCatalog([row], []), () => logicalTriggerCatalog([], raw([row])),
    () => logicalTriggerCatalog([row, row], raw([row, row])),
    () => logicalTriggerCatalog([row], [['public', 'children', row.name, 'D', true, row.definition]])])
    assert.throws(invoke, { code: 'TRIGGER_INVENTORY' });
  for (const mutate of [value => { value.oid = 10; }, value => { value.extra = 'unexpected'; },
    value => { value.columns = [null]; }, value => { value.argumentsHex = 'not-hex'; },
    value => { value.function.extra = true; }, value => { value.constraint.extra = true; },
    value => { value.partition.extra = true; }, value => { value.parentOid = 'unknown'; }]) {
    const changed = structuredClone(row); mutate(changed);
    assert.throws(() => project([changed]), { code: 'TRIGGER_INVENTORY' });
  }
});

await test('partition parent references resolve logical identities and reject missing or cyclic parents', () => {
  const parent = fixture(), child = renamedPhysical(parent, '11'); child.relation = 'children_partition';
  child.constraint.relation = child.relation; child.constraint.parent = ['public', 'children', 'children_parent_fk'];
  child.parentOid = parent.oid;
  const restoredParent = renamedPhysical(parent, '20'), restoredChild = renamedPhysical(child, '21');
  restoredChild.parentOid = restoredParent.oid;
  assert.deepEqual(project([parent, child]), project([restoredParent, restoredChild]));
  const missing = structuredClone(child); missing.parentOid = '999';
  assert.throws(() => project([parent, missing]), { code: 'TRIGGER_INVENTORY' });
  const cycle = structuredClone(parent); cycle.parentOid = child.oid;
  assert.throws(() => project([cycle, child]), { code: 'TRIGGER_INVENTORY' });
});

await test('co-firing RI order changes remain detected even when logical row fields match', () => {
  const a = fixture(), b = renamedPhysical(a, '20'); b.constraint.name = 'second_fk';
  const expected = project([a, b]);
  const changed = project([renamedPhysical(a, '40'), renamedPhysical(b, '30')]);
  assert.deepEqual(changed.rows, expected.rows);
  assert.notDeepEqual(changed.firingOrders, expected.firingOrders);
});

await test('overlapping multi-event masks preserve RI/user order for each effective event and role', () => {
  const ri = fixture(), user = fixture(); user.oid = '15'; user.name = 'RI_ConstraintTrigger_c_15';
  user.internal = false; user.type = 21; user.definition = 'user INSERT OR UPDATE definition';
  const before = project([ri, user]), after = project([renamedPhysical(ri, '20'), user]);
  assert.notDeepEqual(before.firingOrders, after.firingOrders);
  assert.ok(before.firingOrders.some(([key, order]) => key[4] === 'INSERT' && order.length === 2));
  assert.ok(before.firingOrders.some(([key, order]) => key[4] === 'UPDATE' && order.length === 1));
  const replica = structuredClone(user); replica.enabled = 'R';
  assert.notDeepEqual(project([ri, replica]), before);
  const always = structuredClone(user); always.enabled = 'A';
  assert.ok(project([ri, always]).firingOrders.some(([key]) => key[5] === 'replica'));
});

await test('disjoint events do not invent a co-firing order dependency', () => {
  const insert = fixture(), update = renamedPhysical(insert, '20');
  update.type = 17; update.function.name = 'RI_FKey_check_upd'; update.function.source = update.function.name;
  assert.deepEqual(project([insert, update]), project([renamedPhysical(insert, '40'), renamedPhysical(update, '30')]));
});
