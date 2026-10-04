from pathlib import Path
import sys,json,traceback,io
sys.path.insert(0,str(Path(__file__).resolve().parent))
from common import *
import images,documents,media,archives,office

def one(path,action,params):
    kind=category(path)
    if action.startswith('pack:'):return archives.convert([path],action.split(':',1)[1],{**params,'_pack':True})
    if action.startswith('convert:'):
        fmt=action.split(':',1)[1]
        if kind=='image': return images.convert(path,fmt,params)
        if kind in ('video','audio'): return media.convert(path,fmt,params)
        if kind=='document': return documents.convert(path,fmt,params)
        if kind=='office':return office.convert(path,fmt,params)
        return archives.convert([path],fmt,params)
    if action=='extractArchive': return archives.extract(path,params)
    if kind=='office':return office.edit_metadata(path,params) if action=='removeMetadata' else office.selected_pdf(path,params)
    if kind=='image': return images.tool(path,action,params)
    if kind=='video': return media.video_tool(path,action,params)
    if kind=='audio': return media.audio_tool(path,action,params)
    if kind=='document': return documents.tool(path,action,params)
    raise ValueError('此文件不支持所选工具')

def execute(job,result_path):
    paths=job['Paths']; action=job['Action']; params=job.get('Parameters') or {}
    files=[]
    grouped=action.startswith('pack:') or action in ('createPDF','createCollage','mergePDF','joinVideos','officeMergePDF') or action.startswith('convert:') and len(paths)>1 and all(category(p)=='archive' and Path(p).suffix.lower() not in ('.zip','.tar','.gz','.tgz','.rar') for p in paths)
    total=1 if grouped else len(paths)
    def progress(phase,processed,current=None):
        target=Path(str(result_path)+'.progress');temp=Path(str(target)+'.tmp')
        temp.write_text(json.dumps({'Phase':phase,'Processed':processed,'Total':total,'Current':current},ensure_ascii=False),encoding='utf8');temp.replace(target)
    progress('processing',0,Path(paths[0]).name)
    if grouped:
        try:
            output=office.merge(paths,params) if action=='officeMergePDF' else images.collage(paths,params) if action=='createCollage' else documents.images_document(paths,'pdf',params) if action=='createPDF' else documents.merge(paths,params) if action=='mergePDF' else media.join(paths,params) if action=='joinVideos' else archives.convert(paths,action.split(':')[1],{**params,'_pack':action.startswith('pack:')})
            files.append({'Input':paths[0],'Output':output,'Error':None})
        except Exception as error: files.append({'Input':paths[0],'Output':None,'Error':str(error)})
    else:
        for path in paths:
            progress('processing',len(files),Path(path).name)
            try: files.append({'Input':path,'Output':one(path,action,params),'Error':None})
            except Exception as error: files.append({'Input':path,'Output':None,'Error':str(error)})
            progress('processing',len(files),Path(path).name)
    Path(result_path).write_text(json.dumps({'Files':files},ensure_ascii=False),encoding='utf8')
    progress('completed',len(files),Path(paths[-1]).name)

def inspect(path,folder,frame=None):
    folder=Path(folder); folder.mkdir(parents=True,exist_ok=True); kind=category(path); result={'Kind':kind,'Preview':None,'Width':0,'Height':0,'Duration':0,'FrameRate':0,'Metadata':{},'Pages':0}
    if kind=='image':
        image=open_image(path,frame); result.update(Width=image.width,Height=image.height,Frames=image.info.get('source_frames',1),Frame=image.info.get('source_frame',0),Metadata=images.metadata(path)); image.thumbnail((1000,800)); preview=folder/'preview.png'; image.save(preview); result['Preview']=str(preview)
    elif kind in ('audio','video'):
        info=probe(path); result['Duration']=duration(info)
        result['Metadata']={'format':info.get('format',{}).get('tags',{}),'streams':{str(s['index']):s.get('tags',{}) for s in info['streams']},'chapters':{str(i):s.get('tags',{}) for i,s in enumerate(info.get('chapters',[]))}}
        if kind=='video':
            stream=video_stream(info); result.update(Width=int(stream['width']),Height=int(stream['height'])); a,b=map(float,stream.get('avg_frame_rate','30/1').split('/')); result['FrameRate']=a/b if b and a else 30
            preview=folder/'preview.png'; ffmpeg(['-i',path,'-frames:v','1','-vf',r'scale=min(1000\,iw):-2',preview]); result['Preview']=str(preview)
        else:
            preview=folder/'waveform.png'; ffmpeg(['-i',path,'-filter_complex','aformat=channel_layouts=mono,showwavespic=s=1000x260:colors=0xff6500','-frames:v','1',preview]); result['Preview']=str(preview)
    elif kind=='office':return office.inspect(path,folder)
    elif Path(path).suffix.lower()=='.pdf':
        import pypdfium2 as pdfium
        doc=pdfium.PdfDocument(path); result['Pages']=len(doc); result['Metadata']=documents.metadata(path)
        preview=folder/'preview.png'; doc[0].render(scale=1.3).to_pil().save(preview); result['Preview']=str(preview); doc.close()
    return result

