import assert from 'node:assert/strict';
import { chmodSync, lstatSync, mkdirSync, mkdtempSync, readFileSync, realpathSync, rmdirSync, rmSync, symlinkSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, relative } from 'node:path';
import test from 'node:test';
import { program } from 'playwright/lib/program';
import { privatePlaywrightOutput } from '../ui/helpers/private-playwright-output.mjs';

const command = program.commands.find(item => item.name() === 'test');
const variables = ['HOME', 'XDG_CACHE_HOME', 'PLAYWRIGHT_LAST_RUN_OUTPUT_FILE', 'TEST_WORKER_INDEX'];
const optionNames = ['output', 'list', 'lastFailed', 'lastFailedFile'];
function fixture(run) {
  const tempParent=realpathSync(tmpdir());
  const root = mkdtempSync(join(tempParent, 'coglatas-output-control-'));
  const saved = new Map(variables.map(key => [key, process.env[key]]));
  const savedOptions = new Map(optionNames.map(key => [key, command.opts()[key]]));
  try {
    for (const key of variables) delete process.env[key];
    for (const key of optionNames) command.setOptionValue(key, undefined);
    mkdirSync(join(root, 'home'), {mode:0o700}); mkdirSync(join(root, 'cache'), {mode:0o700});
    process.env.HOME = join(root, 'home'); process.env.XDG_CACHE_HOME = join(root, 'cache');
    return run(root);
  } finally {
    for (const [key,value] of saved) { if(value===undefined) delete process.env[key]; else process.env[key]=value; }
    for (const [key,value] of savedOptions) command.setOptionValue(key,value);
    assert.equal(lstatSync(root).isSymbolicLink(),false);
    assert.equal(dirname(realpathSync(root)),tempParent);
    rmSync(root,{recursive:true,force:true});
  }
}
function ownedOutput(output, root) {
  assert.match(relative(join(root,'cache','coglatas','playwright-output'),output), /^[a-f0-9]{64}[/\\](?:mobile|thread)[/\\]playwright-output$/u);
  const stat=lstatSync(dirname(output)); assert.equal(stat.uid,process.getuid()); assert.equal(stat.mode&0o777,0o700); assert.equal(stat.isSymbolicLink(),false);
}

