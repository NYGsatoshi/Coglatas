// Read-only diagnostic: original raw reports are never modified or imported.
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const assert = require('node:assert/strict');
const { TestCoverage } = require('internal/test_runner/coverage');
const cwd = process.argv[2], rawPath = process.argv[3], outputPath = process.argv[4];
const targets = ['tests/functional/fixtures/functional-diagnostics.mjs',
  'tests/functional/fixtures/messaging-observation.mjs', 'tests/ui/helpers/search-drawer-observation.mjs',
  'tests/functional/helpers/thread-probe-diagnostics.mjs', 'tests/functional/helpers/thread-probe-observation.mjs',
  'tests/ui/helpers/group-keyboard-observation.mjs'];
const sha256 = bytes => crypto.createHash('sha256').update(bytes).digest('hex');
const urls = new Set(targets.map(p => 'file://' + cwd + '/' + p));
const records = fs.readdirSync(rawPath).filter(f => /^coverage-.*\.json$/.test(f)).map(file => {
  const original = fs.readFileSync(rawPath + '/' + file);
  const data = JSON.parse(original);
  data.result = data.result.filter(s => urls.has(s.url));
  return { file, originalDigest: sha256(original), data };
}).filter(r => r.data.result.length);
const mapBindings = [];
for (const record of records) for (const script of record.data.result) {
  const map = record.data['source-map-cache']?.[script.url]?.data;
  if (!map) continue;
  for (let i = 0; i < map.sources.length; i++) {
    const sourceUrl = map.sources[i];
    assert(urls.has(sourceUrl), 'Unexpected mapped helper source ' + sourceUrl);
    const actual = fs.readFileSync(new URL(sourceUrl));
    assert.equal(typeof map.sourcesContent?.[i], 'string');
    assert(actual.equals(Buffer.from(map.sourcesContent[i], 'utf8')), 'Source map content differs from Git bytes');
    mapBindings.push({ file: record.file, source: sourceUrl.slice(('file://' + cwd + '/').length), sha256: sha256(actual) });
  }
}
const ascending = [...records].sort((a,b) => a.file.localeCompare(b.file));
const orders = { directory: records, ascending, descending: [...ascending].reverse() };
const outputs = {};
for (const [name, order] of Object.entries(orders)) {
  const tracker = new TestCoverage('/unused', undefined, { cwd, sourceMaps: true,
    coverageIncludeGlobs: targets, coverageExcludeGlobs: [] });
  const merged = new Map();
  for (const record of order) {
    const data = JSON.parse(JSON.stringify(record.data));
    tracker.mergeCoverage(merged, tracker.mapCoverageWithSourceMap(data));
  }
  tracker.getCoverageFromDirectory = () => [...merged.values()];
  const summary = tracker.summary();
  outputs[name] = summary.files.map(f => ({ ...f, path: path.relative(cwd, f.path),
    lines: f.lines?.map(l => ({ line: l.line, count: l.count, ignore: l.ignore })) })).sort((a,b) => a.path.localeCompare(b.path));
  assert.equal(outputs[name].length, 6);
}
for (const record of records) assert.equal(sha256(fs.readFileSync(rawPath + '/' + record.file)), record.originalDigest);
const result = { referenceSha: process.argv[5], originalRun: null,
  node: process.version, nodeInternalSourceSha256: sha256(process.binding('natives')['internal/test_runner/coverage']),
  affectedRawRecords: records.length, sourceMapBindings: mapBindings, outputs,
  orderResultsIdentical: JSON.stringify(outputs.ascending) === JSON.stringify(outputs.descending),
  originalRawBytesPreserved: true, importedReportRewritten: false,
  scope: 'Diagnostic replay of six helper modules using native internal mapper/merger. Bounded five-suite collection comparison; not a Main measurement or a proof of every historical fluctuation.' };
fs.writeFileSync(outputPath, JSON.stringify(result, null, 2) + '\n');
console.log(JSON.stringify({ node: result.node, records: records.length, verifiedMappedBindings: mapBindings.length,
  orderResultsIdentical: result.orderResultsIdentical, outputs: Object.fromEntries(Object.entries(outputs).map(([k,v]) => [k,v.map(f => ({path:f.path,totalLineCount:f.totalLineCount,coveredLineCount:f.coveredLineCount,totalBranchCount:f.totalBranchCount,coveredBranchCount:f.coveredBranchCount}))])) }));
