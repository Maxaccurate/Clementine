using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ZestDrop;

internal sealed record Field(string Name, string Label, string Default = "", string Kind = "text", string[]? Choices = null);
// Parts: the tools a combined entry opens together. Chain: each part works on the previous parts' result.
internal sealed record Operation(string Id, string Label, bool Tool = false, Field[]? Fields = null, bool Ordered = false, string[]? Parts = null, bool Chain = false);

internal sealed record ToolGroup(string Label, List<Operation> Items);

internal static class Catalog
{
    // Files with ten or more tools show categories first; this is how each kind of file is divided.
    private static readonly Dictionary<string, (string Label, string[] Ids)[]> Categories = new()
    {
        ["image"] = [("尺寸与方向", ["cropImage", "rotateImage", "resizeImage"]), ("外观与标记", ["editImage", "frameImage", "watermark", "redactImage"]),
            ("输出与整理", ["compress", "removeMetadata", "removeLocation", "ocrImage", "makeIcon", "createPDF", "createCollage", "createAnimation", "packArchive"])],
        ["video"] = [("剪辑", ["trimVideo", "splitVideo", "joinVideos", "changeVideoSpeed", "videoSnapshots", "videoEffects"]), ("画面", ["cropVideo", "rotateVideo", "videoSettings", "watermark", "redactVideo"]),
            ("声音与字幕", ["muteVideo", "subtitlesAudio"]), ("输出与整理", ["compress", "removeMetadata", "removeLocation", "videoToGif", "packArchive"])],
        ["audio"] = [("剪辑", ["trimAudio", "redactAudio", "joinAudio"]), ("声音", ["normalizeAudio", "audioChannels", "audioEffects"]),
            ("输出与整理", ["compress", "removeMetadata", "audioToVideo", "ringtone", "packArchive"])],
        ["document"] = [("页面", ["splitPDF", "mergePDF", "organizePDF", "extractPdfImages", "pdfNumbers"]), ("安全与识别", ["pdfPassword", "ocrPDF", "watermark", "removeMetadata"]),
            ("输出与整理", ["compress", "packArchive"])],
    };

    // When a single file has ten or more tools, related tools share one window. The older two-wheel layout stays
    // available (tray menu) and is used instead when chosen.
    public const string LayoutKey = "wheelLayout";
    public static bool TwoWheels => Settings.GetString(LayoutKey) == "twoWheels";
    private static readonly Dictionary<string, (string Id, string Label, string[] Parts, bool Chain)[]> Merged = new()
    {
        ["image"] = [("merge:cropRotate", "裁剪与旋转", ["cropImage", "rotateImage"], true), ("merge:sizeCompress", "尺寸与压缩", ["resizeImage", "compress"], true),
            ("merge:frameWatermark", "背景与水印", ["frameImage", "watermark"], true)],
        ["video"] = [("merge:trimSplit", "剪辑时段与分段", ["trimVideo", "splitVideo"], false), ("merge:speedEffects", "速度与效果", ["changeVideoSpeed", "videoEffects"], true),
            ("merge:cropRotateVideo", "画面裁剪与旋转", ["cropVideo", "rotateVideo"], true), ("merge:resolutionCompress", "分辨率与压缩", ["videoSettings", "compress"], true),
            ("merge:soundSubtitles", "声音与字幕", ["muteVideo", "subtitlesAudio"], true), ("merge:watermarkRedact", "水印与打码", ["watermark", "redactVideo"], true),
            ("merge:framesGif", "保存帧与 GIF", ["videoSnapshots", "videoToGif"], false)],
    };

    // Replaces each tool that belongs to a combined entry by that entry (in the place of its first tool).
    // Several files can share a combined window as well; each of its tools then saves on its own.
    private static List<string> Merge(string kind, List<string> ids)
    {
        if (TwoWheels || ids.Count < 10 || !Merged.TryGetValue(kind, out var plan))
            return ids;
        var result = new List<string>();
        foreach (string id in ids)
        {
            var entry = plan.FirstOrDefault(m => m.Parts.Contains(id) && m.Parts.All(ids.Contains));
            string chosen = entry.Id ?? id;
            if (!result.Contains(chosen))
                result.Add(chosen);
        }
        return result;
    }

    private static Operation? MergedDefinition(string id) =>
        Merged.Values.SelectMany(x => x).Where(m => m.Id == id).Select(m => new Operation(m.Id, L.T(m.Label), true, null, false, m.Parts, m.Chain)).FirstOrDefault();

