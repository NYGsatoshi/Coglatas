/// <reference types="node" />
import {readFileSync} from 'node:fs';
import {resolve} from 'node:path';
import {webcrypto} from 'node:crypto';
import {vi} from 'vitest';
import {readPreviewBundle, resolveLocation} from './projectide-preview.adapter';
import {readRootSource} from './projectide-root-source.reader';
import {readPassRecording} from './projectide-pass-recordings.reader';
import {readRecordedPassBoundary} from './projectide-pass-boundary.reader';
import {readPassBoundarySourcePage, passBoundarySourcePageMatches} from './projectide-pass-source-navigation.reader';
import {ProjectIdeWorkbenchStore} from './projectide-workbench.store';
vi.stubGlobal('crypto',webcrypto);
const original = JSON.parse(readFileSync(resolve('public/assets/projectide/pass-recordings-v1.json'),'utf8'));
const bundle = () => readPreviewBundle(structuredClone(original));
async function ready(key = 'pass') {
  const value = bundle().cases.find(item => item.key === key)!; const sources = (await readRootSource(value))!;
  const recording = (await readPassRecording(value,sources,'diagnostic'))!;
  const boundary = (await readRecordedPassBoundary(value,sources,recording,5,'AvailableOriginalJson'))!;
  return {value,sources,recording,boundary};
}
async function storeReady() {
  const store = new ProjectIdeWorkbenchStore(); await store.load(async()=>bundle()); store.choose('pass'); store.loadResult();
  expect(await store.loadRootSource()).toBe(true); expect(await store.loadPassRecording('diagnostic')).toBe(true);
  expect(await store.loadRecordedPassBoundary(5,'available')).toBe(true); return store;
}
describe('Original pass map Source navigation',()=>{
  it.each(original.cases.map((item: {key:string})=>item.key) as string[])('derives bounded original-order targets without rerunning: %s',async key=>{
    const {value,sources,boundary}=await ready(key); const page=readPassBoundarySourcePage(value,sources,boundary,0,1);
    if(key==='test-assertion-navigation') {expect(page).toBeNull();expect(boundary.bodies).toHaveLength(0);return;}
    expect(page).not.toBeNull();expect(page!.targets).toHaveLength(1);expect(page!.offset).toBe(0);expect(page!.maximumTargets).toBe(1);
    expect(passBoundarySourcePageMatches(value,sources,boundary,page!)).toBe(true);
    const map=JSON.parse(boundary.bodies.find(body=>body.domain==='coglatas.semantic-source-map/1')!.text);
    const entries=[...map.nodes.map((item: {irNodeId:string;location:unknown})=>({kind:'node',identity:item.irNodeId,location:item.location})),
      ...map.relations.map((item: {relationId:string;location:unknown})=>({kind:'relation',identity:item.relationId,location:item.location}))];
    expect(page!.totalTargets).toBe(entries.length);expect(page!.originalMapDigest).toEqual(value.sourceMapDigest);
    expect(page!.originalRecordDigest).toBe(boundary.originalRecordDigest);
    for(let offset=0;offset<entries.length;offset++) {
      const selected=readPassBoundarySourcePage(value,sources,boundary,offset,1)!;
      expect(selected.targets).toEqual([entries[offset]]);expect(typeof resolveLocation(value,selected.targets[0].location,true)).toBe('object');
    }
    expect(readPassBoundarySourcePage(value,sources,boundary,2_147_483_647,1)!.targets).toHaveLength(0);
  });
  it('refuses copied admissions, changed roots and mutable target locations',async()=>{
    const {value,sources,boundary}=await ready(); const page=readPassBoundarySourcePage(value,sources,boundary)!;
    expect(passBoundarySourcePageMatches(value,sources,boundary,{...page})).toBe(false);
    expect(()=>readPassBoundarySourcePage(value,sources,{...boundary})).toThrow();
    const otherSources=(await readRootSource(value))!;expect(passBoundarySourcePageMatches(value,otherSources,boundary,page)).toBe(false);
    expect(()=>Object.assign(page.targets[0].location.context,{kind:'changed'})).toThrow();
    Object.assign(value,{freshness:'Stale'});expect(passBoundarySourcePageMatches(value,sources,boundary,page)).toBe(false);
    expect(()=>readPassBoundarySourcePage(value,sources,boundary)).toThrow();
  });
  it.each([[-1,32],[0,0],[0,257],[0,1.5],[NaN,32],[2_147_483_648,32]])('rejects invalid page bounds %s/%s',async(offset,maximum)=>{
    const {value,sources,boundary}=await ready();expect(()=>readPassBoundarySourcePage(value,sources,boundary,offset,maximum)).toThrow();
  });
  it.each(['manifest','budget','source','gap'] as const)('does not borrow a map when selected bodies are %s',async choice=>{
    const {value,sources,recording}=await ready();const selected=(await readRecordedPassBoundary(value,sources,recording,choice==='source'?0:choice==='gap'?3:5,
      choice==='manifest'?'Manifest':'AvailableOriginalJson',choice==='budget'?1:1_048_576))!;
    expect(readPassBoundarySourcePage(value,sources,selected)).toBeNull();expect(value.sourceMap).not.toBeNull();
  });
  it('uses the exact current page and actual member, and preserves unrelated navigation',async()=>{
    const store=await storeReady();expect(store.loadPassBoundarySourcePage(0,1)).toBe(true);const first=store.passBoundarySourcePage()!;
    expect(store.navigatePassBoundarySource(first,first.targets[0])).toBe(true);const target=store.sourceTarget()!;
    expect(store.sourceLocation()).toBe(first.targets[0].location);expect(target.location.entityId).toBe(first.targets[0].location.entityId);
    expect(store.loadPassBoundarySourcePage(1,1)).toBe(true);expect(store.sourceTarget()).toBeNull();const next=store.passBoundarySourcePage()!;
    expect(store.navigatePassBoundarySource(first,first.targets[0])).toBe(false);
    expect(store.navigatePassBoundarySource(next,{...next.targets[0]})).toBe(false);
    expect(store.navigatePassBoundarySource(next,first.targets[0])).toBe(false);
    const record=store.context()!.declarations[0];store.select(record.recordType==='entity'?{kind:'entity',id:record.identity}:{kind:'relation',id:record.identity});expect(store.navigate()).toBe(true);const ordinary=store.sourceTarget();
    expect(store.loadPassBoundarySourcePage()).toBe(true);expect(store.sourceTarget()).toBe(ordinary);
    expect(store.navigatePassBoundarySource(next,next.targets[0])).toBe(false);expect(store.sourceTarget()).toBeNull();
    expect(store.loadPassBoundarySourcePage(-1)).toBe(false);expect(store.passBoundarySourcePage()).toBeNull();
  });
  it.each(['source','boundary','run','whole','choose','clear','mutate','remove'] as const)('withdraws both the page and its owned Source target on %s',async action=>{
    const store=await storeReady();expect(store.loadPassBoundarySourcePage()).toBe(true);const page=store.passBoundarySourcePage()!;
    expect(store.navigatePassBoundarySource(page,page.targets[0])).toBe(true);const value=store.context()!;
    if(action==='source')await store.loadRootSource();if(action==='boundary')await store.loadRecordedPassBoundary(0,'available');
    if(action==='run')await store.loadPassRecording('retry');if(action==='whole')await store.loadRecordedPasses();
    if(action==='choose')store.choose('unknown');if(action==='clear')store.clear('denied');
    if(action==='mutate')Object.assign(value.passRecordings!.diagnostic.manifest,{text:'{}'});
    if(action==='remove')Object.assign(store.bundle()!,{cases:store.bundle()!.cases.filter(item=>item!==value)});
    expect(store.passBoundarySourcePage()).toBeNull();expect(store.sourceTarget()).toBeNull();expect(store.sourceLocation()).toBeNull();
    expect(page.targets.length).toBeGreaterThan(0);expect(store.navigatePassBoundarySource(page,page.targets[0])).toBe(false);
  });
});
