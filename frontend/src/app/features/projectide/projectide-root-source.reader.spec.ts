/// <reference types="node" />
import { createHash, webcrypto } from 'node:crypto';
import { readFileSync } from 'node:fs';
import { vi } from 'vitest';
import { readPreviewBundle } from './projectide-preview.adapter';
import { readRootSource, rootSourceMatches } from './projectide-root-source.reader';
import { readDiagnosticPremises } from './projectide-diagnostic-premises.reader';

vi.stubGlobal('crypto',webcrypto);
const load=()=>JSON.parse(readFileSync('public/assets/projectide/root-diagnostic-premises-v1.json','utf8'));
const hash=(domain:string,text:string)=>({algorithm:'sha-256',value:createHash('sha256').update(domain).update(new Uint8Array([0])).update(text).digest('hex')});
async function ready(key='fail'){
  const value=readPreviewBundle(load()).cases.find(c=>c.key===key)!;
  const root=(await readRootSource(value))!;expect(root).not.toBeNull();return{value,root};
}
describe('Explicit root Source and original diagnostic premise admission',()=>{
  it.each(['fail','pass','unknown','test-assertion-navigation'])('replays native root input/result/source and every metadata mode: %s',async key=>{
    const {value,root}=await ready(key);const before=JSON.stringify(value.result);
    expect(rootSourceMatches(value,root)).toBe(true);expect(value.scopedSources).toBeUndefined();
    expect(root.sourceText).toBe(value.sourceText);expect(hash('coglatas.project-source/1',root.sourceText)).toEqual(value.sourceDigest);
    expect(Object.isFrozen(root.source)).toBe(true);expect(Object.isFrozen(root.projection.result)).toBe(true);
    for(const diagnostic of value.result.diagnostics)for(const choice of ['manifest','available','budget'] as const){
      const snapshot=(await readDiagnosticPremises(value,root,diagnostic,choice))!;
      expect(snapshot.data['bodyScope']).toBe('RootCompilerInputObjects');expect(snapshot.data['authority']).toBe('NotEvaluated');
      expect(snapshot.data['earlierStates']).toBe('NotRecorded');expect(snapshot.data['relationshipStructure']).toBe('NotRecorded');
      expect(snapshot.data).toEqual(JSON.parse(value.diagnosticPremises!.diagnostics.find(d=>d.diagnosticId===diagnostic.diagnosticId)![choice].text));
      if(diagnostic.evidence.kind==='coglatas.missing'){
        expect(snapshot.state).toBe(choice==='manifest'?'ManifestOnly':'NoRecordedPremises');expect(snapshot.bodies).toHaveLength(0);expect(snapshot.references).toHaveLength(0);
      }else{
        expect(snapshot.state).toBe(choice==='manifest'?'ManifestOnly':choice==='budget'?'BodyBudgetExceeded':'CapturedKnownBodies');
        expect(snapshot.bodies).toHaveLength(choice==='available'?2:0);
        if(choice==='available'){
          const source=snapshot.bodies.find(b=>b.kind==='RootSource')!;expect(source.text).toBe(value.sourceText);expect(source.digest).toEqual(value.sourceDigest);
          expect(hash('coglatas.project-source/1',source.text)).toEqual(source.digest);
          expect(snapshot.references.filter(r=>r.resolution==='Unavailable')).toHaveLength(diagnostic.evidence.premises.length-2);
        }
      }
    }
    expect(JSON.stringify(value.result)).toBe(before);
  });
  it('refuses a copied admission token and copied or foreign diagnostic even with matching identity',async()=>{
    const a=await ready(),b=await ready();
    expect(rootSourceMatches(a.value,{...a.root})).toBe(false);
    await expect(readDiagnosticPremises(a.value,{...a.root},a.value.result.diagnostics[0])).rejects.toThrow();
    await expect(readDiagnosticPremises(a.value,a.root,b.value.result.diagnostics[0])).rejects.toThrow();
    await expect(readDiagnosticPremises(a.value,a.root,{...a.value.result.diagnostics[0]})).rejects.toThrow();
  });
  it.each(['format','input-byte','input-identity','result-byte','result-identity','source-byte','oversized'])('refuses altered original root framing: %s',async mutation=>{
    const value=readPreviewBundle(load()).cases[0];const p=value.rootSource! as unknown as Record<string,unknown>;
    if(mutation==='format')p['format']='coglatas.local-root-source-projection/99';
    else if(mutation==='input-byte')p['inputText']=p['inputText']+' ';
    else if(mutation==='input-identity')p['inputText']=load().cases[1].rootSource.inputText;
    else if(mutation==='result-byte')(p['result'] as {text:string}).text+=' ';
    else if(mutation==='result-identity')p['result']=load().cases[1].rootSource.result;
    else if(mutation==='source-byte')(value as unknown as {sourceText:string}).sourceText+=' ';
    else p['inputText']=' '.repeat(4_194_305);
    await expect(readRootSource(value)).rejects.toThrow();
  });
  it.each(['scope','trust','relationship','earlier','retention','budget','diagnostic'])('rejects rehashed changed available metadata even for a manifest read: %s',async mutation=>{
    const value=readPreviewBundle(load()).cases[0];const frame=value.diagnosticPremises!.diagnostics[0].available as {text:string;digest:ReturnType<typeof hash>};const data=JSON.parse(frame.text);
    if(mutation==='scope')data.bodyScope='RetainedCompilerInputObjects';else if(mutation==='trust')data.producerTrust='Trusted';
    else if(mutation==='relationship')data.relationshipStructure='AND';else if(mutation==='earlier')data.earlierStates='Recorded';
    else if(mutation==='retention')data.retentionPolicy='Adopted';else if(mutation==='budget')data.maximumBodyBytes++;
    else data.basis.diagnosticId='00000099-0000-4000-8000-000000000001';
    frame.text=JSON.stringify(data);frame.digest=hash('coglatas.local-diagnostic-premise-snapshot/1',frame.text);
    const root=(await readRootSource(value))!;
    await expect(readDiagnosticPremises(value,root,value.result.diagnostics[0],'manifest')).rejects.toThrow();
  });
  it('withdraws admission when the fixed package changes after admission',async()=>{
    const {value,root}=await ready();(value.rootSource! as unknown as {inputText:string}).inputText+=' ';
    expect(rootSourceMatches(value,root)).toBe(false);await expect(readDiagnosticPremises(value,root,value.result.diagnostics[0])).rejects.toThrow();
  });
  it('rejects an input changed while its original digest is being checked',async()=>{
    const value=readPreviewBundle(load()).cases[0];const original=crypto.subtle.digest.bind(crypto.subtle);let release!:()=>void;
    const spy=vi.spyOn(crypto.subtle,'digest').mockImplementationOnce((algorithm,data)=>new Promise<ArrayBuffer>((resolve,reject)=>{release=()=>{void original(algorithm,data).then(resolve,reject);};}));
    try{const pending=readRootSource(value);await vi.waitFor(()=>expect(release).toBeTypeOf('function'));
      (value.rootSource! as unknown as {inputText:string}).inputText+=' ';release();await expect(pending).rejects.toThrow();
    }finally{spy.mockRestore();}
  });
});