def preview(path,action,params,folder,paths=None):
    folder=Path(folder); folder.mkdir(parents=True,exist_ok=True)
    if category(path)=='office':return office.inspect(path,folder)
    if category(path)=='image':
        image=open_image(path,params.get('imageFrame'))
        if action=='editImage': image=images.edit(image,params)
        elif action=='cropImage': image=images.crop(image,params)
        elif action=='redactImage': image=images.redact(image,params)
        elif action=='frameImage': image=images.framed(image,params)
        elif action=='createCollage': image=images.collage(paths or [path],params,preview=True)
        image.thumbnail((1200,900)); target=folder/'live-preview.png'; image.save(target); return {'Preview':str(target)}
    if category(path)=='video':
        info=probe(path); t=max(0,min(number(params,'time',0)+number(params,'frame',0)/(number(params,'fps',30)),max(0,duration(info)-.04)))
        target=folder/'live-preview.png'; raw=folder/'source-frame.png'; ffmpeg(['-ss',t,'-i',path,'-frames:v','1',raw]); image=open_image(raw)
        if action=='cropVideo': image=images.crop(image,params)
        elif action=='redactVideo':
            regions=[r for r in rects(params,*image.size) if float(r.get('start',0))<=t<=(float(r.get('end',0)) or duration(info))]
            if regions:image=images.redact(image,{**params,'regions':json.dumps(regions)})
        image.thumbnail((1200,900)); image.save(target); return {'Preview':str(target),'SourcePreview':str(raw)}
    if Path(path).suffix.lower()=='.pdf':
        import pypdfium2 as pdfium
        doc=pdfium.PdfDocument(path,password=params.get('password') or None); page=max(0,min(len(doc)-1,int(number(params,'pageNumber',1))-1)); target=folder/'live-preview.png'
        image=doc[page].render(scale=1.3).to_pil()
        rotations=json.loads(params.get('rotations','{}') or '{}'); angle=int(rotations.get(str(page+1),number(params,'rotation',0)))
        if angle:image=image.rotate(-angle,expand=True)
        image.save(target); doc.close();return {'Preview':str(target)}
    return inspect(path,folder)

def playback(path,action,params,folder):
    folder=Path(folder);folder.mkdir(parents=True,exist_ok=True);kind=category(path);info=probe(path);start=max(0,min(number(params,'time',0),max(0,duration(info)-.1)));length=min(30,duration(info)-start)
    if action in ('trimAudio','trimVideo'):
        first=number(params,'start',0);last=number(params,'end',0) or duration(info)
        if first<0 or last<=first or last>duration(info)+.1:raise ValueError('起止时间超出文件范围')
        start=max(first,min(start,last-.001));length=min(30,last-start)
    if kind=='audio':
        target=folder/'playback.wav';filters=[]
        if action=='normalizeAudio':filters.append(f"loudnorm=I={number(params,'loudness',-16)}:LRA={number(params,'range',11)}:TP={number(params,'peak',-1.5)}")
        if action=='audioChannels':
            left=number(params,'leftGain',1);right=number(params,'rightGain',1);filters.append('aformat=channel_layouts=stereo');filters.append(f'pan=mono|c0={left*.5}*c0+{right*.5}*c1' if params.get('channels','mono')=='mono' else f'pan=stereo|c0={left}*c0|c1={right}*c1')
        if action=='trimAudio' and truth(params.get('removeSilence','false')):filters+=['silenceremove=start_periods=1:start_threshold=-40dB','areverse','silenceremove=start_periods=1:start_threshold=-40dB','areverse']
        args=['-ss',start,'-i',path,'-t',length,'-vn']
        if action=='redactAudio':
            raw=params.get('ranges','').strip();ranges=json.loads(raw) if raw else [{'start':number(params,'start',0),'end':number(params,'end',0)}]
            active=[(max(0,float(r['start'])-start),min(length,float(r['end'])-start)) for r in ranges if float(r['end'])>start and float(r['start'])<start+length]
            if active:
                expression='+'.join(f'between(t,{a},{b})' for a,b in active)
                graph=f"[0:a]asetpts=PTS-STARTPTS,aformat=sample_rates=48000:channel_layouts=stereo,volume=0:enable='{expression}'[speech];sine=frequency=1000:sample_rate=48000:duration={length},aformat=channel_layouts=stereo,volume='if({expression},0.15,0)':eval=frame[beep];[speech][beep]amix=inputs=2:normalize=0[a]"
                args+=['-filter_complex',graph,'-map','[a]']
        if filters:args+=['-af',','.join(filters)]
        ffmpeg([*args,'-c:a','pcm_s16le',target])
    else:
        target=folder/'playback.mp4';filters=[r'scale=min(1000\,iw):-2'];args=['-ss',start,'-i',path,'-t',length]
        if action=='changeVideoSpeed':filters.insert(0,f"setpts=PTS/{number(params,'speed',2)}");args+=['-af',media.tempo(number(params,'speed',2))] if any(s['codec_type']=='audio' for s in info['streams']) else []
        if action=='cropVideo':
            w=int(number(params,'width',video_stream(info)['width']));h=int(number(params,'height',video_stream(info)['height']));ratio=params.get('ratio','free')
            if w>0 and h>0:w,h=fit_ratio(w,h,ratio)
            filters.insert(0,f"crop={w}:{h}:{int(number(params,'x',0))}:{int(number(params,'y',0))}")
        if action=='muteVideo':args+=['-an']
        if action=='redactVideo':
            regions=rects(params,int(video_stream(info)['width']),int(video_stream(info)['height']))
            active=[{**r,'start':max(0,float(r.get('start',0))-start),'end':min(length,(float(r.get('end',0)) or duration(info))-start)} for r in regions if (float(r.get('end',0)) or duration(info))>start and float(r.get('start',0))<start+length]
            graph=media.redact_graph(info,{**params,'regions':json.dumps(active)})+r';[outv]scale=min(1000\,iw):-2[playv]'
            args+=['-filter_complex',graph,'-map','[playv]','-map','0:a?'];filters=[]
        if filters:filters.append('pad=ceil(iw/2)*2:ceil(ih/2)*2')
        ffmpeg([*args,*(['-vf',','.join(filters)] if filters else []),*media.video_args('mp4'),target])
    return {'Preview':str(target)}

