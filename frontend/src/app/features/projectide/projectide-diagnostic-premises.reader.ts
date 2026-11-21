import { AdmittedRootSource, rootSourceMatches } from './projectide-root-source.reader';
import { Diagnostic, Digest, PreviewCase } from './projectide.contracts';
import { AdmittedScopedSources, scopedSourcesMatch } from './projectide-scoped-sources';
import { originalJsonArrayValues } from './projectide-original-json';
import { parseLocalTestJson as parse, verifyLocalTestDigest as verify } from './projectide-tests.reader';

interface Frame { readonly text: string; readonly digest: Digest; }
export interface DiagnosticPremiseProjection {
  readonly format: 'coglatas.local-diagnostic-premise-projection/1';
  readonly diagnostics: readonly { readonly diagnosticId: string; readonly manifest: Frame; readonly available: Frame; readonly budget: Frame }[];
}
export type DiagnosticPremiseChoice = 'manifest' | 'available' | 'budget';
type Kind = 'RootInput' | 'BindingRegion' | 'AnalysisRegion' | 'RootSource';
export type DiagnosticPremiseSources = AdmittedScopedSources | AdmittedRootSource;
export const isRootPremiseSource = (sources: DiagnosticPremiseSources): sources is AdmittedRootSource => 'kind' in sources && sources.kind==='root-source';
export function diagnosticPremiseSourcesMatch(value: PreviewCase, sources: DiagnosticPremiseSources, diagnostic: Diagnostic): boolean {
  return isRootPremiseSource(sources) ? rootSourceMatches(value,sources) && equal(diagnostic.primaryLocation.context,value.context) : scopedSourcesMatch(value,sources);
}
type Resolution = 'ResolvedRetainedObject' | 'Unavailable' | 'Ambiguous' | 'LookupLimit';
export interface DiagnosticPremiseReference {
  readonly digest: Digest; readonly kind: Kind | null; readonly resolution: Resolution; readonly bodyCaptured: boolean;
}
export interface DiagnosticPremiseBody extends Frame { readonly kind: Kind; }
export interface AdmittedDiagnosticPremises extends Frame {
  readonly sources: DiagnosticPremiseSources; readonly diagnostic: Diagnostic; readonly choice: DiagnosticPremiseChoice;
  readonly mode: 'Manifest' | 'AvailableBodies'; readonly state: string; readonly data: Readonly<Record<string, unknown>>;
  readonly maximumBodyBytes: number; readonly retainedBodyBytes: number; readonly lookupComplete: boolean;
  readonly references: readonly DiagnosticPremiseReference[]; readonly bodies: readonly DiagnosticPremiseBody[];
}
const fail = (): never => { throw new Error('unsupported-diagnostic-premises'); };
const object = (v: unknown): v is Record<string, unknown> => !!v && typeof v === 'object' && !Array.isArray(v);
const obj = (v: unknown): Record<string, unknown> => object(v) ? v : fail();
const fields = (v: unknown, keys: readonly string[]) => object(v) && Object.keys(v).length === keys.length && keys.every(k => Object.hasOwn(v,k));
const equal = (a: unknown,b: unknown): boolean => a===b || Array.isArray(a)&&Array.isArray(b)&&a.length===b.length&&a.every((v,i)=>equal(v,b[i])) ||
  object(a)&&object(b)&&Object.keys(a).length===Object.keys(b).length&&Object.keys(a).every(k=>Object.hasOwn(b,k)&&equal(a[k],b[k]));
