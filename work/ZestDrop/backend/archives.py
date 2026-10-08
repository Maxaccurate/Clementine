from pathlib import Path,PurePosixPath
import gzip,tarfile,zipfile,shutil,stat,tempfile,struct,zlib
from common import *

SEVEN=BASE/'runtime/7zip/7z.exe'
# Given when no password was typed, so 7-Zip reports a wrong password instead of waiting for someone to type one.
NO_PASSWORD='zestdrop-no-password-given'

def vint(value):
    output=bytearray()
    while value>=128:output.append((value&127)|128);value>>=7
    output.append(value);return bytes(output)

def rar_header(stream,body):
    header=vint(len(body))+body;stream.write(struct.pack('<I',zlib.crc32(header)&0xffffffff)+header)

def stored_rar(folder,path):
    # RAR 5.0 store method, matching Tangerine's uncompressed RAR export.
    # Structure follows https://www.rarlab.com/technote.htm.
    with open(path,'wb') as output:
        output.write(b'Rar!\x1a\x07\x01\x00');rar_header(output,vint(1)+vint(0)+vint(0))
        for file in sorted(folder.rglob('*')):
            directory=file.is_dir();size=0 if directory else file.stat().st_size;crc=0
            if not directory:
                with open(file,'rb') as source:
                    for block in iter(lambda:source.read(1<<20),b''):crc=zlib.crc32(block,crc)
            name=file.relative_to(folder).as_posix().encode('utf8');flags=6|(1 if directory else 0)
            body=vint(2)+vint(2)+vint(size)+vint(flags)+vint(size)+vint(16 if directory else 32)+struct.pack('<I',int(file.stat().st_mtime)&0xffffffff)+struct.pack('<I',crc&0xffffffff)+vint(0)+vint(0)+vint(len(name))+name
            rar_header(output,body)
            if not directory:
                with open(file,'rb') as source:shutil.copyfileobj(source,output)
        rar_header(output,vint(5)+vint(0)+vint(0))

MAX_ENTRIES=20000
MAX_BYTES=10_000_000_000

def check_scale(count,size):
    if count>MAX_ENTRIES or size>MAX_BYTES: raise ValueError(T('压缩包解压规模超过当前限制'))

def safe_name(name):
    name=name.replace('\\','/'); path=PurePosixPath(name)
    if path.is_absolute() or '..' in path.parts or any(':' in part for part in path.parts): raise ValueError(T('压缩包包含不安全的文件路径'))
    return path

def extract_to(path,folder,params=None):
    path=Path(path); suffix=path.suffix.lower(); secret=(params or {}).get('password','')
    if suffix=='.zip' and not secret and not zip_needs_password(path):
        with zipfile.ZipFile(path) as archive:
            entries=archive.infolist(); check_scale(len(entries),sum(x.file_size for x in entries))
            for entry in entries:
                safe_name(entry.filename)
                if stat.S_ISLNK(entry.external_attr>>16): raise ValueError(T('暂不解压符号链接'))
            archive.extractall(folder)
    elif tarfile.is_tarfile(path):
        with tarfile.open(path) as archive:
            entries=archive.getmembers(); check_scale(len(entries),sum(x.size for x in entries))
            for entry in entries:
                safe_name(entry.name)
                if not (entry.isfile() or entry.isdir()): raise ValueError(T('暂不解压链接或设备文件'))
            archive.extractall(folder,filter='data')
    elif suffix in ('.gz','.gzip'):
        # gzip has no trustworthy size header, so enforce the limit while decompressing.
        written=0
        with gzip.open(path,'rb') as source,open(folder/path.stem,'wb') as target:
            for block in iter(lambda:source.read(1<<20),b''):
                written+=len(block); check_scale(1,written); target.write(block)
    elif suffix in ('.rar','.7z','.zip'):
        if not SEVEN.exists(): raise ValueError(T('RAR 解包需要本机 7-Zip'))
        password=[f'-p{secret or NO_PASSWORD}']
        try: listing=process([SEVEN,'l','-slt','-ba','-sccUTF-8',*password,path]); count=size=0
        except RuntimeError as error: raise wrong_password(error) from None
        for line in listing.splitlines():
            if line.startswith('Path = '): safe_name(line[7:]); count+=1
            if line.startswith('Size = ') and line[7:].strip().isdigit(): size+=int(line[7:])
            if line.startswith(('Symbolic Link = ','Hard Link = ')) and line.split('=',1)[1].strip(): raise ValueError(T('暂不解压链接'))
        check_scale(count,size)
        try: process([SEVEN,'x','-y','-sccUTF-8',f'-o{folder}',*password,path])
        except RuntimeError as error: raise wrong_password(error) from None
        for file in folder.rglob('*'):
            if file.is_symlink(): raise ValueError(T('解压结果包含链接'))
    else: raise ValueError(T('不支持的压缩包格式'))

