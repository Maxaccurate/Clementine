from pathlib import Path
import re
from common import *

# Removes or replaces where a photo or video was taken; everything else (camera, date, orientation, quality) stays.
GPS=0x8825
XMP_HEADER=b'http://ns.adobe.com/xap/1.0/\x00'

def no_location(): return ValueError(T('文件里没有位置信息，无需移除'))

def run(path,params):
    """mode "strip" removes the location; mode "set" writes the coordinates typed in, or those of another photo."""
    kind=category(path)
    if kind not in ('image','video'): raise ValueError(T('此文件不支持所选工具'))
    place=target(params) if params.get('mode','strip')=='set' else None
    return image(path,place) if kind=='image' else video(path,place)

# --- reading and writing coordinates ---------------------------------------------------------------------------
def parse(text):
    """Latitude and longitude from text such as "31.2304, 121.4737" or "31.2304 N 121.4737 E"."""
    parts=re.findall(r'(-?\d+(?:\.\d+)?)\s*°?\s*([NSEWnsew])?',text or '')
    if len(parts)!=2: raise ValueError(T('请按“纬度, 经度”填写坐标，例如 31.2304, 121.4737'))
    (lat,ns),(lon,ew)=parts; lat=float(lat)*(-1 if ns.upper()=='S' else 1); lon=float(lon)*(-1 if ew.upper()=='W' else 1)
    if not (-90<=lat<=90 and -180<=lon<=180): raise ValueError(T('纬度需在 -90 到 90 之间，经度需在 -180 到 180 之间'))
    return lat,lon

def target(params):
    source=params.get('locationSource','').strip()
    if source:
        place=read(source)
        if place is None: raise ValueError(T('所选文件里没有位置信息，无法借用'))
        return place
    return parse(params.get('coordinates',''))

def from_gps(gps):
    """Decimal degrees from an EXIF GPS block, or None."""
    try:
        def degrees(value,ref): d,m,s=(float(x) for x in value); return (d+m/60+s/3600)*(-1 if str(ref).upper() in ('S','W') else 1)
        return degrees(gps[2],gps.get(1,'N')),degrees(gps[4],gps.get(3,'E'))
    except (KeyError,TypeError,ValueError,ZeroDivisionError): return None

def from_tags(info):
    """Decimal degrees from a video's location tag (ISO 6709, such as +37.7749-122.4194/), or None."""
    for tags in [info.get('format',{}).get('tags',{}),*(s.get('tags',{}) for s in info.get('streams',[]))]:
        for key,value in tags.items():
            if 'location' in key.lower() or key.lower().endswith('iso6709'):
                found=re.match(r'\s*([+-]\d+(?:\.\d+)?)([+-]\d+(?:\.\d+)?)',str(value))
                if found: return float(found.group(1)),float(found.group(2))
    return None

def read(path):
    """Where a photo or video was taken, or None."""
    if category(path)=='video': return from_tags(probe(path))
    from PIL import Image
    import pillow_heif
    pillow_heif.register_heif_opener()
    with Image.open(path) as picture: return from_gps(picture.getexif().get_ifd(GPS))

def describe(place): return None if place is None else f'{place[0]:.6f}, {place[1]:.6f}'

def gps_block(place):
    from PIL.TiffImagePlugin import IFDRational
    def dms(value):
        # Seconds as an exact fraction (ten-thousandths), so readers show 49.44 rather than 49.4400000000023.
        value=abs(value); d=int(value); m=int((value-d)*60); s=round((value-d-m/60)*3600*10000)
        return (IFDRational(d,1),IFDRational(m,1),IFDRational(s,10000))
    lat,lon=place
    return {0:b'\x02\x03\x00\x00',1:'N' if lat>=0 else 'S',2:dms(lat),3:'E' if lon>=0 else 'W',4:dms(lon)}

