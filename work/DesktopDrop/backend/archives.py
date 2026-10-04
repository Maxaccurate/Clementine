from pathlib import Path,PurePosixPath
import gzip,tarfile,zipfile,shutil,stat,tempfile,os,struct,zlib
from common import *

SEVEN=BASE/'runtime/7zip/7z.exe'

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

def safe_name(name):
    name=name.replace('\\','/'); path=PurePosixPath(name)
    if path.is_absolute() or '..' in path.parts or any(':' in part for part in path.parts): raise ValueError('压缩包包含不安全的文件路径')
    return path

def extract_to(path,folder):
    path=Path(path); suffix=path.suffix.lower()
    if suffix=='.zip':
        with zipfile.ZipFile(path) as archive:
            entries=archive.infolist()
            if len(entries)>20000 or sum(x.file_size for x in entries)>10_000_000_000: raise ValueError('压缩包解压规模超过当前限制')
            for entry in entries:
                safe_name(entry.filename)
                if stat.S_ISLNK(entry.external_attr>>16): raise ValueError('暂不解压符号链接')
            archive.extractall(folder)
    elif tarfile.is_tarfile(path):
        with tarfile.open(path) as archive:
            entries=archive.getmembers()
            if len(entries)>20000 or sum(x.size for x in entries)>10_000_000_000: raise ValueError('压缩包解压规模超过当前限制')
            for entry in entries:
                safe_name(entry.name)
                if not (entry.isfile() or entry.isdir()): raise ValueError('暂不解压链接或设备文件')
            archive.extractall(folder,filter='data')
    elif suffix in ('.gz','.gzip'):
        with gzip.open(path,'rb') as source,open(folder/path.stem,'wb') as target: shutil.copyfileobj(source,target)
    elif suffix=='.rar':
        if not SEVEN.exists(): raise ValueError('RAR 解包需要本机 7-Zip')
        listing=process([SEVEN,'l','-slt','-ba','-sccUTF-8',path])
        for line in listing.splitlines():
            if line.startswith('Path = '): safe_name(line[7:])
            if line.startswith(('Symbolic Link = ','Hard Link = ')) and line.split('=',1)[1].strip(): raise ValueError('暂不解压链接')
        process([SEVEN,'x','-y','-sccUTF-8',f'-o{folder}',path])
        for file in folder.rglob('*'):
            if file.is_symlink(): raise ValueError('解压结果包含链接')
    else: raise ValueError('不支持的压缩包格式')

def pack(folder,path,fmt):
    files=sorted(folder.rglob('*'))
    if fmt=='zip':
        with zipfile.ZipFile(path,'w',zipfile.ZIP_DEFLATED,compresslevel=8) as archive:
            for file in files:
                archive.write(file,file.relative_to(folder).as_posix()+('/' if file.is_dir() else ''))
    elif fmt in ('tar','gz'):
        with tarfile.open(path,'w:gz' if fmt=='gz' else 'w') as archive:
            for file in folder.iterdir(): archive.add(file,arcname=file.name)
    elif fmt=='rar':
        stored_rar(folder,path)
    else: raise ValueError('不支持的压缩包输出')

def convert(paths,fmt,params):
    with tempfile.TemporaryDirectory(prefix='desktopdrop-archive-') as temp:
        folder=Path(temp)
        if not params.get('_pack') and len(paths)==1 and Path(paths[0]).suffix.lower() in ('.zip','.tar','.gz','.tgz','.rar'): extract_to(paths[0],folder)
        else:
            for path in paths:
                path=Path(path); target=folder/path.name
                if target.exists(): raise ValueError('选中文件有相同名称，请分开处理')
                if path.is_dir(): shutil.copytree(path,target,symlinks=False)
                else: shutil.copy2(path,target)
        extension='.tar.gz' if fmt=='gz' else '.'+fmt
        with output_file(paths[0],'packed' if params.get('_pack') else 'converted',extension) as state: pack(folder,state['temp'],fmt)
    return state['output']

def extract(path,params):
    with output_folder(path,'extracted',allow_empty=True) as state: extract_to(path,state['temp'])
    return state['output']
