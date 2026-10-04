from pathlib import Path
from contextlib import contextmanager
import io, json, os, re, shutil, subprocess, tempfile, uuid

BASE=Path(__file__).resolve().parent.parent
FFMPEG=BASE/'runtime/ffmpeg/ffmpeg.exe'
FFPROBE=BASE/'runtime/ffmpeg/ffprobe.exe'
IMAGE_EXT={'.jpg','.jpeg','.png','.webp','.heic','.heif','.tif','.tiff','.svg','.avif','.bmp'}
VIDEO_EXT={'.mp4','.mov','.mkv','.webm','.avi','.wmv','.gif'}
AUDIO_EXT={'.mp3','.m4a','.wav','.flac','.ogg','.opus','.aiff','.aif','.wma'}
FONT=next((Path(x) for x in ['C:/Windows/Fonts/msyh.ttc','C:/Windows/Fonts/arial.ttf','C:/Windows/Fonts/simsun.ttc'] if Path(x).exists()),None)

def category(path):
    p=Path(path); ext=p.suffix.lower()
    if ext in IMAGE_EXT: return 'image'
    if ext in VIDEO_EXT: return 'video'
    if ext in AUDIO_EXT: return 'audio'
    if ext in {'.ppt','.pptx','.pptm','.pps','.ppsx','.odp','.doc','.docx','.docm','.rtf','.odt','.xls','.xlsx','.xlsm','.xlsb','.ods','.csv','.tsv'}:return 'office'
    if ext in {'.pdf','.txt','.srt','.vtt'}: return 'document'
    return 'archive'

def number(params,name,default=0):
    value=params.get(name,default)
    if value in ('',None): return float(default)
    if ':' in str(value):
        parts=str(value).split(':'); return sum(float(x)*60**i for i,x in enumerate(reversed(parts)))
    return float(value)

def truth(value): return str(value).lower() in ('true','1','yes','on')

def fit_ratio(width,height,ratio):
    if ratio in ('','free',None):return width,height
    import math
    try:
        a,b=map(float,str(ratio).split(':'))
        if not math.isfinite(a) or not math.isfinite(b) or min(a,b)<=0:raise ValueError()
    except (ValueError,TypeError):raise ValueError('裁剪比例的宽、高必须是大于 0 的数字') from None
    if width/height>a/b:width=int(height*a/b)
    else:height=int(width*b/a)
    if min(width,height)<1:raise ValueError('当前选区无法容纳此比例，请扩大选区或调整比例')
    return width,height

def process(args,timeout=1800):
    result=subprocess.run([str(x) for x in args],input='',capture_output=True,text=True,encoding='utf8',errors='replace',timeout=timeout,creationflags=0x08000000 if os.name=='nt' else 0)
    if result.returncode:
        message='\n'.join(result.stderr.strip().splitlines()[-8:]) or result.stdout.strip()[-1200:]
        raise RuntimeError(message or f'处理引擎退出码 {result.returncode}')
    return result.stdout

def ffmpeg(args): return process([FFMPEG,'-hide_banner','-loglevel','error','-nostdin','-y',*args])
def probe(path): return json.loads(process([FFPROBE,'-v','error','-show_format','-show_streams','-show_chapters','-of','json',path]))
def video_stream(info): return next(x for x in info['streams'] if x['codec_type']=='video')
def audio_stream(info): return next(x for x in info['streams'] if x['codec_type']=='audio')
def duration(info): return float(info['format'].get('duration') or next((x.get('duration') for x in info['streams'] if x.get('duration')),0))

def output_stem(path,suffix):
    name=Path(path).name
    name=re.sub(r'(?i)(\.tar\.gz|\.tgz|\.[^.]+)$','',name)
    return name+'-'+suffix

def commit(temp,path,suffix,extension):
    parent=Path(path).resolve().parent; stem=output_stem(path,suffix)
    for i in range(10000):
        target=parent/(stem+(f'-{i}' if i else '')+extension)
        try:
            # On Windows rename fails if the target exists; never replace user files.
            if target.exists(): continue
            os.rename(temp,target)
            return str(target)
        except FileExistsError: continue
    raise OSError('无法分配输出文件名')

