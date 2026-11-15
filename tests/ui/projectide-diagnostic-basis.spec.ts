import { readFile } from 'node:fs/promises';
import { expect, test } from '@playwright/test';
import { expectNoAccessibilityViolations } from './a11y';

for (const locale of ['en','ja'] as const) for (const family of ['false','unknown']) {
  test('reads actual diagnostic basis and missing evidence by keyboard in '+locale+' '+family, async ({page}) => {
    const bundle=JSON.parse(await readFile('frontend/public/assets/projectide/compiler-outputs-v1.json','utf8'));
    await page.addInitScript(locale => { localStorage.setItem('coglatas.locale',locale); window.__COGLATAS_FEATURE_FLAGS__={'projectide.syntheticPreview':true}; },locale);
    await page.goto('/app/projectide/preview');
    await page.getByRole('button',{name:locale==='en'?'Open Compiler output example':'Compiler出力の例を開く',exact:true}).click();
    for (const revision of ['base','hypothesis','draft']) {
      const value=bundle.cases.find((v:{key:string})=>v.key==='tests-publication-'+family+'-'+revision);
      await page.getByTestId('ide-context').selectOption(value.key);
      await page.getByRole('button',{name:locale==='en'?'Load result fixture':'結果Fixtureを読み込む',exact:true}).click();
      await page.getByRole('button',{name:locale==='en'?'Problems':'問題',exact:true}).click();
      const diagnostic=value.result.diagnostics[0];
      const select=page.getByRole('button',{name:(locale==='en'?'Inspect diagnostic':'診断を調べる')+': '+diagnostic.code,exact:true});
      await select.focus(); await select.press('Enter');
      const panel=page.getByTestId('ide-diagnostic-basis'); await expect(panel).toBeVisible();
      await expect(panel).toContainText(diagnostic.diagnosticId); await expect(panel).toContainText(value.inputDigest.value);
      await expect(panel).toContainText(value.resultDigest.value); await expect(panel).toContainText(diagnostic.evidence.kind);
      await expect(panel.getByTestId('ide-diagnostic-basis-premise')).toHaveText(diagnostic.evidence.premises.map((p:{algorithm:string,value:string})=>p.algorithm+':'+p.value));
      await expect(panel.getByTestId('ide-diagnostic-basis-missing')).toHaveText(diagnostic.evidence.missingReason??(locale==='en'?'No missing reason was recorded.':'欠落理由は記録されていません。'));
      await expect(panel).toContainText(locale==='en'?'AND/OR, observed-order or causal relationships are not resolved':'AND/OR、観測順序、因果関係は解決されません');
      await expect(panel.getByRole('button',{name:locale==='en'?'Previous premise references':'前の根拠参照',exact:true})).toBeDisabled();
      await expect(panel.getByRole('button',{name:locale==='en'?'Next premise references':'次の根拠参照',exact:true})).toBeDisabled();
      const locationSummary=panel.locator('summary'); await locationSummary.focus(); await locationSummary.press('Space');
      const location=panel.getByTestId('ide-diagnostic-basis-location'); await expect(location).toBeVisible();
      expect(JSON.parse(await location.textContent()??'null')).toEqual(diagnostic.primaryLocation);
      await expectNoAccessibilityViolations(page,'app-projectide-workbench');
      await page.getByTestId('ide-context').selectOption(value.key.replace(revision,revision==='base'?'hypothesis':'base'));
      await expect(panel).toHaveCount(0);
    }
  });
}
