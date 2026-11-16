/// <reference types="node" />
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { createHash, webcrypto } from 'node:crypto';
import { vi } from 'vitest';
import { originalJsonArrayValues } from './projectide-original-json';
import { DeclaredOperatorNode, OriginalOperatorBody, originalOperatorMember, validateOriginalOperatorGraph } from './projectide-operator-recording-structure';

vi.stubGlobal('crypto', webcrypto);
const original = JSON.parse(readFileSync(resolve('public/assets/projectide/operator-recordings-v1.json'), 'utf8'));
const keys = ['fail', 'unknown', 'operator-and-short-circuit', 'operator-nested-duplicates', 'operator-or-unknown'];
function recipe(key = 'operator-nested-duplicates', label = 'diagnostic'): { predicate: string; declared: DeclaredOperatorNode[]; bodies: OriginalOperatorBody[]; evaluation: OriginalOperatorBody } {
  const row = structuredClone(original.cases.find((r: { key: string }) => r.key === key));
  const source = JSON.parse(row.sourceText); const index = source.declarations.findIndex((d: { kind: string }) => d.kind === 'coglatas.constraint');
  const payload = originalJsonArrayValues(row.sourceText, 'declarations', 'payload')[index];
  const selected = row.operatorRecordings[label].diagnostics[0].available;
  const metadata = JSON.parse(selected.frame.text).evaluations[0].metadata.originalObservation;
  return { predicate: originalOperatorMember(payload, 'predicate'), declared: metadata.nodes, bodies: selected.operatorBodies, evaluation: selected.evaluationFrames[0] };
}
function hash(tag: string, text: string): string { return createHash('sha256').update(tag + String.fromCharCode(0)).update(text).digest('hex'); }
function rewrite(value: ReturnType<typeof recipe>, ordinal: number, change: (node: Record<string, unknown>) => void): void {
  const body = value.bodies.find(b => JSON.parse(b.text).ordinal === ordinal)!; const node = JSON.parse(body.text); change(node);
  // Deliberately rebound negative claim. Original fixture subjects remain unchanged.
  const text = JSON.stringify(node); const digest = { algorithm: 'sha-256' as const, value: hash('coglatas.observed-predicate-node/1', text) };
  const index = value.bodies.indexOf(body); value.bodies[index] = { text, digest, bytes: new TextEncoder().encode(text).length };
  value.declared[ordinal] = { ordinal, digest, bytes: value.bodies[index].bytes };
}
function rewriteEvaluation(value: ReturnType<typeof recipe>, change: (frame: Record<string, unknown>) => void): void {
  const data = JSON.parse(value.evaluation.text); change(data); const text = JSON.stringify(data);
  value.evaluation = { text, digest: { algorithm: 'sha-256', value: hash('coglatas.local-predicate-evaluation/1', text) }, bytes: new TextEncoder().encode(text).length };
}
async function read(value: ReturnType<typeof recipe>) { return validateOriginalOperatorGraph(value.predicate, value.declared, value.bodies, value.evaluation); }

describe('Actual original operator structure and bounded Source AST', () => {
  for (const label of ['diagnostic', 'retry']) {
    it.each(keys)('validates actual observed Source and original frames: ' + label + '/%s', async key => {
      const input = recipe(key, label); const nodes = await read(input);
      expect(nodes).toHaveLength(input.declared.length); expect(Object.isFrozen(nodes)).toBe(true);
      for (const node of nodes) {
        expect(Object.isFrozen(node)).toBe(true); expect(Object.isFrozen(node.original)).toBe(true);
        expect(input.bodies.find(b => b.digest.value === node.original.digest.value)?.text).toBe(node.original.text);
      }
    });
  }
  it('preserves actual nested preorder and duplicate original literal premises', async () => {
    const input = recipe(); const nodes = await read(input);
    expect(nodes.map(n => n.ordinal)).toEqual([0, 1, 2, 3, 4, 5]);
    expect(nodes[0].evaluatedChildren).toEqual([1, 4]); expect(nodes[0].skippedOperands).toBe(1);
    expect(nodes.filter(n => n.literal).map(n => n.literal!.premiseOrdinal)).toEqual([0, 1, 2]);
    const premises = nodes.filter(n => n.literal).map(n => n.literal!.digest.value);
    expect(premises).toHaveLength(3); expect(new Set(premises).size).toBe(2);
  });
  const changes: readonly [string, (v: ReturnType<typeof recipe>) => void][] = [
    ['false root truth', v => rewrite(v, 0, n => n['truth'] = 'True')],
    ['false literal truth', v => rewrite(v, 2, n => n['truth'] = 'True')],
    ['reversed children', v => rewrite(v, 0, n => n['evaluatedChildren'] = [4, 1])],
    ['duplicate children', v => rewrite(v, 0, n => n['evaluatedChildren'] = [1, 1])],
    ['root cycle', v => rewrite(v, 0, n => n['evaluatedChildren'] = [0])],
    ['foreign path', v => rewrite(v, 3, n => n['path'] = [1, 0])],
    ['unrecorded operand as visited', v => rewrite(v, 0, n => n['skippedOperands'] = 0)],
    ['wrong premise ordinal', v => rewrite(v, 3, n => n['literal'] = { premiseOrdinal: 0, digest: (n['literal'] as { digest: unknown }).digest })],
    ['false literal digest', v => rewrite(v, 3, n => n['literal'] = { premiseOrdinal: 1, digest: { algorithm: 'sha-256', value: 'b'.repeat(64) } })],
    ['unsupported operator', v => rewrite(v, 0, n => n['kind'] = 'coglatas.future')],
    ['fractional ordinal', v => rewrite(v, 0, n => n['ordinal'] = 0.5)],
    ['false derived truth', v => rewriteEvaluation(v, f => f['truth'] = 'True')],
    ['deduplicated ordered premises', v => rewriteEvaluation(v, f => f['premises'] = (f['premises'] as unknown[]).slice(0, 2))],
    ['reordered original premises', v => rewriteEvaluation(v, f => f['premises'] = [...f['premises'] as unknown[]].reverse())],
    ['unknown node field', v => rewrite(v, 0, n => n['future'] = true)],
    ['unsupported Source AST', v => v.predicate = '{"kind":"coglatas.future","operands":[]}'],
    ['invalid Source arity', v => v.predicate = '{"kind":"coglatas.and","operands":[]}'],
    ['duplicate Source fields', v => v.predicate = '{"kind":"coglatas.and","kind":"coglatas.or","operands":[]}']
  ];
  it.each(changes)('refuses structurally false claims even after hashes are rebound: %s', async (_, change) => {
    const value = recipe(); change(value); await expect(read(value)).rejects.toThrow();
  });
  it('refuses corrupted original bytes with their earlier hash', async () => {
    const value = recipe(); value.bodies[0] = { ...value.bodies[0], text: value.bodies[0].text + ' ' };
    await expect(read(value)).rejects.toThrow();
  });
  it('captures private immutable frames before asynchronous hash verification', async () => {
    const value = recipe(); const text = value.bodies[0].text; const pending = read(value);
    value.bodies[0] = { ...value.bodies[0], text: 'changed during verification' };
    const nodes = await pending; expect(nodes.some(n => n.original.text === text)).toBe(true);
    expect(nodes.some(n => n.original.text === 'changed during verification')).toBe(false);
  });
});
