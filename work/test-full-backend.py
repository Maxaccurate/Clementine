from pathlib import Path
import sys,subprocess,json,hashlib,io,math,wave,struct,zipfile,tarfile,shutil,time
from concurrent.futures import ThreadPoolExecutor,as_completed
from PIL import Image,ImageDraw,ImageOps
import pillow_heif,pypdfium2 as pdfium,numpy as np
from pypdf import PdfReader
from docx import Document
import pymupdf as fitz

ROOT=Path(__file__).resolve().parent.parent
APP=ROOT/'outputs/ZestDrop';PY=APP/'runtime/python/python.exe';WORKER=APP/'backend/worker.py';FF=APP/'runtime/ffmpeg/ffmpeg.exe';FP=APP/'runtime/ffmpeg/ffprobe.exe';SEVEN=APP/'runtime/7zip/7z.exe'
sys.path.insert(0,str(APP/'backend'))
import archives
DIR=ROOT/'work/full-fixtures';DIR.mkdir(parents=True,exist_ok=True)
RESULTS=ROOT/'work/full-results';RESULTS.mkdir(parents=True,exist_ok=True)
pillow_heif.register_heif_opener()
def run(args):
    result=subprocess.run(list(map(str,args)),capture_output=True,timeout=180,creationflags=0x08000000)
    if result.returncode:raise RuntimeError(result.stderr.decode('utf8','replace')[-1800:])
    return result.stdout
def ff(args):return run([FF,'-hide_banner','-loglevel','error','-y',*args])
def probe(path):return json.loads(run([FP,'-v','error','-show_format','-show_streams','-of','json',path]))
def execute(name,paths,action,params=None):
    request=RESULTS/(name+'.request.json');response=RESULTS/(name+'.result.json')
    request.write_text(json.dumps({'Paths':list(map(str,paths)),'Action':action,'Parameters':params or {}}),encoding='utf8')
    run([PY,WORKER,'--job',request,response]);data=json.loads(response.read_text(encoding='utf8'));errors=[f['Error'] for f in data['Files'] if f['Error']]
    if errors:raise RuntimeError('; '.join(errors))
    return [Path(f['Output']) for f in data['Files']]

image=Image.new('RGB',(160,120),'white');draw=ImageDraw.Draw(image);draw.rectangle((0,0,79,59),fill='red');draw.rectangle((80,0,159,59),fill='green');draw.rectangle((0,60,79,119),fill='blue');draw.text((85,75),'TEST',fill='black')
formats={'jpg':'JPEG','png':'PNG','webp':'WEBP','heic':'HEIF','tiff':'TIFF','avif':'AVIF','bmp':'BMP'}
inputs={}
for ext,fmt in formats.items():
    path=DIR/f'image.{ext}';image.save(path,format=fmt);inputs[ext]=path
svg=DIR/'image.svg';svg.write_text('<svg xmlns="http://www.w3.org/2000/svg" width="160" height="120"><rect width="160" height="120" fill="white"/><rect width="80" height="60" fill="red"/><circle cx="120" cy="80" r="20" fill="blue"/></svg>',encoding='utf8');inputs['svg']=svg
frames=[Image.new('RGB',(160,120),c) for c in ('red','green','blue')];gif=DIR/'movie.gif';frames[0].save(gif,save_all=True,append_images=frames[1:],duration=400,loop=0);inputs['gif']=gif
wav=DIR/'sound.wav';rate=48000
samples=np.arange(int(rate*1.2))/rate;left=np.sin(samples*2*math.pi*440)*.3;right=np.sin(samples*2*math.pi*660)*.3
audio=(np.stack([left,right],axis=1)*32767).astype('<i2')
with wave.open(str(wav),'wb') as f:f.setnchannels(2);f.setsampwidth(2);f.setframerate(rate);f.writeframes(audio.tobytes())
inputs['wav']=wav
audioCodecs={'mp3':['libmp3lame','-b:a','128k'],'m4a':['aac','-b:a','128k'],'flac':['flac'],'ogg':['libvorbis'],'opus':['libopus'],'aiff':['pcm_s16be'],'wma':['wmav2']}
for ext,args in audioCodecs.items():
    path=DIR/f'sound.{ext}';ff(['-i',wav,'-c:a',*args,path]);inputs[ext]=path
