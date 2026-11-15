import { Digest } from './projectide.contracts';
import { originalJsonArrayValues } from './projectide-original-json';
import { parseLocalTestJson as parse, verifyLocalTestDigest as verify } from './projectide-tests.reader';

export interface OperatorTextFrame { readonly text: string; readonly digest: Digest; }
export interface OriginalOperatorBody extends OperatorTextFrame { readonly bytes: number; }
export type OperatorTruth = 'True' | 'False' | 'Unknown';
export interface OriginalOperatorNode {
  readonly ordinal: number; readonly path: readonly number[]; readonly kind: string; readonly truth: OperatorTruth;
  readonly evaluatedChildren: readonly number[]; readonly skippedOperands: number;
  readonly literal: { readonly premiseOrdinal: number; readonly digest: Digest } | null;
  readonly original: OriginalOperatorBody;
}
export interface DeclaredOperatorNode { readonly ordinal: number; readonly digest: Digest; readonly bytes: number; }
const fail = (): never => { throw new Error('unsupported-original-operator-structure'); };
const object = (v: unknown): v is Record<string, unknown> => !!v && typeof v === 'object' && !Array.isArray(v);
const obj = (v: unknown): Record<string, unknown> => object(v) ? v : fail();
const fields = (v: unknown, names: readonly string[]) => object(v) && Object.keys(v).length === names.length && names.every(n => Object.hasOwn(v, n));
const same = (a: unknown, b: unknown): boolean => a === b || Array.isArray(a) && Array.isArray(b) && a.length === b.length && a.every((v, i) => same(v, b[i])) ||
  object(a) && object(b) && Object.keys(a).length === Object.keys(b).length && Object.keys(a).every(k => Object.hasOwn(b, k) && same(a[k], b[k]));
const integer = (v: unknown, min: number, max: number): v is number => typeof v === 'number' && Number.isSafeInteger(v) && v >= min && v <= max;
const digest = (v: unknown): v is Digest => fields(v, ['algorithm', 'value']) && obj(v)['algorithm'] === 'sha-256' &&
  typeof obj(v)['value'] === 'string' && /^[a-f0-9]{64}$/u.test(obj(v)['value'] as string);
const truth = (v: unknown): v is OperatorTruth => v === 'True' || v === 'False' || v === 'Unknown';
const freeze = <T>(v: T): T => { if (v && typeof v === 'object') { Object.values(v).forEach(freeze); Object.freeze(v); } return v; };
const byteLength = (text: string) => new TextEncoder().encode(text).length;
/** Extract an original object member without reconstructing its hash subject. The temporary wrapper is never hashed. */
export function originalOperatorMember(text: string, name: string): string {
  return originalJsonArrayValues('{"frames":[' + text + ']}', 'frames', name)[0] ?? fail();
}
function validateAst(text: string, depth: number, count: { value: number }): void {
  if (depth > 32 || ++count.value > 1024) return fail();
  const ast = obj(parse(text));
  if (ast['kind'] === 'coglatas.literal') {
    if (!fields(ast, ['kind', 'value'])) return fail();
    const value = obj(ast['value']); const type = obj(value['type']);
    if (!fields(type, ['kind', 'schemaVersion', 'nullable', 'acceptsUnknown']) || type['kind'] !== 'coglatas.boolean' ||
      type['schemaVersion'] !== 1 || typeof type['nullable'] !== 'boolean' || typeof type['acceptsUnknown'] !== 'boolean') return fail();
    if (value['state'] === 'coglatas.known') {
      if (!fields(value, ['type', 'state', 'value']) || typeof value['value'] !== 'boolean') return fail();
    } else if (value['state'] === 'coglatas.unknown') {
      if (!fields(value, ['type', 'state', 'reason', 'basis']) || type['acceptsUnknown'] !== true ||
        typeof value['reason'] !== 'string' || value['reason'].length > 256 || !value['reason'].includes('.') || !object(value['basis'])) return fail();
    } else return fail();
    return;
  }
  if (!fields(ast, ['kind', 'operands']) || !['coglatas.and', 'coglatas.or', 'coglatas.not'].includes(ast['kind'] as string) ||
    !Array.isArray(ast['operands']) || (ast['kind'] === 'coglatas.not' ? ast['operands'].length !== 1 : ast['operands'].length < 2 || ast['operands'].length > 32)) return fail();
  for (const child of originalJsonArrayValues(text, 'operands')) validateAst(child, depth + 1, count);
}

