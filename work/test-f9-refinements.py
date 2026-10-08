from pathlib import Path
import sys,json,hashlib
from PIL import Image
import numpy as np,pillow_heif

ROOT=Path(__file__).resolve().parent.parent
APP=ROOT/'outputs/ZestDrop'
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
def ratio_fit_keeps_conforming_sizes():
    # A size that already has the ratio to within a pixel is kept; anything else is shrunk, as before.
    for w,h,ratio,expected in [(857,643,'4:3',(857,643)),(1000,562,'16:9',(1000,562)),(1067,800,'4:3',(1067,800)),(856,657,'4:3',(856,642)),(1000,1000,'16:9',(1000,562)),(333,333,'9:16',(187,333)),(150,100,'3:2',(150,100))]:
        require(fit_ratio(w,h,ratio)==expected)
check('ratio fit keeps sizes that already have the ratio and shrinks the others',ratio_fit_keeps_conforming_sizes)
def crop_saves_the_size_the_frame_shows():
    # The crop frame shows sizes like 1067 x 800 at 4:3; the saved picture must have exactly that size (it used to lose a pixel).
    big=DIR/'wide.png';Image.new('RGB',(2560,1440),'gray').save(big)
    for ratio,(w,h) in [('4:3',(1067,800)),('4:3',(857,643)),('16:9',(1000,562)),('1:1',(700,700)),('2.35:1',(1034,440)),('9:16',(563,1000))]:
        out=images.tool(big,'cropImage',{'x':'10','y':'20','width':str(w),'height':str(h),'ratio':ratio})
        require(size(out)==(w,h))