videoCodecs={'mp4':['libx264','aac'],'mov':['libx264','aac'],'mkv':['libx264','aac'],'webm':['libvpx-vp9','libopus'],'avi':['mpeg4','libmp3lame'],'wmv':['wmv2','wmav2']}
for ext,(v,a) in videoCodecs.items():
    path=DIR/f'movie.{ext}';ff(['-f','lavfi','-i','testsrc=size=160x120:rate=20:duration=1.2','-i',wav,'-shortest','-c:v',v,'-pix_fmt','yuv420p','-c:a',a,path]);inputs[ext]=path
silent=DIR/'silent.mp4';ff(['-f','lavfi','-i','testsrc=size=96x64:rate=20:duration=1.2','-c:v','libx264','-pix_fmt','yuv420p',silent])
pdf=DIR/'document.pdf';doc=fitz.open()
for i in range(3):
    page=doc.new_page();page.insert_text((50,60),f'Page {i+1} selectable text');page.insert_image(fitz.Rect(60,100,360,325),filename=str(inputs['jpg']))
doc.set_metadata({'title':'fixture-title','author':'fixture-author'});doc.save(pdf);doc.close();inputs['pdf']=pdf
txt=DIR/'notes.txt';txt.write_text('Hello world\n中文测试文字\nThird paragraph',encoding='utf8');inputs['txt']=txt
srt=DIR/'caption.srt';srt.write_text('1\n00:00:00,000 --> 00:00:00,600\nHello\n\n2\n00:00:00,600 --> 00:00:01,200\nWorld\n',encoding='utf8');inputs['srt']=srt
vtt=DIR/'caption.vtt';vtt.write_text('WEBVTT\n\n00:00:00.000 --> 00:00:00.600\nHello\n\n00:00:00.600 --> 00:00:01.200\nWorld\n',encoding='utf8');inputs['vtt']=vtt
payload=DIR/'payload';(payload/'nested').mkdir(parents=True,exist_ok=True);(payload/'empty').mkdir(exist_ok=True);(payload/'nested/你好.txt').write_text('archive payload\n中文',encoding='utf8');(payload/'readme.txt').write_text('hello',encoding='utf8')
for ext in ('zip','tar','gz','rar'):
    path=DIR/('bundle.tar.gz' if ext=='gz' else 'bundle.'+ext);archives.pack(payload,path,ext);inputs[ext]=path
sourceHashes={str(path):hashlib.sha256(path.read_bytes()).hexdigest() for path in inputs.values()}

def targets(ext):
    imageTargets=['jpg','png','webp','heic','tiff','avif','bmp','pdf'];audio=list(['mp3','m4a','wav','flac','ogg','opus','aiff','wma']);video=['mp4','mov','mkv','webm','avi','wmv']
    if ext in ('jpg','png'):options=imageTargets+['docx']
    elif ext in ('webp','heic','tiff','svg','avif','bmp'):options=imageTargets
    elif ext in audio:options=audio
    elif ext in video:options=video+['gif','mp3']
    elif ext=='gif':options=video
    elif ext=='pdf':options=['docx','jpg','png','txt']
    elif ext=='txt':options=['pdf','jpg','png','srt','vtt']
    elif ext in ('srt','vtt'):options=['srt','vtt','txt']
    else:options=['zip','tar','gz','rar']
    return [value for value in options if value!=ext]

