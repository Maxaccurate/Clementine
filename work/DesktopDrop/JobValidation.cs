using System;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace DesktopDrop;

internal static class JobValidation
{
    public static void ResolveRatio(Dictionary<string,string> values)
    {
        if(values.GetValueOrDefault("ratio")!="custom")return;
        if(!double.TryParse(values.GetValueOrDefault("ratioWidth"),NumberStyles.Float,CultureInfo.InvariantCulture,out double width)||!double.TryParse(values.GetValueOrDefault("ratioHeight"),NumberStyles.Float,CultureInfo.InvariantCulture,out double height)||!double.IsFinite(width)||!double.IsFinite(height)||width<=0||height<=0)throw new ArgumentException("自定义比例的宽、高都需要大于 0，例如 3 : 2 或 2.35 : 1。");
        values["ratio"]=$"{width.ToString(CultureInfo.InvariantCulture)}:{height.ToString(CultureInfo.InvariantCulture)}";
    }
    public static double Time(string value)
    {
        string[] parts=value.Trim().Split(':');
        if(parts.Length>3||parts.Any(p=>!double.TryParse(p,NumberStyles.Float,CultureInfo.InvariantCulture,out double v)||!double.IsFinite(v)||v<0))throw new ArgumentException("时间格式应为秒数或 时:分:秒。");
        if(parts.Length>1&&parts.Skip(1).Any(p=>double.Parse(p,CultureInfo.InvariantCulture)>=60))throw new ArgumentException("时间中的分、秒需小于 60。");
        return parts.Reverse().Select((p,i)=>double.Parse(p,CultureInfo.InvariantCulture)*Math.Pow(60,i)).Sum();
    }
    public static void Check(ConversionJob job,int width=0,int height=0,double duration=0,int pages=0)
    {
        var values=job.Parameters??new Dictionary<string,string>();
        double N(string key,double fallback=0)=>values.TryGetValue(key,out var v)&&!string.IsNullOrWhiteSpace(v)?Time(v):fallback;
        double Signed(string key)=>double.Parse(values[key],CultureInfo.InvariantCulture);
        void Range(string key,double min,double max,bool integer=false)
        {
            if(!values.TryGetValue(key,out string? raw)||string.IsNullOrWhiteSpace(raw))return;
            if(!double.TryParse(raw,NumberStyles.Float,CultureInfo.InvariantCulture,out double v)||!double.IsFinite(v)||v<min||v>max||integer&&v!=Math.Truncate(v))throw new ArgumentException($"{Catalog.Definition(job.Action,Catalog.Category(job.Paths[0])).Fields?.FirstOrDefault(f=>f.Name==key)?.Label??key}需在 {min}–{max} 范围内"+(integer?"，并填写整数。":"。"));
        }
        foreach(var field in Catalog.Definition(job.Action,Catalog.Category(job.Paths[0])).Fields??[])
        {
            if(field.Kind=="number")
            {
                if(field.Name is "start"or"end"or"time")_ = N(field.Name);
                else if(!double.TryParse(values.GetValueOrDefault(field.Name,field.Default),NumberStyles.Float,CultureInfo.InvariantCulture,out double v)||!double.IsFinite(v))throw new ArgumentException(field.Label+"需要有效数字。");
            }
            if(field.Kind=="json"&&values.TryGetValue(field.Name,out string? raw)&&!string.IsNullOrWhiteSpace(raw))
            {
                try{using var doc=JsonDocument.Parse(raw);if(doc.RootElement.ValueKind!=(field.Name=="rotations"?JsonValueKind.Object:JsonValueKind.Array))throw new JsonException();}
                catch(JsonException){throw new ArgumentException(field.Label+"的格式有误，请检查高级参数。");}
            }
        }
        foreach(string k in new[]{"x","y","width","height","maxEdge","targetKB","padding","radius","shadow","backgroundBlur","gap","columns"})Range(k,0,1000000,true);
        Range("frame",-1000000,1000000,true);
        foreach(string k in new[]{"canvasWidth","canvasHeight"})Range(k,1,20000,true);
        Range("parts",2,1000,true);Range("pagesPerFile",1,100000,true);Range("blockSize",2,100,true);Range("speed",.125,8);Range("bitrate",8,512);Range("leftGain",0,10);Range("rightGain",0,10);
        Range("quality",1,Catalog.Category(job.Paths[0])=="video"?51:100,true);Range("exposure",-10,10);Range("contrast",0,5);Range("saturation",0,5);
        foreach(string k in new[]{"temperature","shadows","highlights"})Range(k,-100,100);
        foreach(string k in new[]{"clarity","dehaze","grain"})Range(k,0,100);
        Range("denoise",0,30);Range("loudness",-70,-5);Range("range",1,50);Range("peak",-9,0);
        if(values.TryGetValue("ratio",out string? ratio)&&ratio!="free")
        {
            string[] parts=ratio.Split(':');if(parts.Length!=2||parts.Any(p=>!double.TryParse(p,NumberStyles.Float,CultureInfo.InvariantCulture,out double v)||!double.IsFinite(v)||v<=0))throw new ArgumentException("裁剪比例的宽、高必须大于 0。");
        }
        if(values.ContainsKey("rotation")&&!new[]{0d,90,180,270}.Contains(Signed("rotation")))throw new ArgumentException("旋转角度只能是 0、90、180 或 270。");
        if(values.ContainsKey("canvasWidth")&&N("canvasWidth")*N("canvasHeight")>40000000)throw new ArgumentException("画布最多支持 4000 万像素。");
        if(job.Action=="frameImage"&&N("padding")*2>=Math.Min(N("canvasWidth"),N("canvasHeight")))throw new ArgumentException("留白必须小于画布短边的一半。");
        if(job.Action is "cropImage"or"cropVideo"or"redactImage"or"redactVideo")
        {
            if(N("width")<1||N("height")<1)throw new ArgumentException("请先拖动选择有效区域。");
            if(width>0&&N("x")+N("width")>width||height>0&&N("y")+N("height")>height)throw new ArgumentException("选区超出原始画面，请缩小或重新选择。");
        }
        if(values.ContainsKey("start")&&values.ContainsKey("end"))
        {
            double start=N("start"),end=N("end");if(end==0&&duration>0)end=duration;
            if(end>0&&end<=start||duration>0&&(start>=duration||end>duration+.001))throw new ArgumentException("结束时间必须晚于开始时间，且在文件时长内。");
        }
        foreach(string key in new[]{"times","splitPoints"})if(values.TryGetValue(key,out string? raw)&&!string.IsNullOrWhiteSpace(raw))foreach(string item in raw.Split(',')){double t=Time(item);if(duration>0&&t>=duration||key=="splitPoints"&&t<=0)throw new ArgumentException("时间点需在文件时长内。");}
        if(values.TryGetValue("pageOrder",out string? pageOrder)&&!string.IsNullOrWhiteSpace(pageOrder))foreach(string item in pageOrder.Split(','))if(!int.TryParse(item.Trim(),out int page)||page<1||pages>0&&page>pages)throw new ArgumentException($"页码需为 1{(pages>0?$"–{pages}":"")} 的整数，以逗号分隔。");
    }
}
