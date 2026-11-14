"""Read provider-bound originals for an isolated diagnostic; never publish coverage."""
import hashlib,json,os,subprocess,zipfile
from pathlib import Path,PurePosixPath

root=Path.cwd();sha=os.environ['PRODUCT_SHA'];run_id=int(os.environ['SONAR_RUN_ID'])
def api(path):return json.loads(subprocess.check_output(['gh','api','repos/NYGsatoshi/Coglatas/'+path]))
run=api('actions/runs/'+str(run_id))
assert run['head_sha']==sha and run['head_branch']=='main' and run['event']=='push'
assert run['status']=='completed' and run['conclusion']=='success'
product=root/'product';assert subprocess.check_output(['git','rev-parse','HEAD'],cwd=product,text=True).strip()==sha
tree=subprocess.check_output(['git','rev-parse','HEAD^{tree}'],cwd=product,text=True).strip()
image='node:26@sha256:438603bbb5cda404b9416f0b5c8026f61501ab911760d861ec28c36b5d675725'
assert 'FROM '+image+' AS frontend-deps' in (product/'Dockerfile').read_text()
inputs=root/'inputs';inputs.mkdir();(root/'outputs').mkdir()
subprocess.run(['git','-c','core.autocrlf=false','-c','core.eol=lf','archive','--format=tar','--output='+str(inputs/'source.tar'),sha],cwd=product,check=True)
packet=api('actions/runs/'+str(run_id)+'/artifacts?per_page=100');assert len(packet['artifacts'])==packet['total_count']
artifacts=[]
for name,folder in [('sonar-node-coverage','node'),('sonar-node-raw-v8','raw')]:
 selected=[a for a in packet['artifacts'] if a['name']==name];assert len(selected)==1
 a=selected[0];assert not a['expired'] and a['workflow_run']['head_sha']==sha and a['workflow_run']['id']==run_id
 archive=inputs/(folder+'.zip')
 with archive.open('wb') as stream:subprocess.run(['gh','api','repos/NYGsatoshi/Coglatas/actions/artifacts/'+str(a['id'])+'/zip'],stdout=stream,check=True)
 assert 'sha256:'+hashlib.sha256(archive.read_bytes()).hexdigest()==a['digest']
 destination=inputs/folder;destination.mkdir()
 with zipfile.ZipFile(archive) as z:
  assert len(z.namelist())==len(set(z.namelist()))
  for member in z.infolist():
   p=PurePosixPath(member.filename)
   assert not p.is_absolute() and '..' not in p.parts and '\\' not in member.filename and ':' not in member.filename
   assert (member.external_attr>>16)&0o170000!=0o120000
  z.extractall(destination)
 artifacts.append({'id':a['id'],'name':name,'digest':a['digest'],'run':run_id,'attempt':run['run_attempt'],'sourceSha':sha})
representation=json.loads((inputs/'node/representation.json').read_bytes());assert representation['sourceSha']==sha and representation['nodeVersion']=='v26.13.0'
result={'productSourceSha':sha,'productTree':tree,'sourceSonarRun':run_id,'sourceSonarAttempt':run['run_attempt'],'workflowExecutionSha':os.environ['GITHUB_SHA'],'workflowRun':os.environ['GITHUB_RUN_ID'],'workflowAttempt':os.environ['GITHUB_RUN_ATTEMPT'],'nodeImage':image,'sourceArchiveSha256':hashlib.sha256((inputs/'source.tar').read_bytes()).hexdigest(),'originalArtifacts':artifacts,'originalsRewritten':False,'coverageImport':False,'scope':'Independent native replay of original six-helper inputs only; no Main measurement, official Gate, acceptance, finding disposition or Release GO.'}
(root/'outputs/input-bindings.json').write_text(json.dumps(result,indent=2)+'\n');print(json.dumps(result))
