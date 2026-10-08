from pathlib import Path
import json,re,tempfile
from PIL import Image
from common import *

SCRIPT=Path(__file__).with_name('ocr.ps1')
CJK=re.compile(r'(?<=[　-〿㐀-鿿＀-￯])\s+(?=[　-〿㐀-鿿＀-￯])')
LIMIT=8000

def recognise(images,language):
    """Read text from picture files. Returns one entry per picture: its pixel size and the lines (text and box) found."""
    with tempfile.TemporaryDirectory(prefix='zestdrop-ocr-') as temp:
        out=Path(temp)/'result.json'
        process(['powershell.exe','-NoProfile','-NonInteractive','-ExecutionPolicy','Bypass','-File',SCRIPT,'-Language',language or 'auto','-Out',out,*images],timeout=900)
        data=json.loads(out.read_text(encoding='utf-8-sig'))
    if isinstance(data,dict) and data.get('error')=='noengine': raise ValueError(T('Windows 没有可用的文字识别语言。请在“设置 → 时间和语言 → 语言和区域”中为所需语言安装“光学字符识别”。'))
    for page in data:
        for line in page['lines']: line['text']=CJK.sub('',line['text'])
    return data

def prepared(image,folder,name):
    """Pictures are saved as PNG for the recogniser, shrunk if they exceed what it accepts."""
    if max(image.size)>LIMIT: image=image.copy(); image.thumbnail((LIMIT,LIMIT))
    target=Path(folder)/name; image.convert('RGB').save(target); return target,image.size

def page_text(page): return '\n'.join(line['text'] for line in page['lines'])

def add_text_layer(doc_page,page,scale_x,scale_y):
    """An invisible copy of the recognised text, so the page can be searched and selected."""
    for line in page['lines']:
        if not line['text'].strip() or line['h']<=0: continue
        size=max(4,line['h']*scale_y*.8)
        doc_page.insert_text((line['x']*scale_x,(line['y']+line['h'])*scale_y-line['h']*scale_y*.15),line['text'],fontname='china-s',fontsize=size,render_mode=3)

def run(path,action,params):
    language=params.get('language','auto').strip(); output=params.get('output','txt')
    with tempfile.TemporaryDirectory(prefix='zestdrop-ocr-input-') as temp:
        if action=='ocrImage':
            images=[prepared(open_image(path,params.get('imageFrame')),temp,'page-1.png')]
            pages=recognise([str(images[0][0])],language); text=page_text(pages[0])
            if output=='pdf':
                import pymupdf as fitz
                with output_file(path,'ocrImage','.pdf') as state:
                    width,height=images[0][1]; doc=fitz.open(); page=doc.new_page(width=width*.75,height=height*.75); page.insert_image(page.rect,filename=str(images[0][0]))
                    add_text_layer(page,pages[0],.75,.75); doc.save(state['temp'],garbage=3,deflate=True); doc.close()
                return state['output']
        else:
            import pypdfium2 as pdfium,pymupdf as fitz
            source=pdfium.PdfDocument(str(path),password=params.get('password') or None); count=len(source)
            if count>200: raise ValueError(T('一次最多识别 200 页'))
            files=[]; sizes=[]
            for index in range(count):
                image=source[index].render(scale=200/72).to_pil(); target,size=prepared(image,temp,f'page-{index+1}.png'); files.append(str(target)); sizes.append(size)
            source.close(); pages=recognise(files,language); text='\n\n'.join(page_text(page) for page in pages)
            if output=='pdf':
                from pdftools import open_pdf
                doc=open_pdf(path,params)
                for index,page in enumerate(pages):
                    target=doc[index]; add_text_layer(target,page,target.rect.width/sizes[index][0],target.rect.height/sizes[index][1])
                with output_file(path,'ocrPDF','.pdf') as state: doc.save(state['temp'],garbage=3,deflate=True)
                doc.close(); return state['output']
    if not text.strip(): raise ValueError(T('没有识别到文字'))
    with output_file(path,action,'.txt') as state: state['temp'].write_text(text,encoding='utf8')
    return state['output']
