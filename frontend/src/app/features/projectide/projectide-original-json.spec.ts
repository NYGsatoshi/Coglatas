/// <reference types="node" />
import { createHash } from 'node:crypto';
import { readFileSync } from 'node:fs';
import { originalJsonArrayValues } from './projectide-original-json';

const cases = JSON.parse(readFileSync('public/assets/projectide/compiler-outputs-v1.json','utf8')).cases;
const hash = (domain: string, text: string) => createHash('sha256').update(domain).update(new Uint8Array([0])).update(text).digest('hex');
describe('Original JSON value bytes', () => {
  it.each(cases.map((c: {key:string}) => [c.key,c]))('retains actual native region bodies and original digests: %s', (_key, value) => {
    for (const [slot,domain] of [['binding','coglatas.semantic-binding-region/1'],['analysis','coglatas.compiler-dependency-region/1']]) {
      const frame=value.scopedSources[slot]; const native=JSON.parse(frame.text).regions;
      const slices=originalJsonArrayValues(frame.text,'regions','region'); expect(slices).toHaveLength(native.length);
      slices.forEach((text,i)=>{expect(JSON.parse(text)).toEqual(native[i].region);expect(hash(domain,text)).toBe(native[i].digest.value);});
    }
    const diagnostics=originalJsonArrayValues(value.scopedSources.result.text,'diagnostics');
    expect(diagnostics.map(text=>JSON.parse(text))).toEqual(value.result.diagnostics);
  });
  it('preserves original whitespace and numeric lexemes rather than reserializing', () => {
    const body='{ "number" : 1.00, "nested": ["{}[]", {"x": true}] }';
    const text=' \n { "regions" : [ { "region" : '+body+' , "other": 7 } ] } \t';
    const [slice]=originalJsonArrayValues(text,'regions','region'); expect(slice).toBe(body);
    expect(slice).not.toBe(JSON.stringify(JSON.parse(body))); expect(JSON.parse(slice).number).toBe(1);
  });
  it('skips escaped quotes, backslashes and delimiters inside string values', () => {
    const body=JSON.stringify({value:'quote" slash'+String.fromCharCode(92)+' braces{}[]',unicode:'テスト 🧩'});
    const text=JSON.stringify({before:'[]{}',rows:[{noise:'"',value:JSON.parse(body)}],after:'x'});
    const [slice]=originalJsonArrayValues(text,'rows','value'); expect(slice).toBe(body);
  });
  it('returns empty arrays and exact primitive value lexemes', () => {
    expect(originalJsonArrayValues('{"rows":[]}','rows')).toEqual([]);
    expect(originalJsonArrayValues('{"rows":[1.00,null,true,"x"]}','rows')).toEqual(['1.00','null','true','"x"']);
  });
  it.each(['{"rows":[],"rows":[]}','{"rows":[{"value":1,"value":2}]}','{"rows":[1,]}','{"rows":[1.7976931348623157e+309]}'])('refuses invalid or ambiguous original input: %s', text => {
    expect(()=>originalJsonArrayValues(text,'rows','value')).toThrow();
  });
  it('refuses missing paths, wrong container types and oversized frames', () => {
    expect(()=>originalJsonArrayValues('{"rows":[{}]}','rows','missing')).toThrow();
    expect(()=>originalJsonArrayValues('{"rows":{}}','rows')).toThrow();
    expect(()=>originalJsonArrayValues('{"other":[]}','rows')).toThrow();
    expect(()=>originalJsonArrayValues(' '.repeat(4_194_305),'rows')).toThrow();
  });
});