def zip_needs_password(path):
    try:
        with zipfile.ZipFile(path) as archive: return any(entry.flag_bits&1 for entry in archive.infolist())
    except zipfile.BadZipFile: return False

def wrong_password(error):
    text=str(error)
    if 'password' in text.lower() or 'Wrong' in text or 'Cannot open encrypted' in text: return ValueError(T('压缩包需要正确的密码'))
    return error

def pack(folder,path,fmt):
    files=sorted(folder.rglob('*'))
    if fmt=='7z':
        if not SEVEN.exists(): raise ValueError(T('7z 打包需要本机 7-Zip'))
        process([SEVEN,'a','-t7z','-mx=5','-sccUTF-8','-bd',path,'*'],cwd=folder)
        return
    if fmt=='zip':
        with zipfile.ZipFile(path,'w',zipfile.ZIP_DEFLATED,compresslevel=8) as archive:
            for file in files:
                archive.write(file,file.relative_to(folder).as_posix()+('/' if file.is_dir() else ''))
    elif fmt in ('tar','gz'):
        with tarfile.open(path,'w:gz' if fmt=='gz' else 'w') as archive:
            for file in folder.iterdir(): archive.add(file,arcname=file.name)
    elif fmt=='rar':
        stored_rar(folder,path)
    else: raise ValueError(T('不支持的压缩包输出'))

def copy_selection(paths,folder):
    for path in paths:
        path=Path(path); target=folder/path.name
        if target.exists(): raise ValueError(T('选中文件有相同名称，请分开处理'))
        if path.is_dir(): shutil.copytree(path,target,symlinks=False)
        else: shutil.copy2(path,target)

def convert(paths,fmt,params):
    with tempfile.TemporaryDirectory(prefix='zestdrop-archive-') as temp:
        folder=Path(temp)
        if not params.get('_pack') and len(paths)==1 and Path(paths[0]).suffix.lower() in ('.zip','.tar','.gz','.tgz','.rar','.7z'): extract_to(paths[0],folder)
        else: copy_selection(paths,folder)
        extension='.tar.gz' if fmt=='gz' else '.'+fmt
        with output_file(paths[0],'packed' if params.get('_pack') else 'converted',extension) as state: pack(folder,state['temp'],fmt)
    return state['output']

def extract(path,params):
    with output_folder(path,'extracted',allow_empty=True) as state: extract_to(path,state['temp'],params)
    return state['output']

def pack_archive(paths,params):
    """Pack files and folders as ZIP or 7z with an optional password (AES-256) and an optional volume size."""
    fmt=params.get('format','zip'); secret=params.get('password',''); level=int(number(params,'level',5)); volume=number(params,'splitMB',0)
    if fmt not in ('zip','7z') or not 1<=level<=9 or not 0<=volume<=100000: raise ValueError(T('压缩设置无效'))
    if not SEVEN.exists(): raise ValueError(T('7z 打包需要本机 7-Zip'))
    with tempfile.TemporaryDirectory(prefix='zestdrop-archive-') as temp:
        folder=Path(temp)/'content'; folder.mkdir(); copy_selection(paths,folder)
        options=['-sccUTF-8','-bd',f'-mx={level}']
        if secret: options+=[f'-p{secret}']+(['-mem=AES256'] if fmt=='zip' else ['-mhe=on'])
        if volume: options+=[f'-v{int(volume)}m']
        if volume:
            with output_folder(paths[0],'packArchive') as state: process([SEVEN,'a',f'-t{fmt}',*options,state['temp']/(output_stem(paths[0],'packed')+'.'+fmt),'*'],cwd=folder)
        else:
            with output_file(paths[0],'packArchive','.'+fmt) as state: process([SEVEN,'a',f'-t{fmt}',*options,state['temp'],'*'],cwd=folder)
    return state['output']
