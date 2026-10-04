from pathlib import Path
import subprocess,json,hashlib,tarfile
from PIL import Image
from docx import Document

root=Path(__file__).resolve().parent.parent
app=root/'outputs/ZestDrop';fixtures=root/'work/full-fixtures'
cases=[('image.png','jpg'),('movie.mp4','mov'),('sound.wav','mp3'),('document.pdf','docx'),('bundle.zip','tar')]
checks=[]
for name,fmt in cases:
    source=fixtures/name;before=hashlib.sha256(source.read_bytes()).hexdigest()
    process=subprocess.run([str(app/'ZestDrop.exe'),'--convert',fmt,str(source)],timeout=120)
    data=json.loads((app/'last-cli-result.json').read_text(encoding='utf8'))
    output=Path(data['Files'][0]['Output']) if data['Files'][0]['Output'] else None
    valid=process.returncode==0 and output is not None and output.is_file()
    if valid and fmt=='jpg':valid=Image.open(output).format=='JPEG'
    if valid and fmt in ('mov','mp3'):
        info=json.loads(subprocess.check_output([str(app/'runtime/ffmpeg/ffprobe.exe'),'-v','error','-show_streams','-of','json',str(output)]))
        valid=any(stream['codec_type']==('audio' if fmt=='mp3' else 'video') for stream in info['streams'])
    if valid and fmt=='docx':valid=bool(Document(output).paragraphs)
    if valid and fmt=='tar':
        with tarfile.open(output) as archive:valid='nested/你好.txt' in archive.getnames()
    valid=valid and hashlib.sha256(source.read_bytes()).hexdigest()==before
    checks.append({'source':name,'format':fmt,'passed':valid,'error':data['Files'][0]['Error']})
report={'passed':sum(row['passed'] for row in checks),'failed':sum(not row['passed'] for row in checks),'checks':checks}
(root/'outputs/app-dispatch-tests.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf8')
print(json.dumps(report,ensure_ascii=False))
raise SystemExit(bool(report['failed']))
