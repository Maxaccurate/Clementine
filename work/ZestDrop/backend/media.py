from pathlib import Path
import json
from common import *

AUDIO_CODECS={
    'mp3':['-c:a','libmp3lame','-b:a','192k'], 'm4a':['-c:a','aac','-b:a','160k'],
    'wav':['-c:a','pcm_s16le'], 'flac':['-c:a','flac','-compression_level','8'],
    'ogg':['-c:a','libvorbis','-q:a','4'], 'opus':['-c:a','libopus','-b:a','128k'],
    'aiff':['-c:a','pcm_s16be'], 'wma':['-c:a','wmav2','-b:a','160k']}
VIDEO_CODECS={
    'mp4':['-c:v','libx264','-preset','medium','-crf','23','-c:a','aac','-b:a','160k','-movflags','+faststart'],
    'mov':['-c:v','libx264','-preset','medium','-crf','23','-c:a','aac','-b:a','160k'],
    'mkv':['-c:v','libx264','-preset','medium','-crf','23','-c:a','aac','-b:a','160k'],
    'webm':['-c:v','libvpx-vp9','-crf','32','-b:v','0','-c:a','libopus','-b:a','128k'],
    'avi':['-c:v','mpeg4','-q:v','4','-c:a','libmp3lame','-b:a','160k'],
    'wmv':['-c:v','wmv2','-b:v','1800k','-c:a','wmav2','-b:a','128k']}

def video_args(fmt): return [*VIDEO_CODECS[fmt],'-pix_fmt','yuv420p']
def audio_args(fmt,params):
    result=list(AUDIO_CODECS[fmt]); bitrate=params.get('bitrate','')
    if bitrate and fmt not in ('wav','flac','aiff'):
        result=['-c:a',result[1],'-b:a',f'{int(float(bitrate))}k']
    return result

def convert(path,fmt,params):
    with output_file(path,'converted','.'+fmt) as state:
        if fmt in AUDIO_CODECS: ffmpeg(['-i',path,'-map','0:a:0','-vn',*audio_args(fmt,params),state['temp']])
        elif fmt=='gif':
            graph=r'[0:v]fps=12,scale=min(640\,iw):-2:flags=lanczos,split[a][b];[a]palettegen[p];[b][p]paletteuse[out]'
            ffmpeg(['-i',path,'-filter_complex',graph,'-map','[out]','-an',state['temp']])
        else: ffmpeg(['-i',path,'-map','0:v:0','-map','0:a?','-vf','pad=ceil(iw/2)*2:ceil(ih/2)*2',*video_args(fmt),state['temp']])
    return state['output']

def tempo(speed):
    if not .125<=speed<=8: raise ValueError(T('速度范围为 0.125 到 8 倍'))
    factors=[]
    while speed>2: factors.append('atempo=2'); speed/=2
    while speed<.5: factors.append('atempo=0.5'); speed/=.5
    return ','.join([*factors,f'atempo={speed}'])

# Shared with the playback preview (worker.py), so what is heard there is what gets saved.
SILENCE_TRIM=['silenceremove=start_periods=1:start_threshold=-40dB','areverse','silenceremove=start_periods=1:start_threshold=-40dB','areverse']
def loudnorm(params): return f"loudnorm=I={number(params,'loudness',-16)}:LRA={number(params,'range',11)}:TP={number(params,'peak',-1.5)}"
def channel_filters(params):
    left=number(params,'leftGain',1); right=number(params,'rightGain',1)
    return ['aformat=channel_layouts=stereo',f'pan=mono|c0={left*.5}*c0+{right*.5}*c1' if params.get('channels','mono')=='mono' else f'pan=stereo|c0={left}*c0|c1={right}*c1']
def beep_graph(ranges,length,prepare=''):
    """Silences the given (start, end) ranges and plays a soft 1 kHz beep over them."""
    expression='+'.join(f'between(t,{a},{b})' for a,b in ranges)
    return f"[0:a]{prepare}aformat=sample_rates=48000:channel_layouts=stereo,volume=0:enable='{expression}'[speech];sine=frequency=1000:sample_rate=48000:duration={length},aformat=channel_layouts=stereo,volume='if({expression},0.15,0)':eval=frame[beep];[speech][beep]amix=inputs=2:normalize=0[a]"

