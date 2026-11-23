import { parseLocalTestJson as parse } from './projectide-tests.reader';

type Span = { readonly start: number; readonly end: number };
const fail = (): never => { throw new Error('unsupported-original-json-slice'); };

/** Extract complete value bytes from an already bounded valid frame, without reserializing parsed data. */
export function originalJsonArrayValues(text: string, arrayName: string, member?: string): readonly string[] {
  if (typeof text !== 'string' || new TextEncoder().encode(text).length > 4_194_304 || !arrayName) return fail();
  parse(text); // Preserve existing duplicate-member, Unicode, safe-number, depth and node limits.
  const whitespace = (index: number) => { while (index < text.length && /[\t\n\r ]/u.test(text[index])) index++; return index; };
  const stringEnd = (start: number): number => {
    if (text[start] !== '"') return fail();
    for (let i = start + 1; i < text.length; i++) {
      if (text[i] === String.fromCharCode(92)) i++;
      else if (text[i] === '"') return i + 1;
    }
    return fail();
  };
  const valueEnd = (start: number): number => {
    if (text[start] === '"') return stringEnd(start);
    if (text[start] === '{' || text[start] === '[') {
      let depth = 0;
      for (let i = start; i < text.length; i++) {
        const char = text[i];
        if (char === '"') { i = stringEnd(i) - 1; continue; }
        if (char === '{' || char === '[') { if (++depth > 256) return fail(); }
        else if ((char === '}' || char === ']') && --depth === 0) return i + 1;
      }
      return fail();
    }
    let i = start;
    while (i < text.length && !/[,}\]\t\n\r ]/u.test(text[i])) i++;
    return i > start ? i : fail();
  };
  const objectMembers = (span: Span): ReadonlyMap<string, Span> => {
    let index = whitespace(span.start); if (text[index++] !== '{') return fail();
    const members = new Map<string, Span>(); index = whitespace(index);
    while (text[index] !== '}') {
      const keyStart = index; const end = stringEnd(index); const key: unknown = JSON.parse(text.slice(keyStart, end));
      if (typeof key !== 'string' || members.has(key)) return fail();
      index = whitespace(end); if (text[index++] !== ':') return fail(); index = whitespace(index);
      const start = index; index = valueEnd(index); if (index > span.end) return fail(); members.set(key, { start, end: index });
      index = whitespace(index); if (text[index] === '}') break; if (text[index++] !== ',') return fail(); index = whitespace(index);
    }
    if (text[index] !== '}' || index + 1 !== span.end) return fail();
    return members;
  };
  const rootEnd = valueEnd(whitespace(0)); if (whitespace(rootEnd) !== text.length) return fail();
  const array = objectMembers({ start: whitespace(0), end: rootEnd }).get(arrayName); if (!array) return fail();
  let index = whitespace(array.start); if (text[index++] !== '[') return fail(); index = whitespace(index);
  const values: string[] = [];
  while (text[index] !== ']') {
    const start = index; index = valueEnd(index); if (index > array.end || values.length >= 20_000) return fail();
    const span = member === undefined ? { start, end: index } : objectMembers({ start, end: index }).get(member);
    if (!span) return fail(); values.push(text.slice(span.start, span.end));
    index = whitespace(index); if (text[index] === ']') break; if (text[index++] !== ',') return fail(); index = whitespace(index);
  }
  if (text[index] !== ']' || index + 1 !== array.end) return fail();
  return Object.freeze(values);
}