/** Validate recorded visits against their original bounded Boolean Source AST; publish only supplied original frames. */
export async function validateOriginalOperatorGraph(predicateText: string, declaredInput: readonly DeclaredOperatorNode[],
  originalBodiesInput: readonly OriginalOperatorBody[], evaluationInput: OriginalOperatorBody): Promise<readonly OriginalOperatorNode[]> {
  const captured = freeze(structuredClone({ declared: declaredInput, originalBodies: originalBodiesInput, evaluation: evaluationInput }));
  const { declared, originalBodies, evaluation } = captured;
  validateAst(predicateText, 1, { value: 0 });
  if (!Array.isArray(declared) || !integer(declared.length, 1, 1024) || originalBodies.length !== declared.length ||
    !fields(evaluation, ['text', 'digest', 'bytes']) || !digest(evaluation.digest) || evaluation.bytes !== byteLength(evaluation.text) ||
    !await verify('coglatas.local-predicate-evaluation/1', evaluation.text, evaluation.digest)) return fail();
  const frame = obj(parse(evaluation.text));
  if (!fields(frame, ['format', 'truth', 'premises']) || frame['format'] !== 'coglatas.local-predicate-evaluation/1' ||
    !truth(frame['truth']) || !Array.isArray(frame['premises']) || frame['premises'].length > 1024 || !frame['premises'].every(digest)) return fail();
  const bodies = new Map<string, OriginalOperatorBody>();
  for (const body of originalBodies) {
    if (!fields(body, ['text', 'digest', 'bytes']) || typeof body.text !== 'string' || !digest(body.digest) ||
      !integer(body.bytes, 1, 1_048_576) || body.bytes !== byteLength(body.text) || bodies.has(body.digest.value) ||
      !await verify('coglatas.observed-predicate-node/1', body.text, body.digest)) return fail();
    bodies.set(body.digest.value, body);
  }
  const nodes: OriginalOperatorNode[] = [];
  for (const [index, metadata] of declared.entries()) {
    if (!fields(metadata, ['ordinal', 'digest', 'bytes']) || metadata.ordinal !== index || !digest(metadata.digest) || !integer(metadata.bytes, 1, 1_048_576)) return fail();
    const body = bodies.get(metadata.digest.value); if (!body || body.bytes !== metadata.bytes) return fail();
    const data = obj(parse(body.text));
    if (!fields(data, ['format', 'ordinal', 'path', 'kind', 'truth', 'evaluatedChildren', 'skippedOperands', 'literal']) ||
      data['format'] !== 'coglatas.observed-predicate-node/1' || data['ordinal'] !== index || !truth(data['truth']) ||
      !Array.isArray(data['path']) || data['path'].length > 31 || !data['path'].every(v => integer(v, 0, 31)) ||
      !Array.isArray(data['evaluatedChildren']) || data['evaluatedChildren'].length > 32 || !data['evaluatedChildren'].every(v => integer(v, 0, 1023)) ||
      new Set(data['evaluatedChildren']).size !== data['evaluatedChildren'].length || !integer(data['skippedOperands'], 0, 32)) return fail();
    const literal = data['literal'];
    if (literal !== null && (!fields(literal, ['premiseOrdinal', 'digest']) || !integer(obj(literal)['premiseOrdinal'], 0, 1023) || !digest(obj(literal)['digest']))) return fail();
    nodes.push(freeze({ ordinal: index, path: data['path'] as number[], kind: data['kind'] as string, truth: data['truth'] as OperatorTruth,
      evaluatedChildren: data['evaluatedChildren'] as number[], skippedOperands: data['skippedOperands'] as number,
      literal: literal as OriginalOperatorNode['literal'], original: structuredClone(body) }));
  }
  let next = 0; const premises: Digest[] = [];
  async function visit(index: number, path: readonly number[], astText: string): Promise<OperatorTruth> {
    if (index !== next++ || index >= nodes.length) return fail();
    const node = nodes[index]; const ast = obj(parse(astText));
    if (!same(node.path, path) || node.kind !== ast['kind']) return fail();
    if (node.kind === 'coglatas.literal') {
      const value = obj(ast['value']); const expected: OperatorTruth = value['state'] === 'coglatas.unknown' ? 'Unknown' : value['value'] ? 'True' : 'False';
      if (node.truth !== expected || node.evaluatedChildren.length || node.skippedOperands || !node.literal || node.literal.premiseOrdinal !== premises.length ||
        !await verify('coglatas.evaluated-predicate-literal/1', originalOperatorMember(astText, 'value'), node.literal.digest)) return fail();
      premises.push(node.literal.digest); return node.truth;
    }
    if (node.literal !== null) return fail();
    const operands = originalJsonArrayValues(astText, 'operands');
    if (node.skippedOperands !== operands.length - node.evaluatedChildren.length) return fail();
    if (node.kind === 'coglatas.not') {
      if (node.evaluatedChildren.length !== 1 || node.skippedOperands) return fail();
      const result = await visit(node.evaluatedChildren[0], [...path, 0], operands[0]);
      if (node.truth !== (result === 'Unknown' ? 'Unknown' : result === 'True' ? 'False' : 'True')) return fail();
      return node.truth;
    }
    let decisive = false; let unknown = false;
    for (const [operand, child] of node.evaluatedChildren.entries()) {
      if (decisive || operand >= operands.length) return fail();
      const result = await visit(child, [...path, operand], operands[operand]);
      if (result === 'Unknown') unknown = true;
      if (node.kind === 'coglatas.and' && result === 'False' || node.kind === 'coglatas.or' && result === 'True') decisive = true;
    }
    if (node.skippedOperands && !decisive) return fail();
    const expected: OperatorTruth = decisive ? node.kind === 'coglatas.and' ? 'False' : 'True' : unknown ? 'Unknown' : node.kind === 'coglatas.and' ? 'True' : 'False';
    if (node.truth !== expected) return fail(); return node.truth;
  }
  const observedTruth = await visit(0, [], predicateText);
  if (next !== nodes.length || frame['truth'] !== observedTruth || !same(frame['premises'], premises)) return fail();
  return freeze(nodes);
}
