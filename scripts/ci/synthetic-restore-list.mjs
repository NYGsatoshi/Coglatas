import { createHash } from 'node:crypto';

const MAX_BYTES = 16 * 1024 * 1024, MAX_ENTRIES = 100_000;
const reject = () => {
  const error = new Error('Synthetic restore list rejected.');
  error.code = 'RESTORE_LIST'; throw error;
};
const require = condition => { if (!condition) reject(); };
const hash = bytes => createHash('sha256').update(bytes).digest('hex');
const unsigned = value => /^(?:0|[1-9]\d*)$/.test(value) && Number(value) <= 4_294_967_295;

function parseEntry(line, index) {
  const body = line.replace(/\r?\n$/, '');
  require(!body.includes('\r') && !body.includes('\0'));
  if (/^\s*$/.test(body) || body.startsWith(';')) return null;
  const match = /^([1-9]\d*); (\d+) (\d+) (.+)$/.exec(body);
  require(match && Number(match[1]) <= 2_147_483_647 && unsigned(match[2]) && unsigned(match[3]));
  const foreignKey = match[4].startsWith('FK CONSTRAINT');
  if (foreignKey) require(match[2] === '2606' && match[3] !== '0' && /^FK CONSTRAINT .+/.test(match[4]));
  return { id: match[1], oid: Number(match[3]), foreignKey, index, line };
}

function parseList(bytes) {
  require(Buffer.isBuffer(bytes) && bytes.length > 0 && bytes.length <= MAX_BYTES && bytes.at(-1) === 10);
  let text;
  try { text = new TextDecoder('utf-8', { fatal: true }).decode(bytes); } catch { reject(); }
  require(Buffer.from(text).equals(bytes));
  let lineCount = 0;
  for (const character of text) if (character === '\n') require(++lineCount <= MAX_ENTRIES * 4);
  const lines = text.split(/(?<=\n)/), entries = lines.map((line, index) => parseEntry(line, index)).filter(Boolean);
  require(entries.length > 0 && entries.length <= MAX_ENTRIES);
  require(new Set(entries.map(entry => entry.id)).size === entries.length);
  const foreignKeys = entries.filter(entry => entry.foreignKey);
  require(new Set(foreignKeys.map(entry => entry.oid)).size === foreignKeys.length);
  return { lines, entries, foreignKeys };
}

/** Reject omissions, substitutions, duplicates and moves outside original FK slots. */
export function verifyRestoreList(original, planned) {
  const before = parseList(original), after = parseList(planned);
  require(before.lines.length === after.lines.length && before.entries.length === after.entries.length &&
    before.foreignKeys.length === after.foreignKeys.length);
  const originals = new Map(before.entries.map(entry => [entry.id, entry.line]));
  require(after.entries.every(entry => originals.get(entry.id) === entry.line));
  const slots = new Set(before.foreignKeys.map(entry => entry.index));
  require(before.lines.every((line, index) => slots.has(index) || line === after.lines[index]));
  require(after.foreignKeys.every((entry, index, entries) => slots.has(entry.index) &&
    (index === 0 || entries[index - 1].oid < entry.oid)));
  return { entryCount: before.entries.length, foreignKeyCount: before.foreignKeys.length };
}

/** Replay original FK creation order; physical OIDs never enter snapshot identity. */
export function planRestoreList(original) {
  const parsed = parseList(original), lines = [...parsed.lines];
  const ordered = [...parsed.foreignKeys].sort((left, right) => left.oid - right.oid);
  parsed.foreignKeys.forEach((entry, index) => { lines[entry.index] = ordered[index].line; });
  const bytes = Buffer.from(lines.join(''));
  return { bytes, ...verifyRestoreList(original, bytes) };
}

const INSTALL = 'set -eu; umask 077; test ! -e /tmp/coglatas-recovery.list; test ! -L /tmp/coglatas-recovery.list; ' +
  '(set -C; cat > /tmp/coglatas-recovery.list); ' +
  'test "$(stat -c %a /tmp/coglatas-recovery.list)" = 600; sha256sum /tmp/coglatas-recovery.list';

/** The caller admits only its fresh owned PostgreSQL containers. Never interpolate list data. */
export function installRestoreList(compose, project, bytes) {
  parseList(bytes);
  const result = compose(project, ['exec', '-T', 'postgres', '/bin/sh', '-c', INSTALL], { input: bytes });
  require(result.status === 0 && result.stdout.toString('utf8').trim() === hash(bytes) + '  /tmp/coglatas-recovery.list');
}
