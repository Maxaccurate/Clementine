from pathlib import Path
import sys,json,zipfile,hashlib
root=Path(__file__).resolve().parent.parent;app=root/'outputs/ZestDrop';sys.path.insert(0,str(app/'backend'))
import worker
source=root/'work/packing-fixture.pptx'
with zipfile.ZipFile(source,'w') as package:package.writestr('fixture-data.txt','the original package must remain intact')
original=source.read_bytes();response=root/'work/packing-result.json'
worker.execute({'Paths':[str(source)],'Action':'pack:zip','Parameters':{}},response)
result=json.loads(response.read_text());output=Path(result['Files'][0]['Output'])
assert '-packed' in output.name and result['Files'][0]['Error'] is None
with zipfile.ZipFile(output) as archive:assert archive.read(source.name)==original
assert source.read_bytes()==original
print('packing retains the original file as one intact ZIP entry; packed naming passed')
