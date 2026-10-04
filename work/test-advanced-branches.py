from pathlib import Path
import sys,json,subprocess,io,zipfile
from PIL import Image,ImageOps
import numpy as np,pillow_heif
ROOT=Path(__file__).resolve().parent.parent;APP=ROOT/'outputs/ZestDrop';FF=APP/'runtime/ffmpeg/ffmpeg.exe'
sys.path.insert(0,str(APP/'backend'))
import worker,media,images,archives
DIR=ROOT/'work/full-fixtures';OUT=ROOT/'work/branch-results';OUT.mkdir(exist_ok=True);checks=[]
def check(name,fn):
    try:fn();checks.append({'test':name,'passed':True})
    except Exception as e:checks.append({'test':name,'passed':False,'error':str(e)});print('FAIL',name,str(e)[:220],flush=True)
def require(value):
    if not value:raise AssertionError('unexpected output')
def execute(name,path,action,params):
    target=OUT/(name+'.json');worker.execute({'Paths':[str(path)],'Action':action,'Parameters':params},target);row=json.loads(target.read_text())['Files'][0]
    if row['Error']:raise RuntimeError(row['Error'])
    return Path(row['Output'])
alpha=DIR/'alpha-test.png';im=Image.new('RGBA',(160,120),(255,0,0,0));im.paste((20,160,50,255),(80,0,160,120));im.save(alpha);pillow_heif.register_heif_opener()
for fmt in ('png','webp','heic','avif','tiff'):
    check('alpha preservation '+fmt,lambda fmt=fmt:require(Image.open(execute('alpha-'+fmt,alpha,'convert:'+fmt,{})).convert('RGBA').getpixel((10,10))[3]==0))
tagged=DIR/'tagged.jpg';exif=Image.Exif();exif[315]='Fixture Artist';exif[34855]=200;exif[274]=6;Image.open(DIR/'image.png').save(tagged,exif=exif)
check('editable image metadata',lambda:require(Image.open(execute('edit-tags',tagged,'removeMetadata',{'remove':'false','metadata':json.dumps({'Artist':'Changed Artist','ISOSpeedRatings':200})})).getexif()[315]=='Changed Artist'))
check('single image tag removal',lambda:require(315 not in Image.open(execute('remove-one-tag',tagged,'removeMetadata',{'remove':'false','metadata':json.dumps({'Artist':None})})).getexif()))
for orientation in range(1,9):
    def orient(orientation=orientation):
        path=DIR/f'orientation-new-{orientation}.jpg';e=Image.Exif();e[274]=orientation;Image.open(DIR/'image.png').save(path,exif=e);expected=ImageOps.exif_transpose(Image.open(path));output=execute('orientation-'+str(orientation),path,'convert:png',{});require(np.array_equal(np.asarray(expected),np.asarray(Image.open(output))))
    check('orientation '+str(orientation),orient)
for layout in ('grid','row','column','featured'):
    check('collage '+layout,lambda layout=layout:require(Image.open(images.collage([DIR/'image.png',DIR/'image.jpg'],{'layout':layout,'canvasWidth':'400','canvasHeight':'300'})).size==(400,300)))
for aspect in ('landscape','portrait','square'):
    check('visualizer '+aspect,lambda aspect=aspect:require(Path(media.audio_tool(DIR/'sound.wav','audioToVideo',{'aspect':aspect})).exists()))
check('static visualizer background',lambda:require(Path(media.audio_tool(DIR/'sound.wav','audioToVideo',{'backgroundImage':str(DIR/'image.jpg')})).exists()))
check('silent video speed',lambda:require(Path(media.video_tool(DIR/'silent.mp4','changeVideoSpeed',{'speed':'0.5'})).exists()))
check('pixelation and small-area blur',lambda:require(Path(media.video_tool(DIR/'movie.mp4','redactVideo',{'regions':json.dumps([{'x':20,'y':20,'width':40,'height':40,'start':0,'end':1,'style':'pixelate'},{'x':90,'y':80,'width':8,'height':8,'start':0,'end':1,'style':'blur'}])})).exists()))
check('target video size',lambda:require(Path(media.video_tool(DIR/'movie.mp4','compress',{'targetKB':'12','maxEdge':'100'})).stat().st_size<=12288))
check('target audio size',lambda:require(Path(media.audio_tool(DIR/'sound.mp3','compress',{'targetKB':'12','bitrate':'96'})).stat().st_size<=12288))
check('target image size',lambda:require(Path(images.tool(DIR/'image.png','compress',{'targetKB':'2'})).stat().st_size<=2048))
def audio_tags():
    output=media.audio_tool(DIR/'sound.mp3','removeMetadata',{'remove':'false','metadata':json.dumps({'format':{'artist':'Changed Artist'}})});require(media.probe(output)['format']['tags']['artist']=='Changed Artist')