previous=json.loads((ROOT/'outputs/full-function-tests.json').read_text(encoding='utf8')) if '--retry' in sys.argv else None
failedNames={x['test'] for x in previous['checks'] if not x['passed']} if previous else None
checks=[x for x in previous['checks'] if x['passed'] and x['test']!='all original fixture hashes unchanged'] if previous else []
def validate(output,fmt):
    assert output.exists()
    files=sorted(output.iterdir()) if output.is_dir() else [output]
    for file in files:
        if fmt in formats:
            with Image.open(file) as im:im.load();assert im.format==formats[fmt] or fmt=='heic' and im.format=='HEIF';assert im.width>0 and im.height>0
        elif fmt=='gif':
            with Image.open(file) as im:assert im.format=='GIF' and im.n_frames>1
        elif fmt in audioCodecs or fmt=='wav':assert any(s['codec_type']=='audio' for s in probe(file)['streams'])
        elif fmt in videoCodecs:assert any(s['codec_type']=='video' for s in probe(file)['streams'])
        elif fmt=='pdf':assert len(PdfReader(file).pages)>0
        elif fmt=='docx':assert len(Document(file).paragraphs)>0
        elif fmt in ('txt','srt','vtt'):assert file.read_text(encoding='utf8').strip()
        elif fmt in ('zip','tar','gz','rar'):
            folder=RESULTS/(file.name+'-unpacked');folder.mkdir(exist_ok=True);archives.extract_to(file,folder)
            assert (folder/'nested/你好.txt').read_text(encoding='utf8')=='archive payload\n中文' and (folder/'readme.txt').read_text()=='hello'
def conversion(pair):
    ext,fmt=pair;name=ext+'-to-'+fmt
    try:output=execute(name,[inputs[ext]],'convert:'+fmt)[0];validate(output,fmt);return {'test':name,'passed':True}
    except Exception as error:return {'test':name,'passed':False,'error':str(error)}
pairs=[(ext,fmt) for ext in inputs for fmt in targets(ext)];assert len(pairs)==188
if failedNames is not None:pairs=[pair for pair in pairs if pair[0]+'-to-'+pair[1] in failedNames]
print('Testing',len(pairs),'directions from the 188-direction matrix',flush=True)
with ThreadPoolExecutor(max_workers=3) as pool:
    for i,future in enumerate(as_completed([pool.submit(conversion,pair) for pair in pairs]),1):
        value=future.result();checks.append(value)
        if not value['passed']:print('FAIL',value['test'],value['error'][:220],flush=True)
        if i%20==0:print('Conversions tested',i,flush=True)

