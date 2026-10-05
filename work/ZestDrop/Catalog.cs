using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ZestDrop;

internal sealed record Field(string Name, string Label, string Default = "", string Kind = "text", string[]? Choices = null);
internal sealed record Operation(string Id, string Label, bool Tool = false, Field[]? Fields = null, bool Ordered = false);

internal static class Catalog
{
    public static string Extension(string path)
    {
        string ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        return ext switch { "jpeg" => "jpg", "tif" => "tiff", "heif" => "heic", "aif" => "aiff", "tgz" => "gz", _ => ext };
    }
    public static readonly string[] Images = ["jpg", "png", "webp", "heic", "tiff", "svg", "avif", "bmp"];
    public static readonly string[] Audio = ["mp3", "m4a", "wav", "flac", "ogg", "opus", "aiff", "wma"];
    public static readonly string[] Video = ["mp4", "mov", "mkv", "webm", "avi", "wmv"];
    public static readonly string[] Archives = ["zip", "tar", "gz", "rar"];
    public static readonly string[] OfficeSlides = ["ppt", "pptx", "pptm", "pps", "ppsx", "odp"];
    public static readonly string[] OfficeWords = ["doc", "docx", "docm", "rtf", "odt"];
    public static readonly string[] OfficeSheets = ["xls", "xlsx", "xlsm", "xlsb", "ods", "csv", "tsv"];
    private static readonly string[] ImageTargets = ["jpg", "png", "webp", "heic", "tiff", "avif", "bmp", "pdf"];
    public static string Category(string path)
    {
        string ext = Extension(path);
        return Images.Contains(ext) ? "image" : Audio.Contains(ext) ? "audio" : Video.Contains(ext) || ext == "gif" ? "video" : OfficeSlides.Contains(ext) || OfficeWords.Contains(ext) || OfficeSheets.Contains(ext) ? "office" : new[] { "pdf", "txt", "srt", "vtt" }.Contains(ext) ? "document" : Archives.Contains(ext) ? "archive" : "file";
    }
    public static bool PackingOnly(string[] paths) => paths.Length > 0 && paths.All(path => Directory.Exists(path) || Category(path) == "file");
    private static string[] Targets(string ext)
    {
        if (OfficeSlides.Contains(ext))
            return OfficeBridge.Available("PowerPoint") ? ["pdf", "png", "jpg", "txt", "pptx", "ppt", "odp"] : new[] { "pptx", "pptm", "ppsx" }.Contains(ext) ? ["txt"] : [];
        if (OfficeWords.Contains(ext))
            return OfficeBridge.Available("Word") ? ["pdf", "png", "jpg", "txt", "docx", "doc", "rtf", "odt", "html"] : new[] { "docx", "docm" }.Contains(ext) ? ["txt"] : [];
        if (OfficeSheets.Contains(ext))
            return OfficeBridge.Available("Excel") ? ["pdf", "png", "jpg", "xlsx", "xls", "ods", "csv", "tsv", "json", "txt"] : ext is "xlsx" or "xlsm" ? ["csv", "tsv", "json", "txt"] : ext is "csv" or "tsv" ? ["xlsx", "csv", "tsv", "json", "txt"] : [];
        return ext is "jpg" or "png" ? [.. ImageTargets, "docx"] : Images.Contains(ext) ? ImageTargets : Audio.Contains(ext) ? Audio : Video.Contains(ext) ? [.. Video, "gif", "mp3"] : ext == "gif" ? Video : ext == "pdf" ? ["docx", "jpg", "png", "txt"] : ext == "txt" ? ["pdf", "jpg", "png", "srt", "vtt"] : ext is "srt" or "vtt" ? ["srt", "vtt", "txt"] : Archives;
    }
    public static List<Operation> Options(string[] paths, bool tools)
    {
        if (paths.Length == 0 || paths.Any(p => !File.Exists(p) && !Directory.Exists(p)))
            return [];
        if (tools)
        {
            var ids = Tools(paths[0], paths.Length).ToList();
            foreach (string path in paths.Skip(1))
                ids = ids.Intersect(Tools(path, paths.Length)).ToList();
            return ids.Select(id => Definition(id, Category(paths[0]))).ToList();
        }
        if (PackingOnly(paths))
            return Archives.Select(target => new Operation("pack:" + target, L.T("打包 ") + (target == "gz" ? "GZIP" : target.ToUpperInvariant()))).ToList();
        string ext = Extension(paths[0]);
        var targets = Targets(ext).ToList();
        foreach (string path in paths.Skip(1))
            targets = targets.Intersect(Targets(Extension(path))).ToList();
        return targets.Where(target => !paths.All(p => Extension(p) == target)).Select(target => new Operation("convert:" + target, target == "gz" ? "GZIP" : target.ToUpperInvariant())).ToList();
    }
    private static string[] Tools(string path, int count)
    {
        string kind = Category(path), ext = Extension(path);
        if (kind == "image")
        {
            if (ext == "svg")
                return count > 1 ? ["createPDF", "createCollage"] : [];
            if (ext == "bmp")
                return count > 1 ? ["compress", "createPDF", "createCollage"] : ["compress", "editImage", "frameImage", "cropImage", "redactImage"];
            return count > 1 ? ["compress", "removeMetadata", "createPDF", "createCollage"] : ["compress", "removeMetadata", "editImage", "frameImage", "cropImage", "redactImage"];
        }
        if (kind == "audio")
            return count > 1 ? ["compress", "normalizeAudio"] : ["compress", "removeMetadata", "normalizeAudio", "audioToVideo", "trimAudio", "audioChannels", "redactAudio"];
        if (kind == "video")
            return ext == "gif" ? ["removeMetadata"] : count > 1 ? ["compress", "muteVideo", "joinVideos"] : ["compress", "removeMetadata", "muteVideo", "trimVideo", "cropVideo", "changeVideoSpeed", "videoSnapshots", "splitVideo", "redactVideo"];
        if (kind == "office")
        {
            string family = OfficeSlides.Contains(ext) ? "PowerPoint" : OfficeWords.Contains(ext) ? "Word" : "Excel";
            bool render = OfficeBridge.Available(family), modern = new[] { "pptx", "pptm", "ppsx", "docx", "docm", "xlsx", "xlsm", "xlsb" }.Contains(ext);
            if (count > 1)
                return render ? ["officeMergePDF"] : [];
            return render ? modern ? ["officePdf", "removeMetadata"] : ["officePdf"] : modern ? ["removeMetadata"] : [];
        }
        if (ext == "pdf")
            return count > 1 ? ["compress", "splitPDF", "mergePDF"] : ["compress", "removeMetadata", "splitPDF", "organizePDF"];
        return Archives.Contains(ext) ? ["extractArchive"] : [];
    }
    private static Field N(string name, string label, string value) => new(name, label, value, "number");
    private static Field T(string name, string label, string value = "", string kind = "text") => new(name, label, value, kind);
    private static Field C(string name, string label, string value, params string[] choices) => new(name, label, value, "choice", choices);
    private static Field[] Crop => [N("x", L.T("左侧 x（像素）"), "0"), N("y", L.T("顶部 y（像素）"), "0"), N("width", L.T("宽度（像素）"), "0"), N("height", L.T("高度（像素）"), "0")];
    private static Field[] Times => [N("start", L.T("开始（秒 / 时:分:秒）"), "0"), N("end", L.T("结束（0 表示结尾）"), "0")];
    private static Field[] Ratio => [C("ratio", L.T("比例"), "free", "free", "1:1", "4:3", "16:9", "9:16", "custom"), N("ratioWidth", L.T("宽比例"), "3"), N("ratioHeight", L.T("高比例"), "2")];
    public static Operation Definition(string id, string kind) => id switch
    {
        "officePdf" => new(id, L.T("选页导出 PDF"), true, [T("pageOrder", L.T("导出页码（逗号分隔，留空全部）"))]),
        "officeMergePDF" => new(id, L.T("合并为 PDF"), true, [], true),
        "compress" => new(id, L.T("压缩"), true, kind == "audio" ? [N("bitrate", L.T("比特率（kbps）"), "96"), N("targetKB", L.T("目标大小（KB，0 不限制）"), "0")] : [N("quality", kind == "video" ? L.T("CRF（越高压缩越强）") : L.T("质量（1–100）"), kind == "video" ? "28" : "75"), N("maxEdge", L.T("最长边（像素）"), kind == "video" ? "1280" : "2000"), N("targetKB", L.T("目标大小（KB，0 不限制）"), "0")]),
        "removeMetadata" => new(id, L.T("元数据"), true, [T("remove", L.T("移除全部元数据"), "true", "bool"), T("search", L.T("搜索元数据字段")), T("metadata", L.T("元数据字段（关闭全部移除后可修改）"), "{}", "metadata")]),
        "editImage" => new(id, L.T("编辑图片"), true, [N("exposure", L.T("曝光（EV）"), "0"), N("contrast", L.T("对比度（1 原始）"), "1"), N("saturation", L.T("饱和度（1 原始）"), "1"), N("temperature", L.T("色温（-100 到 100）"), "0"), N("shadows", L.T("阴影（-100 到 100）"), "0"), N("highlights", L.T("高光（-100 到 100）"), "0"), N("clarity", L.T("清晰度（0–100）"), "0"), N("dehaze", L.T("去雾（0–100）"), "0"), N("denoise", L.T("降噪（0–30）"), "0"), N("grain", L.T("颗粒（0–100）"), "0"), T("annotations", L.T("标注（JSON，可选）"), "", "json")]),
        "frameImage" => new(id, L.T("添加背景"), true, [N("canvasWidth", L.T("画布宽度"), "1400"), N("canvasHeight", L.T("画布高度"), "1000"), N("padding", L.T("留白"), "80"), N("radius", L.T("圆角"), "24"), N("shadow", L.T("阴影"), "20"), T("background", L.T("背景色"), "#f0ebff"), T("gradient", L.T("渐变结束色")), T("backgroundImage", L.T("背景图片"), "", "file"), N("backgroundBlur", L.T("背景模糊"), "0")]),
        "cropImage" => new(id, L.T("裁剪"), true, [.. Crop, .. Ratio]),
        "redactImage" => new(id, L.T("图片打码"), true, [.. Crop, C("style", L.T("打码方式"), "pixelate", "pixelate", "blur", "solid"), N("blockSize", L.T("马赛克颗粒（像素）"), "12"), T("color", L.T("覆盖颜色"), "#000000"), T("regions", L.T("多个区域"), "", "json")]),
        "createPDF" => new(id, L.T("创建 PDF"), true, [], true),
        "createCollage" => new(id, L.T("拼图"), true, [C("layout", L.T("布局"), "grid", "grid", "row", "column", "featured"), N("canvasWidth", L.T("画布宽度"), "1600"), N("canvasHeight", L.T("画布高度"), "1200"), N("columns", L.T("网格列数（0 自动）"), "0"), N("gap", L.T("间距"), "16"), N("radius", L.T("圆角"), "0"), T("background", L.T("背景色"), "#ffffff")], true),
        "muteVideo" => new(id, L.T("移除音频"), true),
        "trimVideo" => new(id, L.T("裁剪时段"), true, Times),
        "cropVideo" => new(id, L.T("裁剪画面"), true, [.. Crop, .. Ratio]),
        "changeVideoSpeed" => new(id, L.T("调整速度"), true, [N("speed", L.T("速度（0.125–8）"), "2")]),
        "joinVideos" => new(id, L.T("拼接视频"), true, [], true),
        "videoSnapshots" => new(id, L.T("保存帧"), true, [N("time", L.T("时间（秒）"), "0"), N("frame", L.T("相对帧数"), "0"), T("times", L.T("多个时间点（秒，逗号分隔）"))]),
        "splitVideo" => new(id, L.T("分段"), true, [N("parts", L.T("均分段数"), "2"), T("splitPoints", L.T("自定义分割点（秒，逗号分隔）"))]),
        "redactVideo" => new(id, L.T("视频打码"), true, [.. Crop, .. Times, C("style", L.T("打码方式"), "pixelate", "pixelate", "blur", "solid"), N("blockSize", L.T("马赛克颗粒（像素）"), "12"), T("regions", L.T("多个区域和各自时段"), "", "json")]),
        "normalizeAudio" => new(id, L.T("标准化响度"), true, [N("loudness", L.T("目标响度（LUFS）"), "-16"), N("range", L.T("响度范围（LRA）"), "11"), N("peak", L.T("真峰值（dBTP）"), "-1.5")]),
        "audioToVideo" => new(id, L.T("音频可视化"), true, [C("aspect", L.T("画面比例"), "landscape", "landscape", "portrait", "square"), T("backgroundImage", L.T("静态背景图（留空为波形）"), "", "file")]),
        "trimAudio" => new(id, L.T("裁剪音频"), true, [.. Times, T("removeSilence", L.T("去掉开头和结尾静音"), "false", "bool")]),
        "audioChannels" => new(id, L.T("声道"), true, [C("channels", L.T("输出声道"), "mono", "mono", "stereo"), N("leftGain", L.T("左侧音量（1 原始）"), "1"), N("rightGain", L.T("右侧音量（1 原始）"), "1")]),
        "redactAudio" => new(id, L.T("消音蜂鸣"), true, [.. Times, T("ranges", L.T("多个时段"), "", "json")]),
        "splitPDF" => new(id, L.T("拆分 PDF"), true, [N("pagesPerFile", L.T("每份页数"), "1"), T("password", L.T("PDF 密码（如需要）"), "", "password")]),
        "mergePDF" => new(id, L.T("合并 PDF"), true, [T("password", L.T("PDF 密码（如需要）"), "", "password")], true),
        "organizePDF" => new(id, L.T("管理页面"), true, [T("pageOrder", L.T("页码顺序（可重复或省略）")), N("rotation", L.T("全部旋转角度（0/90/180/270）"), "0"), T("rotations", L.T("逐页旋转，如 {\"1\":90}"), "{}", "json"), T("password", L.T("PDF 密码（如需要）"), "", "password")]),
        "extractArchive" => new(id, L.T("解包"), true),
        _ => throw new ArgumentException(L.T("未知工具 ") + id)
    };
}
