from pathlib import Path
import sys,json,hashlib,importlib.util
from PIL import Image

ROOT=Path(__file__).resolve().parent.parent
BUILD=ROOT/'outputs/ZestDrop'
sys.path.insert(0,str(BUILD/'backend'))
import worker,common
DIR=ROOT/'work/progress-fixtures';DIR.mkdir(exist_ok=True)
checks=[]
def check(name,fn):
    try:fn();checks.append({'test':name,'passed':True})
    except Exception as ex:checks.append({'test':name,'passed':False,'error':str(ex)})
def require(value):
    if not value:raise AssertionError('unexpected progress state')

inputs=[DIR/f'照片 {i}.png' for i in range(1,4)]
for i,path in enumerate(inputs):Image.new('RGB',(32,24),('red','green','blue')[i]).save(path)
before={str(p):hashlib.sha256(p.read_bytes()).hexdigest() for p in inputs}
def batch():
    response=DIR/'batch.json';captured=[];actual=worker.one
    def observed(path,action,params):
        value=json.loads(Path(str(response)+'.progress').read_text(encoding='utf8'));captured.append(value)
        require(value['Phase']=='processing' and value['Current']==Path(path).name)
        # The current item has not yet finished, so the completed count must exclude it.
        require(value['Processed']==len(captured)-1 and value['Total']==3)
        return actual(path,action,params)
    worker.one=observed
    try:worker.execute({'Paths':list(map(str,inputs)),'Action':'convert:jpg'},response)
    finally:worker.one=actual
    value=json.loads(Path(str(response)+'.progress').read_text(encoding='utf8'))
    require(value['Phase']=='completed' and value['Processed']==value['Total']==3)
    result=json.loads(response.read_text(encoding='utf8'));require(all(Path(f['Output']).exists() for f in result['Files']))
    require(not Path(str(response)+'.progress.tmp').exists())
check('per-file progress is published before work and tracks actual completed files',batch)
def grouped():
    response=DIR/'grouped.json';actual=worker.images.collage
    def observed(paths,params):
        value=json.loads(Path(str(response)+'.progress').read_text(encoding='utf8'));require(value['Processed']==0 and value['Total']==1)
        return actual(paths,params)
    worker.images.collage=observed
    try:worker.execute({'Paths':list(map(str,inputs)),'Action':'createCollage','Parameters':{'canvasWidth':'400','canvasHeight':'300'}},response)
    finally:worker.images.collage=actual
    value=json.loads(Path(str(response)+'.progress').read_text(encoding='utf8'));require(value['Processed']==value['Total']==1 and value['Phase']=='completed')
check('grouped operations count one output task instead of claiming N files are independently done',grouped)
def partial_failure():
    response=DIR/'partial.json';worker.execute({'Paths':[str(inputs[0]),str(DIR/'missing.png'),str(inputs[2])],'Action':'convert:jpg'},response)
    value=json.loads(Path(str(response)+'.progress').read_text(encoding='utf8'));files=json.loads(response.read_text(encoding='utf8'))['Files']
    require(value['Phase']=='completed' and value['Processed']==3 and value['Total']==3)
    require(files[0]['Output'] and files[1]['Error'] and files[2]['Output'])
check('failed files do not stop progress or hide partial-success results',partial_failure)
check('source image hashes are preserved',lambda:require(all(hashlib.sha256(p.read_bytes()).hexdigest()==before[str(p)] for p in inputs)))
report={'passed':sum(x['passed'] for x in checks),'failed':sum(not x['passed'] for x in checks),'checks':checks}
(ROOT/'outputs/progress-worker-tests.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf8')
print(json.dumps(report,ensure_ascii=False),flush=True);sys.exit(bool(report['failed']))