    public static List<ToolGroup>? Grouped(List<Operation> operations, string kind)
    {
        if (operations.Count < 10 || !Categories.TryGetValue(kind, out var plan))
            return null;
        var groups = new List<ToolGroup>();
        var placed = new HashSet<string>();
        void Add(string label, IEnumerable<Operation> items)
        {
            var list = items.ToList();
            if (list.Count == 0)
                return;
            groups.Add(new ToolGroup(L.T(label), list));
            foreach (var item in list)
                placed.Add(item.Id);
        }
        foreach (var (label, ids) in plan)
            Add(label, ids.Select(id => operations.FirstOrDefault(o => o.Id == id)).Where(o => o != null).Select(o => o!));
        Add("流程", operations.Where(o => o.Id.StartsWith(Flows.Prefix)));
        Add("其他", operations.Where(o => !placed.Contains(o.Id)));
        return groups.Count >= 2 ? groups : null;
    }

    public static string Extension(string path)
    {
        string ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        return ext switch { "jpeg" => "jpg", "tif" => "tiff", "heif" => "heic", "aif" => "aiff", "tgz" => "gz", _ => ext };
    }
    public static readonly string[] Images = ["jpg", "png", "webp", "heic", "tiff", "svg", "avif", "bmp"];
    public static readonly string[] Audio = ["mp3", "m4a", "wav", "flac", "ogg", "opus", "aiff", "wma"];
    public static readonly string[] Video = ["mp4", "mov", "mkv", "webm", "avi", "wmv"];
    public static readonly string[] Archives = ["zip", "tar", "gz", "rar", "7z"];
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
            ids = Merge(Category(paths[0]), ids);
            var chosen = ids.Select(id => Definition(id, Category(paths[0]))).Select(o => paths.Length > 1 && o.Chain ? o with { Chain = false } : o).ToList();
            var flows = Flows.For(paths);
            // Flows follow the tools; if they would make the wheel too crowded they share one entry.
            if (!TwoWheels && chosen.Count + flows.Count >= 10 && flows.Count > 1)
                chosen.Add(new Operation(Flows.MenuId, L.T("流程…"), true));
            else
                chosen.AddRange(flows);
            return chosen;
        }
        if (PackingOnly(paths))
            return Archives.Select(target => new Operation("pack:" + target, L.T("打包 ") + (target == "gz" ? "GZIP" : target.ToUpperInvariant()))).ToList();
        string ext = Extension(paths[0]);
        var targets = Targets(ext).ToList();
        foreach (string path in paths.Skip(1))
            targets = targets.Intersect(Targets(Extension(path))).ToList();
        return targets.Where(target => !paths.All(p => Extension(p) == target)).Select(target => new Operation("convert:" + target, target == "gz" ? "GZIP" : target.ToUpperInvariant())).ToList();
    }
    public static string[] ToolIds(string kind) => Tools(kind switch { "image" => "x.jpg", "video" => "x.mp4", "audio" => "x.mp3", _ => "x.pdf" }, 1);
    private static string[] Tools(string path, int count)
    {
        string kind = Category(path), ext = Extension(path);
        if (kind == "image")
        {
            if (ext == "svg")
                return count > 1 ? ["createPDF", "createCollage", "createAnimation", "packArchive"] : ["resizeImage", "makeIcon"];
            if (ext == "bmp")
                return count > 1 ? ["compress", "rotateImage", "resizeImage", "watermark", "createPDF", "createCollage", "createAnimation", "packArchive"] : ["compress", "editImage", "frameImage", "cropImage", "rotateImage", "resizeImage", "watermark", "makeIcon", "ocrImage", "redactImage"];
            return count > 1 ? ["compress", "removeMetadata", "removeLocation", "rotateImage", "resizeImage", "watermark", "createPDF", "createCollage", "createAnimation", "packArchive"] : ["compress", "removeMetadata", "removeLocation", "editImage", "frameImage", "cropImage", "rotateImage", "resizeImage", "watermark", "makeIcon", "ocrImage", "redactImage"];
        }
        if (kind == "audio")
            return count > 1 ? ["compress", "normalizeAudio", "audioEffects", "joinAudio", "packArchive"] : ["compress", "removeMetadata", "normalizeAudio", "audioToVideo", "trimAudio", "audioChannels", "audioEffects", "ringtone", "redactAudio"];
        if (kind == "video")
            return ext == "gif" ? ["removeMetadata"] : count > 1 ? ["compress", "removeLocation", "muteVideo", "rotateVideo", "videoSettings", "watermark", "joinVideos", "packArchive"] : ["compress", "removeMetadata", "removeLocation", "muteVideo", "trimVideo", "cropVideo", "rotateVideo", "changeVideoSpeed", "videoSnapshots", "splitVideo", "videoSettings", "videoEffects", "videoToGif", "watermark", "subtitlesAudio", "redactVideo"];
        if (kind == "office")
        {
            string family = OfficeSlides.Contains(ext) ? "PowerPoint" : OfficeWords.Contains(ext) ? "Word" : "Excel";
            bool render = OfficeBridge.Available(family), modern = new[] { "pptx", "pptm", "ppsx", "docx", "docm", "xlsx", "xlsm", "xlsb" }.Contains(ext);
            if (count > 1)
                return render ? ["officeMergePDF"] : [];
            return render ? modern ? ["officePdf", "removeMetadata"] : ["officePdf"] : modern ? ["removeMetadata"] : [];
        }
        if (ext == "pdf")
            return count > 1 ? ["compress", "splitPDF", "mergePDF", "watermark", "packArchive"] : ["compress", "removeMetadata", "splitPDF", "organizePDF", "pdfPassword", "pdfNumbers", "extractPdfImages", "watermark", "ocrPDF"];
        if (kind == "file")
            return ["packArchive"];
        return Archives.Contains(ext) ? ["extractArchive"] : [];
    }
    private static Field N(string name, string label, string value) => new(name, label, value, "number");
    private static Field T(string name, string label, string value = "", string kind = "text") => new(name, label, value, kind);
    private static Field C(string name, string label, string value, params string[] choices) => new(name, label, value, "choice", choices);
    private static Field[] Crop => [N("x", L.T("左侧 x（像素）"), "0"), N("y", L.T("顶部 y（像素）"), "0"), N("width", L.T("宽度（像素）"), "0"), N("height", L.T("高度（像素）"), "0")];
    private static Field[] Rotation(bool video) => [N("angle", L.T("旋转角度（0–360°，顺时针）"), "0"), C("flip", L.T("翻转"), "none", "none", "horizontal", "vertical"), C("expand", L.T("非直角旋转时的画布"), video ? "expand" : "crop", video ? ["expand", "keep"] : ["crop", "expand", "keep"]), T("background", video ? L.T("空白处颜色") : L.T("空白处颜色（留空为透明）"), video ? "#000000" : "")];
    private static Field[] Positions => [C("position", L.T("位置"), "bottomRight", "bottomRight", "bottomCenter", "bottomLeft", "middleRight", "center", "middleLeft", "topRight", "topCenter", "topLeft")];
    private static Field[] Times => [N("start", L.T("开始（秒 / 时:分:秒）"), "0"), N("end", L.T("结束（0 表示结尾）"), "0")];
    private static Field[] Ratio => [C("ratio", L.T("比例"), "free", "free", "1:1", "4:3", "16:9", "9:16", "custom"), N("ratioWidth", L.T("宽比例"), "3"), N("ratioHeight", L.T("高比例"), "2")];
    public static Operation Definition(string id, string kind) => MergedDefinition(id) ?? id switch
    {
        "officePdf" => new(id, L.T("选页导出 PDF"), true, [T("pageOrder", L.T("导出页码（逗号分隔，留空全部）"))]),
        "officeMergePDF" => new(id, L.T("合并为 PDF"), true, [], true),
        "compress" => new(id, L.T("压缩"), true, kind == "audio" ? [N("bitrate", L.T("比特率（kbps）"), "96"), N("targetKB", L.T("目标大小（KB，0 不限制）"), "0")] : [N("quality", kind == "video" ? L.T("CRF（越高压缩越强）") : L.T("质量（1–100）"), kind == "video" ? "28" : "75"), N("maxEdge", L.T("最长边（像素）"), kind == "video" ? "1280" : "2000"), N("targetKB", L.T("目标大小（KB，0 不限制）"), "0")]),
        "removeMetadata" => new(id, L.T("元数据"), true, [T("remove", L.T("移除全部元数据"), "true", "bool"), T("search", L.T("搜索元数据字段")), T("metadata", L.T("元数据字段（关闭全部移除后可修改）"), "{}", "metadata")]),
        "editImage" => new(id, L.T("编辑图片"), true, [N("exposure", L.T("曝光（EV）"), "0"), N("contrast", L.T("对比度（1 原始）"), "1"), N("saturation", L.T("饱和度（1 原始）"), "1"), N("temperature", L.T("色温（-100 到 100）"), "0"), N("shadows", L.T("阴影（-100 到 100）"), "0"), N("highlights", L.T("高光（-100 到 100）"), "0"), N("clarity", L.T("清晰度（0–100）"), "0"), N("dehaze", L.T("去雾（0–100）"), "0"), N("denoise", L.T("降噪（0–30）"), "0"), N("grain", L.T("颗粒（0–100）"), "0"), T("annotations", L.T("标注（JSON，可选）"), "", "json")]),
        "frameImage" => new(id, L.T("添加背景"), true, [N("canvasWidth", L.T("画布宽度"), "1400"), N("canvasHeight", L.T("画布高度"), "1000"), N("padding", L.T("留白"), "80"), N("radius", L.T("圆角"), "24"), N("shadow", L.T("阴影"), "20"), T("background", L.T("背景色"), "#f0ebff"), T("gradient", L.T("渐变结束色")), T("backgroundImage", L.T("背景图片"), "", "file"), N("backgroundBlur", L.T("背景模糊"), "0")]),
        "cropImage" => new(id, L.T("裁剪"), true, [.. Crop, .. Ratio]),
        "resizeImage" => new(id, L.T("调整尺寸"), true, [C("mode", L.T("方式"), "percent", "percent", "edge", "size"), N("percent", L.T("缩放比例（%）"), "50"), N("edge", L.T("最长边（像素）"), "1920"), N("width", L.T("宽度（像素，0 为不限）"), "0"), N("height", L.T("高度（像素，0 为不限）"), "0"), T("keepRatio", L.T("同时填宽和高时保持比例"), "true", "bool")]),
        "watermark" => new(id, L.T("水印"), true, [T("text", L.T("水印文字")), T("logo", L.T("水印图片（可选）"), "", "file:image"), .. Positions, C("layout", L.T("排布"), "single", "single", "tiled"), N("opacity", L.T("不透明度（0–100）"), "60"), N("textSize", L.T("文字大小（占短边 %）"), "5"), N("scale", L.T("图片宽度（占画面 %）"), "20"), N("rotation", L.T("旋转角度"), "0"), N("margin", L.T("边距（占短边 %）"), "3"), T("color", L.T("文字颜色"), "#ffffff")]),
        "makeIcon" => new(id, L.T("制作图标"), true, [C("iconKind", L.T("输出"), "ico", "ico", "favicon")]),
        "ocrImage" => new(id, L.T("文字识别"), true, [T("language", L.T("识别语言（auto 为系统语言，如 zh-Hans-CN、en-US）"), "auto"), C("output", L.T("输出"), "txt", "txt", "pdf")]),
        "ocrPDF" => new(id, L.T("文字识别"), true, [T("language", L.T("识别语言（auto 为系统语言，如 zh-Hans-CN、en-US）"), "auto"), C("output", L.T("输出"), "txt", "txt", "pdf"), T("password", L.T("PDF 密码（如需要）"), "", "password")]),
        "createAnimation" => new(id, L.T("制作动图 / 幻灯片"), true, [C("format", L.T("输出"), "gif", "gif", "mp4"), N("seconds", L.T("每张停留（秒）"), "1"), N("maxEdge", L.T("最长边（像素）"), "720"), T("loop", L.T("GIF 循环播放"), "true", "bool")], true),
        "videoToGif" => new(id, L.T("转为 GIF"), true, [.. Times, N("fps", L.T("帧率"), "12"), N("width", L.T("宽度（像素）"), "480"), T("loop", L.T("循环播放"), "true", "bool")]),
        "videoSettings" => new(id, L.T("分辨率 / 帧率"), true, [C("height", L.T("分辨率（高度）"), "original", "original", "2160", "1440", "1080", "720", "480", "360"), N("fps", L.T("帧率（0 保持原样）"), "0")]),
        "videoEffects" => new(id, L.T("淡入淡出 / 倒放 / 循环"), true, [N("fadeIn", L.T("淡入（秒）"), "0"), N("fadeOut", L.T("淡出（秒）"), "0"), T("reverse", L.T("倒放（仅限 2 分钟内）"), "false", "bool"), N("loops", L.T("重复次数（1 为不重复）"), "1")]),
        "subtitlesAudio" => new(id, L.T("字幕 / 音轨"), true, [T("subtitleFile", L.T("字幕文件（.srt / .ass / .vtt）"), "", "file:subtitle"), N("subtitleSize", L.T("字幕大小"), "22"), T("audioFile", L.T("音频文件（替换或混入）"), "", "file:audio"), T("mix", L.T("与原声混合（关闭则替换原声）"), "false", "bool"), N("volume", L.T("新音频音量（1 原始）"), "1")]),
        "audioEffects" => new(id, L.T("音量 / 淡入淡出"), true, [N("gainDb", L.T("音量调整（dB）"), "0"), N("fadeIn", L.T("淡入（秒）"), "0"), N("fadeOut", L.T("淡出（秒）"), "0"), T("denoise", L.T("降低背景噪声"), "false", "bool")]),
        "joinAudio" => new(id, L.T("拼接音频"), true, [], true),
        "ringtone" => new(id, L.T("手机铃声"), true, [N("start", L.T("开始（秒）"), "0"), N("length", L.T("长度（秒，最长 40）"), "30")]),
        "pdfPassword" => new(id, L.T("PDF 密码"), true, [C("mode", L.T("操作"), "add", "add", "remove"), T("password", L.T("当前密码（已加密时填写）"), "", "password"), T("newPassword", L.T("新密码（添加保护时填写）"), "", "password")]),
        "pdfNumbers" => new(id, L.T("页码 / 页眉页脚"), true, [T("numberFormat", L.T("页码格式（{n} 当前页，{total} 总页数）"), "{n} / {total}"), C("position", L.T("页码位置"), "bottomRight", "bottomRight", "bottomCenter", "bottomLeft", "topRight", "topCenter", "topLeft"), N("startAt", L.T("起始页码"), "1"), T("header", L.T("页眉文字")), T("footer", L.T("页脚文字")), N("fontSize", L.T("字号"), "10"), T("password", L.T("PDF 密码（如需要）"), "", "password")]),
        "extractPdfImages" => new(id, L.T("提取图片"), true, [N("minSize", L.T("忽略短边小于此值的图片（像素）"), "64"), T("password", L.T("PDF 密码（如需要）"), "", "password")]),
        "packArchive" => new(id, L.T("加密 / 分卷压缩"), true, [C("format", L.T("格式"), "zip", "zip", "7z"), T("password", L.T("密码（可留空）"), "", "password"), N("level", L.T("压缩等级（1–9）"), "5"), N("splitMB", L.T("分卷大小（MB，0 不分卷）"), "0")], false),
        "rotateImage" or "rotateVideo" => new(id, L.T("旋转 / 翻转"), true, Rotation(id == "rotateVideo")),
        "redactImage" => new(id, L.T("图片打码"), true, [.. Crop, C("style", L.T("打码方式"), "pixelate", "pixelate", "blur", "solid"), N("blockSize", L.T("马赛克颗粒（像素）"), "12"), T("color", L.T("覆盖颜色"), "#000000"), T("regions", L.T("多个区域"), "", "json")]),
        "createPDF" => new(id, L.T("创建 PDF"), true, [], true),
        "createCollage" => new(id, L.T("拼图"), true, [C("layout", L.T("布局"), "grid", "grid", "row", "column", "featured"), N("canvasWidth", L.T("画布宽度"), "1600"), N("canvasHeight", L.T("画布高度"), "1200"), N("columns", L.T("网格列数（0 自动）"), "0"), N("gap", L.T("间距"), "16"), N("radius", L.T("圆角"), "0"), T("background", L.T("背景色"), "#ffffff")], true),
        "muteVideo" => new(id, L.T("移除音频"), true),
        "removeLocation" => new(id, L.T("移除位置信息"), true),
        "trimVideo" => new(id, L.T("裁剪时段"), true, Times),
        "cropVideo" => new(id, L.T("裁剪画面"), true, [.. Crop, .. Ratio]),
        "changeVideoSpeed" => new(id, L.T("调整速度"), true, [N("speed", L.T("速度（0.125–8）"), "2")]),
        "joinVideos" => new(id, L.T("拼接视频"), true, [], true),
        "videoSnapshots" => new(id, L.T("保存帧"), true, [N("time", L.T("时间（秒）"), "0"), N("frame", L.T("相对帧数"), "0"), T("times", L.T("多个时间点（秒，逗号分隔）")), T("sheet", L.T("合成一张缩略图总览"), "false", "bool"), N("sheetCount", L.T("总览缩略图数量"), "12"), N("sheetColumns", L.T("总览列数"), "4")]),
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
        "extractArchive" => new(id, L.T("解包"), true, [T("password", L.T("压缩包密码（如需要）"), "", "password")]),
        _ => throw new ArgumentException(L.T("未知工具 ") + id)
    };
}
