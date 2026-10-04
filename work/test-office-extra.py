from pathlib import Path
import sys,json,zipfile,subprocess,csv
from openpyxl import load_workbook
from pypdf import PdfReader
ROOT=Path(__file__).resolve().parent.parent;APP=ROOT/'outputs/DesktopDrop';DIR=ROOT/'work/office-fixtures';sys.path.insert(0,str(APP/'backend'))
import office
checks=[]
def check(name,fn):
    try:fn();checks.append({'test':name,'passed':True})
    except Exception as error:checks.append({'test':name,'passed':False,'error':str(error)});print('FAIL',name,str(error),flush=True)
def require(value):
    if not value:raise AssertionError('unexpected output')
for source,extension,old,new in [('slides.pptx','pptm','application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml','application/vnd.ms-powerpoint.presentation.macroEnabled.main+xml'),('document.docx','docm','application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml','application/vnd.ms-word.document.macroEnabled.main+xml'),('sheets.xlsx','xlsm','application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml','application/vnd.ms-excel.sheet.macroEnabled.main+xml')]:
    target=DIR/(Path(source).stem+'.'+extension)
    with zipfile.ZipFile(DIR/source) as original,zipfile.ZipFile(target,'w',zipfile.ZIP_DEFLATED) as modified:
        for entry in original.infolist():
            data=original.read(entry.filename)
            if entry.filename=='[Content_Types].xml':data=data.replace(old.encode(),new.encode())
            modified.writestr(entry,data)
    check(extension+' input to PDF',lambda target=target:require(len(PdfReader(office.convert(target,'pdf',{})).pages)>0))
for source in ['slides-converted.odp','document-converted.rtf','document-converted.odt','sheets-converted.ods']:
    path=DIR/source;check(path.suffix+' input to PDF',lambda path=path:require(len(PdfReader(office.convert(path,'pdf',{})).pages)>0))
for fmt in ['xlsx','pdf','png','jpg']:
    check('UTF8 CSV to '+fmt,lambda fmt=fmt:require(Path(office.convert(DIR/'source.csv',fmt,{})).exists()))
def literal_csv():
    path=office.convert(DIR/'source.csv','xlsx',{});workbook=load_workbook(path);cell=workbook.active['B3'];require(cell.value=='=1+2' and cell.data_type=='s');workbook.close()
check('CSV leading equals retained as literal data',literal_csv)
def spreadsheet_values():
    path=office.convert(DIR/'sheets.xlsx','json',{});data=json.loads(Path(path).read_text());require(data[0]['Rows'][1][0]=='中文项目' and data[0]['Rows'][2][1]==130 and data[1]['Rows'][0][1]==42)
check('Excel formula values and all sheets preserved',spreadsheet_values)
for name in ['slides.pptx','document.docx','sheets.xlsx']:
    def menu(name=name):
        target=DIR/(name+'.capabilities.json');subprocess.run([str(APP/'DesktopDrop.exe'),'--capabilities',str(DIR/name),str(target)],check=True,timeout=20)
        data=json.loads(target.read_text());ids=[op['Id'] for op in data['Conversions']];require(data['Category']=='office' and 'convert:pdf' in ids and 'convert:png' in ids and not any(id.startswith('pack:') for id in ids))
    check('actual menu classification '+name,menu)
report={'passed':sum(x['passed'] for x in checks),'failed':sum(not x['passed'] for x in checks),'checks':checks};(ROOT/'outputs/office-extra-tests.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf8');print(json.dumps(report,ensure_ascii=False),flush=True);sys.exit(bool(report['failed']))
