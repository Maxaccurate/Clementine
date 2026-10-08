from pathlib import Path
import io
from common import *

def open_pdf(path,params):
    import pymupdf as fitz
    doc=fitz.open(path)
    if doc.needs_pass and not doc.authenticate(params.get('password','')): raise ValueError(T('PDF 密码错误'))
    return doc

def password(path,params):
    import pymupdf as fitz
    locked=fitz.open(path).needs_pass; doc=open_pdf(path,params); mode=params.get('mode','add'); new=params.get('newPassword','')
    if mode=='add':
        if not new: raise ValueError(T('请输入要设置的新密码'))
        options={'encryption':fitz.PDF_ENCRYPT_AES_256,'user_pw':new,'owner_pw':new}; suffix='pdfPassword'
    else:
        if not locked: raise ValueError(T('这个 PDF 没有设置密码'))
        options={'encryption':fitz.PDF_ENCRYPT_NONE}; suffix='unlocked'
    with output_file(path,suffix,'.pdf') as state: doc.save(state['temp'],garbage=3,deflate=True,**options)
    doc.close(); return state['output']

def numbers(path,params):
    import pymupdf as fitz
    doc=open_pdf(path,params); size=number(params,'fontSize',10); start=int(number(params,'startAt',1)); header=params.get('header','').strip(); footer=params.get('footer','').strip()
    pattern=params.get('numberFormat','{n} / {total}').strip(); position=params.get('position','bottomRight')
    if not 6<=size<=48: raise ValueError(T('字号需在 6 到 48 之间'))
    if not (pattern or header or footer): raise ValueError(T('请填写页码格式、页眉或页脚'))
    def put(page,text,horizontal,vertical):
        if not text: return
        width=fitz.get_text_length(text,fontname='china-s',fontsize=size); rect=page.rect; margin=36
        x={'Left':margin,'Center':(rect.width-width)/2,'Right':rect.width-margin-width}[horizontal]; y=margin if vertical=='top' else rect.height-margin+size
        page.insert_text((x,y),text,fontname='china-s',fontsize=size,color=(.2,.2,.2))
    vertical='top' if position.startswith('top') else 'bottom'; horizontal=position[3:] if vertical=='top' else position[6:]
    for index,page in enumerate(doc):
        try: label=pattern.format(n=start+index,total=len(doc)+start-1) if pattern else ''
        except (KeyError,IndexError,ValueError): raise ValueError(T('页码格式无效；可使用 {n} 和 {total}')) from None
        put(page,label,horizontal,vertical); put(page,header,'Center','top') if not (vertical=='top' and horizontal=='Center' and label) else None
        put(page,footer,'Center','bottom') if not (vertical=='bottom' and horizontal=='Center' and label) else None
    with output_file(path,'pdfNumbers','.pdf') as state: doc.save(state['temp'],garbage=3,deflate=True)
    doc.close(); return state['output']

def extract_images(path,params):
    doc=open_pdf(path,params); smallest=int(number(params,'minSize',64)); seen=set(); count=0
    with output_folder(path,'extractPdfImages',allow_empty=True) as state:
        for number_,page in enumerate(doc,1):
            for item in page.get_images(full=True):
                xref=item[0]
                if xref in seen: continue
                seen.add(xref); picture=doc.extract_image(xref)
                if min(picture['width'],picture['height'])<smallest: continue
                count+=1; (state['temp']/f'page{number_:03d}-image{count:03d}.{picture["ext"]}').write_bytes(picture['image'])
        if not count: raise ValueError(T('PDF 中没有可提取的图片'))
    doc.close(); return state['output']

def watermark(path,params):
    import imagetools
    doc=open_pdf(path,params)
    for page in doc:
        # The mark is drawn at twice the page size so text stays sharp, then laid over the page.
        layer=imagetools.watermark_layer((max(1,int(page.rect.width*2)),max(1,int(page.rect.height*2))),params); data=io.BytesIO(); layer.save(data,format='PNG')
        page.insert_image(page.rect,stream=data.getvalue(),overlay=True)
    with output_file(path,'watermark','.pdf') as state: doc.save(state['temp'],garbage=3,deflate=True)
    doc.close(); return state['output']

ACTIONS={'pdfPassword':password,'pdfNumbers':numbers,'extractPdfImages':extract_images,'watermark':watermark}