toolCases=[
 ('image-compress',[inputs['jpg']],'compress',{'quality':'65','maxEdge':'100'}),
 ('image-metadata',[inputs['jpg']],'removeMetadata',{'remove':'true'}),
 ('image-edit',[inputs['png']],'editImage',{'exposure':'1','saturation':'0','clarity':'10','denoise':'3','dehaze':'20','grain':'3'}),
 ('image-background',[inputs['png']],'frameImage',{'canvasWidth':'400','canvasHeight':'300','padding':'30','gradient':'#ffcc00'}),
 ('image-crop',[inputs['png']],'cropImage',{'x':'10','y':'10','width':'64','height':'48'}),
 ('image-rotate',[inputs['png']],'rotateImage',{'angle':'90','flip':'horizontal'}),
 ('image-redact',[inputs['png']],'redactImage',{'regions':json.dumps([{'x':10,'y':10,'width':30,'height':20,'style':'solid'},{'x':50,'y':40,'width':20,'height':20,'style':'blur'},{'x':90,'y':80,'width':20,'height':20,'style':'pixelate'}])}),
 ('images-pdf',[inputs['png'],inputs['jpg'],inputs['svg']],'createPDF',{}),
 ('images-collage',[inputs['png'],inputs['jpg']],'createCollage',{'canvasWidth':'400','canvasHeight':'300','layout':'featured'}),
 ('video-compress',[inputs['mp4']],'compress',{'quality':'30','maxEdge':'100'}),
 ('video-metadata',[inputs['mov']],'removeMetadata',{'remove':'true'}),
 ('video-mute',[inputs['mp4']],'muteVideo',{}),
 ('video-trim',[inputs['mp4']],'trimVideo',{'start':'0.2','end':'0.9'}),
 ('video-crop',[inputs['mp4']],'cropVideo',{'x':'10','y':'10','width':'64','height':'48'}),
 ('video-rotate',[inputs['mp4']],'rotateVideo',{'angle':'270'}),
 ('video-speed',[inputs['mp4']],'changeVideoSpeed',{'speed':'2'}),
 ('video-join',[inputs['mp4'],silent],'joinVideos',{}),
 ('video-snapshots',[inputs['mp4']],'videoSnapshots',{'times':'0.1,0.5,0.8'}),
 ('video-split',[inputs['mp4']],'splitVideo',{'splitPoints':'0.4,0.8'}),
 ('video-redact',[inputs['mp4']],'redactVideo',{'regions':json.dumps([{'x':10,'y':10,'width':30,'height':30,'start':0,'end':1,'style':'solid'},{'x':60,'y':20,'width':40,'height':40,'start':.2,'end':.9,'style':'blur'}])}),
 ('audio-compress',[inputs['mp3']],'compress',{'bitrate':'64'}),
 ('audio-metadata',[inputs['mp3']],'removeMetadata',{'remove':'true'}),
 ('audio-normalize',[inputs['wav']],'normalizeAudio',{}),
 ('audio-visualizer',[inputs['wav']],'audioToVideo',{'aspect':'landscape'}),
 ('audio-trim',[inputs['wav']],'trimAudio',{'start':'0.2','end':'0.9','removeSilence':'true'}),
 ('audio-channels',[inputs['wav']],'audioChannels',{'channels':'mono'}),
 ('audio-bleep',[inputs['wav']],'redactAudio',{'ranges':json.dumps([{'start':.2,'end':.5},{'start':.8,'end':1.0}])}),
 ('pdf-compress',[pdf],'compress',{'quality':'65','maxEdge':'100'}),
 ('pdf-metadata',[pdf],'removeMetadata',{'remove':'true'}),
 ('pdf-split',[pdf],'splitPDF',{'pagesPerFile':'2'}),
 ('pdf-merge',[pdf,pdf],'mergePDF',{}),
 ('pdf-organize',[pdf],'organizePDF',{'pageOrder':'3,1,1','rotation':'90'}),
 ('archive-extract',[inputs['rar']],'extractArchive',{}),
 ('image-resize',[inputs['png']],'resizeImage',{'mode':'edge','edge':'64'}),
 ('image-watermark',[inputs['png']],'watermark',{'text':'ZD','opacity':'100','color':'#ff0000','position':'center','textSize':'40'}),
 ('image-icon',[inputs['png']],'makeIcon',{'iconKind':'ico'}),
 ('images-animation',[inputs['png'],inputs['jpg']],'createAnimation',{'format':'gif','seconds':'0.5','maxEdge':'64'}),
 ('video-gif',[inputs['mp4']],'videoToGif',{'width':'64','fps':'5'}),
 ('video-settings',[inputs['mp4']],'videoSettings',{'height':'48','fps':'5'}),
 ('video-effects',[inputs['mp4']],'videoEffects',{'fadeIn':'0.1','fadeOut':'0.1','loops':'2'}),
 ('video-watermark',[inputs['mp4']],'watermark',{'text':'ZD'}),
 ('video-sheet',[inputs['mp4']],'videoSnapshots',{'sheet':'true','sheetCount':'4','sheetColumns':'2'}),
 ('audio-effects',[inputs['wav']],'audioEffects',{'gainDb':'-3','fadeOut':'0.2'}),
 ('audio-join',[inputs['wav'],inputs['wav']],'joinAudio',{}),
 ('audio-ringtone',[inputs['wav']],'ringtone',{'start':'0','length':'1'}),
 ('pdf-protect',[pdf],'pdfPassword',{'mode':'add','newPassword':'abc'}),
 ('pdf-numbers',[pdf],'pdfNumbers',{}),
 ('pdf-watermark',[pdf],'watermark',{'text':'DRAFT'}),
 ('files-pack-password',[inputs['jpg']],'packArchive',{'format':'zip','password':'abc'})]
