import{readFileSync}from'node:fs';import{createHash}from'node:crypto';import assert from'node:assert/strict';
const[repo,manifest]=process.argv.slice(2),r=JSON.parse(readFileSync(manifest));assert.equal(process.version,r.nodeVersion);assert.equal(r.sourceSha,process.env.PRODUCT_SHA);assert.equal(r.originalBytesRewritten,false);assert.equal(r.subjects.length,6);
for(const s of r.subjects){assert.ok(!s.path.includes('..')&&!s.path.startsWith('/'));const b=readFileSync(repo+'/'+s.path);assert.equal(createHash('sha256').update(b).digest('hex'),s.sourceSha256);assert.equal(b.toString().length,s.utf16Length);}
console.log(JSON.stringify({sha:r.sourceSha,node:process.version,subjects:r.subjects.length,sourceBytesVerified:true}));
