/** Bounded AST interpretation only. No eval, VM, transpilation or source execution. */
import ts from '../../frontend/node_modules/typescript/lib/typescript.js';
import { createHash } from 'node:crypto';
import { readFileSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import path from 'node:path';
import { hashPlaywrightTestId, constructSourceOnlyDiscovery } from '../../tests/ui/playwright-shard-evidence.mjs';
import { extractStaticSpecFiles, verifySourceDiscovery } from './qualify-playwright-full-source-list.mjs';

export const BASELINE = '3865b09a2d3ea3cb82f450c6d19ec643ffbae01f';
const projects = new Map([['chromium-desktop', 'CHROMIUM_DESKTOP'], ['chromium-mobile', 'CHROMIUM_MOBILE']]);
const refuse = () => { throw new Error('SOURCE_APPLICABILITY_REFUSED'); };
const check = value => { if (!value) refuse(); };
const digest = value => createHash('sha256').update(value).digest('hex');
const normalize = value => value.replace(/\r\n/g, '\n');
const named = (node, name) => ts.isIdentifier(node) && node.text === name;
const callName = node => node && ts.isCallExpression(node) ? node.expression.getText() : '';
const callback = call => call.arguments.findLast(node => ts.isArrowFunction(node) || ts.isFunctionExpression(node));
const expression = statement => ts.isExpressionStatement(statement) ? statement.expression : null;
const isDeclaration = call => ts.isCallExpression(call) && ['test', 'test.skip'].includes(callName(call)) &&
  call.arguments.length >= 2 && callback(call) === call.arguments.at(-1) && callback(call) !== call.arguments[0];
function visit(node, fn) { fn(node); ts.forEachChild(node, child => visit(child, fn)); }

/** Compare complete canonical source anchors, never a set of approved native IDs. */
export function verifySourceAnchors(candidate, baseline) {
  check(candidate instanceof Map && baseline instanceof Map && candidate.size === baseline.size && candidate.size > 0);
  for (const [file, text] of candidate) {
    check(typeof text === 'string' && text.length > 0 && text.length <= 1024 * 1024 &&
      typeof baseline.get(file) === 'string' && normalize(text) === normalize(baseline.get(file)));
  }
}

/** Interpret only original leading skips and local single-statement skip helpers. */
export function analyzeSpec(file, text) {
  check(typeof file === 'string' && typeof text === 'string' && text.length <= 1024 * 1024);
  const ast = ts.createSourceFile(file, normalize(text), ts.ScriptTarget.Latest, true, ts.ScriptKind.TS);
  check(ast.parseDiagnostics.length === 0);
  const helpers = new Map(), allSkips = new Set(), helperCalls = new Set(), consumed = new Set(), declarations = [];
  visit(ast, node => {
    if (!ts.isCallExpression(node)) return;
    if (callName(node) === 'test.skip' && !isDeclaration(node)) allSkips.add(node);
    if (callName(node) === 'test.fixme' || /^test\[/.test(callName(node))) refuse();
    if (isDeclaration(node)) declarations.push(node);
  });
  for (const statement of ast.statements) {
    if (!ts.isFunctionDeclaration(statement) || !statement.name || !statement.body) continue;
    const contained = [...allSkips].filter(node => node.pos >= statement.pos && node.end <= statement.end);
    if (!contained.length) continue;
    check(statement.parameters.length === 1 && ts.isIdentifier(statement.parameters[0].name) &&
      statement.body.statements.length === 1 && expression(statement.body.statements[0]) === contained[0] && contained.length === 1);
    check(!helpers.has(statement.name.text));
    helpers.set(statement.name.text, statement);
    consumed.add(contained[0]);
  }
  visit(ast, node => { if (ts.isCallExpression(node) && helpers.has(callName(node))) helperCalls.add(node); });

  function predicate(skip, parameter) {
    check(callName(skip) === 'test.skip' && skip.arguments.length === 2 && ts.isStringLiteral(skip.arguments[1]));
    const binary = skip.arguments[0];
    check(ts.isBinaryExpression(binary) && binary.operatorToken.kind === ts.SyntaxKind.ExclamationEqualsEqualsToken &&
      ts.isStringLiteral(binary.right) && projects.has(binary.right.text));
    const member = binary.left;
    check(ts.isPropertyAccessExpression(member) && member.name.text === 'name' &&
      ts.isPropertyAccessExpression(member.expression) && member.expression.name.text === 'project' &&
      named(member.expression.expression, parameter));
    consumed.add(skip);
    return binary.right.text;
  }
  function leading(fn) {
    check(fn && ts.isBlock(fn.body));
    const first = expression(fn.body.statements[0] ?? ast);
    if (!first || (!allSkips.has(first) && !helperCalls.has(first))) return null;
    check(fn.parameters.length === 2 && ts.isIdentifier(fn.parameters[1].name));
    const parameter = fn.parameters[1].name.text;
    if (allSkips.has(first)) return { target: predicate(first, parameter), trace: first.getText(ast) };
    const helper = helpers.get(callName(first));
    check(first.arguments.length === 1 && named(first.arguments[0], parameter));
    consumed.add(first);
    return { target: predicate(expression(helper.body.statements[0]), helper.parameters[0].name.text),
      trace: first.getText(ast) + '\0' + helper.getText(ast) };
  }
  const cases = declarations.map(node => {
    const scopes = [];
    for (let parent = node.parent; parent; parent = parent.parent) {
      if (ts.isCallExpression(parent) && ['test.describe', 'test.describe.skip'].includes(callName(parent))) {
        check(ts.isStringLiteral(parent.arguments[0]) && callback(parent));
        scopes.unshift(parent);
      }
    }
    const conditions = [];
    const own = leading(callback(node));
    if (own) conditions.push({ ...own, origin: 'CASE' });
    for (const scope of scopes) {
      const hooks = callback(scope).body.statements.map(expression).filter(value => callName(value) === 'test.beforeEach');
      for (const hook of hooks) {
        const condition = leading(callback(hook));
        if (condition) {
          check(hooks.length === 1);
          conditions.push({ ...condition, origin: 'BEFORE_EACH', scope: scope.getText(ast) });
        }
      }
    }
    check(conditions.length <= 1);
    const start = ast.getLineAndCharacterOfPosition(node.getStart(ast));
    const end = ast.getLineAndCharacterOfPosition(node.expression.end);
    check(start.line === end.line);
    // Generated unconditional titles are supplied by the native SDK, never evaluated here.
    // Conditional cases must have literal declaration titles for an unambiguous binding.
    check(!conditions.length || ts.isStringLiteral(node.arguments[0]));
    return { line: start.line + 1, column: start.character + 1, columnEnd: end.character + 1,
      title: ts.isStringLiteral(node.arguments[0]) ? node.arguments[0].text : null,
      scopes: scopes.map(scope => scope.arguments[0].text),
      declaredSkip: callName(node) === 'test.skip' || scopes.some(scope => callName(scope) === 'test.describe.skip'),
      condition: conditions[0] ?? null, binding: digest(node.getText(ast) + '\0' + JSON.stringify(conditions)) };
  });
  check([...allSkips, ...helperCalls].every(node => consumed.has(node)));
  return cases;
}

/** Pure builder; caller independently establishes the complete SDK suite and source anchors. */
export function buildApplicability({ sourceSha, files, baselineFiles, tests, configuredProjects }) {
  check(/^[a-f0-9]{40}$/.test(sourceSha) && Array.isArray(tests) && tests.length > 0 && tests.length <= 5000 &&
    Array.isArray(configuredProjects) && configuredProjects.length === projects.size &&
    new Set(configuredProjects).size === projects.size && configuredProjects.every(name => projects.has(name)));
  verifySourceAnchors(files, baselineFiles);
  const analyses = new Map([...files].filter(([file]) => file.endsWith('.spec.ts')).map(([file, text]) => [file, analyzeSpec(file, text)]));
  const seen = new Set();
  const rows = tests.map(test => {
    check(test && typeof test.project === 'string' && projects.has(test.project));
    const id = hashPlaywrightTestId(test.id);
    check(!seen.has(id)); seen.add(id);
    const matches = analyses.get(test.file)?.filter(row => row.line === test.line &&
      Number.isInteger(test.column) && row.column <= test.column && test.column <= row.columnEnd);
    check(matches?.length === 1);
    const declaration = matches[0];
    check((declaration.title === null || test.title === declaration.title) &&
      JSON.stringify(test.scopes) === JSON.stringify(declaration.scopes));
    check(test.expectedStatus === (declaration.declaredSkip ? 'skipped' : 'passed'));
    const condition = declaration.condition;
    const classification = declaration.declaredSkip ? 'DECLARATION_SKIP' :
      condition && condition.target !== test.project ? 'CONDITIONAL_SKIP' : 'RUN';
    check(!(declaration.declaredSkip && condition));
    return { id, project: projects.get(test.project), classification,
      bindingSha256: digest('coglatas.playwright-applicability-binding.v1\0' +
        JSON.stringify({ fileDigest: digest(normalize(files.get(test.file))), declaration: declaration.binding,
          line: test.line, column: test.column, project: test.project, id })) };
  }).sort((a, b) => a.id < b.id ? -1 : a.id > b.id ? 1 : 0);
  const source = constructSourceOnlyDiscovery({ sourceSha,
    discoveredTests: tests.map(test => ({ id: test.id, expectedStatus: test.expectedStatus })) });
  verifySourceDiscovery(source, sourceSha, tests.length);
  const payload = { sourceSha, suite: 'angular-static', rosterSha256: source.rosterSha256,
    anchorSha: BASELINE, sourceManifestSha256: digest(JSON.stringify([...files].sort(([a], [b]) => a < b ? -1 : a > b ? 1 : 0)
      .map(([file, text]) => [file, digest(normalize(text))]))), tests: rows };
  return { source, applicability: { schema: 'coglatas.playwright-source-applicability.v1', ...payload,
    applicabilitySha256: digest(JSON.stringify(payload)), artifactTrust: 'EXTERNAL_VERIFICATION_REQUIRED',
    requiredCheckQualified: false, qualification: 'AST_SOURCE_APPLICABILITY_ONLY_NOT_EXECUTION' } };
}

/** Local status.showUntrackedFiles must never hide untracked qualification input. */
export function verifyCleanCheckout(git) {
  check(git(['status', '--porcelain', '--untracked-files=all']).trim() === '');
}

/** Read pinned Git blobs and compare the actual checkout; no input source is evaluated. */
export function collectApplicability(root, sourceSha, config, suite) {
  check(/^[a-f0-9]{40}$/.test(sourceSha) && ts.version === '6.0.3');
  const git = args => {
    const result = spawnSync('git', args, { cwd: root, encoding: 'utf8', timeout: 15000, maxBuffer: 2 * 1024 * 1024 });
    check(!result.error && !result.signal && result.status === 0); return result.stdout;
  };
  check(git(['rev-parse', 'HEAD']).trim() === sourceSha);
  verifyCleanCheckout(git);
  const runner = 'tests/ui/run-angular-playwright.mjs';
  const selected = extractStaticSpecFiles(git(['show', sourceSha + ':' + runner]));
  const paths = [...selected, runner, 'playwright.config.ts'];
  const files = new Map(paths.map(file => [file, git(['show', sourceSha + ':' + file])]));
  const baselineFiles = new Map(paths.map(file => [file, git(['show', BASELINE + ':' + file])]));
  for (const [file, text] of files) check(normalize(readFileSync(path.join(root, file), 'utf8')) === normalize(text));
  check(config.projects.length === 2 && config.projects.every(project =>
    (project.use.browserName ?? project.use.defaultBrowserType) === 'chromium' &&
    project.use.isMobile === (project.name === 'chromium-mobile')));
  const tests = suite.allTests().map(test => {
    const scopes = [];
    for (let parent = test.parent; parent; parent = parent.parent) if (parent.type === 'describe') scopes.unshift(parent.title);
    return { id: test.id, expectedStatus: test.expectedStatus, title: test.title, scopes,
      file: path.relative(root, test.location.file).split(path.sep).join('/'),
      line: test.location.line, column: test.location.column, project: test.parent.project().name };
  });
  const result = buildApplicability({ sourceSha, files, baselineFiles, tests, configuredProjects: config.projects.map(p => p.name) });
  // The shared verifier's omitted third argument deliberately enforces its original
  // full-suite default of 456. Passing tests.length here would weaken that boundary.
  verifySourceDiscovery(result.source, sourceSha);
  check(git(['rev-parse', 'HEAD']).trim() === sourceSha);
  return result;
}