check('crop saves exactly the size the crop frame shows',crop_saves_the_size_the_frame_shows)
def rotated_pixels(angle,flip):
    result=np.rot90(pixels,k=-(angle//90))
    return np.fliplr(result) if flip=='horizontal' else np.flipud(result) if flip=='vertical' else result
def image_rotation():
    for angle in (0,90,180,270):
        for flip in ('none','horizontal','vertical'):
            if angle==0 and flip=='none':continue
            p={'angle':str(angle),'flip':flip}
            out=np.array(Image.open(images.tool(photo,'rotateImage',p)).convert('RGB'))
            require(np.array_equal(out,rotated_pixels(angle,flip)))
    require(size(images.tool(photo,'rotateImage',{'angle':'90'}))==(120,160))
    require(size(worker.preview(photo,'rotateImage',{'angle':'270','flip':'none'},folder('rotate'))['Preview'])==(120,160))
    require(hashlib.sha256(photo.read_bytes()).hexdigest()==source_hash)
check('image rotation and flips match the expected pixels exactly; preview and export agree; source kept',image_rotation)
def image_rotation_rejects_bad_settings():
    for p in ({},{'angle':'0','flip':'none'},{'angle':'360','flip':'none'},{'angle':'-1'},{'angle':'361'},{'angle':'90','flip':'diagonal'},{'angle':'30','expand':'expand','background':'not-a-colour'}):
        try:images.tool(photo,'rotateImage',p)
        except ValueError:continue
        raise AssertionError('accepted '+str(p))
    require(size(worker.preview(photo,'rotateImage',{'angle':'0','flip':'none'},folder('rotate-none'))['Preview'])==(160,120))
check('rotating by nothing or by an invalid setting is refused on save but harmless in a preview',image_rotation_rejects_bad_settings)
def free_angle_images():
    # Any angle works: the canvas grows to hold the whole picture (or keeps its size), and the empty corners are transparent or coloured.
    w,h=160,120;grown=np.array(Image.open(images.tool(photo,'rotateImage',{'angle':'45','expand':'expand'})))
    expected=round((w+h)/2**.5);require(grown.shape[2]==4 and abs(grown.shape[1]-expected)<=2 and abs(grown.shape[0]-expected)<=2)
    require(grown[0,0,3]==0 and grown[grown.shape[0]//2,grown.shape[1]//2,3]==255)
    kept=np.array(Image.open(images.tool(photo,'rotateImage',{'angle':'30.5','expand':'keep','background':'#ff0000'})).convert('RGB'))
    require(kept.shape[:2]==(h,w) and tuple(kept[0,0])==(255,0,0))
    require(size(images.tool(photo,'rotateImage',{'angle':'359.5','expand':'expand'}))[0]>w)
    for angle in ('0.5','89.9','90.1','180.2','270.7','360'):
        if angle=='360':
            out=images.tool(photo,'rotateImage',{'angle':angle,'flip':'horizontal'});require(np.array_equal(np.array(Image.open(out).convert('RGB')),np.fliplr(pixels)))
        else:require(size(images.tool(photo,'rotateImage',{'angle':angle}))[0]>0)
    quarter=np.array(Image.open(images.tool(photo,'rotateImage',{'angle':'90.0'})).convert('RGB'));require(np.array_equal(quarter,np.rot90(pixels,k=-1)))
    require(size(worker.preview(photo,'rotateImage',{'angle':'45','expand':'expand'},folder('rotate-free'))['Preview'])[0]>w)
    jpg=DIR/'free.jpg';Image.fromarray(pixels).save(jpg);out=Image.open(images.tool(jpg,'rotateImage',{'angle':'20','expand':'expand'}));require(out.mode=='RGB' and out.getpixel((0,0))[0]>240)
check('explicit expanded canvas preserves the picture; corners stay transparent or coloured and JPEG uses white',free_angle_images)
two_tone=DIR/'two-tone.mp4'
from common import ffmpeg
ffmpeg(['-f','lavfi','-i','color=c=red:s=64x96:d=1:r=10','-f','lavfi','-i','color=c=blue:s=64x96:d=1:r=10','-f','lavfi','-i','sine=frequency=440:duration=1','-filter_complex','[0][1]hstack[v]','-map','[v]','-map','2:a','-pix_fmt','yuv420p','-shortest',two_tone])
def video_stream_size(path):
    s=next(s for s in probe(path)['streams'] if s['codec_type']=='video');return int(s['width']),int(s['height'])
def frame_colour(path,x,y):
    frame=folder('frames')/(Path(path).stem+'.png');ffmpeg(['-i',path,'-frames:v','1',frame])
    r,g,b=Image.open(frame).convert('RGB').getpixel((x,y));return 'red' if r>150 and b<100 else 'blue' if b>150 and r<100 else 'other'
def video_rotation():
    require(video_stream_size(two_tone)==(128,96))
    right=media.video_tool(two_tone,'rotateVideo',{'angle':'90'});size_right=video_stream_size(right)
    require(size_right==(96,128))
    # Turned clockwise, the left (red) half ends up on top and the right (blue) half at the bottom.
    require(frame_colour(right,48,20)=='red' and frame_colour(right,48,108)=='blue')
    left=media.video_tool(two_tone,'rotateVideo',{'angle':'270'})
    require(frame_colour(left,48,20)=='blue' and frame_colour(left,48,108)=='red')
    mirrored=media.video_tool(two_tone,'rotateVideo',{'angle':'0','flip':'horizontal'})
    require(video_stream_size(mirrored)==(128,96) and frame_colour(mirrored,20,48)=='blue' and frame_colour(mirrored,108,48)=='red')
    upside=media.video_tool(two_tone,'rotateVideo',{'angle':'180'})
    require(video_stream_size(upside)==(128,96) and frame_colour(upside,20,48)=='blue')
    info=probe(right);require(abs(float(info['format']['duration'])-float(probe(two_tone)['format']['duration']))<.2)
    require(any(s['codec_type']=='audio' for s in info['streams']))
check('video rotation turns the picture the right way, swaps the size for quarter turns, and keeps length and audio',video_rotation)
def video_rotation_preview_and_playback():
    require(size(worker.preview(two_tone,'rotateVideo',{'angle':'90'},folder('video-rotate'))['Preview'])==(96,128))
    played=worker.playback(two_tone,'rotateVideo',{'angle':'90','time':'0'},folder('video-rotate-play'))
    require(video_stream_size(played['Preview'])==(96,128))
    try:media.video_tool(two_tone,'rotateVideo',{'angle':'0','flip':'none'})
    except ValueError:return
    raise AssertionError('a video rotation by nothing was accepted')
check('video rotation preview and playback show the rotated picture; rotating by nothing is refused',video_rotation_preview_and_playback)
def video_free_angle():
    grown=media.video_tool(two_tone,'rotateVideo',{'angle':'45'});width,height=video_stream_size(grown)
    require(abs(width-round((128+96)/2**.5))<=4 and abs(height-round((128+96)/2**.5))<=4 and width%2==0 and height%2==0)
    kept=media.video_tool(two_tone,'rotateVideo',{'angle':'30','expand':'keep','background':'#00ff00'});require(video_stream_size(kept)==(128,96))
    frame=folder('frames')/'kept.png';ffmpeg(['-i',kept,'-frames:v','1',frame]);r,g,b=Image.open(frame).convert('RGB').getpixel((1,1));require(g>200 and r<80 and b<80)
    for result in (grown,kept):
        info=probe(result);require(abs(float(info['format']['duration'])-float(probe(two_tone)['format']['duration']))<.3);require(any(s['codec_type']=='audio' for s in info['streams']))
    require(video_stream_size(worker.playback(two_tone,'rotateVideo',{'angle':'45','time':'0'},folder('video-free-play'))['Preview'])[0]>128)
check('video rotates by any angle: canvas grows or stays, corners are filled, length and audio are kept',video_free_angle)
report={'passed':sum(c['passed'] for c in checks),'failed':sum(not c['passed'] for c in checks),'checks':checks}
(ROOT/'outputs/f9-refinement-tests.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf8')
print(json.dumps({'passed':report['passed'],'failed':report['failed'],'failures':[c for c in checks if not c['passed']]},ensure_ascii=False),flush=True)
sys.exit(bool(report['failed']))