if(process.platform!=='linux') {
  await test('non-Linux defaults fail closed while explicit output and safe discovery need no default',()=>fixture(root=>{
    const denied=/require an explicit trusted --output directory/u;
    for(const kind of ['mobile','thread']) assert.throws(()=>privatePlaywrightOutput(kind),denied);
    command.setOptionValue('output',join(root,'chosen-output'));
    assert.equal(privatePlaywrightOutput('mobile'),undefined);
    command.setOptionValue('output',undefined);command.setOptionValue('list',true);
    assert.equal(privatePlaywrightOutput('thread'),undefined);
    command.setOptionValue('lastFailed',true);command.setOptionValue('lastFailedFile','explicit-history.json');
    assert.equal(privatePlaywrightOutput('mobile'),undefined);
    command.setOptionValue('lastFailedFile','');process.env.PLAYWRIGHT_LAST_RUN_OUTPUT_FILE='environment-history.json';
    assert.throws(()=>privatePlaywrightOutput('mobile'),denied);
    command.setOptionValue('lastFailedFile',undefined);
    assert.equal(privatePlaywrightOutput('mobile'),undefined);
    command.setOptionValue('list',false);process.env.TEST_WORKER_INDEX='0';
    assert.throws(()=>privatePlaywrightOutput('mobile'),denied);
    assert.equal(process.env.PLAYWRIGHT_LAST_RUN_OUTPUT_FILE,'environment-history.json');
  }));
} else {
await test('stable private parents isolate kinds and preserve native output-local history',()=>fixture(root=>{
  const mobile=privatePlaywrightOutput('mobile'), thread=privatePlaywrightOutput('thread');
  ownedOutput(mobile,root); ownedOutput(thread,root); assert.notEqual(mobile,thread);
  mkdirSync(mobile);writeFileSync(join(mobile,'.last-run.json'),'owned-history');
  assert.equal(privatePlaywrightOutput('mobile'),mobile);
  assert.equal(readFileSync(join(mobile,'.last-run.json'),'utf8'),'owned-history');
  assert.equal(process.env.PLAYWRIGHT_LAST_RUN_OUTPUT_FILE,undefined);
}));
await test('relative cache setting follows the private synthetic HOME default',()=>fixture(root=>{
  process.env.XDG_CACHE_HOME='relative-cache';
  const output=privatePlaywrightOutput('mobile');
  assert.equal(relative(join(root,'home','.cache'),output).startsWith('coglatas/'),true);
}));
await test('symlink cache and existing file ancestors fail without touching their target',()=>fixture(root=>{
  const target=join(root,'target');mkdirSync(target);writeFileSync(join(target,'canary'),'preserve');
  const alias=join(root,'alias');symlinkSync(target,alias);
  process.env.XDG_CACHE_HOME=alias;
  assert.throws(()=>privatePlaywrightOutput('mobile'),/Private Playwright output is unavailable/u);
  assert.equal(readFileSync(join(target,'canary'),'utf8'),'preserve');
  const file=join(root,'file');writeFileSync(file,'preserve');process.env.XDG_CACHE_HOME=file;
  assert.throws(()=>privatePlaywrightOutput('thread'),/Private Playwright output is unavailable/u);
  command.setOptionValue('output','');command.setOptionValue('list',false);delete process.env.PLAYWRIGHT_LAST_RUN_OUTPUT_FILE;
  assert.throws(()=>privatePlaywrightOutput('mobile'),/Private Playwright output is unavailable/u);
  assert.equal(readFileSync(file,'utf8'),'preserve');
}));
await test('unsafe nonsticky ancestors and nonprivate final parents are never repaired or adopted',()=>fixture(root=>{
  chmodSync(join(root,'cache'),0o777);
  assert.throws(()=>privatePlaywrightOutput('mobile'),/Private Playwright output is unavailable/u);
  assert.equal(lstatSync(join(root,'cache')).mode&0o777,0o777);
  chmodSync(join(root,'cache'),0o700);
  const output=privatePlaywrightOutput('mobile');chmodSync(dirname(output),0o755);
  assert.throws(()=>privatePlaywrightOutput('mobile'),/Private Playwright output is unavailable/u);
  assert.equal(lstatSync(dirname(output)).mode&0o777,0o755);
}));
await test('a preexisting output-parent symlink is rejected and its target preserved',()=>fixture(root=>{
  const output=privatePlaywrightOutput('mobile'), parent=dirname(output);
  rmdirSync(parent);const target=join(root,'target');mkdirSync(target);writeFileSync(join(target,'canary'),'preserve');symlinkSync(target,parent);
  assert.throws(()=>privatePlaywrightOutput('mobile'),/Private Playwright output is unavailable/u);
  assert.equal(lstatSync(parent).isSymbolicLink(),true);assert.equal(readFileSync(join(target,'canary'),'utf8'),'preserve');
}));
await test('explicit output and discovery skip unused cache IO while empty history retains native fallback',()=>fixture(root=>{
  const file=join(root,'cache-file');writeFileSync(file,'preserve');process.env.XDG_CACHE_HOME=file;
  command.setOptionValue('output',join(root,'caller-output'));
  assert.equal(privatePlaywrightOutput('mobile'),undefined);
  command.setOptionValue('output',undefined);command.setOptionValue('list',true);
  assert.equal(privatePlaywrightOutput('thread'),undefined);
  command.setOptionValue('lastFailed',true);command.setOptionValue('lastFailedFile','explicit.json');
  assert.equal(privatePlaywrightOutput('mobile'),undefined);
  command.setOptionValue('lastFailedFile','');process.env.PLAYWRIGHT_LAST_RUN_OUTPUT_FILE='environment.json';
  assert.throws(()=>privatePlaywrightOutput('mobile'),/Private Playwright output is unavailable/u);
  command.setOptionValue('lastFailedFile',undefined);
  assert.equal(privatePlaywrightOutput('mobile'),undefined);
  command.setOptionValue('output','');command.setOptionValue('list',false);delete process.env.PLAYWRIGHT_LAST_RUN_OUTPUT_FILE;
  assert.throws(()=>privatePlaywrightOutput('mobile'),/Private Playwright output is unavailable/u);
  assert.equal(readFileSync(file,'utf8'),'preserve');
}));
await test('spoofed worker markers cannot bypass main directory validation',()=>fixture(root=>{
  const file=join(root,'cache-file');writeFileSync(file,'preserve');process.env.XDG_CACHE_HOME=file;process.env.TEST_WORKER_INDEX='0';
  assert.throws(()=>privatePlaywrightOutput('mobile'),/Private Playwright output is unavailable/u);
}));
await test('unknown kinds are rejected',()=>fixture(()=>assert.throws(()=>privatePlaywrightOutput('unexpected'),/Unknown Playwright output owner/u)));
}
