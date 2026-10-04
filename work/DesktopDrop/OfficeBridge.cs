using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace DesktopDrop;

internal static class OfficeBridge
{
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint process);
    private static void Release(object? value){if(value!=null&&Marshal.IsComObject(value))try{Marshal.FinalReleaseComObject(value);}catch(InvalidComObjectException){}}
    public static string Family(string input)
    {
        string ext=Path.GetExtension(input).ToLowerInvariant();
        return new[]{".ppt",".pptx",".pptm",".pps",".ppsx",".odp"}.Contains(ext)?"PowerPoint":new[]{".doc",".docx",".docm",".rtf",".odt"}.Contains(ext)?"Word":"Excel";
    }
    public static bool Available(string family)=>Type.GetTypeFromProgID(family+".Application")!=null;
    public static void Export(string input,string format,string output)
    {
        string family=Family(input),processName=family=="PowerPoint"?"POWERPNT":family=="Word"?"WINWORD":"EXCEL";
        var before=Process.GetProcessesByName(processName).Select(p=>p.Id).ToHashSet();
        Type type=Type.GetTypeFromProgID(family+".Application")??throw new IOException($"此转换需要已安装的 Microsoft {family}");
        dynamic? app=null,collection=null,document=null;object? security=null,alerts=null,events=null,links=null;bool owned=false;
        input=Path.GetFullPath(input);output=Path.GetFullPath(output);
        // Encrypted modern Office packages are compound files instead of ZIP files.
        if(new[]{".pptx",".pptm",".ppsx",".docx",".docm",".xlsx",".xlsm",".xlsb"}.Contains(Path.GetExtension(input).ToLowerInvariant()))
        {
            using var file=File.OpenRead(input);if(file.ReadByte()!=0x50)throw new IOException("该 Office 文件被加密或内容损坏，请先在 Office 中确认可正常打开并另存为未加密副本");
        }
        try
        {
            app=Activator.CreateInstance(type)??throw new IOException("无法启动 Office 转换引擎");
            try{int hwnd=Convert.ToInt32(app.Hwnd);GetWindowThreadProcessId(new IntPtr(hwnd),out uint pid);owned=pid!=0&&!before.Contains((int)pid);}catch{}
            if(!owned&&before.Count==0)owned=Process.GetProcessesByName(processName).Count(p=>!before.Contains(p.Id))==1;
            security=app.AutomationSecurity;app.AutomationSecurity=3;
            alerts=app.DisplayAlerts;
            if(family=="PowerPoint")
            {
                app.DisplayAlerts=1;collection=app.Presentations;
                document=collection.Open(input,ReadOnly:-1,Untitled:0,WithWindow:0);
                if(format=="txt")File.WriteAllText(output,PresentationText(document),new UTF8Encoding(true));
                else document.SaveAs(output,format switch{"pdf"=>32,"pptx"=>24,"ppt"=>1,"odp"=>35,_=>throw new IOException("不支持的幻灯片输出格式")});
            }
            else if(family=="Word")
            {
                if(owned)app.Visible=false;app.DisplayAlerts=0;collection=app.Documents;
                document=collection.Open(FileName:input,ConfirmConversions:false,ReadOnly:true,AddToRecentFiles:false,Visible:false,PasswordDocument:"",WritePasswordDocument:"");
                if(format=="pdf")document.ExportAsFixedFormat(OutputFileName:output,ExportFormat:17,OpenAfterExport:false);
                else if(format=="txt")
                {
                    dynamic range=document.Content;try{File.WriteAllText(output,((string)range.Text).Replace("\r","\n").Replace("\a","\t"),new UTF8Encoding(true));}finally{Release(range);}
                }
                else document.SaveAs2(FileName:output,FileFormat:format switch{"docx"=>16,"doc"=>0,"rtf"=>6,"odt"=>23,"html"=>8,_=>throw new IOException("不支持的 Word 输出格式")},AddToRecentFiles:false);
            }
            else
            {
                if(owned)app.Visible=false;app.DisplayAlerts=false;events=app.EnableEvents;links=app.AskToUpdateLinks;app.EnableEvents=false;app.AskToUpdateLinks=false;collection=app.Workbooks;
                document=collection.Open(Filename:input,UpdateLinks:0,ReadOnly:true,IgnoreReadOnlyRecommended:true,AddToMru:false,Password:"",WriteResPassword:"");
                if(format=="pdf")document.ExportAsFixedFormat(Type:0,Filename:output,Quality:0,IncludeDocProperties:true,IgnorePrintAreas:false,OpenAfterPublish:false);
                else if(format=="sheet-data")ExportSheetData(document,output);
                else document.SaveAs(Filename:output,FileFormat:format switch{"xlsx"=>51,"xls"=>56,"ods"=>60,_=>throw new IOException("不支持的工作簿输出格式")});
            }
        }
        catch(COMException ex){throw new IOException($"{family} 转换失败：{ex.Message}",ex);}
        finally
        {
            if(document!=null)try
            {
                if(family=="PowerPoint"){document.Saved=-1;document.Close();}
                else if(family=="Word")document.Close(0);
                else document.Close(false);
            }catch{}
            Release(document);
            if(app!=null)
            {
                try{if(security!=null)app.AutomationSecurity=security;if(alerts!=null)app.DisplayAlerts=alerts;if(events!=null)app.EnableEvents=events;if(links!=null)app.AskToUpdateLinks=links;}catch{}
                try{if(owned&&collection!=null&&Convert.ToInt32(collection!.Count)==0)app.Quit();}catch{}
            }
            Release(collection);Release(app);
        }
        if(!File.Exists(output)||new FileInfo(output).Length==0)throw new IOException("Office 没有生成有效的输出文件");
    }
    private static string PresentationText(dynamic document)
    {
        var text=new StringBuilder();dynamic slides=document.Slides;
        try
        {
            int count=slides.Count;for(int i=1;i<=count;i++)
            {
                dynamic slide=slides.Item(i);try
                {
                    text.AppendLine($"第 {i} 页");dynamic shapes=slide.Shapes;try{ShapeText(shapes,text);}finally{Release(shapes);}text.AppendLine();
                }finally{Release(slide);}
            }
        }finally{Release(slides);}return text.ToString();
    }
    private static void ShapeText(dynamic shapes,StringBuilder text)
    {
        int count=shapes.Count;for(int i=1;i<=count;i++)
        {
            dynamic shape=shapes.Item(i);try
            {
                if(Convert.ToInt32(shape.Type)==6){dynamic group=shape.GroupItems;try{ShapeText(group,text);}finally{Release(group);}}
                else if(Convert.ToInt32(shape.HasTextFrame)!=0)
                {
                    dynamic frame=shape.TextFrame;try{if(Convert.ToInt32(frame.HasText)!=0){dynamic range=frame.TextRange;try{text.AppendLine(((string)range.Text).Replace("\r","\n"));}finally{Release(range);}}}finally{Release(frame);}
                }
                if(Convert.ToInt32(shape.HasTable)!=0)
                {
                    dynamic table=shape.Table;try
                    {
                        int rows=table.Rows.Count,columns=table.Columns.Count;
                        for(int row=1;row<=rows;row++){for(int col=1;col<=columns;col++){dynamic cell=table.Cell(row,col);try{text.Append((string)cell.Shape.TextFrame.TextRange.Text).Append('\t');}finally{Release(cell);}}text.AppendLine();}
                    }finally{Release(table);}
                }
            }finally{Release(shape);}
        }
    }
    private static void ExportSheetData(dynamic document,string output)
    {
        var sheets=new List<object>();dynamic worksheets=document.Worksheets;
        try
        {
            int count=worksheets.Count;for(int i=1;i<=count;i++)
            {
                dynamic sheet=worksheets.Item(i);dynamic? range=null;try
                {
                    range=sheet.UsedRange;object? value=range.Value2;var rows=new List<object?[]>();
                    if(value is Array array)
                    {
                        for(int row=array.GetLowerBound(0);row<=array.GetUpperBound(0);row++){var values=new List<object?>();for(int col=array.GetLowerBound(1);col<=array.GetUpperBound(1);col++)values.Add(array.GetValue(row,col));rows.Add(values.ToArray());}
                    }
                    else rows.Add([value]);
                    sheets.Add(new{Name=(string)sheet.Name,Rows=rows});
                }finally{Release(range);Release(sheet);}
            }
        }finally{Release(worksheets);}File.WriteAllText(output,JsonSerializer.Serialize(sheets),new UTF8Encoding(false));
    }
}