# --- pictures --------------------------------------------------------------------------------------------------
def image(path,place):
    suffix=Path(path).suffix.lower(); name='no-location' if place is None else 'new-location'
    if suffix in ('.jpg','.jpeg'):
        # JPEG: rewrite only the metadata segments, so the picture itself is not re-encoded.
        data=jpeg_with_location(Path(path).read_bytes(),place)
        with output_file(path,name,suffix) as state: state['temp'].write_bytes(data)
        return state['output']
    picture=open_image(path); exif=picture.getexif()
    if place is None:
        if GPS not in exif: raise no_location()
        del exif[GPS]
    else: exif[GPS]=gps_block(place)
    fmt={'.jpeg':'jpg','.tif':'tiff','.heif':'heic'}.get(suffix,suffix.lstrip('.'))
    with output_file(path,name,suffix) as state: save_image(picture,state['temp'],fmt,95,exif)
    return state['output']

def exif_segment(exif):
    data=exif.tobytes()
    if len(data)+2>0xFFFF: raise ValueError(T('图片的元数据过大，无法处理'))
    return b'\xff\xe1'+(len(data)+2).to_bytes(2,'big')+data

def jpeg_with_location(data,place):
    """The JPEG with its location removed (place None) or replaced; the image data itself is copied unchanged."""
    from PIL import Image
    if data[:2]!=b'\xff\xd8': raise ValueError(T('图片文件已损坏'))
    out=bytearray(data[:2]); i=2; found=False; written=False
    while i+4<=len(data):
        if data[i]!=0xFF: raise ValueError(T('图片文件已损坏'))
        marker=data[i+1]
        if marker in (0xDA,0xD9):  # image data follows; copy the rest unchanged
            break
        if marker==0x01 or 0xD0<=marker<=0xD7:
            out+=data[i:i+2]; i+=2; continue
        length=int.from_bytes(data[i+2:i+4],'big'); segment=data[i:i+2+length]; body=segment[4:]
        if marker==0xE1 and body.startswith(b'Exif\x00\x00'):
            # A second EXIF block (rare) only loses its location; the new one is written once.
            exif=Image.Exif(); exif.load(body); here=None if written else place
            if GPS in exif or here is not None:
                found=found or GPS in exif
                if here is None: exif.pop(GPS,None)
                else: exif[GPS]=gps_block(here)
                segment=exif_segment(exif)
            written=True
        elif marker==0xE1 and body.startswith(XMP_HEADER) and b'GPS' in body:
            # XMP can repeat the old coordinates; drop that block.
            found=True; segment=b''
        elif marker not in (0xE0,0xE1) and place is not None and not written:
            # No EXIF yet: add one with the new location after the JFIF header.
            exif=Image.Exif(); exif[GPS]=gps_block(place); out+=exif_segment(exif); written=True
        out+=segment; i+=2+length
    if place is None and not found: raise no_location()
    if place is not None and not written: raise ValueError(T('图片文件已损坏'))
    return bytes(out+data[i:])

# --- videos ----------------------------------------------------------------------------------------------------
def location_tags(tags):
    return [k for k in tags if 'location' in k.lower() or 'gps' in k.lower() or k.lower().endswith('iso6709') or k=='©xyz']

def video(path,place):
    info=probe(path); suffix=Path(path).suffix.lower(); args=[]
    for key in location_tags(info['format'].get('tags',{})): args+=['-metadata',f'{key}=']
    for stream in info['streams']:
        for key in location_tags(stream.get('tags',{})): args+=[f'-metadata:s:{stream["index"]}',f'{key}=']
    if place is None and not args: raise no_location()
    if place is not None:
        iso=f'{place[0]:+08.4f}{place[1]:+09.4f}/'
        args+=['-metadata',f'location={iso}','-metadata',f'location-eng={iso}']
    # Copy picture and sound as they are. Extra data tracks (where phones may repeat the location) are left out.
    with output_file(path,'no-location' if place is None else 'new-location',suffix) as state:
        ffmpeg(['-i',path,'-map','0:v','-map','0:a?','-map','0:s?','-c','copy','-map_metadata','0',*args,state['temp']])
    return state['output']