def metadata_args(params):
    args=[]
    if truth(params.get('remove','true')): return ['-map_metadata','-1','-map_metadata:s','-1','-map_metadata:c','-1']
    values=json.loads(params.get('metadata','{}'))
    if any(k in values for k in ('format','streams','chapters')):
        for key,val in values.get('format',{}).items(): args+=['-metadata',f'{key}={val or ""}']
        for index,tags in values.get('streams',{}).items():
            for key,val in tags.items(): args+=['-metadata:s:'+str(int(index)),f'{key}={val or ""}']
        for index,tags in values.get('chapters',{}).items():
            for key,val in tags.items(): args+=['-metadata:c:'+str(int(index)),f'{key}={val or ""}']
    else:
        for key,val in values.items(): args+=['-metadata',f'{key}={val or ""}']
    return args

def rotation_filters(params,required=True):
    angle,flip=orientation(params,required)
    if angle%90==0: turn={0:[],90:['transpose=1'],180:['hflip','vflip'],270:['transpose=2']}[int(angle)]
    else:
        radians=f'{angle}*PI/180'; red,green,blue=fill_colour(params,'#000000') or (0,0,0)
        size=('iw','ih') if params.get('expand','expand')=='keep' else (f'rotw({radians})',f'roth({radians})')
        turn=[f'rotate={radians}:ow={size[0]}:oh={size[1]}:c=0x{red:02x}{green:02x}{blue:02x}']
    return [*turn,*{'none':[],'horizontal':['hflip'],'vertical':['vflip']}[flip]]

def redact_graph(info,params):
    stream=video_stream(info); regions=rects(params,int(stream['width']),int(stream['height'])); graph=[]; current='0:v'
    for i,r in enumerate(regions):
        start=float(r.get('start',0)); end=float(r.get('end',0)) or duration(info)
        if start<0 or end<=start: raise ValueError(T('打码时间范围无效'))
        enabled=f"between(t,{start},{end})"; x,y,w,h=(r[k] for k in ('x','y','width','height'))
        output=f'redacted{i}'; style=r.get('style',params.get('style','solid'))
        if style in ('blur','pixelate'):
            block=int(float(r.get('blockSize',params.get('blockSize',18))))
            if not 2<=block<=100: raise ValueError(T('马赛克颗粒需在 2–100 像素范围内'))
            effect=r'boxblur=luma_radius=min(w\,h)/8:luma_power=2:chroma_radius=min(cw\,ch)/8:chroma_power=2' if style=='blur' else f'scale={max(1,w//block)}:{max(1,h//block)}:flags=area,scale={w}:{h}:flags=neighbor'
            graph += [f'[{current}]split[keep{i}][crop{i}]',f'[crop{i}]crop={w}:{h}:{x}:{y},{effect}[cover{i}]',f"[keep{i}][cover{i}]overlay={x}:{y}:enable='{enabled}'[{output}]"]
        else: graph += [f"[{current}]drawbox=x={x}:y={y}:w={w}:h={h}:color=black:t=fill:enable='{enabled}'[{output}]"]
        current=output
    graph += [f'[{current}]pad=ceil(iw/2)*2:ceil(ih/2)*2[outv]']
    return ';'.join(graph)

def split_video(path,params):
    info=probe(path); total=duration(info)
    raw=params.get('splitPoints','').strip()
    points=sorted(set(float(x.strip()) for x in raw.split(','))) if raw else [total*i/int(number(params,'parts',2)) for i in range(1,int(number(params,'parts',2)))]
    if any(x<=0 or x>=total for x in points): raise ValueError(T('分割点必须在视频时间范围内'))
    boundaries=[0,*points,total]
    with output_folder(path,'clips') as state:
        for i,(start,end) in enumerate(zip(boundaries,boundaries[1:])):
            ffmpeg(['-ss',start,'-i',path,'-t',end-start,'-vf','pad=ceil(iw/2)*2:ceil(ih/2)*2',*video_args('mp4'),state['temp']/f'clip-{i+1:04d}.mp4'])
    return state['output']

def snapshots(path,params):
    info=probe(path); rate=video_stream(info).get('avg_frame_rate','30/1'); a,b=map(float,rate.split('/')); fps=a/b if b and a else 30
    times=params.get('times','').strip()
    values=[float(x.strip()) for x in times.split(',')] if times else [number(params,'time',0)+number(params,'frame',0)/fps]
    if any(x<0 or x>=duration(info) for x in values): raise ValueError(T('截图时间超出视频范围'))
    if len(values)==1:
        with output_file(path,'snapshot','.png') as state: ffmpeg(['-ss',values[0],'-i',path,'-frames:v','1',state['temp']])
    else:
        with output_folder(path,'snapshots') as state:
            for i,t in enumerate(values): ffmpeg(['-ss',t,'-i',path,'-frames:v','1',state['temp']/f'frame-{i+1:04d}.png'])
    return state['output']

