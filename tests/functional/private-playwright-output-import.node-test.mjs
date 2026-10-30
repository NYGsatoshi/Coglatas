import assert from 'node:assert/strict';
import { chmodSync, copyFileSync, existsSync, lstatSync, mkdirSync, mkdtempSync, readFileSync, readdirSync, rmSync, symlinkSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { basename, dirname, join } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { fork, spawn, spawnSync } from 'node:child_process';
import test from 'node:test';

const repository = fileURLToPath(new URL('../../', import.meta.url));
const variables = ['TMPDIR', 'TMP', 'TEMP', 'TEST_WORKER_INDEX', 'COGLATAS_MOBILE_PRIVATE_OUTPUT_ROOT', 'COGLATAS_THREAD_PRIVATE_OUTPUT_ROOT', 'PLAYWRIGHT_LAST_RUN_OUTPUT_FILE', 'HOME', 'XDG_CACHE_HOME', 'COGLATAS_MOBILE_PRIVATE_OUTPUT_ROOT_CALLER', 'COGLATAS_THREAD_PRIVATE_OUTPUT_ROOT_CALLER'];
async function fixture(run) {
  const root = mkdtempSync(join(tmpdir(), 'coglatas-output-control-'));
  const saved = new Map(variables.map(key => [key, process.env[key]]));
  try {
    for (const key of ['TMPDIR', 'TMP', 'TEMP']) process.env[key] = root;
    delete process.env.TEST_WORKER_INDEX;
    delete process.env.COGLATAS_MOBILE_PRIVATE_OUTPUT_ROOT;
    delete process.env.COGLATAS_THREAD_PRIVATE_OUTPUT_ROOT;
    delete process.env.COGLATAS_MOBILE_PRIVATE_OUTPUT_ROOT_CALLER;
    delete process.env.COGLATAS_THREAD_PRIVATE_OUTPUT_ROOT_CALLER;
    delete process.env.PLAYWRIGHT_LAST_RUN_OUTPUT_FILE;
    mkdirSync(join(root, 'home'), { mode: 0o700 });
    mkdirSync(join(root, 'cache'), { mode: 0o700 });
    process.env.HOME = join(root, 'home');
    process.env.XDG_CACHE_HOME = join(root, 'cache');
    return await run(root);
  } finally {
    for (const [key, value] of saved) { if (value === undefined) delete process.env[key]; else process.env[key] = value; }
    rmSync(root, { recursive: true, force: true });
  }
}
function assertPrivateOutput(output, root) {
  assert.equal(basename(output), 'playwright-output');
  assert.equal(output.startsWith(join(root,'cache','coglatas','playwright-output') + '/'),true);
  const stat = lstatSync(dirname(output));
  assert.equal(stat.isDirectory(), true);
  assert.equal(stat.isSymbolicLink(), false);
  if (process.platform !== 'win32') {
    assert.equal(stat.uid, process.getuid());
    assert.equal(stat.mode & 0o777, 0o700);
  }
}

await test('actual config imports and worker execution retain selection, reporters and one private output identity', () => fixture(root => {
  const playwright = pathToFileURL(join(repository, 'node_modules/@playwright/test/index.mjs')).href;
  for (const [kind, mode] of [['mobile', 'matrix'], ['thread', 'matrix'], ['thread', 'default-fixture']]) {
    const directory = join(root, kind + '-' + mode); mkdirSync(directory);
    const metadata = join(directory, 'metadata.json'), worker = join(directory, 'worker.json');
    const canonical = pathToFileURL(join(repository, kind === 'mobile' ? 'playwright.mobile-real.config.ts' : 'playwright.thread-investigation.config.ts')).href;
    writeFileSync(join(directory, 'probe.spec.mjs'), `import {test,expect} from ${JSON.stringify(playwright)};
      import{writeFileSync}from'node:fs';
      test('FUNC-MSG-001 uses a dedicated 320px thread pane',async({},info)=>{
        writeFileSync(info.outputPath('canary'),'output');
        writeFileSync(${JSON.stringify(worker)},JSON.stringify({outputDir:info.project.outputDir}));
        expect(1).toBe(1);
      });`);
    writeFileSync(join(directory, 'config.mjs'), `import base from ${JSON.stringify(canonical)};
      import{writeFileSync}from'node:fs';
      if(process.env.TEST_WORKER_INDEX===undefined)writeFileSync(${JSON.stringify(metadata)},JSON.stringify({
        outputDir:base.outputDir,testMatch:base.testMatch,grep:base.grep?.source,
        retries:base.retries,workers:base.workers,reporter:base.reporter,projects:base.projects,
        webServer:base.webServer,maxFailures:base.maxFailures,use:base.use
      }));
      export default {...base,testDir:${JSON.stringify(directory)},testMatch:'probe.spec.mjs',reporter:[['line']],webServer:undefined,...(process.platform==='linux'?{}:{outputDir:${JSON.stringify(join(directory, "legacy-output"))}})};`);
    const run = spawnSync(process.execPath, [join(repository, 'node_modules/@playwright/test/cli.js'), 'test', '--config', join(directory, 'config.mjs')], {
      cwd: directory, encoding: 'utf8', env: { ...process.env, THREAD_INVESTIGATION_MODE: mode },
    });
    assert.equal(run.status, 0, run.stdout + run.stderr);
    const captured = JSON.parse(readFileSync(metadata, 'utf8'));
    if (process.platform === 'linux') {
      assert.equal(JSON.parse(readFileSync(worker, 'utf8')).outputDir, captured.outputDir);
      assertPrivateOutput(captured.outputDir, root);
    } else {
      assert.equal(captured.outputDir, kind === 'mobile' ? '/tmp/coglatas-real-backend-mobile-playwright' : '/tmp/coglatas-thread-mechanism-output');
      assert.equal(JSON.parse(readFileSync(worker, 'utf8')).outputDir, join(directory, 'legacy-output'));
    }
    assert.equal(captured.retries, 0); assert.equal(captured.workers, 1);
    if (kind === 'mobile') {
      assert.deepEqual(captured.testMatch, ['**/messaging/collaboration-owner-journey.spec.ts', '**/files/files-fast-journey.spec.ts', '**/mobile/channel.browser.ts']);
      assert.equal(captured.grep, /FUNC-(?:MSG-001|NOTIF-001|FILE-002|MOBILE-001)\b/.source);
      assert.deepEqual(captured.reporter, [['./tests/functional/mobile/reporter.mjs']]);
    } else {
      assert.equal(captured.testMatch, mode === 'matrix' ? '**/diagnostics/thread-denial-matrix.browser.ts' : '**/ui/message-thread-context.spec.ts');
      assert.equal(captured.maxFailures, 1);
      assert.deepEqual(captured.reporter, [['./tests/diagnostics/thread-matrix-reporter.mjs']]);
      assert.equal(captured.use.trace, 'off'); assert.equal(captured.use.screenshot, 'off'); assert.equal(captured.use.video, 'off');
      assert.equal(captured.webServer.reuseExistingServer, false);
    }
  }
}));

if (process.platform === 'linux') {
await test('last-failed selection survives stable private output and retains explicit environment and CLI precedence', () => fixture(root => {
  const playwright = pathToFileURL(join(repository, 'node_modules/@playwright/test/index.mjs')).href;
  for (const kind of ['mobile', 'thread']) {
    const directory = join(root, 'history-' + kind); mkdirSync(directory);
    const canonical = pathToFileURL(join(repository, kind === 'mobile' ? 'playwright.mobile-real.config.ts' : 'playwright.thread-investigation.config.ts')).href;
    writeFileSync(join(directory, 'history.spec.mjs'), `import {test,expect} from ${JSON.stringify(playwright)};
      test('FUNC-MSG-001 passing history control',()=>expect(1).toBe(1));
      test('FUNC-MSG-001 failing history control',()=>expect(1).toBe(2));`);
    writeFileSync(join(directory, 'config.mjs'), `import base from ${JSON.stringify(canonical)};
      import{writeFileSync}from'node:fs';
      if(process.env.TEST_WORKER_INDEX===undefined)writeFileSync(${JSON.stringify(join(directory,'automatic-history.json'))},JSON.stringify({file:base.outputDir+'/.last-run.json'}));
      export default {...base,testDir:${JSON.stringify(directory)},testMatch:'history.spec.mjs',reporter:[['line']],webServer:undefined,...(process.platform==='linux'?{}:{outputDir:${JSON.stringify(join(directory, "legacy-output"))}})};`);
    const run = (args = [], environment = {}) => spawnSync(process.execPath,
      [join(repository, 'node_modules/@playwright/test/cli.js'), 'test', '--config', join(directory, 'config.mjs'), ...args],
      { cwd: directory, encoding: 'utf8', env: { ...process.env, THREAD_INVESTIGATION_MODE: 'matrix', ...environment } });
    const first = run();
    assert.equal(first.status, 1, first.stdout + first.stderr);
    assert.match(first.stdout + first.stderr, /1 passed/);
    const selected = run(['--last-failed', '--list']);
    assert.equal(selected.status, 0, selected.stdout + selected.stderr);
    assert.match(selected.stdout, /failing history control/);
    assert.doesNotMatch(selected.stdout, /passing history control/);
    assert.match(selected.stdout, /Total: 1 test/);
    const defaultHistory = JSON.parse(readFileSync(join(directory, 'automatic-history.json'), 'utf8')).file;
    assert.ok(defaultHistory.startsWith(join(root, 'cache', 'coglatas', 'playwright-output') + '/'));
    assert.equal(JSON.parse(readFileSync(defaultHistory,'utf8')).failedTests.length,1);
    const environment = { PLAYWRIGHT_LAST_RUN_OUTPUT_FILE: 'environment-history.json' };
    assert.equal(run([], environment).status, 1);
    const explicitHistory = join(directory, 'environment-history.json');
    const explicitBytes = readFileSync(explicitHistory, 'utf8');
    assert.equal(existsSync(defaultHistory),false);
    const cliHistory = join(directory, 'cli-history.json');
    assert.equal(run(['--last-failed-file', cliHistory], environment).status, 1);
    assert.equal(JSON.parse(readFileSync(cliHistory, 'utf8')).failedTests.length, 1);
    assert.equal(readFileSync(explicitHistory, 'utf8'), explicitBytes);
    const cliSelected = run(['--last-failed', '--last-failed-file', cliHistory, '--list'], environment);
    assert.equal(cliSelected.status, 0, cliSelected.stdout + cliSelected.stderr);
    assert.match(cliSelected.stdout, /Total: 1 test/);
    assert.doesNotMatch(cliSelected.stdout, /passing history control/);
    mkdirSync(dirname(defaultHistory),{recursive:true,mode:0o700});
    writeFileSync(defaultHistory,'corrupt-synthetic-history');
    const corrupt=run(['--last-failed','--list']);assert.equal(corrupt.status,0,corrupt.stdout+corrupt.stderr);assert.match(corrupt.stdout,/Total: 2 tests/u);assert.equal(readFileSync(defaultHistory,'utf8'),'corrupt-synthetic-history');
    rmSync(defaultHistory);const cold=run(['--last-failed','--list']);assert.equal(cold.status,0,cold.stdout+cold.stderr);assert.match(cold.stdout,/Total: 2 tests/u);assert.equal(existsSync(defaultHistory),false);

  }
}));

await test('private cache initialization is shared across concurrent processes and separates canonical repositories', async () => fixture(async root => {
  const helper = pathToFileURL(join(repository, 'tests/ui/helpers/private-playwright-output.mjs')).href;
  const source = url => `import {privatePlaywrightOutput} from ${JSON.stringify(url)};const output=privatePlaywrightOutput('mobile');console.log(JSON.stringify({output,history:process.env.PLAYWRIGHT_LAST_RUN_OUTPUT_FILE}));`;
  const child = url => new Promise((resolveChild, reject) => {
    const processChild = spawn(process.execPath, ['--input-type=module', '-e', source(url)], { cwd: root, env: process.env, stdio: ['ignore','pipe','pipe'] });
    let stdout='',stderr=''; processChild.stdout.on('data', value => { stdout+=value; }); processChild.stderr.on('data', value => { stderr+=value; });
    processChild.on('error', reject); processChild.on('close', code => { try { assert.equal(code,0,stderr); resolveChild(JSON.parse(stdout)); } catch(error) { reject(error); } });
  });
  const pair = await Promise.all([child(helper),child(helper)]);
  assert.equal(pair[0].output,pair[1].output);
  const alias = join(root,'repository-alias'); symlinkSync(repository,alias);
  const aliased = await child(pathToFileURL(join(alias,'tests/ui/helpers/private-playwright-output.mjs')).href);
  assert.equal(aliased.output,pair[0].output);
  const other = join(root,'other-repository'); mkdirSync(join(other,'tests/ui/helpers'),{recursive:true,mode:0o700});
  copyFileSync(join(repository,'tests/ui/helpers/private-playwright-output.mjs'),join(other,'tests/ui/helpers/private-playwright-output.mjs'));
  symlinkSync(join(repository,'node_modules'),join(other,'node_modules'));
  const independent = await child(pathToFileURL(join(other,'tests/ui/helpers/private-playwright-output.mjs')).href);
  assert.notEqual(independent.output,pair[0].output);
}));

await test('read-only cwd works with private cache while unavailable cache requires explicit output', async () => fixture(root => {
  assert.notEqual(process.getuid(),0,'The read-only filesystem control must use an unprivileged Linux actor.');
  const directory = join(root,'readonly-control'); mkdirSync(directory);
  const locked = join(root,'readonly-cwd'); mkdirSync(locked); chmodSync(locked,0o500);
  const badCache = join(root,'cache-is-file'); writeFileSync(badCache,'preserve');
  const playwright = pathToFileURL(join(repository,'node_modules/@playwright/test/index.mjs')).href;
  writeFileSync(join(directory,'probe.spec.mjs'),`import {test,expect} from ${JSON.stringify(playwright)};test('FUNC-MSG-001 one-shot control',async({},info)=>{expect(info.outputPath('canary')).toBeTruthy();});`);
  for(const kind of ['mobile','thread']) {
    const canonical=pathToFileURL(join(repository,kind==='mobile'?'playwright.mobile-real.config.ts':'playwright.thread-investigation.config.ts')).href;
    const config=join(directory,kind+'.config.mjs');
    writeFileSync(config,`import base from ${JSON.stringify(canonical)};export default {...base,testDir:${JSON.stringify(directory)},testMatch:'probe.spec.mjs',reporter:[['line']],webServer:undefined};`);
    const run=(args=[],environment={})=>spawnSync(process.execPath,[join(repository,'node_modules/@playwright/test/cli.js'),'test','--config',config,...args],{cwd:locked,encoding:'utf8',env:{...process.env,THREAD_INVESTIGATION_MODE:'matrix',...environment}});
    const normal=run();assert.equal(normal.status,0,normal.stdout+normal.stderr);
    const oneShot=run([],{XDG_CACHE_HOME:badCache});assert.equal(oneShot.status,1);assert.match(oneShot.stderr,/Private Playwright output is unavailable/u);
    const denied=run(['--last-failed','--list'],{XDG_CACHE_HOME:badCache});assert.equal(denied.status,1);assert.match(denied.stderr,/Private Playwright output is unavailable/u);assert.doesNotMatch(denied.stdout,/Listing tests/u);
    const explicit=join(directory,kind+'-explicit.json');const chosen=run(['--last-failed-file',explicit,'--output',join(directory,kind+'-history-output')],{XDG_CACHE_HOME:badCache});assert.equal(chosen.status,0,chosen.stdout+chosen.stderr);assert.equal(JSON.parse(readFileSync(explicit,'utf8')).status,'passed');
    const output=join(directory,kind+'-explicit-output');const selectedOutput=run(['--output',output],{XDG_CACHE_HOME:badCache});assert.equal(selectedOutput.status,0,selectedOutput.stdout+selectedOutput.stderr);assert.equal(JSON.parse(readFileSync(join(output,'.last-run.json'),'utf8')).status,'passed');
  }
  assert.equal(readFileSync(badCache,'utf8'),'preserve');
}));

await test('installed parsed options distinguish option-looking grep values, terminators, repeated and empty explicit history', async () => fixture(root => {
  const directory=join(root,'parsed-options');mkdirSync(directory);
  const playwright=pathToFileURL(join(repository,'node_modules/@playwright/test/index.mjs')).href;
  const canonical=pathToFileURL(join(repository,'playwright.mobile-real.config.ts')).href;
  writeFileSync(join(directory,'probe.spec.mjs'),`import {test,expect} from ${JSON.stringify(playwright)};test('FUNC-MSG-001 passing --last-failed-file',()=>expect(1).toBe(1));test('FUNC-MSG-001 failing --last-failed-file',()=>expect(1).toBe(2));`);
  const config=join(directory,'config.mjs');writeFileSync(config,`import base from ${JSON.stringify(canonical)};export default {...base,testDir:${JSON.stringify(directory)},testMatch:'probe.spec.mjs',reporter:[['line']],webServer:undefined};`);
  const run=(args=[])=>spawnSync(process.execPath,[join(repository,'node_modules/@playwright/test/cli.js'),'test','--config',config,...args],{cwd:directory,encoding:'utf8',env:process.env});
  assert.equal(run(['--grep','--last-failed-file']).status,1);
  const grep=run(['--last-failed','--list','--grep','--last-failed-file']);assert.equal(grep.status,0,grep.stdout+grep.stderr);assert.match(grep.stdout,/Total: 1 test/u);
  const terminated=run(['--last-failed','--list','--','--last-failed-file']);assert.equal(terminated.status,0,terminated.stdout+terminated.stderr);assert.match(terminated.stdout,/Total: 1 test/u);
  const first=join(directory,'first.json'),last=join(directory,'last.json');assert.equal(run(['--last-failed-file',first,'--last-failed-file',last]).status,1);assert.equal(existsSync(first),false);assert.equal(JSON.parse(readFileSync(last,'utf8')).failedTests.length,1);
  const empty=run(['--last-failed-file=','--last-failed','--list']);assert.equal(empty.status,0,empty.stdout+empty.stderr);assert.match(empty.stdout,/Total: 2 tests/u);
}));

await test('explicit output and discovery remain usable with unwritable temp and an independent transform cache', async () => fixture(root => {
  assert.notEqual(process.getuid(),0,'This permission control requires an unprivileged Linux actor.');
  const directory=join(root,'explicit-temp');mkdirSync(directory);
  const bad=join(root,'unwritable-temp');mkdirSync(bad);chmodSync(bad,0o500);
  const transform=join(root,'transform-cache');mkdirSync(transform,{mode:0o700});
  const playwright=pathToFileURL(join(repository,'node_modules/@playwright/test/index.mjs')).href;
  const receipt=join(directory,'worker-output.json');
  writeFileSync(join(directory,'probe.spec.mjs'),`import{test,expect}from ${JSON.stringify(playwright)};import fs from'node:fs';test('FUNC-MSG-001 explicit output consumer',async({},info)=>{fs.writeFileSync(info.outputPath('canary'),'owned');fs.writeFileSync(${JSON.stringify(receipt)},JSON.stringify({output:info.project.outputDir}));expect(1).toBe(1);});`);
  for(const kind of ['mobile','thread']) {
    const canonical=pathToFileURL(join(repository,kind==='mobile'?'playwright.mobile-real.config.ts':'playwright.thread-investigation.config.ts')).href;
    const config=join(directory,kind+'.config.mjs');
    writeFileSync(config,`import base from ${JSON.stringify(canonical)};export default{...base,testDir:${JSON.stringify(directory)},testMatch:'probe.spec.mjs',reporter:[['line']],webServer:undefined};`);
    const run=args=>spawnSync(process.execPath,[join(repository,'node_modules/@playwright/test/cli.js'),'test','--config',config,...args],{cwd:directory,encoding:'utf8',env:{...process.env,TMPDIR:bad,PWTEST_CACHE_DIR:transform,THREAD_INVESTIGATION_MODE:'matrix'}});
    const listed=run(['--list']);assert.equal(listed.status,0,listed.stdout+listed.stderr);assert.match(listed.stdout,/Total: 1 test/u);
    const output=join(directory,kind+'-chosen-output');
    const selectedList=run(['--output',output,'--list']);assert.equal(selectedList.status,0,selectedList.stdout+selectedList.stderr);
    const actual=run(['--output',output]);assert.equal(actual.status,0,actual.stdout+actual.stderr);
    assert.equal(JSON.parse(readFileSync(receipt,'utf8')).output,output);
  }
}));

await test('pinned out-of-process loader preserves serialized explicit output without default cache IO', async () => fixture(async root => {
  const directory=join(root,'loader-control');mkdirSync(directory);
  const bad=join(directory,'cache-file');writeFileSync(bad,'preserve');
  const canonical=pathToFileURL(join(repository,'playwright.mobile-real.config.ts')).href;
  const playwright=pathToFileURL(join(repository,'node_modules/@playwright/test/index.mjs')).href;
  const config=join(directory,'config.mjs'),spec=join(directory,'probe.spec.mjs');
  writeFileSync(config,`import base from ${JSON.stringify(canonical)};export default{...base,testDir:${JSON.stringify(directory)},testMatch:'probe.spec.mjs',reporter:[['line']],webServer:undefined};`);
  writeFileSync(spec,`import{test}from ${JSON.stringify(playwright)};test('FUNC-MSG-001 loader probe',()=>{});`);
  const child=fork(join(repository,'node_modules/playwright/lib/loader/loaderProcessEntry.js'),[],{cwd:directory,env:{...process.env,XDG_CACHE_HOME:bad},stdio:['ignore','pipe','pipe','ipc']});
  let errors='';child.stderr.on('data',value=>{errors+=value;});
  const ready=new Promise((resolveReady,reject)=>{child.once('error',reject);child.on('message',message=>{if(message.method==='ready')resolveReady();});});
  await ready;
  child.send({method:'__init__',params:{processParams:{timeOrigin:{wallTime:Date.now(),monotonicTime:performance.now()}},runnerParams:{location:{configDir:directory,resolvedConfigFile:config},configCLIOverrides:{outputDir:join(directory,'chosen-output')}}}});
  const result=new Promise((resolveResult,reject)=>{child.on('message',message=>{if(message.method==='__dispatch__'&&message.params.id===1)resolveResult(message.params);});child.once('error',reject);});
  child.send({method:'__dispatch__',params:{id:1,method:'loadTestFile',params:{file:spec}}});
  try {const loaded=await result;assert.equal(loaded.error,undefined,JSON.stringify(loaded.error));assert.deepEqual(loaded.result.testErrors,[]);assert.equal(readFileSync(bad,'utf8'),'preserve');}
  finally {const exited=new Promise(resolveExit=>child.once('exit',resolveExit));child.send({method:'__stop__'});assert.equal(await exited,0,errors);}
}));

}
