import { closeSync, constants, fstatSync, lstatSync, openSync, readFileSync, realpathSync } from 'node:fs';
import { execFileSync } from 'node:child_process';
import { dirname, isAbsolute, parse, relative, resolve, sep } from 'node:path';
import { fileURLToPath } from 'node:url';

const reject = stage => { const error = new Error('Native Node coverage validation failed.'); error.code = stage; throw error; };

const moduleFile = realpathSync(fileURLToPath(import.meta.url));
const repositoryRoot = resolve(dirname(moduleFile), '../..');
const reportLocation = 'artifacts/sonar-node/lcov.info';

export function trustedGitExecutable(configured = process.env.COGLATAS_TEST_GIT_EXECUTABLE) {
  const executable = configured ?? (process.platform === 'win32' ? 'C:/Program Files/Git/cmd/git.exe' : '/usr/bin/git');
  if (!isAbsolute(executable) || (process.platform === 'win32' && parse(executable).root.length < 2)) reject('EXECUTABLE');
  return executable;
}

/** Pin regular repository files and verify the opened identity before reading. */
export function readOwnedRepositoryText(root, name) {
  const canonicalRoot = realpathSync(root), file = resolve(canonicalRoot, name);
  const path = relative(canonicalRoot, file);
  if (!path || path === '..' || path.startsWith('..' + sep) || isAbsolute(path)) reject('SOURCE');
  const before = lstatSync(file, { bigint: true });
  if (!before.isFile() || relative(file, realpathSync(file)) !== '') reject('SOURCE');
  let descriptor;
  try {
    descriptor = openSync(file, constants.O_RDONLY | (process.platform === 'win32' ? 0 : constants.O_NOFOLLOW));
    const opened = fstatSync(descriptor, { bigint: true });
    if (!opened.isFile() || opened.dev !== before.dev || opened.ino !== before.ino || relative(file, realpathSync(file)) !== '') reject('SOURCE');
    return readFileSync(descriptor, 'utf8');
  } finally {
    if (descriptor !== undefined) closeSync(descriptor);
  }
}

function validateRecordRows(rows, lineCount) {
  let branches = 0, uncoveredBranches = 0;
  const line = value => { if (!/^\d+$/.test(value) || Number(value) < 1 || Number(value) > lineCount) reject('LINE'); };
  const branchRows = rows.filter(row => row.startsWith('BRDA:'));
  for (const row of branchRows) {
    const match = /^BRDA:(\d+),\d+,\d+,(\d+|-)$/.exec(row);
    if (!match) reject('BRANCH');
    line(match[1]); branches++;
    if (match[2] === '-' || Number(match[2]) === 0) uncoveredBranches++;
  }
  for (const row of rows.filter(row => row.startsWith('DA:'))) {
    const match = /^DA:(\d+),\d+$/.exec(row);
    if (!match) { reject('LINE'); }
    line(match[1]);
  }
  for (const row of rows.filter(row => row.startsWith('FN:'))) line(row.slice(3).split(',')[0]);
  const totals = rows.filter(row => row.startsWith('BRF:'));
  if (totals.length !== 1 || totals[0] !== `BRF:${branchRows.length}`) reject('BRANCH_TOTAL');
  return { branches, uncoveredBranches };
}

/** Validate the original LCOV without rewriting, filtering or inventing hits. */
export function validateNodeCoverage(text, options = {}) {
  const root = realpathSync(options.root ?? repositoryRoot);
  const tracked = options.tracked ?? new Set(execFileSync(trustedGitExecutable(), ['ls-files', '-z'], { cwd: root, encoding: 'utf8' }).split('\0'));
  const read = options.read ?? (file => readOwnedRepositoryText(root, file));
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
    const result = validateRecordRows(rows, lineCount);
    branches += result.branches;
    uncoveredBranches += result.uncoveredBranches;
  }
  return { sourceFiles: paths.size, branches, uncoveredBranches };
}

/** The sole producer is pinned; CLI arguments never become a filesystem path. */
export function runCoverageCli(options = {}) {
  const root = options.root ?? repositoryRoot, args = options.args ?? process.argv.slice(2);
  const info = options.info ?? console.log, error = options.error ?? console.error;
  try {
    if (args.length !== 1 || args[0] !== reportLocation) reject('ARGUMENT');
    const result = validateNodeCoverage(readOwnedRepositoryText(root, reportLocation), { root });
    info('Native Node coverage source mapping verified: ' + JSON.stringify(result));
    return 0;
  } catch {
    error('Native Node coverage must contain only complete, tracked source records with valid original lines.');
    return 1;
  }
}

if (process.argv[1] && realpathSync(process.argv[1]) === moduleFile) {
  process.exitCode = runCoverageCli();
}