const digest = (v: unknown): v is Digest => fields(v,['algorithm','value']) && obj(v)['algorithm']==='sha-256' && typeof obj(v)['value']==='string' && /^[a-f0-9]{64}$/u.test(obj(v)['value'] as string);
const freeze = <T>(v: T): T => { if(v && typeof v==='object'){Object.values(v).forEach(freeze);Object.freeze(v);} return v; };
const byteCount = (v: string) => new TextEncoder().encode(v).length;
const domains: Readonly<Record<Kind,string>> = {RootInput:'coglatas.compiler-input/1',RootSource:'coglatas.project-source/1',BindingRegion:'coglatas.semantic-binding-region/1',AnalysisRegion:'coglatas.compiler-dependency-region/1'};
const nativeAxis = (v: unknown): string => {
  const axes: Readonly<Record<string,string>> = {'coglatas.completed':'Completed','coglatas.not-run':'NotRun','coglatas.cancelled':'Cancelled',
    'coglatas.timed-out':'TimedOut','coglatas.resource-exhausted':'ResourceExhausted','coglatas.internal-error':'InternalError',
    'coglatas.complete':'Complete','coglatas.partial':'Partial','coglatas.none':'None','coglatas.pass':'Pass','coglatas.fail':'Fail','coglatas.inconclusive':'Inconclusive','coglatas.error':'Error'};
  return typeof v==='string' && Object.hasOwn(axes,v)?axes[v]:fail();
};
async function contentDigest(domain: string,text: string): Promise<Digest> {
  const bytes = new Uint8Array(await crypto.subtle.digest('SHA-256',new TextEncoder().encode(domain+String.fromCharCode(0)+text)));
  return {algorithm:'sha-256',value:Array.from(bytes,b=>b.toString(16).padStart(2,'0')).join('')};
}

