import { readFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { expect, test, type Page } from '@playwright/test';
import { expectNoAccessibilityViolations } from './a11y';
const hash = (format: string, text: string) => ({ algorithm: 'sha-256', value: createHash('sha256').update(format + '\0' + text).digest('hex') });
async function openExample(page: Page, locale: 'en' | 'ja') {
  await page.addInitScript(locale => { localStorage.setItem('coglatas.locale', locale); window.__COGLATAS_FEATURE_FLAGS__ = { 'projectide.syntheticPreview': true }; }, locale);
  await page.goto('/app/projectide/preview');
  await page.getByRole('button', { name: locale === 'en' ? 'Open Compiler recording example' : 'Compiler記録の例を開く', exact: true }).click();
}
async function readSources(page: Page, locale: 'en' | 'ja', key: string, regions: number) {
  await page.getByTestId('ide-context').selectOption(key);
  await page.getByRole('button', { name: locale === 'en' ? 'Load result fixture' : '結果Fixtureを読み込む', exact: true }).click();
  await page.getByRole('button', { name: locale === 'en' ? 'Source' : 'ソース', exact: true }).click();
  await page.getByRole('button', { name: locale === 'en' ? 'Load fixed sources' : '固定ソースを読み込む', exact: true }).click();
  await expect(page.getByTestId('ide-scoped-region')).toHaveCount(regions);
}
for (const locale of ['en','ja'] as const) for (const family of ['root','true','false','unknown']) {
  test('reads separate original diagnostic budget and cancelled recordings by keyboard in '+locale+' '+family,async({page})=>{
    const bundle=JSON.parse(await readFile('frontend/public/assets/projectide/compiler-recordings-v1.json','utf8'));
    const writes:string[]=[];page.on('request',r=>{if(['POST','PUT','PATCH','DELETE'].includes(r.method()))writes.push(r.method());});
    await openExample(page,locale);
    for(const revision of ['base','hypothesis','draft']){
      const value=bundle.cases.find((v:{key:string})=>v.key==='tests-publication-'+family+'-'+revision);
      await readSources(page,locale,value.key,family==='root'?1:2);
      for(const [label,en,ja] of [['original','View original recording','元の記録を見る'],['diagnostic','View recorded diagnostic rerun','記録済みの診断用再実行を見る'],['budget','View recorded byte budget gap','記録済みの本文予算超過を見る'],['cancelled','View recorded cancellation','記録済みのキャンセルを見る']]){
        const button=page.getByRole('button',{name:locale==='en'?en:ja,exact:true});await button.focus();await button.press(label==='diagnostic'?'Space':'Enter');
        const panel=page.getByTestId('ide-compiler-recording');await expect(panel).toBeFocused();const frame=value.compilerRecordings[label];const manifest=JSON.parse(frame.manifest.text);const receipt=JSON.parse(frame.receipt.text);
        await expect(panel.getByTestId('ide-compiler-recording-run')).toHaveText(manifest.runId);await expect(panel).toContainText(receipt.attemptId);await expect(panel).toContainText(manifest.sessionId);await expect(panel).toContainText(value.sourceDigest.value);await expect(panel).toContainText(value.inputDigest.value);
        await expect(panel.getByTestId('ide-compiler-recording-body')).toHaveCount(label==='diagnostic'?3:0);
        if(label==='diagnostic'){
          const first=panel.getByTestId('ide-compiler-recording-body').first();await first.locator('summary').focus();await first.locator('summary').press('Space');await expect(first.locator('pre')).toBeVisible();expect(await first.locator('pre').textContent()).toBe(frame.snapshot.bodies[0].text);await expect(first).toContainText(frame.snapshot.bodies[0].digest.value);
          expect(manifest.runId).not.toBe(JSON.parse(value.compilerRecordings.original.manifest.text).runId);expect(receipt.attemptId).not.toBe(JSON.parse(value.compilerRecordings.original.receipt.text).attemptId);
        }
        if(label==='cancelled'){await expect(panel.getByTestId('ide-compiler-recording-no-result')).toContainText(locale==='en'?'cancelled before a Compiler result':'Compiler結果の生成前にキャンセル');await expect(panel.getByTestId('ide-compiler-recording-state')).toHaveCount(0);}
        if(label==='budget')await expect(panel.getByTestId('ide-compiler-recording-state')).toContainText(locale==='en'?'exceed':'超えています');
        await expect(page.getByTestId('ide-analysis-status')).toContainText(value.result.verdict);await expectNoAccessibilityViolations(page,'app-projectide-workbench');
      }
      await page.getByRole('button',{name:locale==='en'?'Load fixed sources':'固定ソースを読み込む',exact:true}).click();await expect(page.getByTestId('ide-compiler-recording')).toHaveCount(0);
    }
    expect(writes).toEqual([]);
  });
}
for(const locale of ['en','ja'] as const) for(const mode of ['origin','materializations','cancel-result','body','duplicate-run','session','foreign-record','trust']){
  test('refuses altered recorded invocation '+mode+' in '+locale,async({page})=>{
    const bundle=JSON.parse(await readFile('frontend/public/assets/projectide/compiler-recordings-v1.json','utf8'));const value=bundle.cases.find((v:{key:string})=>v.key==='tests-publication-true-base');const p=value.compilerRecordings;const f=p.diagnostic;
    if(mode==='cancel-result')p.cancelled.host.result=value.result;
    else if(mode==='body')f.snapshot.bodies[0].text+=' ';
    else if(mode==='foreign-record')p.diagnostic=bundle.cases.find((v:{key:string})=>v.key==='tests-publication-true-hypothesis').compilerRecordings.diagnostic;
    else{const m=JSON.parse(f.manifest.text);if(mode==='origin')m.origin.attemptId='00000099-0000-4000-8000-000000000001';if(mode==='materializations')m.materializationsDigest.value='b'.repeat(64);if(mode==='duplicate-run')m.runId=JSON.parse(p.original.manifest.text).runId;if(mode==='session')m.sessionId='00000099-0000-4000-8000-000000000001';if(mode==='trust')m.authority='Allowed';f.manifest.text=JSON.stringify(m);f.manifest.digest=hash(m.format,f.manifest.text);const r=JSON.parse(f.receipt.text);r.manifestDigest=f.manifest.digest;f.receipt.text=JSON.stringify(r);f.receipt.digest=hash(r.format,f.receipt.text);}
    await page.route('**/assets/projectide/compiler-recordings-v1.json',r=>r.fulfill({status:200,contentType:'application/json',body:JSON.stringify(bundle)}));await openExample(page,locale);await readSources(page,locale,value.key,2);
    await page.getByRole('button',{name:locale==='en'?'View recorded diagnostic rerun':'記録済みの診断用再実行を見る',exact:true}).click();await expect(page.getByRole('alert')).toContainText(locale==='en'?'This recording could not be verified':'記録を検証できませんでした');await expect(page.getByTestId('ide-compiler-recording')).toHaveCount(0);await expect(page.getByTestId('ide-scoped-region')).toHaveCount(2);await expectNoAccessibilityViolations(page,'app-projectide-workbench');
  });
}
