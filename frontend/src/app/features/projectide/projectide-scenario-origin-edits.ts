import { RevisionContext, sameContext } from './projectide.contracts';
import { isProjectLocationShape } from './projectide-preview.validation';
const object = (value: unknown): value is Record<string, unknown> => !!value && typeof value === 'object' && !Array.isArray(value);
const fields = (value: Record<string, unknown>, keys: readonly string[]) => Object.keys(value).length === keys.length && keys.every(key => Object.hasOwn(value, key));
const equal = (a: unknown, b: unknown): boolean => a === b || (Array.isArray(a) && Array.isArray(b) ? a.length === b.length && a.every((x, i) => equal(x, b[i])) :
  object(a) && object(b) && Object.keys(a).length === Object.keys(b).length && Object.keys(a).every(key => Object.hasOwn(b, key) && equal(a[key], b[key])));

/** Validate only the exact Core-announced context edits and Set permutations in a detached value comparison buffer. No typed traversal, canonicalizer, resolver or editable Source. */
export function coreOriginEditsMatch(declared: unknown, applied: unknown, edits: unknown, before: RevisionContext, after: RevisionContext): boolean {
  if (!Array.isArray(edits) || edits.length > 4096) return false;
  const buffer: unknown = structuredClone(declared); const seen = new Set<string>();
  const at = (path: readonly string[]): unknown => {
    let node = buffer;
    for (const key of path) {
      if (!node || typeof node !== 'object' || !Object.hasOwn(node, key) || Array.isArray(node) && !/^(0|[1-9][0-9]*)$/u.test(key)) throw new Error('invalid-core-origin-path');
      if (object(node) && typeof node['state'] === 'string' && object(node['type']) && typeof node['type']['kind'] === 'string' &&
          (node['state'] !== 'coglatas.known' || key !== 'value')) throw new Error('opaque-core-origin-path');
      node = (node as Record<string, unknown>)[key];
    }
    return node;
  };
  try {
    for (const edit of edits) {
      if (!object(edit) || typeof edit['path'] !== 'string' || edit['path'].length > 262144 || !edit['path'].startsWith('/') || /~(?![01])/u.test(edit['path'])) return false;
      const path = edit['path'].slice(1).split('/').map(key => key.replace(/~1/gu, '/').replace(/~0/gu, '~'));
      const key = edit['kind'] + ':' + edit['path']; if (seen.has(key)) return false; seen.add(key);
      if (edit['kind'] === 'OriginContext' && fields(edit, ['kind', 'path'])) {
        if (path.length < 3 || path.at(-3) !== 'value' || path.at(-2) !== 'origin' || path.at(-1) !== 'context') return false;
        const typed = at(path.slice(0, -3));
        if (!object(typed) || typed['state'] !== 'coglatas.known' || !object(typed['type']) ||
            !['coglatas.entity-reference', 'coglatas.document-reference', 'coglatas.resource-reference', 'coglatas.external-snapshot-reference'].includes(String(typed['type']['kind'])) ||
            !object(typed['value']) || !isProjectLocationShape(typed['value']['origin']) || !sameContext(typed['value']['origin'].context, before) || !equal(at(path), before)) return false;
        (typed['value']['origin'] as unknown as Record<string, unknown>)['context'] = structuredClone(after);
      } else if (edit['kind'] === 'SetOrder' && fields(edit, ['kind', 'path', 'order'])) {
        if (path.at(-1) !== 'value') return false; const typed = at(path.slice(0, -1));
        if (!object(typed) || typed['state'] !== 'coglatas.known' || !object(typed['type']) || typed['type']['kind'] !== 'coglatas.set' ||
            typed['type']['identity'] !== 'coglatas.canonical-value' || !Array.isArray(typed['value']) || !Array.isArray(edit['order'])) return false;
        const values = typed['value']; const order = edit['order'];
        if (order.length !== values.length || new Set(order).size !== values.length || order.some(index => !Number.isSafeInteger(index) || index < 0 || index >= values.length)) return false;
        typed['value'] = order.map(index => values[index]);
      } else return false;
    }
    return equal(buffer, applied);
  } catch { return false; }
}