/** Reads native snapshot metadata and exact retained body bytes; no evaluation, inferred reason graph or current access grant. */
export async function readDiagnosticPremises(value: PreviewCase, sources: DiagnosticPremiseSources, diagnostic: Diagnostic,
  choice: DiagnosticPremiseChoice='manifest'): Promise<AdmittedDiagnosticPremises | null> {
  if(!['manifest','available','budget'].includes(choice)||!diagnosticPremiseSourcesMatch(value,sources,diagnostic)||!value.result.diagnostics.includes(diagnostic))return fail();
  if(value.diagnosticPremises==null)return null;
  const projection=value.diagnosticPremises;
  if(!fields(projection,['format','diagnostics'])||projection.format!=='coglatas.local-diagnostic-premise-projection/1'||
    !Array.isArray(projection.diagnostics)||projection.diagnostics.length!==value.result.diagnostics.length||projection.diagnostics.length>20_000||
    projection.diagnostics.some((entry,i)=>!fields(entry,['diagnosticId','manifest','available','budget'])||entry.diagnosticId!==value.result.diagnostics[i].diagnosticId))return fail();
  const index=value.result.diagnostics.indexOf(diagnostic);const selected=projection.diagnostics[index];const copy=structuredClone(selected);
  const diagnosticTexts=originalJsonArrayValues(sources.projection.result.text,'diagnostics');
  if(diagnosticTexts.length!==value.result.diagnostics.length||!equal(parse(diagnosticTexts[index]),diagnostic))return fail();
  const diagnosticDigest=await contentDigest('coglatas.compiler-diagnostic/1',diagnosticTexts[index]);
  const wanted=new Set(diagnostic.evidence.premises.map(d=>d.value));
  type Candidate={kind:Kind;digest:Digest;text:string};
  const candidates: Candidate[]=[];let lookupComplete=true;
  if(wanted.size){
    if(isRootPremiseSource(sources)){
      candidates.push({kind:'RootInput',digest:value.inputDigest,text:sources.projection.inputText},
        {kind:'RootSource',digest:value.sourceDigest,text:sources.sourceText});
    }else{
    const binding=obj(parse(sources.projection.binding.text));const analysis=obj(parse(sources.projection.analysis.text));
    if(!Array.isArray(binding['regions'])||!Array.isArray(analysis['regions']))return fail();
    const bindings=binding['regions'];const analyses=analysis['regions'];
    lookupComplete=2+bindings.length+analyses.length<=4096;
    if(lookupComplete){
      const bindingTexts=originalJsonArrayValues(sources.projection.binding.text,'regions','region');
      const analysisTexts=originalJsonArrayValues(sources.projection.analysis.text,'regions','region');
      if(bindingTexts.length!==bindings.length||analysisTexts.length!==analyses.length)return fail();
      candidates.push({kind:'RootInput',digest:value.inputDigest,text:sources.projection.inputText},
        {kind:'RootSource',digest:value.sourceDigest,text:value.sourceText});
      for(const [entries,texts,kind] of [[bindings,bindingTexts,'BindingRegion'],[analyses,analysisTexts,'AnalysisRegion']] as const) {
        for(let i=0;i<entries.length;i++){
          const data=obj(entries[i]);const reference=data['digest'];
          if(!digest(reference)||!equal(parse(texts[i]),data['region']))return fail();
          if(wanted.has(reference.value))candidates.push({kind,digest:reference,text:texts[i]});
        }
      }
    }
  }
  }
  const references: Omit<DiagnosticPremiseReference,'bodyCaptured'>[]=diagnostic.evidence.premises.map(d=>{
    const copy: Digest={algorithm:'sha-256',value:d.value};if(!lookupComplete)return {digest:copy,kind:null,resolution:'LookupLimit'};
    const matches=candidates.filter(c=>equal(c.digest,d));
    return matches.length===1?{digest:copy,kind:matches[0].kind,resolution:'ResolvedRetainedObject'}:
      {digest:copy,kind:null,resolution:matches.length?'Ambiguous':'Unavailable'};
  });
  const known: DiagnosticPremiseBody[]=[];
  for(const reference of references){
    if(reference.resolution!=='ResolvedRetainedObject')continue;
    const candidate=candidates.find(c=>equal(c.digest,reference.digest))!;
    if(byteCount(candidate.text)>4_194_304||obj(parse(candidate.text))['format']!==domains[candidate.kind]||
      !await verify(domains[candidate.kind],candidate.text,reference.digest))return fail();
    known.push({kind:candidate.kind,digest:reference.digest,text:candidate.text});
  }
  const allBodyBytes=known.reduce((n,b)=>n+byteCount(b.text),0);
  const admitted: Partial<Record<DiagnosticPremiseChoice,AdmittedDiagnosticPremises>>={};
  for(const label of ['manifest','available','budget'] as const){
    const frame=copy[label];if(!fields(frame,['text','digest'])||typeof frame.text!=='string'||byteCount(frame.text)>1_048_576||
      !digest(frame.digest)||!await verify('coglatas.local-diagnostic-premise-snapshot/1',frame.text,frame.digest))return fail();
    const mode=label==='manifest'?'Manifest':'AvailableBodies';const maximumBodyBytes=label==='budget'?1:1_048_576;
    const state=!lookupComplete?'LookupLimitReached':mode==='Manifest'?'ManifestOnly':references.length===0?'NoRecordedPremises':
      allBodyBytes>maximumBodyBytes?'BodyBudgetExceeded':known.length?'CapturedKnownBodies':'NoKnownBodies';
    const bodies=state==='CapturedKnownBodies'?known:[];const retainedBodyBytes=bodies.reduce((n,b)=>n+byteCount(b.text),0);
    const refs=references.map(r=>({...r,bodyCaptured:state==='CapturedKnownBodies'&&r.resolution==='ResolvedRetainedObject'}));
    const result=obj(value.result);const data=obj(parse(frame.text));
    const expected={format:'coglatas.local-diagnostic-premise-snapshot/1',state,mode,maximumBodyBytes,retainedBodyBytes,lookupComplete,
      bodyScope:isRootPremiseSource(sources)?'RootCompilerInputObjects':'RetainedCompilerInputObjects',relationshipStructure:'NotRecorded',earlierStates:'NotRecorded',authority:'NotEvaluated',producerTrust:'NotEvaluated',eligibility:'NotEvaluated',retentionPolicy:'NotAdopted',
      basis:{inputDigest:value.inputDigest,resultDigest:value.resultDigest,sourceDigest:value.sourceDigest,context:value.context,compiler:result['compiler'],
        diagnosticId:diagnostic.diagnosticId,diagnosticDigest,ruleId:diagnostic.ruleId,ruleVersion:diagnostic.ruleVersion,stageOrdinal:diagnostic.stageOrdinal,
        location:diagnostic.primaryLocation,evidence:diagnostic.evidence},
      result:{execution:nativeAxis(result['execution']),coverage:nativeAxis(result['coverage']),verdict:nativeAxis(result['verdict']),evidenceComplete:result['evidenceComplete']},references:refs};
    if(!equal(data,expected))return fail();
    admitted[label]=Object.freeze({sources,diagnostic,choice:label,mode,state,data:freeze(data),text:frame.text,digest:freeze(frame.digest),
      maximumBodyBytes,retainedBodyBytes,lookupComplete,references:freeze(structuredClone(refs)),bodies:freeze(structuredClone(bodies))});
  }
  if(!diagnosticPremiseSourcesMatch(value,sources,diagnostic)||value.result.diagnostics[index]!==diagnostic||!equal(selected,copy))return fail();
  return admitted[choice]??fail();
}
