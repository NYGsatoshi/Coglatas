import { createHash } from 'node:crypto';
import { readFile } from 'node:fs/promises';
import { expect, test, type Page } from '@playwright/test';
import { expectNoAccessibilityViolations } from './a11y';
const hash=(domain:string,text:string)=>({algorithm:'sha-256',value:createHash('sha256').update(domain).update(new Uint8Array([0])).update(text).digest('hex')});
async function openExample(page:Page,locale:'en'|'ja'){
  await page.addInitScript(locale=>{localStorage.setItem('coglatas.locale',locale);window.__COGLATAS_FEATURE_FLAGS__={'projectide.syntheticPreview':true};},locale);
  await page.goto('/app/projectide/preview');
  await page.getByRole('button',{name:locale==='en'?'Open captured premise example':'捕捉済みの根拠の例を開く',exact:true}).click();
}
async function selectDiagnostic(page:Page,locale:'en'|'ja',key:string,code:string){
  await page.getByTestId('ide-context').selectOption(key);
  await page.getByRole('button',{name:locale==='en'?'Load result fixture':'結果Fixtureを読み込む',exact:true}).click();
  await page.getByRole('button',{name:locale==='en'?'Source':'ソース',exact:true}).click();
  await page.getByRole('button',{name:locale==='en'?'Load fixed sources':'固定ソースを読み込む',exact:true}).click();
  await page.getByRole('button',{name:locale==='en'?'Problems':'問題',exact:true}).click();
  const button=page.getByRole('button',{name:(locale==='en'?'Inspect diagnostic':'診断を調べる')+': '+code,exact:true});await button.focus();await button.press('Enter');
}
for(const locale of ['en','ja'] as const)for(const family of ['false','unknown']){
  test('reads original captured premise bytes and gaps by keyboard in '+locale+' '+family,async({page})=>{
    const bundle=JSON.parse(await readFile('frontend/public/assets/projectide/diagnostic-premises-v1.json','utf8'));const writes:string[]=[];
    page.on('request',r=>{if(['POST','PUT','PATCH','DELETE'].includes(r.method()))writes.push(r.method());});await openExample(page,locale);
    for(const revision of ['base','hypothesis','draft']){
      const value=bundle.cases.find((c:{key:string})=>c.key==='tests-publication-'+family+'-'+revision);const diagnostic=value.result.diagnostics[0];
      await selectDiagnostic(page,locale,value.key,diagnostic.code);
      for(const [choice,en,ja] of [['manifest','Read premise manifest','根拠のマニフェストを読む'],['available','Read captured premise bodies','捕捉済みの根拠本文を読む'],['budget','Read budget-limited snapshot','本文予算を制限したスナップショットを読む']]){
        const button=page.getByRole('button',{name:locale==='en'?en:ja,exact:true});await button.focus();await button.press(choice==='available'?'Space':'Enter');
        const panel=page.getByTestId('ide-diagnostic-premises');await expect(panel).toBeFocused();const meta=JSON.parse(value.diagnosticPremises.diagnostics[0][choice].text);
        await expect(panel.getByTestId('ide-diagnostic-premise-reference')).toHaveCount(meta.references.length);
        const captured=meta.references.filter((r:{bodyCaptured:boolean})=>r.bodyCaptured);const bodies=panel.getByTestId('ide-diagnostic-premise-body');await expect(bodies).toHaveCount(captured.length);
        for(let i=0;i<captured.length;i++){
          const details=bodies.nth(i);await details.locator('summary').focus();await details.locator('summary').press('Space');const pre=details.locator('pre');await expect(pre).toBeVisible();
          const text=await pre.textContent()??'';const reference=captured[i];const domain=reference.kind==='RootInput'?'coglatas.compiler-input/1':reference.kind==='BindingRegion'?'coglatas.semantic-binding-region/1':'coglatas.compiler-dependency-region/1';
          expect(hash(domain,text)).toEqual(reference.digest);if(reference.kind==='RootInput')expect(text).toBe(value.scopedSources.inputText);
        }
        if(choice==='budget'&&family==='false')await expect(panel.getByTestId('ide-diagnostic-premises-state')).toContainText(locale==='en'?'No partial body set was retained':'一部の本文だけを保持することはありません');
        if(family==='unknown'){await expect(page.getByTestId('ide-diagnostic-basis-missing')).toHaveText(diagnostic.evidence.missingReason);await expect(panel.getByTestId('ide-diagnostic-premises-state')).toContainText(locale==='en'?(choice==='manifest'?'Manifest only':'No premise references'):(choice==='manifest'?'マニフェストのみ':'根拠参照が記録されていません'));}
        await expect(page.getByTestId('ide-analysis-status')).toContainText(value.result.verdict);await expectNoAccessibilityViolations(page,'app-projectide-workbench');
      }
      await page.getByRole('button',{name:locale==='en'?'Source':'ソース',exact:true}).click();await page.getByRole('button',{name:locale==='en'?'Load fixed sources':'固定ソースを読み込む',exact:true}).click();await expect(page.getByTestId('ide-diagnostic-premises')).toHaveCount(0);
    }
    expect(writes).toEqual([]);
  });
}
for(const locale of ['en','ja'] as const)for(const tamper of ['trust','diagnostic','foreign']){
  test('refuses altered native premise '+tamper+' in '+locale,async({page})=>{
    const bundle=JSON.parse(await readFile('frontend/public/assets/projectide/diagnostic-premises-v1.json','utf8'));const value=bundle.cases.find((c:{key:string})=>c.key==='tests-publication-false-base');const entry=value.diagnosticPremises.diagnostics[0];
    if(tamper==='foreign')entry.available=bundle.cases.find((c:{key:string})=>c.key==='tests-publication-false-hypothesis').diagnosticPremises.diagnostics[0].available;
    else{const data=JSON.parse(entry.available.text);if(tamper==='trust')data.producerTrust='Trusted';else data.basis.diagnosticId='00000099-0000-4000-8000-000000000001';entry.available.text=JSON.stringify(data);entry.available.digest=hash('coglatas.local-diagnostic-premise-snapshot/1',entry.available.text);}
    await page.route('**/assets/projectide/diagnostic-premises-v1.json',r=>r.fulfill({status:200,contentType:'application/json',body:JSON.stringify(bundle)}));await openExample(page,locale);
    await selectDiagnostic(page,locale,value.key,value.result.diagnostics[0].code);await page.getByRole('button',{name:locale==='en'?'Read premise manifest':'根拠のマニフェストを読む',exact:true}).click();
    await expect(page.getByRole('alert')).toContainText(locale==='en'?'Captured premises could not be verified':'捕捉済みの根拠を検証できませんでした');await expect(page.getByTestId('ide-diagnostic-premises')).toHaveCount(0);
    await expect(page.getByTestId('ide-analysis-status')).toContainText(value.result.verdict);await expectNoAccessibilityViolations(page,'app-projectide-workbench');
  });
}
