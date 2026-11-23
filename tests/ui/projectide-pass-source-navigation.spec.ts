import {readFileSync} from 'node:fs';
import {expect,test,type Page} from '@playwright/test';
import {expectNoAccessibilityViolations} from './a11y';
const bundle=JSON.parse(readFileSync('frontend/public/assets/projectide/pass-recordings-v1.json','utf8'));
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
 test('navigates only from explicitly read original pass map targets: '+entry.key+' '+locale,async({page})=>{
  const writes:string[]=[];page.on('request',request=>{if(['POST','PUT','PATCH','DELETE'].includes(request.method()))writes.push(request.method());});
  await open(page,locale,entry.key);await button(page,locale,'Read captured diagnostic run','記録済みの診断実行を読む');
  await select(page,5);await expect(page.getByTestId('ide-pass-source-read')).toHaveCount(0);
  await button(page,locale,'Read this boundary’s original JSON','この境界の原本JSONを読む');await expect(page.getByTestId('ide-pass-boundary')).toBeFocused();
  if(entry.key==='test-assertion-navigation'){
   await expect(page.getByTestId('ide-pass-source-read')).toHaveCount(0);await expect(page.getByTestId('ide-pass-source-page')).toHaveCount(0);
   await expect(page.getByTestId('ide-pass-boundary')).toContainText('No original IR was produced.');
  }else{
   await expect(page.getByTestId('ide-pass-source-page')).toHaveCount(0);
   const original=entry.passRecordings.diagnostic.snapshots.available.bodies.find((body:{domain:string})=>body.domain==='coglatas.semantic-source-map/1');
   const map=JSON.parse(original.text),targets=[...map.nodes,...map.relations];
   await button(page,locale,'Read mapped Source targets','対応するSourceの移動先を読む');await expect(page.getByTestId('ide-pass-boundary')).toBeFocused();
   const panel=page.getByTestId('ide-pass-source-page');await expect(panel).toContainText(original.digest.value);
   await expect(panel.getByTestId('ide-pass-source-target')).toHaveCount(targets.length);
   for(const index of [0,...(map.relations.length?[map.nodes.length]:[])]){
    const target=targets[index],location=target.location,control=panel.getByTestId('ide-pass-source-target').nth(index);
    await expect(control).toContainText(target.irNodeId??target.relationId);await control.focus();await control.press('Enter');
    const source=page.getByTestId('ide-source-target');await expect(source).toBeFocused();
    const record=[...entry.declarations,...entry.relations].find((record:{location:{entityId?:string;relationId?:string}})=>location.entityId?record.location.entityId===location.entityId:record.location.relationId===location.relationId);
    await expect(source.locator('pre')).toHaveText(JSON.stringify(record.data,null,2));await expect(page.getByTestId('ide-source-location')).toContainText('/');
   }
   await expectNoAccessibilityViolations(page,'app-projectide-workbench');
   expect(await page.evaluate(()=>document.documentElement.scrollWidth<=window.innerWidth)).toBe(true);
   await button(page,locale,'Read this boundary’s original JSON','この境界の原本JSONを読む');
   await expect(page.getByTestId('ide-pass-source-page')).toHaveCount(0);await expect(page.getByTestId('ide-source-target')).toHaveCount(0);
   await button(page,locale,'Read mapped Source targets','対応するSourceの移動先を読む');
   await button(page,locale,'View this boundary with a one-byte limit','この境界を1バイト上限で表示');
   await expect(page.getByTestId('ide-pass-source-read')).toHaveCount(0);await expect(page.getByTestId('ide-pass-source-page')).toHaveCount(0);
  }
  await expect(page.getByTestId('ide-analysis-status')).toContainText(entry.result.verdict);
  if(entry.key.startsWith('operator-requirement-'))expect(entry.result.evidenceComplete).toBe(false);
  await expectNoAccessibilityViolations(page,'app-projectide-workbench');expect(writes).toEqual([]);
  await button(page,locale,'Read fixed root Source','固定されたルートソースを読む');await expect(page.getByTestId('ide-pass-source-page')).toHaveCount(0);
  await expect(page.getByTestId('ide-source-target')).toHaveCount(0);
 });
}
