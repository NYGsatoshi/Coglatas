"""Collect isolated exact-source native controls; never import or rewrite coverage."""
import hashlib,json,os,re,secrets,shutil,subprocess,tarfile,time,xml.etree.ElementTree as ET
from pathlib import Path

root=Path.cwd();code=root/'diagnostic/scripts/verification/native-node26-diagnostic';out=root/'outputs';out.mkdir()
baseline=os.environ['BASELINE_SHA'];candidate=os.environ['CANDIDATE_SHA']
assert all(re.fullmatch('[a-f0-9]{40}',s) for s in (baseline,candidate))
def run(args,**kwargs):return subprocess.run(args,check=True,**kwargs)
def capture(args,**kwargs):return subprocess.check_output(args,text=True,**kwargs).strip()
def api(path):return json.loads(capture(['gh','api','repos/NYGsatoshi/Coglatas/'+path]))
assert api('git/ref/heads/main')['object']['sha']==baseline
pr=api('pulls/1804');assert pr['head']['sha']==candidate and pr['state']=='open'
sdk='mcr.microsoft.com/dotnet/sdk:10.0.403@sha256:212fe4cb74627d5adf9817a4088906ae3edff1bd853bb27565d4b46731efe61e'
pg='postgres@sha256:e3d24f6906d0760bf4da0dae872415391ededefad23fd471c47dc53b0ddf3c42'
digest=lambda b:hashlib.sha256(b).hexdigest()
execution={k:os.environ[k] for k in ['GITHUB_SHA','GITHUB_RUN_ID','GITHUB_RUN_ATTEMPT']}
projects=['Coglatas.Domain','Coglatas.Application','Coglatas.Infrastructure','Coglatas.UI.Core','Coglatas.Web','Coglatas.SecurityArchitecture','Coglatas.Tests']
ns={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
summary=[]
for kind,sha,expected in [('baseline',baseline,1),('candidate',candidate,4)]:
 source=root/kind;assert capture(['git','rev-parse','HEAD'],cwd=source)==sha
 assert sdk in (source/'Dockerfile').read_text() and pg in (source/'.github/workflows/ci.yml').read_text()
 target=out/kind;target.mkdir();archive=target/'source.tar'
 run(['git','-c','core.autocrlf=false','-c','core.eol=lf','archive','--format=tar','--output='+str(archive),sha],cwd=source)
 tree=capture(['git','rev-parse','HEAD^{tree}'],cwd=source)
 binding={'source':sha,'tree':tree,'sourceArchiveSha256':digest(archive.read_bytes()),'sdkImage':sdk,'postgresImage':pg,'execution':execution,'scope':'Separate isolated native diagnostic, not official Main CI/Gate, coverage import, production operation, disposition or acceptance.'}
 (target/'provider.original.json').write_text(json.dumps(binding,indent=2)+'\n')
 token=secrets.token_hex(12);network='owned-child-'+token;label='coglatas.diagnostic='+token
 network_id=capture(['docker','network','create','--label',label,network]);assert re.fullmatch('[a-f0-9]{64}',network_id)
 pg_id=None
 try:
  password=secrets.token_hex(24)
  process_env={**os.environ,'POSTGRES_PASSWORD':password}
  pg_id=capture(['docker','run','--detach','--rm','--network',network,'--network-alias','postgres','--label',label,'--env','POSTGRES_PASSWORD','--env','POSTGRES_INITDB_ARGS=--set=log_error_verbosity=terse --set=log_min_error_statement=panic --set=log_parameter_max_length=0 --set=log_parameter_max_length_on_error=0',pg],env=process_env)
  assert re.fullmatch('[a-f0-9]{64}',pg_id)
  ready=False
  for _ in range(60):
   if subprocess.run(['docker','exec',pg_id,'pg_isready','-U','postgres'],stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL).returncode==0:ready=True;break
   time.sleep(.5)
  assert ready
  process_env.update(POSTGRES_TEST_CONNECTION_STRING='Host=postgres;Database=postgres;Username=postgres;Password='+password+';Include Error Detail=false',NATIVE_SOURCE_SHA=sha,NATIVE_RUN_OWNERS=str(kind=='candidate').lower(),NATIVE_TEST_FILTER='FullyQualifiedName~LegacyChannelPostMutationPersistencePostgreSqlTests'+('|FullyQualifiedName~OwnedChildProcessShutdownTests' if kind=='candidate' else ''))
  args=['docker','run','--rm','--network',network,'--label',label,'--cpus','2','--memory','4g']
  for key in ['POSTGRES_TEST_CONNECTION_STRING','NATIVE_SOURCE_SHA','NATIVE_RUN_OWNERS','NATIVE_TEST_FILTER']:args+=['--env',key]
  for host,guest,ro in [(archive,'/input/source.tar',True),(code/'owned-child-native.sh','/input/run.sh',True),(target,'/evidence',False)]:args+=['--mount','type=bind,source='+str(host)+',target='+guest+(',readonly' if ro else '')]
  args += [sdk,'bash','/input/run.sh']
  with (target/'native.original.log').open('wb') as stream:run(args,env=process_env,stdout=stream,stderr=subprocess.STDOUT)
 finally:
  if pg_id:
   assert capture(['docker','inspect',pg_id,'--format','{{index .Config.Labels "coglatas.diagnostic"}}'])==token
   run(['docker','stop','--time','10',pg_id],stdout=subprocess.DEVNULL)
  assert capture(['docker','network','inspect',network_id,'--format','{{index .Labels "coglatas.diagnostic"}}'])==token
  run(['docker','network','rm',network_id],stdout=subprocess.DEVNULL)
 # Collect only after all collector processes have exited and restored the assemblies.
 binaries=[];assemblies=target/'assemblies';assemblies.mkdir()
 for project in projects:
  for ext in ['dll','pdb']:
   name=project+'.'+ext;original=target/'build/bin'/project/'release'/name;executed=target/'build/bin/Coglatas.Tests/release'/name
   data=original.read_bytes();assert executed.read_bytes()==data
   if ext=='dll':assert sha.encode() in data
   shutil.copyfile(original,assemblies/name);binaries.append({'name':name,'sha256':digest(data),'bytes':len(data)})
 phases=[]
 for phase,total in [('focused',expected)]+([('owning',11)] if kind=='candidate' else []):
  folder=target/phase;trx=folder/'native-child.trx';doc=ET.parse(trx).getroot();results=doc.findall('./t:Results/t:UnitTestResult',ns)
  assert len(results)==total and all(x.attrib['outcome']=='Passed' for x in results)
  names=[x.attrib['testName'] for x in results]
  if phase=='focused':
   assert sum('LegacyChannelPostMutationPersistencePostgreSqlTests.' in n for n in names)==1
   if kind=='candidate':assert sum('OwnedChildProcessShutdownTests.' in n for n in names)==3
  else:assert all(any(owner in n for owner in ['LegacyChannelLifecyclePersistencePostgreSqlTests.','SecurityArchitectureSignalRTests.']) for n in names)
  reports=list(folder.rglob('coverage.opencover.xml'));assert reports and len({digest(p.read_bytes()) for p in reports})==1
  ET.parse(reports[0]);phases.append({'phase':phase,'passed':total,'failed':0,'skipped':0,'tests':names,'trxSha256':digest(trx.read_bytes()),'rawCoverageSha256':digest(reports[0].read_bytes())})
 sealed={'binding':binding,'executedDllPdb':binaries,'phases':phases,'originalReportsRewritten':False,'coverageImport':False,'compilerAttestation':False}
 (target/'receipt.json').write_text(json.dumps(sealed,indent=2)+'\n');summary.append({'kind':kind,'sha':sha,'tree':tree,'phases':phases,'receiptSha256':digest((target/'receipt.json').read_bytes())})
 # Bundle immutable evidence only; intermediate build/cache directories remain owned by this disposable hosted runner.
 with tarfile.open(out/(kind+'-native-originals.tar.gz'),'w:gz') as tar:
  for name in ['source.tar','provider.original.json','native.original.log','receipt.json','assemblies','focused']+(['owning'] if kind=='candidate' else []):tar.add(target/name,arcname=kind+'/'+name)
 index={'execution':execution,'results':summary,'originalReportsRewritten':False,'coverageImport':False}
 (out/'comparison-progress.json').write_text(json.dumps(index,indent=2)+'\n');print(json.dumps(summary[-1]))