def frame_step(path,params):
    t=number(params,'time',0);direction=int(number(params,'direction',1))
    data=json.loads(process([FFPROBE,'-v','error','-select_streams','v:0','-show_frames','-show_entries','frame=best_effort_timestamp_time','-read_intervals',f'{max(0,t-2)}%{t+2}','-of','json',path]))
    values=sorted(float(frame['best_effort_timestamp_time']) for frame in data['frames'] if frame.get('best_effort_timestamp_time'))
    candidates=[v for v in values if v>t+1e-6] if direction>0 else [v for v in values if v<t-1e-6]
    return {'Time':min(candidates) if direction>0 and candidates else max(candidates) if candidates else t}

def analyze(path,params):
    import subprocess,re
    filters=f"loudnorm=I={number(params,'loudness',-16)}:LRA={number(params,'range',11)}:TP={number(params,'peak',-1.5)}:print_format=json"
    result=subprocess.run([str(FFMPEG),'-hide_banner','-nostdin','-i',str(path),'-af',filters,'-f','null','-'],capture_output=True,text=True,encoding='utf8',errors='replace',creationflags=0x08000000)
    if result.returncode:raise RuntimeError(result.stderr[-1200:])
    value=json.loads(result.stderr[result.stderr.rfind('{'):result.stderr.rfind('}')+1]);return {'Input':value['input_i'],'Output':value['output_i']}

if __name__=='__main__':
    try:
        mode=sys.argv[1]
        if mode=='--job': execute(json.loads(Path(sys.argv[2]).read_text(encoding='utf-8-sig')),sys.argv[3])
        elif mode=='--inspect': Path(sys.argv[3]).write_text(json.dumps(inspect(sys.argv[2],sys.argv[4],int(sys.argv[5]) if len(sys.argv)>5 and int(sys.argv[5])>=0 else None),ensure_ascii=False),encoding='utf8')
        elif mode in ('--preview','--playback','--frame-step','--analyze'):
            request=json.loads(Path(sys.argv[2]).read_text(encoding='utf-8-sig'));path=request['Paths'][0];action=request['Action'];params=request.get('Parameters') or {}
            value=analyze(path,params) if mode=='--analyze' else frame_step(path,params) if mode=='--frame-step' else playback(path,action,params,sys.argv[4]) if mode=='--playback' else preview(path,action,params,sys.argv[4],request['Paths'])
            Path(sys.argv[3]).write_text(json.dumps(value),encoding='utf8')
        else: raise ValueError('未知处理模式')
    except Exception as error:
        if len(sys.argv)>3: Path(sys.argv[3]).write_text(json.dumps({'Error':str(error)},ensure_ascii=False),encoding='utf8')
        sys.exit(1)
