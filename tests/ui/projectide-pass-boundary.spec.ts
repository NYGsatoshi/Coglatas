import {createHash} from 'node:crypto';
import {readFileSync} from 'node:fs';
import {expect,test,type Page} from '@playwright/test';
import {expectNoAccessibilityViolations} from './a11y';
const bundle=JSON.parse(readFileSync('frontend/public/assets/projectide/pass-recordings-v1.json','utf8'));
const hash=(domain:string,text:string)=>({algorithm:'sha-256',value:createHash('sha256').update(domain+String.fromCharCode(0)+text).digest('hex')});
async function button(page:Page,locale:'en'|'ja',en:string,ja:string){
 const control=page.getByRole('button',{name:locale==='en'?en:ja,exact:true});await control.focus();await control.press('Enter');
}
async function open(page:Page,locale:'en'|'ja',key:string){
 await page.addInitScript(locale=>{localStorage.setItem('coglatas.locale',locale);window.__COGLATAS_FEATURE_FLAGS__={'projectide.syntheticPreview':true};},locale);
 await page.goto('/app/projectide/preview');await button(page,locale,'Open recorded pass example','パス記録の例を開く');
 await expect(page.getByTestId('ide-context').locator('option')).toHaveCount(bundle.cases.length+1);
 await page.getByTestId('ide-context').selectOption(key);await expect(page.getByTestId('ide-context')).toHaveValue(key);
 await button(page,locale,'Load result fixture','結果Fixtureを読み込む');await button(page,locale,'Source','ソース');
 await expect(page.getByTestId('ide-pass-inspect-boundary')).toHaveCount(0);
 await button(page,locale,'Read fixed root Source','固定されたルートソースを読む');await expect(page.getByTestId('ide-root-source')).toBeFocused();
}
async function select(page:Page,ordinal:number){
 const control=page.locator('[data-testid="ide-pass-inspect-boundary"][data-pass-ordinal="'+ordinal+'"]');
 await control.focus();await control.press('Enter');await expect(page.getByTestId('ide-pass-boundary')).toBeFocused();
}
for(const locale of ['en','ja'] as const)for(const entry of bundle.cases){
 test('reads selected original pass boundaries without recovering capture gaps: '+entry.key+' '+locale,async({page})=>{
  const writes:string[]=[];page.on('request',request=>{if(['POST','PUT','PATCH','DELETE'].includes(request.method()))writes.push(request.method());});
  await open(page,locale,entry.key);
  await button(page,locale,'Read original Manifest run','元のManifest実行を読む');await select(page,0);
  await expect(page.getByTestId('ide-pass-boundary-body')).toHaveCount(0);
  await button(page,locale,'Read this boundary’s original JSON','この境界の原本JSONを読む');
  await expect(page.getByTestId('ide-pass-boundary-body')).toHaveCount(0);
  await button(page,locale,'Read captured diagnostic run','記録済みの診断実行を読む');
  await expect(page.getByTestId('ide-pass-boundary')).toHaveCount(0);await expect(page.getByTestId('ide-pass-inspect-boundary')).toHaveCount(10);
  const recording=entry.passRecordings.diagnostic,record=JSON.parse(recording.observation.text);
  for(const ordinal of [0,2,3,5,9]){
   await select(page,ordinal);const panel=page.getByTestId('ide-pass-boundary');const event=record.events[ordinal];
   await expect(panel.getByTestId('ide-pass-boundary-gap')).toHaveText(event.gap);
   await expect(panel.getByTestId('ide-pass-boundary-body')).toHaveCount(0);
   await button(page,locale,'Read this boundary’s original JSON','この境界の原本JSONを読む');await expect(panel).toBeFocused();
   const wanted=new Set(event.originalBodies.map((r:{domain:string;digest:{value:string}})=>r.domain+':'+r.digest.value));
   const supplied=recording.snapshots.available.bodies.filter((body:{key:string})=>wanted.has(body.key));
   const bodies=panel.getByTestId('ide-pass-boundary-body');await expect(bodies).toHaveCount(supplied.length);
   for(let i=0;i<supplied.length;i++){
    const details=bodies.nth(i);await details.locator('summary').focus();await details.locator('summary').press('Space');
    const text=await details.locator('pre').textContent();expect(text).toBe(supplied[i].text);expect(hash(supplied[i].domain,text??'')).toEqual(supplied[i].digest);
   }
   if(ordinal===0){expect(supplied).toHaveLength(1);expect(supplied[0].text).toBe(entry.sourceText);}
   if(ordinal===3)expect(supplied).toHaveLength(0);
   if(ordinal===5&&entry.key==='test-assertion-navigation'){expect(supplied).toHaveLength(0);await expect(panel).toContainText('No original IR was produced.');}
   await expect(page.getByTestId('ide-analysis-status')).toContainText(entry.result.verdict);
  }
  await select(page,0);await button(page,locale,'View this boundary with a one-byte limit','この境界を1バイト上限で表示');
  await expect(page.getByTestId('ide-pass-boundary-body')).toHaveCount(0);
  await expect(page.getByTestId('ide-pass-boundary')).toContainText(locale==='en'?'exceed the selected viewing limit':'表示上限を超え');
  await button(page,locale,'Read capture-limited run','記録上限に達した実行を読む');await select(page,0);
  await button(page,locale,'Read this boundary’s original JSON','この境界の原本JSONを読む');await expect(page.getByTestId('ide-pass-boundary-body')).toHaveCount(0);
  await button(page,locale,'Read event-limited run','イベント上限ありの実行を読む');
  await expect(page.getByTestId('ide-pass-inspect-boundary')).toHaveCount(0);await expect(page.getByTestId('ide-pass-boundary')).toHaveCount(0);
  await button(page,locale,'Read cancelled attempt','キャンセルされた試行を読む');
  await expect(page.getByTestId('ide-pass-inspect-boundary')).toHaveCount(0);await expect(page.getByTestId('ide-pass-boundary')).toHaveCount(0);
  await button(page,locale,'Read run after cancellation','キャンセル後の実行を読む');await select(page,0);
  await button(page,locale,'Read this boundary’s original JSON','この境界の原本JSONを読む');await expect(page.getByTestId('ide-pass-boundary-body')).toHaveCount(1);
  if(entry.key.startsWith('operator-requirement-'))expect(entry.result.evidenceComplete).toBe(false);
  await expectNoAccessibilityViolations(page,'app-projectide-workbench');
  expect(await page.evaluate(()=>document.documentElement.scrollWidth<=window.innerWidth)).toBe(true);expect(writes).toEqual([]);
  await button(page,locale,'Read fixed root Source','固定されたルートソースを読む');
  await expect(page.getByTestId('ide-pass-boundary')).toHaveCount(0);await expect(page.getByTestId('ide-pass-boundary-body')).toHaveCount(0);
 });
}
