import { readFileSync, readdirSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { createHash } from 'node:crypto';

// Explicit developer regeneration from actual Core exports. Never run during tests/build.
const args = process.argv.slice(2);
if (args.length !== 2 || args[0] !== '--producer-dir') throw new Error('Supply --producer-dir with the complete original Core export directory.');
const root = resolve(args[1]);
const profiles = ["compiler-outputs-v1.json","compiler-recordings-v1.json","dependency-analysis-v1.json","diagnostic-premises-v1.json","execution-plan-v1.json","graph-closure-v1.json","operator-recordings-v1.json","pass-recordings-v1.json","predicate-recordings-v1.json","preview-v1.json","regional-operator-recordings-v1.json","root-diagnostic-premises-v1.json","scenario-v1.json","scoped-publication-v2.json","scoped-sources-v1.json","semantic-index-large-v1.json","semantic-index-v1.json","simulation-comparison-v1.json","simulation-debug-v1.json","simulation-profile-v1.json","simulation-v1.json","test-discovery-v1.json","test-edit-v1.json"];
if (JSON.stringify(readdirSync(root).filter(name => name.endsWith('.json')).sort()) !== JSON.stringify(profiles)) throw new Error('Expected all 23 original producer profiles.');
const equalContext = (a, b) => canonical(a) === canonical(b);
const canonical = value => JSON.stringify(order(value));
function order(value) { return Array.isArray(value) ? value.map(order) : value !== null && typeof value === 'object' ? Object.fromEntries(Object.keys(value).sort().map(key => [key, order(value[key])])) : value; }
const pins = new Map();
let projectedCases = 0;
for (const profile of profiles) {
  const raw = readFileSync(resolve(root, profile));
  if (raw.length > 33554432) throw new Error('Producer profile exceeds its existing bound.');
  const bundle = JSON.parse(raw.toString('utf8'));
  for (const value of bundle.cases) {
    const projection = value.localProjectTestDiscovery;
    if (!projection) continue;
    const input = value.inputDigest;
    if (input?.algorithm !== 'sha-256' || !/^[a-f0-9]{64}$/.test(input.value) || createHash('sha256').update('coglatas.compiler-input/1\0' + projection.inputText).digest('hex') !== input.value) throw new Error('Original Core input identity mismatch.');
    const inspectionTests = value.semanticInspection?.symbols.filter(item => item.kind === 'coglatas.project-test' && equalContext(item.location.context, value.context)).map(item => ({ testId: item.location.entityId, decodeStatus: item.status }));
    const retained = projection.binding?.retainedTests ?? inspectionTests;
    if (!Array.isArray(retained) || retained.length > 256 || new Set(retained.map(item => item.testId)).size !== retained.length) throw new Error('Invalid actual Core retention roster.');
    const entries = retained.map(item => {
      if (!/^[a-f0-9]{8}(?:-[a-f0-9]{4}){3}-[a-f0-9]{12}$/.test(item.testId) || !['Decoded', 'Invalid', 'Unsupported'].includes(item.decodeStatus)) throw new Error('Unexpected retained identity/status.');
      return [item.testId, item.decodeStatus];
    }).sort(([a], [b]) => a < b ? -1 : a > b ? 1 : 0);
    if (projection.binding && inspectionTests && canonical(entries) !== canonical(inspectionTests.map(item => [item.testId, item.decodeStatus]).sort(([a], [b]) => a < b ? -1 : a > b ? 1 : 0))) throw new Error('Contradictory Core retention projections.');
    const previous = pins.get(input.value);
    if (previous && canonical(previous) !== canonical(entries)) throw new Error('One original Core input has conflicting retention.');
    pins.set(input.value, entries); projectedCases++;
  }
}
const ordered = [...pins].sort(([a], [b]) => a < b ? -1 : a > b ? 1 : 0);
const body = '// Generated explicitly from all original Core producer profiles.\n// Fixed synthetic example integrity only; no Registry, producer Trust, execution or access grant.\n// Do not regenerate from served projections during tests or build.\n' +
  "type CoreStatus = 'Decoded' | 'Invalid' | 'Unsupported';\n" +
  'const retainedByInput = new Map<string, readonly (readonly [string, CoreStatus])[]>(\n' + JSON.stringify(ordered, null, 2) + '\n);\n\n' +
  'export function matchesPinnedLocalTestRetention(inputDigest: string, tests: readonly { readonly testId: string; readonly decodeStatus: string }[]): boolean {\n' +
  '  const expected = retainedByInput.get(inputDigest);\n  if (!expected || expected.length !== tests.length) return false;\n' +
  '  const actual = new Map(tests.map(test => [test.testId, test.decodeStatus]));\n' +
  '  return actual.size === tests.length && expected.every(([testId, status]) => actual.get(testId) === status);\n}\n';
writeFileSync(fileURLToPath(new URL('../src/app/features/projectide/projectide-test-discovery.evidence.ts', import.meta.url)), body);
console.log(JSON.stringify({ profiles: profiles.length, projectedCases, pinnedInputs: pins.size }));
