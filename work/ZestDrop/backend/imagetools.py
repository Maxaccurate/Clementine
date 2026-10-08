from pathlib import Path
import tempfile
from PIL import Image,ImageOps,ImageFont,ImageDraw,ImageColor
from common import *

def resize(image,params):
    width,height=image.size; mode=params.get('mode','percent')
    if mode=='percent': factor=number(params,'percent',50)/100; new=(round(width*factor),round(height*factor))
    elif mode=='edge': factor=number(params,'edge',1920)/max(width,height); new=(round(width*factor),round(height*factor))
    else:
        target_w=int(number(params,'width',0)); target_h=int(number(params,'height',0))
        if target_w and target_h:
            if truth(params.get('keepRatio','true')): factor=min(target_w/width,target_h/height); new=(round(width*factor),round(height*factor))
            else: new=(target_w,target_h)
        elif target_w: new=(target_w,round(height*target_w/width))
        elif target_h: new=(round(width*target_h/height),target_h)
        else: raise ValueError(T('请输入目标宽度或高度'))
    new=(max(1,new[0]),max(1,new[1]))
    if new[0]>60000 or new[1]>60000 or new[0]*new[1]>80_000_000: raise ValueError(T('目标尺寸无效或过大'))
    if image.mode in ('P','1'): image=image.convert('RGBA' if 'transparency' in image.info else 'RGB')
    return image.resize(new,Image.Resampling.LANCZOS)

POSITIONS={'topLeft':(0,0),'topCenter':(.5,0),'topRight':(1,0),'middleLeft':(0,.5),'center':(.5,.5),'middleRight':(1,.5),'bottomLeft':(0,1),'bottomCenter':(.5,1),'bottomRight':(1,1)}

def watermark_mark(size,params):
    """The watermark (a logo, text, or both stacked) as a translucent RGBA picture sized for a target of this many pixels."""
    width,height=size; text=params.get('text','').strip(); logo=params.get('logo','').strip(); parts=[]
    if not text and not logo: raise ValueError(T('请输入水印文字或选择水印图片'))
    if logo:
        mark=open_image(logo).convert('RGBA'); w=max(1,int(width*number(params,'scale',20)/100))
        parts.append(mark.resize((w,max(1,round(mark.height*w/mark.width))),Image.Resampling.LANCZOS))
    if text:
        font=ImageFont.truetype(str(FONT),max(8,int(min(width,height)*number(params,'textSize',5)/100))); box=font.getbbox(text)
        layer=Image.new('RGBA',(box[2]+12,box[3]+12),(0,0,0,0)); draw=ImageDraw.Draw(layer)
        try: colour=ImageColor.getrgb(params.get('color') or '#ffffff')[:3]
        except ValueError: raise ValueError(T('空白处颜色无效')) from None
        draw.text((7,7),text,font=font,fill=(0,0,0,110)); draw.text((6,6),text,font=font,fill=(*colour,255)); parts.append(layer)
    gap=6; mark=Image.new('RGBA',(max(p.width for p in parts),sum(p.height for p in parts)+gap*(len(parts)-1)),(0,0,0,0)); top=0
    for part in parts: mark.alpha_composite(part,((mark.width-part.width)//2,top)); top+=part.height+gap
    angle=number(params,'rotation',0)
    if angle%360: mark=mark.rotate(angle,Image.Resampling.BICUBIC,expand=True)
    opacity=min(100,max(0,number(params,'opacity',60)))/100
    mark.putalpha(mark.getchannel('A').point(lambda a:int(a*opacity))); return mark

def watermark_layer(size,params):
    """A transparent picture of the target's size with the watermark placed on it, or repeated across it."""
    mark=watermark_mark(size,params)
    if mark.width>size[0] or mark.height>size[1]: mark=ImageOps.contain(mark,size,Image.Resampling.LANCZOS)
    layer=Image.new('RGBA',size,(0,0,0,0))
    if params.get('layout','single')=='tiled':
        for row,y in enumerate(range(0,size[1],max(1,mark.height*3))):
            for x in range(-(row%2)*mark.width,size[0],max(1,mark.width*2)):
                layer.paste(mark,(x,y),mark)
        return layer
    margin=int(min(size)*number(params,'margin',3)/100); fx,fy=POSITIONS.get(params.get('position','bottomRight'),(1,1))
    layer.paste(mark,(max(0,int(margin+(size[0]-mark.width-2*margin)*fx)),max(0,int(margin+(size[1]-mark.height-2*margin)*fy))),mark)
    return layer

def make_icon(path,params):
    image=open_image(path).convert('RGBA'); factor=256/max(image.size)
    if factor>1: image=image.resize((round(image.width*factor),round(image.height*factor)),Image.Resampling.LANCZOS)
    side=max(image.size); square=Image.new('RGBA',(side,side),(0,0,0,0)); square.paste(image,((side-image.width)//2,(side-image.height)//2))
    if params.get('iconKind','ico')=='ico':
        with output_file(path,'makeIcon','.ico') as state: square.save(state['temp'],format='ICO',sizes=[(s,s) for s in (16,24,32,48,64,128,256)])
        return state['output']
    with output_folder(path,'makeIcon') as state:
        for s in (16,32,48,64,192,512): square.resize((s,s),Image.Resampling.LANCZOS).save(state['temp']/f'icon-{s}.png')
        square.resize((180,180),Image.Resampling.LANCZOS).save(state['temp']/'apple-touch-icon.png')
        square.save(state['temp']/'favicon.ico',format='ICO',sizes=[(16,16),(32,32),(48,48)])
        (state['temp']/'favicon-snippet.html').write_text('<link rel="icon" href="/favicon.ico" sizes="any">\n<link rel="icon" type="image/png" sizes="32x32" href="/icon-32.png">\n<link rel="apple-touch-icon" href="/apple-touch-icon.png">\n',encoding='utf8')
    return state['output']

def animation(paths,params):
    """Several pictures, one after another, as a GIF or an MP4 slideshow."""
    fmt=params.get('format','gif'); seconds=number(params,'seconds',1); edge=int(number(params,'maxEdge',720))
    if not .1<=seconds<=30: raise ValueError(T('每张停留时间需在 0.1 到 30 秒之间'))
    if not 64<=edge<=3840: raise ValueError(T('最长边需在 64 到 3840 像素之间'))
    first=open_image(paths[0]); factor=min(1,edge/max(first.size)); size=(max(2,round(first.width*factor)//2*2),max(2,round(first.height*factor)//2*2))
    with tempfile.TemporaryDirectory(prefix='zestdrop-animation-') as temp:
        for index,path in enumerate(paths):
            frame=open_image(path).convert('RGBA'); background=Image.new('RGBA',frame.size,'white'); background.alpha_composite(frame)
            ImageOps.pad(background.convert('RGB'),size,color='white').save(Path(temp)/f'frame-{index:04d}.png')
        pattern=str(Path(temp)/'frame-%04d.png'); rate=f'1/{seconds}'
        with output_file(paths[0],'createAnimation','.mp4' if fmt=='mp4' else '.gif') as state:
            if fmt=='mp4': ffmpeg(['-framerate',rate,'-i',pattern,'-r','30','-c:v','libx264','-pix_fmt','yuv420p','-crf','20','-movflags','+faststart',state['temp']])
            else: ffmpeg(['-framerate',rate,'-i',pattern,'-filter_complex','[0:v]split[a][b];[a]palettegen[p];[b][p]paletteuse','-loop','0' if truth(params.get('loop','true')) else '-1',state['temp']])
    return state['output']
