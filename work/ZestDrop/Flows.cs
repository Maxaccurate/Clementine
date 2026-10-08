using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace ZestDrop;

// A flow runs several tools one after another on each file; every step uses a preset saved from that tool's window
// (or the tool's defaults), and each step works on the previous step's result.
internal sealed record FlowStep(string Action, string? Preset);
internal sealed record Flow(string Name, string Kind, List<FlowStep> Steps);

internal static class Flows
{
    private const string Key = "flows";
    public const string Prefix = "flow:";

    public static List<Flow> Load()
    {
        try
        { return JsonSerializer.Deserialize<List<Flow>>(Settings.GetString(Key) ?? "[]") ?? []; }
        catch (JsonException) { return []; }
    }

    public static void Save(List<Flow> flows) => Settings.Set(Key, JsonSerializer.Serialize(flows));

    // The tools a flow step can use: those whose settings can be saved as a preset and that suit the kind of file.
    public static List<string> StepTools(string kind) => Catalog.ToolIds(kind).Where(Presets.Supported).ToList();

    // Flows appear after the tools in the wheel and in the pop-up menu for files of their kind.
    public static List<Operation> For(string[] paths)
    {
        string kind = Catalog.Category(paths[0]);
        if (paths.Any(p => Catalog.Category(p) != kind))
            return [];
        return Load().Where(flow => flow.Kind == kind && flow.Steps.Count > 0).Select(flow => new Operation(Prefix + flow.Name, "▶ " + flow.Name)).ToList();
    }

    public static ConversionJob Job(string[] paths, string operationId)
    {
        var flow = Load().FirstOrDefault(f => Prefix + f.Name == operationId) ?? throw new ArgumentException(L.T("找不到这个流程"));
        var steps = flow.Steps.Select(step =>
        {
            var values = Catalog.Definition(step.Action, flow.Kind).Fields?.ToDictionary(f => f.Name, f => f.Default) ?? [];
            if (step.Preset != null && Presets.Named(step.Action, step.Preset) is { } saved)
                foreach (var (field, value) in saved)
                    values[field] = value;
            return new Dictionary<string, object> { ["action"] = step.Action, ["params"] = values };
        }).ToList();
        return new ConversionJob(paths, "flow", new Dictionary<string, string> { ["steps"] = JsonSerializer.Serialize(steps) });
    }
}
