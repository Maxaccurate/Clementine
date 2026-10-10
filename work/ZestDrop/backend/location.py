from pathlib import Path
from common import *

# Removes only where a photo or video was taken; everything else (camera, date, orientation, quality) stays.
GPS=0x8825
XMP_HEADER=b'http://ns.adobe.com/xap/1.0/\x00'

def no_location(): return ValueError(T('文件里没有位置信息，无需移除'))

def remove(path,params):
    kind=category(path)
    if kind=='image': return image(path)
    if kind=='video': return video(path)
    raise ValueError(T('此文件不支持所选工具'))

def image(path):
    from PIL import Image
    suffix=Path(path).suffix.lower()
    if suffix in ('.jpg','.jpeg'):
        # JPEG: rewrite only the metadata segments, so the picture itself is not re-encoded.
        data=jpeg_without_location(Path(path).read_bytes())
        with output_file(path,'removeLocation',suffix) as state: state['temp'].write_bytes(data)
        return state['output']
    picture=open_image(path); exif=picture.getexif()
    if GPS not in exif: raise no_location()
    del exif[GPS]
    fmt={'.jpeg':'jpg','.tif':'tiff','.heif':'heic'}.get(suffix,suffix.lstrip('.'))
    with output_file(path,'removeLocation',suffix) as state: save_image(picture,state['temp'],fmt,95,exif)
    return state['output']

def jpeg_without_location(data):
    from PIL import Image
    if data[:2]!=b'\xff\xd8': raise ValueError(T('图片文件已损坏'))
    out=bytearray(data[:2]); i=2; found=False
    while i+4<=len(data):
        if data[i]!=0xFF: raise ValueError(T('图片文件已损坏'))
        marker=data[i+1]
        if marker in (0xDA,0xD9):  # image data follows; copy the rest unchanged
            break
        if marker==0x01 or 0xD0<=marker<=0xD7:
            out+=data[i:i+2]; i+=2; continue
        length=int.from_bytes(data[i+2:i+4],'big'); segment=data[i:i+2+length]; body=segment[4:]
        if marker==0xE1 and body.startswith(b'Exif\x00\x00'):
            exif=Image.Exif(); exif.load(body)
            if GPS in exif:
                del exif[GPS]; found=True; new=exif.tobytes()
                if len(new)+2>0xFFFF: raise ValueError(T('图片的元数据过大，无法处理'))
                segment=b'\xff\xe1'+(len(new)+2).to_bytes(2,'big')+new
        elif marker==0xE1 and body.startswith(XMP_HEADER) and b'GPS' in body:
            # XMP can repeat the coordinates; drop that block.
            found=True; segment=b''
        out+=segment; i+=2+length
    if not found: raise no_location()
    return bytes(out+data[i:])

def location_tags(tags):
    return [k for k in tags if 'location' in k.lower() or 'gps' in k.lower() or k.lower().endswith('iso6709') or k=='©xyz']

def video(path):
    info=probe(path); suffix=Path(path).suffix.lower(); args=[]
    for key in location_tags(info['format'].get('tags',{})): args+=['-metadata',f'{key}=']
    for stream in info['streams']:
        for key in location_tags(stream.get('tags',{})): args+=[f'-metadata:s:{stream["index"]}',f'{key}=']
    if not args: raise no_location()
    # Copy picture and sound as they are. Extra data tracks (where phones may repeat the location) are left out.
    with output_file(path,'removeLocation',suffix) as state:
        ffmpeg(['-i',path,'-map','0:v','-map','0:a?','-map','0:s?','-c','copy','-map_metadata','0',*args,state['temp']])
    return state['output']