@contextmanager
def output_file(path,suffix,extension):
    temp=Path(path).resolve().parent/('.zestdrop-'+uuid.uuid4().hex+extension)
    state={'temp':temp,'output':None}
    try:
        yield state
        if not temp.is_file() or temp.stat().st_size==0: raise RuntimeError('引擎未生成有效输出')
        state['output']=commit(temp,path,suffix,extension)
    finally:
        if temp.exists(): temp.unlink()

@contextmanager
def output_folder(path,suffix,allow_empty=False):
    temp=Path(tempfile.mkdtemp(prefix='.zestdrop-',dir=Path(path).resolve().parent))
    state={'temp':temp,'output':None}
    try:
        yield state
        if not allow_empty and not any(temp.iterdir()): raise RuntimeError('没有生成输出内容')
        state['output']=commit(temp,path,suffix,'')
    finally:
        if temp.exists(): shutil.rmtree(temp)

def open_image(path,frame=None):
    from PIL import Image,ImageOps,ImageCms
    import pillow_heif
    pillow_heif.register_heif_opener()
    if Path(path).suffix.lower()=='.svg':
        import resvg_py
        image=Image.open(io.BytesIO(resvg_py.svg_to_bytes(svg_path=str(path))))
    else: image=Image.open(path)
    frames=getattr(image,'n_frames',1)
    if frame is not None and int(frame)>=0:
        if int(frame)>=frames: raise ValueError('所选图像帧不存在')
        image.seek(int(frame))
    selected=image.tell()
    if image.width*image.height>80_000_000: raise ValueError('图片超过 8000 万像素上限')
    image=ImageOps.exif_transpose(image)
    profile=image.info.get('icc_profile')
    if profile:
        try:
            image=ImageCms.profileToProfile(image,ImageCms.ImageCmsProfile(io.BytesIO(profile)),ImageCms.createProfile('sRGB'),outputMode='RGBA' if 'A' in image.getbands() else 'RGB')
        except (ValueError,OSError): pass
    image.load(); result=image.copy(); image.close()
    result.info['source_frames']=frames; result.info['source_frame']=selected
    return result

def save_image(image,path,fmt,quality=90,metadata=None):
    from PIL import Image
    fmt=fmt.lower(); mode={'jpg':'JPEG','jpeg':'JPEG','png':'PNG','webp':'WEBP','heic':'HEIF','tiff':'TIFF','tif':'TIFF','avif':'AVIF','bmp':'BMP'}[fmt]
    if mode in ('JPEG','BMP'):
        if 'A' in image.getbands():
            background=Image.new('RGB',image.size,'white'); background.paste(image,mask=image.getchannel('A')); image=background
        else: image=image.convert('RGB')
    options={'quality':int(quality)} if mode in ('JPEG','WEBP','HEIF','AVIF') else {}
    if mode=='PNG': options={'optimize':True}
    if mode=='TIFF': options={'compression':'tiff_deflate'}
    if metadata:
        if mode in ('JPEG','WEBP','PNG','HEIF','TIFF','AVIF'): options['exif']=metadata.tobytes()
    image.save(path,format=mode,**options)

def rects(params,width,height):
    raw=params.get('regions','').strip()
    if raw:
        result=json.loads(raw)
        if not isinstance(result,list): raise ValueError('区域应为 JSON 数组')
    else:
        result=[{'x':number(params,'x',0),'y':number(params,'y',0),'width':number(params,'width',width/2),'height':number(params,'height',height/2),'start':number(params,'start',0),'end':number(params,'end',0),'style':params.get('style','solid')}]
    for r in result:
        r['x']=max(0,int(r.get('x',0))); r['y']=max(0,int(r.get('y',0)))
        r['width']=min(int(r.get('width',width/2)),width-r['x']); r['height']=min(int(r.get('height',height/2)),height-r['y'])
        if r['width']<=0 or r['height']<=0: raise ValueError('区域超出画面范围')
    return result
