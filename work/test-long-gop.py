from pathlib import Path
import sys,subprocess,json
root=Path(__file__).resolve().parent.parent;app=root/'outputs/ZestDrop';sys.path.insert(0,str(app/'backend'))
import worker
source=root/'work/full-fixtures/long-gop.mp4'
subprocess.run([str(app/'runtime/ffmpeg/ffmpeg.exe'),'-v','error','-y','-f','lavfi','-i','testsrc=size=160x120:rate=10:duration=12','-c:v','libx264','-pix_fmt','yuv420p','-g','200','-keyint_min','200','-sc_threshold','0',str(source)],check=True,creationflags=0x08000000)
value=worker.frame_step(source,{'time':'8','direction':'1'})['Time'];passed=abs(value-8.1)<.001
(root/'outputs/frame-step-regression.json').write_text(json.dumps({'passed':passed,'source_time':8,'next_frame_time':value}),encoding='utf8')
print('long-GOP frame step',value,'passed',passed)
raise SystemExit(not passed)