check('editable audio metadata',audio_tags)
def preview(kind,path,action,params,extra=None):
    folder=OUT/(kind+'-'+action);folder.mkdir(exist_ok=True)
    result=worker.inspect(str(path),folder) if kind=='inspect' else worker.playback(str(path),action,params,folder) if kind=='playback' else worker.preview(str(path),action,params,folder)
    require(Path(result['Preview']).exists())
    if extra:extra(Path(result['Preview']))
for path in (DIR/'image.png',DIR/'movie.mp4',DIR/'sound.wav',DIR/'document.pdf'):
    check('inspect '+path.suffix,lambda path=path:preview('inspect',path,'info',{}))
check('live crop',lambda:preview('preview',DIR/'image.png','cropImage',{'x':'10','y':'10','width':'64','height':'48'},lambda p:require(Image.open(p).size==(64,48))))
check('live edit',lambda:preview('preview',DIR/'image.png','editImage',{'exposure':'1'}))
check('live background',lambda:preview('preview',DIR/'image.png','frameImage',{'canvasWidth':'400','canvasHeight':'300','padding':'40'}))
check('live video redaction',lambda:preview('preview',DIR/'movie.mp4','redactVideo',{'x':'10','y':'10','width':'30','height':'30','start':'0','end':'1','time':'.2'},lambda p:require(max(Image.open(p).convert('RGB').getpixel((20,20)))<5)))
check('PDF page preview',lambda:preview('preview',DIR/'document.pdf','organizePDF',{'pageNumber':'3'}))
check('real frame timestamp stepping',lambda:require(abs(worker.frame_step(DIR/'movie.mp4',{'time':'.1','direction':'1'})['Time']-.15)<.001))
check('normalized loudness',lambda:require(abs(float(worker.analyze(DIR/'sound.wav',{})['Output'])+16)<1))
check('normalized playback',lambda:preview('playback',DIR/'sound.wav','normalizeAudio',{}))
check('redacted video playback',lambda:preview('playback',DIR/'movie.mp4','redactVideo',{'x':'10','y':'10','width':'30','height':'30','start':'0','end':'1'}))
def bleep():
    result=worker.playback(DIR/'sound.wav','redactAudio',{'ranges':json.dumps([{'start':.2,'end':.5}])},OUT)
    data=subprocess.run([str(FF),'-v','error','-i',result['Preview'],'-ac','1','-ar','48000','-f','f32le','-'],capture_output=True,check=True).stdout
    signal=np.frombuffer(data,dtype='<f4');segment=signal[int(.28*48000):int(.42*48000)];freq=np.fft.rfftfreq(len(segment),1/48000);peak=freq[np.abs(np.fft.rfft(segment)).argmax()];require(abs(peak-1000)<15)
check('1000 Hz beep replaces original speech in preview',bleep)
empty=DIR/'empty.zip'
with zipfile.ZipFile(empty,'w'):pass
check('empty archive',lambda:require(Path(archives.extract(empty,{})).is_dir()))
def traversal():
    path=DIR/'unsafe.zip'
    with zipfile.ZipFile(path,'w') as archive:archive.writestr('../escaped.txt','no')
    try:archives.extract(path,{})
    except ValueError:return
    raise AssertionError('unsafe archive was accepted')
check('archive traversal blocked',traversal)
report={'passed':sum(x['passed'] for x in checks),'failed':sum(not x['passed'] for x in checks),'checks':checks};(ROOT/'outputs/advanced-branch-tests.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf8');print(json.dumps({'passed':report['passed'],'failed':report['failed'],'failures':[x for x in checks if not x['passed']]},ensure_ascii=False),flush=True)
sys.exit(0 if report['failed']==0 else 1)
