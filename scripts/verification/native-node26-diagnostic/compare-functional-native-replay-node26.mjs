import assert from 'node:assert/strict';
import {readFileSync,writeFileSync} from 'node:fs';
import {createHash} from 'node:crypto';
const [prefix,nodeDir=prefix,rawDir=prefix+'/raw']=process.argv.slice(2),receipt=JSON.parse(readFileSync(nodeDir+'/representation.json')),diagnostic=JSON.parse(readFileSync(prefix+'-native-order.original.json'));
const report=readFileSync(nodeDir+'/lcov.info'),hash=b=>createHash('sha256').update(b).digest('hex');
assert.equal(hash(report),receipt.reportSha256);assert.equal(diagnostic.referenceSha,receipt.sourceSha);assert.equal(diagnostic.node,process.argv[5]);assert.match(process.argv[5],/^v26\.\d+\.\d+$/);assert.equal(diagnostic.sourceMapBindings.length,0);
const subjects=new Set(receipt.subjects.map(s=>s.path)),blocks=new Map();
for(const block of report.toString().split('end_of_record').filter(s=>s.trim())){const r=block.trim().split(/\r?\n/),path=r.find(l=>l.startsWith('SF:')).slice(3);if(!subjects.has(path))continue;
 const da=r.filter(l=>l.startsWith('DA:')).map(l=>l.slice(3).split(',').map(Number)),br=r.filter(l=>l.startsWith('BRDA:')).map(l=>{const a=l.slice(5).split(',');return {line:+a[0],count:+a[3]};});
 const fn=r.filter(l=>l.startsWith('FN:')).map(l=>({line:+l.slice(3,l.indexOf(',')),name:l.slice(l.indexOf(',')+1)})),fh=r.filter(l=>l.startsWith('FNDA:')).map(l=>({name:l.slice(l.indexOf(',')+1),count:+l.slice(5,l.indexOf(','))}));
 assert.equal(fn.length,fh.length);const functions=fn.map((f,i)=>{assert.equal(f.name,fh[i].name);return{line:f.line,count:fh[i].count};});
 const counter=k=>Number(r.find(l=>l.startsWith(k+':'))?.slice(k.length+1));
 const stats={totalLineCount:counter('LF'),coveredLineCount:counter('LH'),totalBranchCount:counter('BRF'),coveredBranchCount:counter('BRH'),totalFunctionCount:counter('FNF'),coveredFunctionCount:counter('FNH')};
 assert.equal(stats.totalLineCount,da.length);assert.equal(stats.coveredLineCount,da.filter(x=>x[1]>0).length);assert.equal(stats.totalBranchCount,br.length);assert.equal(stats.coveredBranchCount,br.filter(x=>x.count>0).length);
 blocks.set(path,{path,da,br,functions,stats});
}
assert.equal(blocks.size,6);
const canonical=x=>x.map(v=>({line:v.line,count:v.count})).sort((a,b)=>a.line-b.line||a.count-b.count);
const differentNonzeroLineCounts=new Set();
for(const [order,files] of Object.entries(diagnostic.outputs))for(const f of files){const block=blocks.get(f.path);assert.ok(block);
 for(const [key,value] of Object.entries(block.stats))assert.equal(f[key],value,order+' '+f.path+' '+key);
 assert.deepEqual(f.lines.map(l=>[l.line,l.count>0]).sort((a,b)=>a[0]-b[0]),block.da.map(([line,count])=>[line,count>0]).sort((a,b)=>a[0]-b[0]),order+' '+f.path+' line bitmap');
 assert.deepEqual(canonical(f.branches),canonical(block.br),order+' '+f.path+' branch locations/counts');
 assert.deepEqual(canonical(f.functions),canonical(block.functions),order+' '+f.path+' function locations/counts');
 const hits=new Map(block.da);if(f.lines.some(l=>hits.get(l.line)!==l.count))differentNonzeroLineCounts.add(f.path);
}
let rawZeroRanges=0,rawMatchedRanges=0;
for(const f of receipt.rawFiles){const bytes=readFileSync(rawDir+'/'+f.name);assert.equal(hash(bytes),f.sha256);const data=JSON.parse(bytes);for(const script of data.result){if(!receipt.subjects.some(s=>script.url.endsWith('/'+s.path)))continue;for(const fn of script.functions)for(const range of fn.ranges){rawMatchedRanges++;if(range.count===0)rawZeroRanges++;}}}
const result={sha:receipt.sourceSha,node:diagnostic.node,nodeInternalSourceSha256:diagnostic.nodeInternalSourceSha256,rawFiles:receipt.rawFiles.length,affectedRawRecords:diagnostic.affectedRawRecords,subjects:blocks.size,rawMatchedRanges,rawZeroRanges,allOriginalRawDigestsPreserved:true,allSixNativeLineBooleanMapsAndCountsMatchOriginalLcov:true,allSixBranchFunctionLocationsAndCountsMatchOriginalLcov:true,diagnosticOrders:Object.keys(diagnostic.outputs),differentNonzeroLineCountSubjects:[...differentNonzeroLineCounts],fullOutputByteIdentityClaimed:false,originalReportSha256:hash(report),originalReportRewritten:false,sourceMetrics:[...blocks.values()].map(b=>({path:b.path,...b.stats,zeroLineRows:b.da.filter(x=>!x[1]).length,zeroBranchRows:b.br.filter(x=>!x.count).length})),scope:'Read-only native replay of six repaired helpers from current actual source-bound collection. Every native line covered boolean and branch/function location-count multiset matches the original LCOV under all three diagnostic input orders. Raw zero ranges remain; an individual zero range may be covered by another invocation. Other subjects/TS summary defects and nonzero line-hit magnitudes are not claimed fixed. No diagnostic ordering is imported.'};
writeFileSync(prefix+'-native-replay-verification.json',JSON.stringify(result,null,2)+'\n');console.log(JSON.stringify(result));
