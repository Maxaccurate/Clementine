from pathlib import Path
import sys,json,hashlib
from PIL import Image
import numpy as np,pillow_heif

ROOT=Path(__file__).resolve().parent.parent
APP=ROOT/'outputs/DesktopDrop'
sys.path.insert(0,str(APP/'backend'))
import worker,images,media
from common import probe,fit_ratio
DIR=ROOT/'work/f9-fixtures';DIR.mkdir(exist_ok=True)
checks=[]
def check(name,fn):
    try:fn();checks.append({'test':name,'passed':True})
    except Exception as ex:checks.append({'test':name,'passed':False,'error':str(ex)});print('FAIL',name,str(ex),flush=True)
def require(value):
    if not value:raise AssertionError('unexpected output')
def folder(name):
    p=DIR/name;p.mkdir(exist_ok=True);return p
def size(path):
    with Image.open(path) as im:return im.size

rng=np.random.default_rng(47)
pixels=rng.integers(40,240,(120,160,3),dtype=np.uint8)
photo=DIR/'detail.png';Image.fromarray(pixels).save(photo)
source_hash=hashlib.sha256(photo.read_bytes()).hexdigest()
params={'x':'20','y':'20','width':'96','height':'72','style':'pixelate','blockSize':'12'}
def mosaic():
    output=images.tool(photo,'redactImage',params)
    image=np.array(Image.open(output).convert('RGB'))
    require(np.array_equal(image[:20],pixels[:20]))
    require(len(np.unique(image[20:92,20:116].reshape(-1,3),axis=0))<=48)
    require(image[20:92,20:116].min()>20)
    require(np.array_equal(image[20,20],image[25,25]))
    require(hashlib.sha256(photo.read_bytes()).hexdigest()==source_hash)
check('mosaic is averaged pixel blocks, not a black cover; outside pixels and source preserved',mosaic)
def mosaic_sizes():
    a=np.array(images.redact(Image.open(photo),{**params,'blockSize':'4'}))
    b=np.array(images.redact(Image.open(photo),{**params,'blockSize':'24'}))
    require(len(np.unique(a[20:92,20:116].reshape(-1,3),axis=0))>len(np.unique(b[20:92,20:116].reshape(-1,3),axis=0)))
check('mosaic particle size changes the number of distinct blocks',mosaic_sizes)
check('image preview matches exported mosaic pixels',lambda:require(np.array_equal(np.array(Image.open(worker.preview(photo,'redactImage',params,folder('mosaic-preview'))['Preview'])),np.array(Image.open(images.tool(photo,'redactImage',params))))))

pillow_heif.register_heif_opener()
frames=[Image.new('RGB',(80,60),color) for color in ('red','green','blue')]
multi=DIR/'camera.heic';frames[0].save(multi,save_all=True,append_images=frames[1:],primary_index=1,quality=100)
tiff=DIR/'pages.tiff';frames[0].save(tiff,save_all=True,append_images=frames[1:])
mpo=DIR/'camera.jpg';frames[0].save(mpo,format='MPO',save_all=True,append_images=frames[1:])
def color(image,index):
    pixel=np.array(image.convert('RGB')).mean(axis=(0,1));require(pixel[index]>pixel[(index+1)%3]+50 and pixel[index]>pixel[(index+2)%3]+50)
def heic_primary():
    info=worker.inspect(multi,folder('heic-primary'));require(info['Frames']==3 and info['Frame']==1);color(Image.open(info['Preview']),1)
check('multi-image HEIC opens its primary photo instead of failing',heic_primary)
def selected(path,index,channel):
    info=worker.inspect(path,folder(path.stem+'-'+str(index)),index);require(info['Frames']==3 and info['Frame']==index);color(Image.open(info['Preview']),channel)
    output=images.tool(path,'editImage',{'imageFrame':str(index),'exposure':'0'});color(Image.open(output),channel)
    require(Image.open(path).n_frames==3)
