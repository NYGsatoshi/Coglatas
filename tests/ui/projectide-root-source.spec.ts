import { createHash } from 'node:crypto';
import { readFileSync } from 'node:fs';
import { expect,test,type Page } from '@playwright/test';
import { expectNoAccessibilityViolations } from './a11y';
const hash=(domain:string,text:string)=>({algorithm:'sha-256',value:createHash('sha256').update(domain).update(new Uint8Array([0])).update(text).digest('hex')});
const bundle=JSON.parse(readFileSync('frontend/public/assets/projectide/root-diagnostic-premises-v1.json','utf8'));
async function openRoot(page:Page,locale:'en'|'ja',key:string){
  await page.addInitScript(locale=>{localStorage.setItem('coglatas.locale',locale);window.__COGLATAS_FEATURE_FLAGS__={'projectide.syntheticPreview':true};},locale);
  await page.goto('/app/projectide/preview');
  await page.getByRole('button',{name:locale==='en'?'Open root premise example':'ルートの根拠の例を開く',exact:true}).click();
  await page.getByTestId('ide-context').selectOption(key);await page.getByRole('button',{name:locale==='en'?'Load result fixture':'結果Fixtureを読み込む',exact:true}).click();
  await page.getByRole('button',{name:locale==='en'?'Source':'ソース',exact:true}).click();
  const load=page.getByRole('button',{name:locale==='en'?'Read fixed root Source':'固定されたルートソースを読む',exact:true});await load.focus();await load.press('Enter');
}
async function select(page:Page,locale:'en'|'ja',diagnosticId:string){
  await page.getByRole('button',{name:locale==='en'?'Problems':'問題',exact:true}).click();
  const value=bundle.cases.find((v:{result:{diagnostics:{diagnosticId:string}[]}})=>v.result.diagnostics.some(d=>d.diagnosticId===diagnosticId));
  const index=value.result.diagnostics.findIndex((d:{diagnosticId:string})=>d.diagnosticId===diagnosticId);const code=value.result.diagnostics[index].code;
  const occurrence=value.result.diagnostics.slice(0,index).filter((d:{code:string})=>d.code===code).length;
  const button=page.getByRole('button',{name:(locale==='en'?'Inspect diagnostic':'診断を調べる')+': '+code,exact:true}).nth(occurrence);await button.focus();await button.press('Enter');
  await expect(page.getByTestId('ide-diagnostic-basis')).toContainText(diagnosticId);
}
for(const locale of ['en','ja'] as const)for(const value of bundle.cases)for(const [index,diagnostic] of value.result.diagnostics.entries()){
  test('reads exact root premise '+value.key+' diagnostic '+index+' in '+locale,async({page})=>{
    const writes:string[]=[];page.on('request',r=>{if(['POST','PUT','PATCH','DELETE'].includes(r.method()))writes.push(r.method());});
    await openRoot(page,locale,value.key);const source=page.getByTestId('ide-root-source');await expect(source).toBeFocused();
    const sourceBody=source.getByTestId('ide-root-source-body');await sourceBody.locator('summary').focus();await sourceBody.locator('summary').press('Space');
    await expect(sourceBody.locator('pre')).toHaveText(value.sourceText);expect(hash('coglatas.project-source/1',await sourceBody.locator('pre').textContent()??'')).toEqual(value.sourceDigest);
    await select(page,locale,diagnostic.diagnosticId);
    for(const[choice,en,ja]of[['manifest','Read premise manifest','根拠のマニフェストを読む'],['available','Read captured premise bodies','捕捉済みの根拠本文を読む'],['budget','Read budget-limited snapshot','本文予算を制限したスナップショットを読む']]){
      const button=page.getByRole('button',{name:locale==='en'?en:ja,exact:true});await button.focus();await button.press('Enter');
      const panel=page.getByTestId('ide-diagnostic-premises');await expect(panel).toBeFocused();const meta=JSON.parse(value.diagnosticPremises.diagnostics[index][choice].text);
      expect(meta.bodyScope).toBe('RootCompilerInputObjects');await expect(panel).toContainText(locale==='en'?'Only root input objects were searched':'ルート入力のデータだけを検索しました');
      await expect(panel.getByTestId('ide-diagnostic-premise-reference')).toHaveCount(meta.references.length);
      const captured=meta.references.filter((r:{bodyCaptured:boolean})=>r.bodyCaptured);const bodies=panel.getByTestId('ide-diagnostic-premise-body');await expect(bodies).toHaveCount(captured.length);
      for(let i=0;i<captured.length;i++){
        const details=bodies.nth(i);await details.locator('summary').focus();await details.locator('summary').press('Space');const pre=details.locator('pre');await expect(pre).toBeVisible();
        const text=await pre.textContent()??'';const reference=captured[i];expect(hash(reference.kind==='RootSource'?'coglatas.project-source/1':'coglatas.compiler-input/1',text)).toEqual(reference.digest);
        expect(text).toBe(reference.kind==='RootSource'?value.sourceText:value.rootSource.inputText);
      }
      if(diagnostic.evidence.kind==='coglatas.missing')await expect(page.getByTestId('ide-diagnostic-basis-missing')).toHaveText(diagnostic.evidence.missingReason);
      await expect(page.getByTestId('ide-analysis-status')).toContainText(value.result.verdict);await expectNoAccessibilityViolations(page,'app-projectide-workbench');
    }
    await page.getByRole('button',{name:locale==='en'?'Source':'ソース',exact:true}).click();await page.getByRole('button',{name:locale==='en'?'Read fixed root Source':'固定されたルートソースを読む',exact:true}).click();await expect(page.getByTestId('ide-diagnostic-premises')).toHaveCount(0);
    expect(writes).toEqual([]);
  });
}
for(const locale of ['en','ja'] as const)for(const tamper of ['root-result','premise-scope','premise-trust']){
  test('rejects altered '+tamper+' root disclosure in '+locale,async({page})=>{
    const altered=structuredClone(bundle);const value=altered.cases.find((v:{key:string})=>v.key==='fail');
    if(tamper==='root-result')value.rootSource.result=altered.cases.find((v:{key:string})=>v.key==='pass').rootSource.result;
    else{const frame=value.diagnosticPremises.diagnostics[0].available;const data=JSON.parse(frame.text);if(tamper==='premise-scope')data.bodyScope='RetainedCompilerInputObjects';else data.producerTrust='Trusted';frame.text=JSON.stringify(data);frame.digest=hash('coglatas.local-diagnostic-premise-snapshot/1',frame.text);}
    await page.route('**/assets/projectide/root-diagnostic-premises-v1.json',r=>r.fulfill({status:200,contentType:'application/json',body:JSON.stringify(altered)}));await openRoot(page,locale,'fail');
    if(tamper==='root-result'){
      await expect(page.getByRole('alert')).toContainText(locale==='en'?'The root Source could not be verified':'ルートソースを検証できませんでした');await expect(page.getByTestId('ide-root-source-body')).toHaveCount(0);
    }else{
      await select(page,locale,value.result.diagnostics[0].diagnosticId);await page.getByRole('button',{name:locale==='en'?'Read premise manifest':'根拠のマニフェストを読む',exact:true}).click();
      await expect(page.getByRole('alert')).toContainText(locale==='en'?'Captured premises could not be verified':'捕捉済みの根拠を検証できませんでした');await expect(page.getByTestId('ide-diagnostic-premises')).toHaveCount(0);
    }
    await expect(page.getByTestId('ide-analysis-status')).toContainText(value.result.verdict);await expectNoAccessibilityViolations(page,'app-projectide-workbench');
  });
}

for(const locale of ['en','ja'] as const){
  test('reads root Source without inventing a diagnostic for pass in '+locale,async({page})=>{
    const value=bundle.cases.find((v:{key:string})=>v.key==='pass');await openRoot(page,locale,'pass');
    const source=page.getByTestId('ide-root-source');await expect(source).toBeFocused();
    await source.getByTestId('ide-root-source-body').locator('summary').click();const text=await source.getByTestId('ide-root-source-body').locator('pre').textContent()??'';
    expect(text).toBe(value.sourceText);expect(hash('coglatas.project-source/1',text)).toEqual(value.sourceDigest);
    await expect(page.getByTestId('ide-diagnostic-premises')).toHaveCount(0);await expect(page.getByTestId('ide-analysis-status')).toContainText(value.result.verdict);
    await expectNoAccessibilityViolations(page,'app-projectide-workbench');
  });
}