def join(paths,params):
    infos=[probe(path) for path in paths]; first=video_stream(infos[0]); w=int(first['width']); h=int(first['height']); w+=w%2; h+=h%2
    args=[]; graph=[]; inputs=[]
    for i,(path,info) in enumerate(zip(paths,infos)):
        args+=['-i',path]
        graph.append(f'[{i}:v]scale={w}:{h}:force_original_aspect_ratio=decrease,pad={w}:{h}:(ow-iw)/2:(oh-ih)/2,setsar=1,fps=30,setpts=PTS-STARTPTS[v{i}]')
        if has_audio(info): graph.append(f'[{i}:a]aformat=sample_rates=48000:channel_layouts=stereo,apad,atrim=duration={duration(info)},asetpts=PTS-STARTPTS[a{i}]')
        else: graph.append(f'anullsrc=channel_layout=stereo:sample_rate=48000,atrim=duration={duration(info)}[a{i}]')
        inputs.append(f'[v{i}][a{i}]')
    graph.append(''.join(inputs)+f'concat=n={len(paths)}:v=1:a=1[outv][outa]')
    with output_file(paths[0],'joined','.mp4') as state:
        ffmpeg([*args,'-filter_complex',';'.join(graph),'-map','[outv]','-map','[outa]',*video_args('mp4'),state['temp']])
    return state['output']

def video_tool(path,action,params):
    import mediatools
    extra={'videoToGif':mediatools.video_to_gif,'videoSettings':mediatools.video_settings,'videoEffects':mediatools.video_effects,'subtitlesAudio':mediatools.subtitles_audio,'watermark':mediatools.video_watermark}
    if action in extra: return extra[action](path,params)
    if action=='videoSnapshots' and truth(params.get('sheet','false')): return mediatools.contact_sheet(path,params)
    if action=='splitVideo': return split_video(path,params)
    if action=='videoSnapshots': return snapshots(path,params)
    info=probe(path); fmt=Path(path).suffix.lstrip('.').lower()
    if action=='removeMetadata':
        with output_file(path,'metadata','.'+fmt) as state: ffmpeg(['-i',path,'-map','0','-c','copy',*metadata_args(params),state['temp']])
        return state['output']
    fmt=fmt if fmt in VIDEO_CODECS else 'mp4'; args=['-i',path]; vf=[]; af=[]; tail=[]; codec=video_args(fmt)
    if action=='muteVideo':
        tail=['-an']; codec=['-c:v','copy']
    elif action=='trimVideo':
        start=number(params,'start',0); end=number(params,'end',0) or duration(info)
        if start<0 or end<=start or end>duration(info)+.1: raise ValueError(T('起止时间超出视频范围'))
        args=['-ss',start,'-i',path,'-t',end-start]
    elif action=='cropVideo':
        stream=video_stream(info); x=int(number(params,'x',0)); y=int(number(params,'y',0)); w=int(number(params,'width',int(stream['width'])-x)); h=int(number(params,'height',int(stream['height'])-y))
        ratio=params.get('ratio','free')
        if w>0 and h>0:w,h=fit_ratio(w,h,ratio)
        if min(w,h)<=0 or x<0 or y<0 or x+w>int(stream['width']) or y+h>int(stream['height']): raise ValueError(T('裁剪区域超出视频画面'))
        vf.append(f'crop={w}:{h}:{x}:{y}')
    elif action=='rotateVideo': vf+=rotation_filters(params)
    elif action=='changeVideoSpeed':
        speed=number(params,'speed',2); vf.append(f'setpts=PTS/{speed}'); af.append(tempo(speed))
    elif action=='compress':
        edge=int(number(params,'maxEdge',1280)); quality=int(number(params,'quality',28)); target=number(params,'targetKB',0)
        vf.append(fr'scale=min({edge}\,iw):-2')
        if fmt in ('mp4','mov','mkv'): codec=['-c:v','libx264','-preset','medium','-crf',str(quality),'-c:a','aac','-b:a','128k','-pix_fmt','yuv420p']
        if target:
            total=target*8192/max(.1,duration(info))/1000
            audio_bitrate=max(16,min(128,int(total*.2))) if has_audio(info) else 0
            bitrate=max(4,int(total*.88-audio_bitrate-8))
            codec=['-c:v','libx264','-b:v',f'{bitrate}k','-maxrate',f'{bitrate}k','-bufsize',f'{bitrate*2}k','-c:a','aac','-b:a',f'{max(8,audio_bitrate)}k','-pix_fmt','yuv420p']; fmt='mp4'
    elif action=='redactVideo':
        args+=['-filter_complex',redact_graph(info,params),'-map','[outv]','-map','0:a?']
    else: raise ValueError(T('未知视频工具'))
    if action not in ('muteVideo','redactVideo'): vf.append('pad=ceil(iw/2)*2:ceil(ih/2)*2')
    if vf: args+=['-vf',','.join(vf)]
    if af and has_audio(info): args+=['-af',','.join(af)]
    with output_file(path,action,'.'+fmt) as state:
        ffmpeg([*args,*codec,*tail,state['temp']])
        target=int(number(params,'targetKB',0)*1024) if action=='compress' else 0
        if target:
            for attempt in range(4):
                if state['temp'].stat().st_size<=target:break
                bitrate=max(4,int(bitrate*target/state['temp'].stat().st_size*.85));audio_bitrate=max(8,int(audio_bitrate*.8))
                codec=['-c:v','libx264','-b:v',f'{bitrate}k','-maxrate',f'{bitrate}k','-bufsize',f'{bitrate*2}k','-c:a','aac','-b:a',f'{audio_bitrate}k','-pix_fmt','yuv420p'];ffmpeg([*args,*codec,*tail,state['temp']])
            if state['temp'].stat().st_size>target:raise ValueError(T('当前视频无法达到该目标大小，请增大目标或缩小分辨率'))
    return state['output']

