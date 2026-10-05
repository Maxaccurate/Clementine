using System.Collections.Generic;
namespace ZestDrop;
internal sealed record ConversionJob(string[] Paths, string Action, Dictionary<string, string>? Parameters = null);
internal sealed record FileResult(string Input, string? Output, string? Error);
internal sealed record BatchResult(FileResult[] Files);
internal sealed record JobProgress(string Phase, int Processed, int Total, string? Current = null);
