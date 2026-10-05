from pathlib import Path
import sys,json,hashlib
ROOT=Path(__file__).resolve().parent.parent;APP=ROOT/'outputs/ZestDrop';sys.path.insert(0,str(APP/'backend'))
import worker
from common import ffmpeg,probe,duration
DIR=ROOT/'work/player-fixtures';DIR.mkdir(exist_ok=True);checks=[]
movie=DIR/'source.mp4';audio=DIR/'source.wav'
ffmpeg(['-f','lavfi','-i','testsrc2=size=160x120:rate=10','-f','lavfi','-i','sine=frequency=440:sample_rate=48000','-t','35','-c:v','libx264','-pix_fmt','yuv420p','-c:a','aac',movie])
ffmpeg(['-f','lavfi','-i','sine=frequency=660:sample_rate=48000','-t','35',audio])
before={str(p):hashlib.sha256(p.read_bytes()).hexdigest() for p in (movie,audio)}
def check(name,fn):
    try:fn();checks.append({'test':name,'passed':True})
    except Exception as ex:checks.append({'test':name,'passed':False,'error':str(ex)})
def require(v):
    if not v:raise AssertionError('unexpected playback output')
def play(path,action,params,tag):
    folder=DIR/tag;folder.mkdir(exist_ok=True);return worker.playback(path,action,params,folder)
for path in (movie,audio):
    def original(path=path):
        r=play(path,'preview-original',{'time':'12','fullPreview':'true'},'full-'+path.suffix[1:]);require(r['Start']==0 and r['Duration']>34.8 and r['SourceDuration']>34.8)
    check('compatible original playback covers full '+path.suffix+' beyond old 30-second limit',original)
def trimmed():
    r=play(movie,'trimVideo',{'start':'10','end':'20','time':'12'},'trim');require(abs(r['Start']-12)<.001 and abs(r['Duration']-8)<.1)
check('trim preview metadata identifies its actual source start and duration',trimmed)
def speed():
    r=play(movie,'changeVideoSpeed',{'speed':'2','time':'5'},'speed');require(r['Rate']==2 and r['Start']==5 and abs(r['Duration']-15)<.2)
check('speed preview exposes source mapping rate',speed)
def crop():
    r=play(movie,'cropVideo',{'x':'0','y':'0','width':'140','height':'120','ratio':'7:3','time':'1'},'crop');s=next(s for s in probe(r['Preview'])['streams'] if s['codec_type']=='video');require((s['width'],s['height'])==(140,60))
check('crop effect is playable with the selected aspect ratio',crop)
def mute():
    r=play(movie,'muteVideo',{'time':'0'},'mute');require(not any(s['codec_type']=='audio' for s in probe(r['Preview'])['streams']))
check('mute effect preview has no audio track',mute)
def visualizer():
    r=play(audio,'audioToVideo',{'aspect':'landscape','time':'2'},'visualizer');s=next(s for s in probe(r['Preview'])['streams'] if s['codec_type']=='video');require(r['Kind']=='video' and (s['width'],s['height'])==(1280,720))
check('audio visualization returns a video for the media player',visualizer)
for action,params in [('normalizeAudio',{}),('audioChannels',{'channels':'mono','leftGain':'0.5','rightGain':'1'}),('redactAudio',{'start':'2','end':'3'})]:
    check('playable audio effect '+action,lambda action=action,params=params:require(play(audio,action,params,action)['Kind']=='audio'))
check('playback preparation preserves source files',lambda:require(all(hashlib.sha256(p.read_bytes()).hexdigest()==before[str(p)] for p in (movie,audio))))
report={'passed':sum(c['passed'] for c in checks),'failed':sum(not c['passed'] for c in checks),'checks':checks};(ROOT/'outputs/media-player-tests.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf8');print(json.dumps(report,ensure_ascii=False),flush=True);sys.exit(bool(report['failed']))
