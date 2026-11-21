/// <reference types="node" />
import { createHash, webcrypto } from 'node:crypto';
import { readFileSync } from 'node:fs';
import { vi } from 'vitest';
import { readPreviewBundle } from './projectide-preview.adapter';
import { readScopedSources } from './projectide-scoped-sources';
import { readDiagnosticPremises } from './projectide-diagnostic-premises.reader';

vi.stubGlobal('crypto',webcrypto);
const load = () => JSON.parse(readFileSync('public/assets/projectide/diagnostic-premises-v1.json','utf8'));
const keys = load().cases.filter((v:{result:{diagnostics:unknown[]}})=>v.result.diagnostics.length).map((v:{key:string})=>v.key);
const hash = (domain:string,text:string) => ({algorithm:'sha-256',value:createHash('sha256').update(domain).update(new Uint8Array([0])).update(text).digest('hex')});
async function ready(key='tests-publication-false-base', mutate?: (raw:ReturnType<typeof load>)=>void) {
  const raw=load();mutate?.(raw);const bundle=readPreviewBundle(raw);const value=bundle.cases.find(c=>c.key===key)!;
  const sources=await readScopedSources(value);expect(sources).not.toBeNull();return {raw,bundle,value,sources:sources!,diagnostic:value.result.diagnostics[0]};
}
describe('Captured diagnostic premise reader',()=>{
  it.each(keys)('matches original native metadata, exact body bytes and gaps: %s',async key=>{
    const {value,sources,diagnostic}=await ready(key);const originalResult=JSON.stringify(value.result);
    for(const choice of ['manifest','available','budget'] as const){
      const actual=(await readDiagnosticPremises(value,sources,diagnostic,choice))!;
      const frame=value.diagnosticPremises!.diagnostics[0][choice];expect(actual.text).toBe(frame.text);expect(actual.digest).toEqual(frame.digest);
      expect(actual.data).toEqual(JSON.parse(frame.text));expect(actual.diagnostic).toBe(diagnostic);expect(actual.references.map(r=>r.digest)).toEqual(diagnostic.evidence.premises);
      expect(actual.data['relationshipStructure']).toBe('NotRecorded');expect(actual.data['authority']).toBe('NotEvaluated');
      expect(actual.data['retentionPolicy']).toBe('NotAdopted');expect(Object.isFrozen(actual.data)).toBe(true);
      if(diagnostic.evidence.kind==='coglatas.missing'){
        expect(actual.references).toHaveLength(0);expect(actual.bodies).toHaveLength(0);
        expect(actual.state).toBe(choice==='manifest'?'ManifestOnly':'NoRecordedPremises');
        expect((actual.data['basis'] as {evidence:{missingReason:string}}).evidence.missingReason).toBe(diagnostic.evidence.missingReason);
      }else{
        expect(actual.references).toHaveLength(3);expect(actual.bodies).toHaveLength(choice==='available'?3:0);
        expect(actual.state).toBe(choice==='manifest'?'ManifestOnly':choice==='budget'?'BodyBudgetExceeded':'CapturedKnownBodies');
        for(const body of actual.bodies){
          const domain={RootInput:'coglatas.compiler-input/1',RootSource:'coglatas.project-source/1',BindingRegion:'coglatas.semantic-binding-region/1',AnalysisRegion:'coglatas.compiler-dependency-region/1'}[body.kind];
          expect(hash(domain,body.text)).toEqual(body.digest);expect(Object.isFrozen(body)).toBe(true);
        }
        if(choice==='available')expect(actual.retainedBodyBytes).toBe(actual.bodies.reduce((n,b)=>n+new TextEncoder().encode(b.text).length,0));
      }
    }
    expect(JSON.stringify(value.result)).toBe(originalResult);
  });
  it('rejects copied diagnostics and another admitted input even with a matching shape',async()=>{
    const a=await ready();const b=await ready('tests-publication-false-hypothesis');
    await expect(readDiagnosticPremises(a.value,a.sources,{...a.diagnostic})).rejects.toThrow();
    await expect(readDiagnosticPremises(b.value,a.sources,b.diagnostic)).rejects.toThrow();
  });
  it('does not mutate the original selected diagnostic while freezing returned metadata',async()=>{
    const {value,sources,diagnostic}=await ready();const actual=(await readDiagnosticPremises(value,sources,diagnostic,'available'))!;
    expect(Object.isFrozen(diagnostic)).toBe(false);expect(Object.isFrozen(actual.bodies)).toBe(true);
    Object.assign(diagnostic.evidence,{missingReason:'coglatas.changed-after-read'});
    expect((actual.data['basis'] as {evidence:{missingReason:string|null}}).evidence.missingReason).toBeNull();
    await expect(readDiagnosticPremises(value,sources,diagnostic)).rejects.toThrow();
  });
  it.each(['authority','producerTrust','eligibility','retentionPolicy','relationshipStructure','earlierStates','state','mode','maximumBodyBytes','retainedBodyBytes','source','diagnostic','kind','body-flag','result','extra'])('refuses rehashed native metadata with altered %s',async field=>{
    const {value,sources,diagnostic}=await ready('tests-publication-false-base',raw=>{
      const v=raw.cases.find((c:{key:string})=>c.key==='tests-publication-false-base');const frame=v.diagnosticPremises.diagnostics[0].available;const data=JSON.parse(frame.text);
      if(field==='authority'||field==='producerTrust'||field==='eligibility')data[field]='Allowed';
      else if(field==='retentionPolicy')data[field]='Pinned';
      else if(field==='relationshipStructure'||field==='earlierStates')data[field]='Recovered';
      else if(field==='state')data.state='ManifestOnly';
      else if(field==='mode')data.mode='Manifest';
      else if(field==='maximumBodyBytes')data.maximumBodyBytes=99;
      else if(field==='retainedBodyBytes')data.retainedBodyBytes--;
      else if(field==='source')data.basis.sourceDigest.value='a'.repeat(64);
      else if(field==='diagnostic')data.basis.diagnosticId='00000099-0000-4000-8000-000000000001';
      else if(field==='kind')data.references[0].kind='Unsupported';
      else if(field==='body-flag')data.references[0].bodyCaptured=false;
      else if(field==='result')data.result.verdict='Pass';
      else data.extra='unapproved';
      frame.text=JSON.stringify(data);frame.digest=hash('coglatas.local-diagnostic-premise-snapshot/1',frame.text);
    });
    // The whole selected diagnostic entry is checked, even while requesting its valid Manifest frame.
    await expect(readDiagnosticPremises(value,sources,diagnostic,'manifest')).rejects.toThrow();
  });
  it.each(['digest','format','foreign-frame','duplicate-entry'])('refuses malformed projection %s',async field=>{
    const {value,sources,diagnostic}=await ready('tests-publication-false-base',raw=>{
      const v=raw.cases.find((c:{key:string})=>c.key==='tests-publication-false-base');
      if(field==='digest')v.diagnosticPremises.diagnostics[0].available.digest.value='b'.repeat(64);
      else if(field==='format')v.diagnosticPremises.format='future/2';
      else if(field==='duplicate-entry')v.diagnosticPremises.diagnostics.push(v.diagnosticPremises.diagnostics[0]);
      else v.diagnosticPremises.diagnostics[0].available=raw.cases.find((c:{key:string})=>c.key==='tests-publication-false-hypothesis').diagnosticPremises.diagnostics[0].available;
    });
    await expect(readDiagnosticPremises(value,sources,diagnostic)).rejects.toThrow();
  });
  it('rejects a payload changed while an original digest is being checked',async()=>{
    const {value,sources,diagnostic}=await ready();const original=crypto.subtle.digest.bind(crypto.subtle);let release!:()=>void;
    const spy=vi.spyOn(crypto.subtle,'digest').mockImplementationOnce((algorithm,data)=>new Promise<ArrayBuffer>((resolve,reject)=>{
      release=()=>{void original(algorithm,data).then(resolve,reject);};
    }));
    try{
      const pending=readDiagnosticPremises(value,sources,diagnostic,'available');
      Object.assign(value.diagnosticPremises!.diagnostics[0].available,{text:value.diagnosticPremises!.diagnostics[0].available.text+' '});release();
      await expect(pending).rejects.toThrow();
    }finally{spy.mockRestore();}
  });
});