def audio_tool(path,action,params):
    import mediatools
    if action=='audioEffects': return mediatools.audio_effects(path,params)
    if action=='ringtone': return mediatools.ringtone(path,params)
    info=probe(path); fmt=Path(path).suffix.lstrip('.').lower(); fmt='aiff' if fmt=='aif' else fmt
    if action=='removeMetadata':
        with output_file(path,'metadata','.'+fmt) as state: ffmpeg(['-i',path,'-map','0:a:0','-c','copy',*metadata_args(params),state['temp']])
        return state['output']
    if action=='audioToVideo':
        aspect=params.get('aspect','landscape'); w,h={'landscape':(1280,720),'portrait':(720,1280),'square':(1080,1080)}[aspect]
        background=params.get('backgroundImage','').strip()
        args=['-i',path]
        if background: args=['-loop','1','-i',background,'-i',path,'-map','0:v:0','-map','1:a:0','-vf',f'scale={w}:{h}:force_original_aspect_ratio=decrease,pad={w}:{h}:(ow-iw)/2:(oh-ih)/2']
        else: args+=['-filter_complex',f'[0:a]showwaves=s={w}x{h}:mode=line:colors=0xff6500:r=30,format=yuv420p[v]','-map','[v]','-map','0:a:0']
        with output_file(path,'visualizer','.mp4') as state: ffmpeg([*args,'-shortest',*video_args('mp4'),state['temp']])
        return state['output']
    args=['-i',path]; filters=[]
    if action=='compress':
        if fmt=='wav': fmt='flac'
        params={**params,'bitrate':params.get('bitrate','96')}
        target=number(params,'targetKB',0)
        if target and fmt not in ('flac','aiff','wav'):
            bitrate=max(32,int(target*8192/max(.1,duration(info))/1000*.85));params['bitrate']=str(min(int(params['bitrate']),bitrate))
    elif action=='normalizeAudio': filters.append(loudnorm(params))
    elif action=='trimAudio':
        start=number(params,'start',0); end=number(params,'end',0) or duration(info)
        if start<0 or end<=start or end>duration(info)+.1: raise ValueError(T('起止时间超出音频范围'))
        args=['-ss',start,'-i',path,'-t',end-start]
        if truth(params.get('removeSilence','false')): filters+=SILENCE_TRIM
    elif action=='audioChannels':
        filters+=channel_filters(params)
    elif action=='redactAudio':
        raw=params.get('ranges','').strip(); ranges=json.loads(raw) if raw else [{'start':number(params,'start',0),'end':number(params,'end',1)}]
        if not ranges:
            with output_file(path,action,'.'+fmt) as state: ffmpeg(['-i',path,'-vn',*audio_args(fmt,params),state['temp']])
            return state['output']
        if any(float(r['start'])<0 or float(r['end'])<=float(r['start']) or float(r['end'])>duration(info)+.1 for r in ranges): raise ValueError(T('消音时间范围无效'))
        args+=['-filter_complex',beep_graph([(float(r['start']),float(r['end'])) for r in ranges],duration(info)),'-map','[a]']
    else: raise ValueError(T('未知音频工具'))
    if filters: args+=['-af',','.join(filters)]
    with output_file(path,action,'.'+fmt) as state:
        ffmpeg([*args,'-vn',*audio_args(fmt,params),state['temp']])
        if action=='compress' and number(params,'targetKB',0) and state['temp'].stat().st_size>number(params,'targetKB')*1024:raise ValueError(T('该音频在当前格式下无法达到目标大小，请增大目标或选择有损格式'))
    return state['output']
