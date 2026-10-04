using ZestDrop;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

internal static class Checks
{
    [STAThread]public static int Main(string[] args)
    {
        var checks=new List<object>();int failed=0;
        void Check(string name,Action action){try{action();checks.Add(new{test=name,passed=true});}catch(Exception ex){failed++;checks.Add(new{test=name,passed=false,error=ex.Message});}}
        void Require(bool value){if(!value)throw new Exception("Unexpected progress result");}
        var root=Path.GetDirectoryName(args[0])!;string file=Path.Combine(root,"progress-read-test.tmp");
        Check("missing progress does not crash UI",()=>Require(ProgressReader.Read(file)==null));
        foreach(string content in new[]{"{bad}","{\"Total\":0,\"Processed\":0}","{\"Total\":2,\"Processed\":3}","{\"Total\":2,\"Processed\":-1}"})
            Check("invalid/partial progress ignored: "+content,()=>{File.WriteAllText(file,content);Require(ProgressReader.Read(file)==null);});
        Check("actual batch counts and Unicode filename read correctly",()=>{File.WriteAllText(file,JsonSerializer.Serialize(new JobProgress("processing",2,5,"照片.png")));var p=ProgressReader.Read(file);Require(p?.Processed==2&&p.Total==5&&p.Current=="照片.png");});
        File.Delete(file);
        Check("embedded window icon loads at high resolution and stays frozen",()=>
        {
            var icon=AppIcon.Window;Require(icon.IsFrozen&&icon.Width==256&&icon.Height==256&&ReferenceEquals(icon,AppIcon.Window));
        });
        Check("tray icon can be used after its resource stream is closed",()=>
        {
            using var icon=AppIcon.CreateTrayIcon();using var bitmap=icon.ToBitmap();Require(icon.Width>=16&&icon.Width==icon.Height&&bitmap.Width==icon.Width);
        });
        Check("worker progress is delivered through the actual runtime monitor",()=>
        {
            string progressFile=file+".progress";File.WriteAllText(progressFile,JsonSerializer.Serialize(new JobProgress("processing",2,5,"照片 3.png")));
            using var seen=new ManualResetEventSlim();JobProgress? received=null;
            using(var monitor=Backend.WatchProgress(file,new Reporter(p=>{received=p;seen.Set();}))){Require(seen.Wait(3000)&&received?.Processed==2&&received.Total==5);}
            File.Delete(progressFile);
        });
        Check("monitor tolerates a partial write and picks up the next valid update",()=>
        {
            string progressFile=file+".progress";File.WriteAllText(progressFile,"{");using var seen=new ManualResetEventSlim();
            using(var monitor=Backend.WatchProgress(file,new Reporter(_=>seen.Set()))){Require(!seen.Wait(250));File.WriteAllText(progressFile,JsonSerializer.Serialize(new JobProgress("processing",1,2,"next.png")));Require(seen.Wait(3000));}
            File.Delete(progressFile);
        });
        Check("single unknown-duration task shows elapsed time without percentage",()=>
        {
            var view=new ActivityIndicator();using var session=view.Begin("生成预览","照片.png");session.Report(new JobProgress("processing",0,1,"照片.png"));var text=string.Join(" ",view.Children.OfType<TextBlock>().Select(t=>t.Text));Require(text.Contains("已用")&&!text.Contains('%')&&!text.Contains("0 / 1"));
        });
        Check("batch indicator uses actual completed count",()=>
        {
            var view=new ActivityIndicator();using var session=view.Begin("保存文件","batch");session.Report(new JobProgress("processing",2,5,"照片 3.png"));Require(view.Children.OfType<TextBlock>().Any(t=>t.Text.Contains("2 / 5")));
        });
        Check("old preview completion cannot hide a newer task",()=>
        {
            var view=new ActivityIndicator();var older=view.Begin("旧预览","old");var newer=view.Begin("新预览","new");older.Dispose();Require(view.Visibility==Visibility.Visible&&view.Children.OfType<TextBlock>().First().Text=="新预览");newer.Dispose();Require(view.Visibility==Visibility.Collapsed);
        });
        Check("finishing one overlapping operation keeps remaining work visible",()=>
        {
            var view=new ActivityIndicator();var older=view.Begin("分析响度","a");var newer=view.Begin("准备播放","b");newer.Dispose();Require(view.Visibility==Visibility.Visible&&view.Children.OfType<TextBlock>().First().Text=="分析响度");older.Dispose();Require(view.Visibility==Visibility.Collapsed);
        });
        Check("late progress from disposed task cannot revive indicator",()=>
        {
            var view=new ActivityIndicator();var session=view.Begin("处理中","a");session.Dispose();session.Report(new JobProgress("processing",1,3,"b"));Require(view.Visibility==Visibility.Collapsed);
        });
        Check("completion replaces busy state and survives late callbacks",()=>
        {
            var view=new ActivityIndicator();var session=view.Begin("处理中","a");view.Complete("处理完成","result.png");session.Report(new JobProgress("processing",0,1,"a"));session.Dispose();Require(view.Visibility==Visibility.Visible&&view.Children.OfType<TextBlock>().First().Text=="处理完成");
        });
        File.WriteAllText(args[0],JsonSerializer.Serialize(new{passed=checks.Count-failed,failed,checks},new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine($"passed={checks.Count-failed}, failed={failed}");return failed==0?0:1;
    }
    private sealed class Reporter(Action<JobProgress> callback):IProgress<JobProgress>{public void Report(JobProgress value)=>callback(value);}
}
