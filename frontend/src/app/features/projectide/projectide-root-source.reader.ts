import { PreviewCase } from './projectide.contracts';
import { validatePreviewCaseBindings } from './projectide-preview.adapter';
import { parseLocalTestJson as parse, verifyLocalTestDigest as verify } from './projectide-tests.reader';

export interface RootSourceProjection {
  readonly format: 'coglatas.local-root-source-projection/1';
  readonly inputText: string;
  readonly result: { readonly text: string; readonly digest: import('./projectide.contracts').Digest };
}
export interface AdmittedRootSource {
  readonly kind: 'root-source'; readonly projection: RootSourceProjection;
  readonly sourceText: string; readonly source: Readonly<Record<string, unknown>>;
}
// Complete fixture fingerprints may contain recorded bodies; keep them outside the published Source view.
const admissions = new WeakMap<AdmittedRootSource, string>();
const fail = (): never => { throw new Error('unsupported-root-source'); };
const object = (v: unknown): v is Record<string, unknown> => !!v && typeof v === 'object' && !Array.isArray(v);
const obj = (v: unknown): Record<string, unknown> => object(v) ? v : fail();
const equal = (a: unknown,b: unknown): boolean => a===b || Array.isArray(a)&&Array.isArray(b)&&a.length===b.length&&a.every((v,i)=>equal(v,b[i])) ||
  object(a)&&object(b)&&Object.keys(a).length===Object.keys(b).length&&Object.keys(a).every(k=>Object.hasOwn(b,k)&&equal(a[k],b[k]));
const fields = (v: unknown, keys: readonly string[]) => object(v)&&Object.keys(v).length===keys.length&&keys.every(k=>Object.hasOwn(v,k));
const bytes = (v: string) => new TextEncoder().encode(v).length;
const freeze = <T>(v: T): T => { if(v&&typeof v==='object'){Object.values(v).forEach(freeze);Object.freeze(v);}return v; };
export function rootSourceMatches(value: PreviewCase, admitted: AdmittedRootSource): boolean {
  return admissions.has(admitted)&&value.freshness==='Current'&&equal(value.context,value.desiredContext)&&JSON.stringify(value)===admissions.get(admitted);
}

/** Reads only the exact retained root input; regional IR coverage and current runtime authority remain separate. */
export async function readRootSource(value: PreviewCase): Promise<AdmittedRootSource | null> {
  if(value.rootSource==null)return null;
  validatePreviewCaseBindings(value); const basis=JSON.stringify(value); if(bytes(basis)>16_777_216)return fail();
  const v=structuredClone(value); const projection=v.rootSource!; const frame=projection.result;
  if(!fields(projection,['format','inputText','result'])||projection.format!=='coglatas.local-root-source-projection/1'||
    typeof projection.inputText!=='string'||bytes(projection.inputText)>4_194_304||!fields(frame,['text','digest'])||
    typeof frame.text!=='string'||bytes(frame.text)>4_194_304||typeof v.sourceText!=='string'||bytes(v.sourceText)>4_194_304)return fail();
  if(!await verify('coglatas.compiler-input/1',projection.inputText,v.inputDigest)||
    !await verify('coglatas.compiler-result/1',frame.text,frame.digest)||!equal(frame.digest,v.resultDigest)||
    !await verify('coglatas.project-source/1',v.sourceText,v.sourceDigest))return fail();
  const input=obj(parse(projection.inputText)); const source=obj(parse(v.sourceText)); const result=obj(parse(frame.text));
  if(!equal(input,v.manifest['manifest'])||!equal(input['sourceContext'],v.context)||!equal(input['sourceDigest'],v.sourceDigest)||
    !equal(source,v.source)||source['format']!=='coglatas.project-source/1'||!equal(source['context'],v.context)||!equal(result,v.result))return fail();
  if(JSON.stringify(value)!==basis||v.freshness!=='Current'||!equal(v.context,v.desiredContext))return fail();
  const admitted: AdmittedRootSource=freeze({kind:'root-source',projection,sourceText:v.sourceText,source});
  admissions.set(admitted,basis); return admitted;
}
