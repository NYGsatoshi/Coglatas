import { readFileSync } from 'node:fs';
import { execFileSync } from 'node:child_process';
import { isAbsolute, relative, resolve, sep } from 'node:path';
import { pathToFileURL } from 'node:url';

const reject = stage => { const error = new Error('Native Node coverage validation failed.'); error.code = stage; throw error; };

/** Validate the original LCOV without rewriting, filtering or inventing hits. */
export function validateNodeCoverage(text, options = {}) {
  const root = resolve(options.root ?? process.cwd());
  const tracked = options.tracked ?? new Set(execFileSync('git', ['ls-files', '-z'], { cwd: root, encoding: 'utf8' }).split('\0'));
  const read = options.read ?? (file => readFileSync(file, 'utf8'));
  if (!text.trim() || !text.trimEnd().endsWith('end_of_record')) reject('RECORD');
  const paths = new Set(); let branches = 0, uncoveredBranches = 0;
  for (const block of text.trimEnd().split('end_of_record').slice(0, -1)) {
    const rows = block.trim().split(/\r?\n/);
    const sources = rows.filter(row => row.startsWith('SF:'));
    if (sources.length !== 1) reject('SOURCE');
    const source = sources[0].slice(3), file = resolve(root, source), path = relative(root, file);
    if (path.endsWith('.node-test.mjs') || !path || path === '..' || path.startsWith('..' + sep) || isAbsolute(path) || !tracked.has(path.replaceAll('\\', '/')) || paths.has(path)) reject('SOURCE');
    paths.add(path);
    const code = read(file), lineCount = code.split('\n').length - Number(code.endsWith('\n'));
    const line = value => { if (!/^\d+$/.test(value) || Number(value) < 1 || Number(value) > lineCount) reject('LINE'); };
    const branchRows = rows.filter(row => row.startsWith('BRDA:'));
    for (const row of branchRows) {
      const match = /^BRDA:(\d+),\d+,\d+,(\d+|-)$/.exec(row);
      if (!match) reject('BRANCH');
      line(match[1]); branches++;
      if (match[2] === '-' || Number(match[2]) === 0) uncoveredBranches++;
    }
    for (const row of rows.filter(row => row.startsWith('DA:'))) {
      const match = /^DA:(\d+),\d+$/.exec(row); if (!match) reject('LINE'); line(match[1]);
    }
    for (const row of rows.filter(row => row.startsWith('FN:'))) line(row.slice(3).split(',')[0]);
    const totals = rows.filter(row => row.startsWith('BRF:'));
    if (totals.length !== 1 || totals[0] !== `BRF:${branchRows.length}`) reject('BRANCH_TOTAL');
  }
  return { sourceFiles: paths.size, branches, uncoveredBranches };
}

if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  try {
    const result = validateNodeCoverage(readFileSync(process.argv[2], 'utf8'));
    console.log('Native Node coverage source mapping verified: ' + JSON.stringify(result));
  } catch {
    console.error('Native Node coverage must contain only complete, tracked source records with valid original lines.');
    process.exitCode = 1;
  }
}
