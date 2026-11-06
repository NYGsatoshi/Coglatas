import assert from 'node:assert/strict';
import test from 'node:test';
import { readFileSync, mkdtempSync, writeFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { analyzeSpec, buildApplicability, verifySourceAnchors, collectApplicability } from '../../scripts/ci/playwright-source-applicability.mjs';

const sourceSha = 'a'.repeat(40), configuredProjects = ['chromium-desktop', 'chromium-mobile'];
const source = `import { test } from '@playwright/test';
test.describe('PRIVATE_SCOPE', () => {
  test.beforeEach(async ({}, info) => { onlyMobile(info); });
  test('PRIVATE_ONE', async ({}, info) => {});
  test('PRIVATE_TWO', async ({}, info) => {});
});
test('PRIVATE_THREE', async ({}, info) => { test.skip(info.project.name !== 'chromium-desktop', 'PRIVATE_REASON'); });
test.describe.skip('PRIVATE_DECLARED_SCOPE', () => { test('PRIVATE_DECLARED', async () => {}); });
function onlyMobile(info) { test.skip(info.project.name !== 'chromium-mobile', 'PRIVATE_REASON'); }
`;
const fixture = (text = source) => {
  const declarations = analyzeSpec('fixture.spec.ts', text);
  const tests = declarations.flatMap((row, index) => configuredProjects.map(project => ({
    id: 'native-' + index + '-' + project, file: 'fixture.spec.ts', title: row.title,
    scopes: row.scopes, line: row.line, column: row.column, project,
    expectedStatus: row.declaredSkip ? 'skipped' : 'passed',
  })));
  return { sourceSha, configuredProjects, files: new Map([['fixture.spec.ts', text]]),
    baselineFiles: new Map([['fixture.spec.ts', source]]), tests };
};

test('AST binds per-case and enclosing-hook predicates without exposing source text', () => {
  const value = buildApplicability(fixture());
  assert.equal(value.source.tests.length, 8);
  assert.equal(value.source.tests.filter(row => row.status === 'skipped').length, 2);
  assert.equal(value.applicability.tests.filter(row => row.classification === 'CONDITIONAL_SKIP').length, 3);
  assert.equal(value.applicability.tests.filter(row => row.classification === 'RUN').length, 3);
  assert.doesNotMatch(JSON.stringify(value), /PRIVATE_|fixture\.spec/);
  assert.equal(value.applicability.requiredCheckQualified, false);
  const again = fixture(); again.tests.reverse();
  assert.deepEqual(buildApplicability(again), value);
});

test('reversed, unknown and dynamic predicates, ambiguous hooks and nonleading helpers reject', () => {
  for (const text of [source.replace("!== 'chromium-mobile'", "=== 'chromium-mobile'"),
    source.replace("!== 'chromium-mobile'", "!== 'firefox-desktop'"),
    source.replace("info.project.name !== 'chromium-mobile'", 'arbitraryFunction()'),
    source.replace('onlyMobile(info);', 'await Promise.resolve(); onlyMobile(info);'),
    source.replace("test.beforeEach(async", "test.beforeEach(async () => {}); test.beforeEach(async"),
    source + "test.beforeEach(async ({}, info) => { test.skip(info.project.name !== 'chromium-mobile', 'PRIVATE'); });",
    source.replace('onlyMobile(info);', 'onlyMobile(otherInfo);')]) {
    assert.throws(() => analyzeSpec('fixture.spec.ts', text), /SOURCE_APPLICABILITY_REFUSED/);
  }
});

test('source anchors reject removed, moved, reversed, extra and changed-scope skip constructs', () => {
  for (const changed of [
    source.replace("test.skip(info.project.name !== 'chromium-desktop', 'PRIVATE_REASON');", ''),
    source.replace('onlyMobile(info);', '').replace("test('PRIVATE_THREE', async ({}, info) => {", "test('PRIVATE_THREE', async ({}, info) => { onlyMobile(info);"),
    source.replace("!== 'chromium-mobile'", "!== 'chromium-desktop'"),
    source.replace("test('PRIVATE_TWO', async ({}, info) => {});", "test('PRIVATE_TWO', async ({}, info) => { onlyMobile(info); });"),
    source.replace("  test.beforeEach", "  test.describe('PRIVATE_CHANGED_SCOPE', () => {}); test.beforeEach"),
    source + "test('PRIVATE_EXTRA', async ({}, info) => { onlyMobile(info); });",
  ]) assert.throws(() => verifySourceAnchors(new Map([['fixture.spec.ts', changed]]), fixture().baselineFiles), /SOURCE_APPLICABILITY_REFUSED/);
});

test('generated unconditional declarations never grant conditional skip authority', () => {
  const generated = "test(`PRIVATE_${value}`, async ({}, info) => {});";
  assert.equal(analyzeSpec('fixture.spec.ts', generated)[0].title, null);
  assert.throws(() => analyzeSpec('fixture.spec.ts', generated.replace('{}',
    "{test.skip(info.project.name !== 'chromium-mobile', 'PRIVATE');}")), /SOURCE_APPLICABILITY_REFUSED/);
});

test('unknown projects, unmatched locations, duplicate cases, source types and checkout identity reject', () => {
  for (const mutate of [
    x => { x.configuredProjects[0] = 'webkit-desktop'; },
    x => { x.tests[0].line++; },
    x => { x.tests[0].scopes = []; },
    x => { x.tests.push(x.tests[0]); },
    x => { x.tests[0].expectedStatus = 'skipped'; },
    x => { x.tests = []; },
    x => { x.files = new Map(); },
    x => { x.sourceSha = 'bad'; },
  ]) {
    const value = fixture(); value.configuredProjects = [...configuredProjects];
    mutate(value); assert.throws(() => buildApplicability(value), /SOURCE_APPLICABILITY_REFUSED/);
  }
  assert.throws(() => collectApplicability(process.cwd(), '0'.repeat(40), {}, {}), /SOURCE_APPLICABILITY_REFUSED/);
});

test('actual native SDK IDs bind to AST declaration, project and describe scope in list-only mode', () => {
  const root = fileURLToPath(new URL('../../', import.meta.url));
  const directory = mkdtempSync(path.join(tmpdir(), 'coglatas-applicability-'));
  try {
    const raw = source.replace("'@playwright/test'", JSON.stringify(path.join(root, 'node_modules/@playwright/test/index.mjs')));
    writeFileSync(path.join(directory, 'fixture.spec.ts'), raw);
    writeFileSync(path.join(directory, 'playwright.config.mjs'),
      `export default {testDir:'.',testMatch:'fixture.spec.ts',projects:[{name:'chromium-desktop'},{name:'chromium-mobile'}]};`);
    const moduleUrl = pathToFileURL(path.join(root, 'scripts/ci/playwright-source-applicability.mjs')).href;
    writeFileSync(path.join(directory, 'reporter.mjs'), `import {buildApplicability} from ${JSON.stringify(moduleUrl)};
      import {readFileSync} from 'node:fs';
      export default class {printsToStdio(){return true;} onBegin(config,suite){
        const text=readFileSync(${JSON.stringify(path.join(directory, 'fixture.spec.ts'))},'utf8');
        const tests=suite.allTests().map(t=>{const scopes=[];for(let p=t.parent;p;p=p.parent)if(p.type==='describe')scopes.unshift(p.title);
          return {id:t.id,file:'fixture.spec.ts',title:t.title,scopes,line:t.location.line,column:t.location.column,
            project:t.parent.project().name,expectedStatus:t.expectedStatus};});
        console.log(JSON.stringify(buildApplicability({sourceSha:${JSON.stringify(sourceSha)},
          files:new Map([['fixture.spec.ts',text]]),baselineFiles:new Map([['fixture.spec.ts',text]]),
          configuredProjects:config.projects.map(p=>p.name),tests})));}}`);
    const result = spawnSync(process.execPath, [path.join(root, 'node_modules/@playwright/test/cli.js'), 'test', '--list',
      '--config', path.join(directory, 'playwright.config.mjs'), '--reporter', path.join(directory, 'reporter.mjs')],
    { encoding: 'utf8', timeout: 30000, maxBuffer: 1024 * 1024 });
    assert.equal(result.status, 0, 'SDK list-only control refused; child output withheld');
    const value = JSON.parse(result.stdout);
    assert.equal(value.applicability.tests.filter(row => row.classification === 'CONDITIONAL_SKIP').length, 3);
    assert.equal(value.source.tests.length, 8);
    assert.doesNotMatch(result.stdout, /PRIVATE_/);
  } finally { rmSync(directory, { recursive: true, force: true }); }
});
