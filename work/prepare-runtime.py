"""Prepare the portable Windows runtimes without changing system installations."""
from pathlib import Path
from html.parser import HTMLParser
from concurrent.futures import ThreadPoolExecutor
import argparse,gzip,os,re,shutil,subprocess,sys,urllib.request,urllib.parse,zipfile

ROOT=Path(__file__).resolve().parent.parent
CACHE=ROOT/'work/downloads'
BASE=ROOT/'outputs/ZestDrop/runtime'

def fetch(url,path):
    if path.exists() and path.stat().st_size:return
    print('Downloading',url,flush=True)
    temp=path.with_suffix(path.suffix+'.download')
    try:
        with urllib.request.urlopen(url,timeout=120) as response,temp.open('wb') as target:shutil.copyfileobj(response,target)
        temp.replace(path)
    finally:
        if temp.exists():temp.unlink()

def python_runtime():
    with urllib.request.urlopen('https://www.python.org/downloads/windows/',timeout=30) as response:raw=response.read()
    if raw.startswith(b'\x1f\x8b'):raw=gzip.decompress(raw)
    class Links(HTMLParser):
        def __init__(self):super().__init__();self.links=[]
        def handle_starttag(self,tag,attrs):
            url=dict(attrs).get('href','')
            if re.search(r'python-3\.13\.\d+-embed-amd64\.zip$',url):self.links.append(url)
    parser=Links();parser.feed(raw.decode('utf8'))
    if not parser.links:raise RuntimeError('Python 3.13 x64 embedded download not found on python.org.')
    url=urllib.parse.urljoin('https://www.python.org/',parser.links[0])
    if urllib.parse.urlparse(url).hostname not in ('python.org','www.python.org'):raise RuntimeError('Unexpected Python download host.')
    package=CACHE/Path(urllib.parse.urlparse(url).path).name;fetch(url,package)
    location=BASE/'python';location.mkdir(exist_ok=True)
    with zipfile.ZipFile(package) as archive:archive.extractall(location)
    next(location.glob('python*._pth')).write_text('python313.zip\n.\nLib/site-packages\nimport site\n',encoding='utf8')
    subprocess.run([sys.executable,'-m','pip','install','--disable-pip-version-check','--only-binary=:all:','--python-version','3.13','--platform','win_amd64','--implementation','cp','--abi','cp313','--target',str(location/'Lib/site-packages'),'-r',str(ROOT/'requirements-runtime.txt')],check=True)
    prune_python(location/'Lib/site-packages')
    print('Portable Python ready',flush=True)

# Files that are only needed to build against or test a package, never to run it.
PRUNE_DIRS={'tests','test','mupdf-devel','f2py','_pyinstaller'}
PRUNE_FILES=('*.pyi','*.c','*.h','*.lib','*.pxd','*.pyx','cv2/opencv_videoio_ffmpeg*.dll')  # the last is OpenCV's own video reader; only cv2.erode and denoising are used

def prune_python(site):
    freed=0
    for path in sorted(site.rglob('*'),key=lambda p:len(p.parts),reverse=True):
        if path.is_dir() and path.name in PRUNE_DIRS:
            freed+=sum(f.stat().st_size for f in path.rglob('*') if f.is_file());shutil.rmtree(path)
    if (site/'bin').is_dir():freed+=sum(f.stat().st_size for f in (site/'bin').iterdir());shutil.rmtree(site/'bin')  # console-script launchers
    for pattern in PRUNE_FILES:
        for path in list(site.rglob(pattern)):
            if path.is_file():freed+=path.stat().st_size;path.unlink()
    print(f'Pruned {freed/2**20:.1f} MB of build-only files from Python packages',flush=True)

def ffmpeg_runtime():
    folder=BASE/'ffmpeg';folder.mkdir(exist_ok=True)
    if (folder/'ffmpeg.exe').exists() and (folder/'ffprobe.exe').exists():return
    url='https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip';package=CACHE/'ffmpeg-release-essentials.zip';fetch(url,package)
    with zipfile.ZipFile(package) as archive:
        for name in archive.namelist():
            leaf=Path(name).name
            if leaf in ('ffmpeg.exe','ffprobe.exe') or 'LICENSE' in leaf.upper() or 'README' in leaf.upper():(folder/leaf).write_bytes(archive.read(name))
    if not all((folder/name).is_file() for name in ('ffmpeg.exe','ffprobe.exe')):raise RuntimeError('FFmpeg archive did not contain the expected tools.')
    (folder/'SOURCE.txt').write_text(f'Binary source: {url}\nOfficial source and license: https://ffmpeg.org/\nBuild configuration: run ffmpeg.exe -version\n',encoding='utf8')
    print('FFmpeg ready',flush=True)

def sevenzip_runtime(directory):
    target=BASE/'7zip';target.mkdir(exist_ok=True)
    if all((target/name).is_file() for name in ('7z.exe','7z.dll','License.txt')):return
    source=Path(directory) if directory else Path(os.environ.get('ProgramFiles','C:/Program Files'))/'7-Zip'
    for name in ('7z.exe','7z.dll','License.txt'):
        if not (source/name).is_file():raise RuntimeError(f'Missing {source/name}. Install 7-Zip or pass --sevenzip-dir with its directory.')
    for name in ('7z.exe','7z.dll','License.txt'):shutil.copy2(source/name,target/name)
    print('7-Zip ready',flush=True)

if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--sevenzip-dir',help='Directory containing 7z.exe, 7z.dll and License.txt')
    parser.add_argument('--prune-only',action='store_true',help='Only remove build-only files from the existing Python packages');args=parser.parse_args()
    if args.prune_only:prune_python(BASE/'python/Lib/site-packages');sys.exit()
    if os.name!='nt':parser.error('The application and portable runtime preparation require Windows.')
    CACHE.mkdir(parents=True,exist_ok=True);BASE.mkdir(parents=True,exist_ok=True)
    sevenzip_runtime(args.sevenzip_dir)
    with ThreadPoolExecutor(max_workers=2) as executor:list(executor.map(lambda fn:fn(),(python_runtime,ffmpeg_runtime)))