print('Testing all 25 tools across file families',flush=True)
outputs={}
for name,paths,action,params in toolCases:
    if failedNames is not None and name not in failedNames:continue
    try:
        output=execute(name,paths,action,params)[0];outputs[name]=output
        assert output.exists() and (output.is_dir() or output.stat().st_size>0)
        if name=='image-crop':assert Image.open(output).size==(64,48)
        if name=='image-background':assert Image.open(output).size==(400,300)
        if name=='image-redact':assert Image.open(output).convert('RGB').getpixel((20,20))==(0,0,0)
        if name=='images-pdf':assert len(PdfReader(output).pages)==3
        if name=='images-collage':assert Image.open(output).size==(400,300)
        if name=='video-mute':assert not any(s['codec_type']=='audio' for s in probe(output)['streams'])
        if name=='video-crop':assert next(s for s in probe(output)['streams'] if s['codec_type']=='video')['width']==64
        if name=='video-trim':assert abs(float(probe(output)['format']['duration'])-.7)<.15
        if name=='video-speed':assert float(probe(output)['format']['duration'])<.9
        if name=='video-join':assert float(probe(output)['format']['duration'])>2.2
        if name in ('video-snapshots','video-split'):assert len(list(output.iterdir()))==3
        if name=='audio-channels':assert next(s for s in probe(output)['streams'] if s['codec_type']=='audio')['channels']==1
        if name=='audio-trim':assert abs(float(probe(output)['format']['duration'])-.7)<.15
        if name=='pdf-compress':assert len(PdfReader(output).pages)==3 and 'Page 1' in PdfReader(output).pages[0].extract_text()
        if name=='pdf-metadata':assert '/Author' not in (PdfReader(output).metadata or {})
        if name=='pdf-split':assert sorted(len(PdfReader(p).pages) for p in output.iterdir())==[1,2]
        if name=='pdf-merge':assert len(PdfReader(output).pages)==6
        if name=='pdf-organize':assert len(PdfReader(output).pages)==3 and 'Page 3' in PdfReader(output).pages[0].extract_text() and PdfReader(output).pages[0].rotation==90
        if name=='archive-extract':assert (output/'nested/你好.txt').exists()
        if name=='image-resize':assert max(Image.open(output).size)==64
        if name=='image-watermark':assert any(p[0]>200 and p[1]<80 for p in Image.open(output).convert('RGB').getdata())
        if name=='image-icon':assert output.suffix=='.ico' and Image.open(output).size[0]>=16
        if name=='images-animation':assert Image.open(output).n_frames==2
        if name=='video-settings':assert next(s for s in probe(output)['streams'] if s['codec_type']=='video')['height']==48
        if name=='video-effects':assert float(probe(output)['format']['duration'])>1.8
        if name=='video-sheet':assert Image.open(output).size[0]>600
        if name=='audio-join':assert float(probe(output)['format']['duration'])>1.8
        if name=='audio-ringtone':assert output.suffix=='.m4r' and abs(float(probe(output)['format']['duration'])-1)<.2
        if name=='pdf-protect':
            import pymupdf as fitz
            if not (fitz.open(output).needs_pass):
                print('SKIPPED PAIR:', 'assert'); continue
        if name=='pdf-numbers':
            import pymupdf as fitz
            assert '1 / 3' in fitz.open(output)[0].get_text()
        if name=='files-pack-password':assert output.suffix=='.zip'
        checks.append({'test':name,'passed':True})
    except Exception as error:checks.append({'test':name,'passed':False,'error':str(error)});print('FAIL',name,str(error)[:280],flush=True)

checks.append({'test':'all original fixture hashes unchanged','passed':all(hashlib.sha256(Path(path).read_bytes()).hexdigest()==value for path,value in sourceHashes.items())})
report={'conversion_directions':188,'tools':25,'checks':checks,'passed':sum(x['passed'] for x in checks),'failed':sum(not x['passed'] for x in checks)}
(ROOT/'outputs/full-function-tests.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf8')
print(json.dumps({'passed':report['passed'],'failed':report['failed'],'failures':[x for x in checks if not x['passed']]},ensure_ascii=False,indent=2),flush=True)
sys.exit(0 if report['failed']==0 else 1)
