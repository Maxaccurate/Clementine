using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Windows.Controls;
using System.Windows.Data;

namespace DesktopDrop;

public sealed class MetadataRow
{
    public string Scope{get;set;}="";
    public string Key{get;set;}="";
    public string Value{get;set;}="";
    public JsonValueKind Kind{get;set;}=JsonValueKind.String;
}
internal sealed class MetadataEditor:DataGrid
{
    private readonly ObservableCollection<MetadataRow> rows=[];
    private bool media;
    private readonly List<(string Scope,string Key)> original=[];
    public MetadataEditor()
    {
        AutoGenerateColumns=false;CanUserAddRows=true;CanUserDeleteRows=true;Height=290;ItemsSource=rows;
        Columns.Add(new DataGridTextColumn{Header="所属",Binding=new Binding("Scope"),Width=new DataGridLength(.65,DataGridLengthUnitType.Star)});
        Columns.Add(new DataGridTextColumn{Header="字段",Binding=new Binding("Key"),Width=new DataGridLength(1,DataGridLengthUnitType.Star)});
        Columns.Add(new DataGridTextColumn{Header="值",Binding=new Binding("Value"),Width=new DataGridLength(1.4,DataGridLengthUnitType.Star)});
    }
    public void Populate(JsonElement element)
    {
        rows.Clear();original.Clear();media=element.TryGetProperty("format",out _);Flatten(element,"");original.AddRange(rows.Select(r=>(r.Scope,r.Key)));
    }
    private void Flatten(JsonElement element,string scope)
    {
        foreach(var property in element.EnumerateObject())
        {
            if(scope==""&&new[]{"Width","Height","Mode","Pages"}.Contains(property.Name))continue;
            if(property.Value.ValueKind==JsonValueKind.Object)Flatten(property.Value,string.IsNullOrEmpty(scope)?property.Name:scope+"."+property.Name);
            else rows.Add(new(){Scope=DisplayScope(scope),Key=property.Name,Kind=property.Value.ValueKind,Value=property.Value.ValueKind==JsonValueKind.String?property.Value.GetString()??"":property.Value.GetRawText()});
        }
    }
    private static string DisplayScope(string scope)=>scope=="format"?"文件":scope.StartsWith("streams.")?"轨道 "+scope[8..]:scope.StartsWith("chapters.")?"章节 "+scope[9..]:scope;
    private string StorageScope(string scope)=>scope=="文件"||media&&scope==""?"format":scope.StartsWith("轨道 ")?"streams."+scope[3..]:scope.StartsWith("章节 ")?"chapters."+scope[3..]:scope;
    public string ToJson()
    {
        CommitEdit(DataGridEditingUnit.Cell,true);CommitEdit(DataGridEditingUnit.Row,true);
        var root=new Dictionary<string,object>();
        var entries=rows.Where(x=>!string.IsNullOrWhiteSpace(x.Key)).ToList();
        entries.AddRange(original.Where(x=>!rows.Any(r=>r.Scope==x.Scope&&r.Key==x.Key)).Select(x=>new MetadataRow{Scope=x.Scope,Key=x.Key,Value="null",Kind=JsonValueKind.Null}));
        foreach(var row in entries)
        {
            var target=root;string scope=StorageScope(row.Scope.Trim());
            foreach(string part in scope.Split('.',StringSplitOptions.RemoveEmptyEntries))
            {
                if(!target.TryGetValue(part,out var child)){child=new Dictionary<string,object>();target[part]=child;}
                target=(Dictionary<string,object>)child;
            }
            object value=row.Value;
            if(row.Kind==JsonValueKind.Number)
            {
                if(string.IsNullOrWhiteSpace(row.Value))value=JsonSerializer.Deserialize<JsonElement>("null");
                else if(long.TryParse(row.Value,NumberStyles.Integer,CultureInfo.InvariantCulture,out long integer))value=integer;
                else value=double.Parse(row.Value,CultureInfo.InvariantCulture);
            }
            else if(row.Kind is JsonValueKind.Array or JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null)value=JsonSerializer.Deserialize<JsonElement>(row.Value);
            target[row.Key]=value;
        }
        return JsonSerializer.Serialize(root);
    }
    public void Filter(string search)
    {
        var view=CollectionViewSource.GetDefaultView(rows);view.Filter=o=>o is MetadataRow row&&(string.IsNullOrWhiteSpace(search)||row.Scope.Contains(search,StringComparison.OrdinalIgnoreCase)||row.Key.Contains(search,StringComparison.OrdinalIgnoreCase)||row.Value.Contains(search,StringComparison.OrdinalIgnoreCase));
    }
}