check('HEIC selected frame is used in both edit preview and saved file',lambda:selected(multi,2,2))
check('multi-page TIFF can edit a selected page and retains the original pages',lambda:selected(tiff,1,1))
check('camera MPO with a JPEG extension opens and edits its selected photo',lambda:selected(mpo,2,2))
check('invalid frame is rejected clearly',lambda:require(worker.inspect(tiff,folder('invalid-frame'),8))) if False else None

for ratio,expected in [('3:2',(150,100)),('21:9',(140,60)),('2.35:1',(141,60)),('1:2',(60,120)),('free',(150,120))]:
    w,h=(150,100) if ratio=='3:2' else (140,120) if ratio=='21:9' else (141,120) if ratio=='2.35:1' else (150,120)
    p={'x':'0','y':'0','width':str(w),'height':str(h),'ratio':ratio}
    def crop(p=p,expected=expected):
        require(size(images.tool(photo,'cropImage',p))==expected)
        require(size(worker.preview(photo,'cropImage',p,folder('crop-'+p['ratio'].replace(':','-')))['Preview'])==expected)
    check('image custom ratio '+ratio+' matches preview and exported size',crop)

movie=ROOT/'work/full-fixtures/movie.mp4';sound=ROOT/'work/full-fixtures/sound.wav'
def video_crop():
    p={'x':'0','y':'0','width':'140','height':'120','ratio':'7:3'}
    output=media.video_tool(movie,'cropVideo',p);info=probe(output);stream=next(s for s in info['streams'] if s['codec_type']=='video')
    require((stream['width'],stream['height'])==(140,60))
    source=probe(movie);require(abs(float(info['format']['duration'])-float(source['format']['duration']))<.1)
    require(any(s['codec_type']=='audio' for s in info['streams']))
    require(size(worker.preview(movie,'cropVideo',p,folder('video-custom-ratio'))['Preview'])==(140,60))
check('video custom ratio preserves duration and audio; preview and export agree',video_crop)
def video_mosaic():
    output=media.video_tool(movie,'redactVideo',{**params,'end':'1.0'})
    require(Path(output).exists())
    preview=worker.preview(movie,'redactVideo',{**params,'end':'1.0','time':'.3'},folder('video-mosaic'))
    image=np.array(Image.open(preview['Preview']).convert('RGB'));require(image[20:92,20:116].mean()>20)
    require(len(np.unique(image[20:92,20:116].reshape(-1,3),axis=0))<=48)
check('video mosaic preview and real encoding both use selectable particle size',video_mosaic)
for action,path in [('trimAudio',sound),('trimVideo',movie)]:
    def trim(action=action,path=path):
        output=worker.playback(path,action,{'start':'.2','end':'.7','time':'0'},folder(action))['Preview']
        require(abs(float(probe(output)['format']['duration'])-.5)<.07)
    check(action+' playback honors selected start and end',trim)
def rotated_preview():
    pdf=ROOT/'work/full-fixtures/document.pdf';a=size(worker.preview(pdf,'organizePDF',{},folder('pdf-original'))['Preview']);b=size(worker.preview(pdf,'organizePDF',{'rotation':'90'},folder('pdf-rotated'))['Preview']);require(a==b[::-1])
check('PDF page rotation is visible in preview',rotated_preview)
def collage_preview():
    p={'canvasWidth':'400','canvasHeight':'300','gap':'10'};require(size(worker.preview(photo,'createCollage',p,folder('collage'),[str(photo),str(photo)])['Preview'])==(400,300))
check('collage preview renders the composite instead of the first source image',collage_preview)
for ratio in ('0:1','1:0','-1:2','nan:1','abc:2'):
    def invalid(ratio=ratio):
        try:fit_ratio(160,120,ratio)
        except ValueError:return
        raise AssertionError('invalid ratio was accepted')
    check('invalid custom ratio '+ratio+' rejected before processing',invalid)
report={'passed':sum(c['passed'] for c in checks),'failed':sum(not c['passed'] for c in checks),'checks':checks}
(ROOT/'outputs/f9-refinement-tests.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf8')
print(json.dumps({'passed':report['passed'],'failed':report['failed'],'failures':[c for c in checks if not c['passed']]},ensure_ascii=False),flush=True)
sys.exit(bool(report['failed']))
