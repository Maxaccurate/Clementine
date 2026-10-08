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
using System.Windows.Media;

internal static class Checks
{
    [STAThread]public static int Main(string[] args)
    {
        var testApplication = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        L.UseForSession("zh"); // assertions below use Chinese text; never touch the saved choice
        var checks=new List<object>();int failed=0;
        void Check(string name,Action action){try{action();checks.Add(new{test=name,passed=true});}catch(Exception ex){failed++;checks.Add(new{test=name,passed=false,error=ex.GetBaseException().Message});}}
        void Require(bool value){if(!value)throw new Exception("Unexpected progress result");}
        Check("bundled Chinese Medium font resolves and includes the packaging label glyphs", () =>
        {
            var face = new System.Windows.Media.Typeface(UiTheme.ChineseFont, FontStyles.Normal, FontWeights.Medium, FontStretches.Normal);
            if (!face.TryGetGlyphTypeface(out var glyphs)) throw new Exception("Bundled font family could not resolve to a physical face.");
            if (glyphs.Weight != FontWeights.Medium) throw new Exception("Bundled face weight: " + glyphs.Weight);
            if (!glyphs.FontUri.OriginalString.Contains("sourcehansanscn-medium.otf", StringComparison.OrdinalIgnoreCase))
                throw new Exception("Unexpected font source: " + glyphs.FontUri);
            foreach (char character in "打包裁剪图片") Require(glyphs.CharacterToGlyphMap.ContainsKey(character));
        });
        Check("layered wheel retains each visible sector's drop target",()=>
        {
            string path=Path.Combine(Path.GetTempPath(),"zestdrop-wheel-"+Guid.NewGuid().ToString("N")+".png");File.WriteAllText(path,"fixture");
            var wheel=new DropWheel((_,_)=>{});
            try
            {
                wheel.Preview([path],false,"convert:jpg");var actions=Catalog.Options([path],false);var hit=typeof(DropWheel).GetMethod("Hit",BindingFlags.NonPublic|BindingFlags.Static)!;var ring=typeof(DropWheel).GetField("first",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(wheel);
                for(int i=0;i<actions.Count;i++){double angle=-Math.PI/2+i*2*Math.PI/actions.Count;Require((int)hit.Invoke(null,[ring!,new Point(190+119*Math.Cos(angle),190+119*Math.Sin(angle)),65d,172d])! == i);}
                Require((int)hit.Invoke(null,[ring!,new Point(190,190),65d,172d])! == -1);Require((int)hit.Invoke(null,[ring!,new Point(190,130),65d,172d])! == -1);Require((int)hit.Invoke(null,[ring!,new Point(1,1),65d,172d])! == -1);
            }
            finally{wheel.Close();File.Delete(path);}
        });
        Check("wheel mode changes keep the dragged file and valid tool sectors",()=>
        {
            string path=Path.Combine(Path.GetTempPath(),"zestdrop-wheel-"+Guid.NewGuid().ToString("N")+".png");File.WriteAllText(path,"fixture");var wheel=new DropWheel((_,_)=>{});
            try{wheel.Preview([path],false,"convert:jpg");wheel.ChangeMode(true);Require(wheel.HasFileDrag&&wheel.ToolsMode);var all=Catalog.Options([path],true);var groups=Catalog.Grouped(all,"image");var actions=groups==null?all:groups.Select((g,i)=>new Operation("__group:"+i,g.Label)).ToList();var hit=typeof(DropWheel).GetMethod("Hit",BindingFlags.NonPublic|BindingFlags.Static)!;var ring=typeof(DropWheel).GetField("first",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(wheel);for(int i=0;i<actions.Count;i++){double angle=-Math.PI/2+i*2*Math.PI/actions.Count;Require((int)hit.Invoke(null,[ring!,new Point(190+119*Math.Cos(angle),190+119*Math.Sin(angle)),65d,172d])! == i);}}
            finally{wheel.Close();File.Delete(path);}
        });
        Check("trim start handle cannot cross end",()=>Require(TrimRange.Start(90,60,120)<60));
        Check("trim end handle cannot cross start",()=>Require(TrimRange.End(10,40,120)>40));
        Check("trim handles stay inside source duration",()=>Require(TrimRange.Start(-2,60,120)==0&&TrimRange.End(200,40,120)==120));
        Check("short clips retain a valid range",()=>Require(TrimRange.Start(.05,.02,.02)==0&&TrimRange.End(0,0,.02)==.02));
        Check("processed clip maps playback time to original source time",()=>{var clip=new MediaSegment(50,30);Require(clip.ToSource(12)==62&&clip.ToMedia(62)==12);});
        Check("speed preview maps time correctly in both directions",()=>{var clip=new MediaSegment(20,15,2);Require(clip.End==50&&clip.ToSource(10)==40&&clip.ToMedia(40)==10);});
        Check("seek outside a processed segment requests another segment",()=>{var clip=new MediaSegment(50,30);Require(!clip.Contains(20)&&!clip.Contains(100)&&clip.Contains(65));});
        Check("time mapping clamps invalid media positions",()=>{var clip=new MediaSegment(50,30);Require(clip.ToMedia(0)==0&&clip.ToMedia(200)==30&&clip.ToSource(-5)==50);});
        Check("playback time labels handle hours and long durations",()=>Require(MediaSegment.Format(130)=="2:10"&&MediaSegment.Format(3662)=="1:01:02"));
        DragShortcutTracker Drag(){var t=new DragShortcutTracker(4,4);t.Update(true,false,false,false,100,100,true);return t;}
        Check("Shift typing without a mouse drag does not trigger",()=>{var t=new DragShortcutTracker(4,4);Require(t.Update(false,true,false,false,100,100,true)==DragMenuRequest.None);});
        Check("Shift clicking or small pointer movement does not trigger",()=>{var t=Drag();Require(t.Update(true,true,false,false,102,102,true)==DragMenuRequest.None);});
        Check("drag plus Shift requests conversion",()=>{var t=Drag();Require(t.Update(true,true,false,false,110,100,true)==DragMenuRequest.Convert);});
        Check("drag plus Ctrl Shift requests tools",()=>{var t=Drag();Require(t.Update(true,true,true,false,110,100,true)==DragMenuRequest.Tools);});
        Check("Ctrl then Shift works",()=>{var t=Drag();Require(t.Update(true,false,true,false,110,100,true)==DragMenuRequest.None);Require(t.Update(true,true,true,false,110,100,true)==DragMenuRequest.Tools);});
        Check("Shift then Ctrl switches immediately to tools",()=>{var t=Drag();Require(t.Update(true,true,false,false,110,100,true)==DragMenuRequest.Convert);Require(t.Update(true,true,true,false,110,100,true)==DragMenuRequest.Tools);});
        Check("keys held before dragging trigger once movement starts",()=>{var t=new DragShortcutTracker(4,4);Require(t.Update(true,true,true,false,100,100,true)==DragMenuRequest.None);Require(t.Update(true,true,true,false,110,100,true)==DragMenuRequest.Tools);});
        Check("holding a shortcut does not reopen the menu",()=>{var t=Drag();t.Update(true,true,false,false,110,100,true);Require(t.Update(true,true,false,false,130,100,true)==DragMenuRequest.None);});
        Check("releasing Ctrl or both keys does not change an open tools menu",()=>{var t=Drag();t.Update(true,true,true,false,110,100,true);Require(t.Update(true,true,false,false,120,100,true)==DragMenuRequest.None);Require(t.Update(true,false,false,false,120,100,true)==DragMenuRequest.None);});
        Check("a new Shift press can switch back to conversion",()=>{var t=Drag();t.Update(true,true,true,false,110,100,true);t.Update(true,false,false,false,120,100,true);Require(t.Update(true,true,false,false,120,100,true)==DragMenuRequest.Convert);});
        Check("Escape suppresses shortcuts for the rest of the drag",()=>{var t=Drag();t.Update(true,true,false,false,110,100,true);t.Cancel();Require(t.Update(true,true,true,false,120,100,true)==DragMenuRequest.None);});
        Check("a new drag works after Escape",()=>{var t=Drag();t.Cancel();t.Update(false,false,false,false,110,100,true);t.Update(true,false,false,false,100,100,true);Require(t.Update(true,true,false,false,110,100,true)==DragMenuRequest.Convert);});
        Check("text selection outside Explorer does not trigger",()=>{var t=new DragShortcutTracker(4,4);t.Update(true,false,false,false,100,100,false);Require(t.Update(true,true,false,false,120,100,true)==DragMenuRequest.None);});
        Check("Ctrl alone or Alt shortcuts do not trigger",()=>{var t=Drag();Require(t.Update(true,false,true,false,120,100,true)==DragMenuRequest.None);Require(t.Update(true,true,true,true,120,100,true)==DragMenuRequest.None);});
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
        Check("temp sweep removes only staging entries created after the job started",()=>
        {
            string root=Path.Combine(Path.GetTempPath(),"zestdrop-sweep-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            try
            {
                string input=Path.Combine(root,"clip.mp4"),older=Path.Combine(root,".zestdrop-older.mp4"),user=Path.Combine(root,"notes.txt");
                File.WriteAllText(input,"x");File.WriteAllText(older,"x");
                var sweep=new TempSweep([input]);
                string partial=Path.Combine(root,".zestdrop-partial.mp4"),folder=Path.Combine(root,".zestdrop-abc");
                File.WriteAllText(partial,"x");Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"frame-0001.png"),"x");File.WriteAllText(user,"x");
                sweep.Run().GetAwaiter().GetResult();
                Require(!File.Exists(partial)&&!Directory.Exists(folder)&&File.Exists(older)&&File.Exists(input)&&File.Exists(user));
            }
            finally{Directory.Delete(root,true);}
        });
        Check("temp sweep retries while a killed encoder still holds the file",()=>
        {
            string root=Path.Combine(Path.GetTempPath(),"zestdrop-sweep-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            try
            {
                string input=Path.Combine(root,"clip.mp4");File.WriteAllText(input,"x");var sweep=new TempSweep([input]);
                string partial=Path.Combine(root,".zestdrop-locked.mp4");var handle=new FileStream(partial,FileMode.CreateNew,FileAccess.Write,FileShare.None);
                var release=new Timer(_=>handle.Dispose(),null,500,Timeout.Infinite);
                sweep.Run().GetAwaiter().GetResult();release.Dispose();
                Require(!File.Exists(partial)&&File.Exists(input));
            }
            finally{Directory.Delete(root,true);}
        });
        Check("pausing a progress session shows 已暂停 and resuming restores the stage", () =>
        {
            var view = new ActivityIndicator();
            var session = view.Begin("正在转换格式", "a.png");
            var heading = (TextBlock)view.Children[0];
            session.SetPaused(true);
            Require(heading.Text == "已暂停");
            session.SetPaused(false);
            Require(heading.Text == "正在转换格式");
        });
        Check("a waiting job explains it starts after the jobs ahead of it", () =>
        {
            var card = new JobCard(new ConversionJob(["a.mp4"], "convert:webm"));
            var texts = Texts(card);
            Require(card.State == JobState.Waiting && texts.Contains("等待处理") && texts.Contains("前面的任务完成后开始"));
            card.Start();
            Require(card.State == JobState.Running && Texts(card).Contains("正在转换格式"));
        });
        Check("one job shows a single card and no stack header", () =>
        {
            var stack = new TaskIndicatorWindow(headless: true);
            stack.Add(new ConversionJob(["B&W"], "pack:zip")).Start();
            Require(stack.ShouldShow && !stack.HeaderVisible && stack.VisibleCards.Count() == 1);
        });
        Check("several jobs stack top to bottom, oldest first, with a summary header", () =>
        {
            var stack = new TaskIndicatorWindow(headless: true);
            var a = stack.Add(new ConversionJob(["a.mp4"], "convert:webm"));
            var b = stack.Add(new ConversionJob(["b.mp4"], "convert:webm"));
            var c = stack.Add(new ConversionJob(["c.mp4"], "convert:webm"));
            a.Start();
            stack.Update();
            Require(stack.HeaderVisible && stack.VisibleCards.SequenceEqual(new[] { a, b, c }));
            Require(stack.HeaderText == "3 个任务 · 进行中 1 · 等待 2");
        });
        Check("collapsing piles the stack under the newest running job; expanding lists all again", () =>
        {
            var stack = new TaskIndicatorWindow(headless: true);
            var a = stack.Add(new ConversionJob(["a.mp4"], "convert:webm"));
            var b = stack.Add(new ConversionJob(["b.mp4"], "convert:webm"));
            stack.Add(new ConversionJob(["c.mp4"], "convert:webm"));
            a.Start(); b.Start(); b.SetPaused(true); stack.Update();
            stack.SetExpanded(false);
            Require(stack.VisibleCards.SequenceEqual(new[] { a }));
            stack.SetExpanded(true);
            Require(stack.VisibleCards.Count() == 3);
        });
        Check("hiding one card keeps the others and the header counts it; show all brings it back", () =>
        {
            var stack = new TaskIndicatorWindow(headless: true);
            var a = stack.Add(new ConversionJob(["a.mp4"], "convert:webm"));
            var b = stack.Add(new ConversionJob(["b.mp4"], "convert:webm"));
            a.Start(); b.Start();
            stack.HideCard(a);
            Require(stack.VisibleCards.SequenceEqual(new[] { b }) && stack.HeaderText.Contains("已隐藏 1"));
            stack.ShowAll();
            Require(stack.VisibleCards.Count() == 2);
        });
        Check("hide all stays hidden for new jobs until the stack empties", () =>
        {
            var stack = new TaskIndicatorWindow(headless: true);
            var a = stack.Add(new ConversionJob(["a.mp4"], "convert:webm"));
            stack.HideAll();
            var b = stack.Add(new ConversionJob(["b.mp4"], "convert:webm"));
            Require(!stack.ShouldShow);
            Require(!stack.Finish(a, "处理完成", "a.webm") && !stack.Finish(b, "处理完成", "b.webm"));
            Require(stack.Cards.Count == 0 && !stack.AllHidden);
            stack.Add(new ConversionJob(["c.mp4"], "convert:webm"));
            Require(stack.ShouldShow);
        });
        Check("a finished visible card shows its result; a finished hidden card is removed and reported", () =>
        {
            var stack = new TaskIndicatorWindow(headless: true);
            var a = stack.Add(new ConversionJob(["a.mp4"], "convert:webm"));
            var b = stack.Add(new ConversionJob(["b.mp4"], "convert:webm"));
            stack.HideCard(b);
            Require(stack.Finish(a, "处理完成", "a.webm") && stack.Cards.Contains(a) && a.State == JobState.Done && Texts(a).Contains("a.webm"));
            Require(!stack.Finish(b, "处理完成", "b.webm") && !stack.Cards.Contains(b));
        });
        Check("cancelling one job never removes another job's staging files in the same folder", () =>
        {
            string root = Path.Combine(Path.GetTempPath(), "zestdrop-sweep-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                string input = Path.Combine(root, "clip.mp4");
                File.WriteAllText(input, "x");
                var sweepA = new TempSweep([input], "aaaa");
                string mine = Path.Combine(root, ".zestdrop-aaaa-1.webm"), theirs = Path.Combine(root, ".zestdrop-bbbb-1.webm");
                File.WriteAllText(mine, "x");
                File.WriteAllText(theirs, "x");
                sweepA.Run().GetAwaiter().GetResult();
                Require(!File.Exists(mine) && File.Exists(theirs) && File.Exists(input));
            }
            finally { Directory.Delete(root, true); }
        });
        Check("pausing freezes a whole process tree and resuming continues it", () =>
        {
            string output = Path.Combine(Path.GetTempPath(), "zestdrop-pause-" + Guid.NewGuid().ToString("N") + ".txt");
            // cmd starts ping as a child; ping writes one line per second, so the file only grows while both run.
            var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c ping -n 40 127.0.0.1 > \"{output}\"") { CreateNoWindow = true, UseShellExecute = false })!;
            try
            {
                Thread.Sleep(2500);
                long before = new FileInfo(output).Length;
                Require(before > 0);
                using (var pause = ProcessTreePause.Suspend(process))
                {
                    Require(pause.Count >= 2);
                    long frozen = new FileInfo(output).Length;
                    Thread.Sleep(3000);
                    Require(new FileInfo(output).Length == frozen);
                    pause.Resume();
                    Thread.Sleep(3000);
                    Require(new FileInfo(output).Length > frozen);
                }
            }
            finally
            {
                try { process.Kill(true); process.WaitForExit(5000); } catch (InvalidOperationException) { }
                try { File.Delete(output); } catch (IOException) { }
            }
        });
        Check("a paused tree can be cancelled directly", () =>
        {
            var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", "/c ping -n 40 127.0.0.1 > nul") { CreateNoWindow = true, UseShellExecute = false })!;
            Thread.Sleep(800);
            using var pause = ProcessTreePause.Suspend(process);
            process.Kill(true);
            Require(process.WaitForExit(5000));
        });
        Check("every interface string used in the app has an English translation", () =>
        {
            // Scan the app's sources for L.T("…") / L.F("…") keys.
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ZestDrop", "backend")))
                dir = dir.Parent;
            Require(dir != null);
            var missing = new List<string>();
            foreach (var file in Directory.GetFiles(Path.Combine(dir!.FullName, "ZestDrop"), "*.cs"))
                foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(file), "L\\.[TF]\\(\"((?:[^\"\\\\]|\\\\.)*)\""))
                {
                    string key = JsonSerializer.Deserialize<string>("\"" + m.Groups[1].Value + "\"")!;
                    if (!L.Translations.ContainsKey(key))
                        missing.Add(Path.GetFileName(file) + ": " + key);
                }
            if (missing.Count > 0)
                throw new Exception("Missing English: " + string.Join(" | ", missing.Distinct()));
        });
        Check("English format strings use only placeholders the code supplies", () =>
        {
            foreach (var (key, english) in L.Translations.Where(t => System.Text.RegularExpressions.Regex.IsMatch(t.Key, @"\{\d")))
            {
                int Max(string text) => System.Text.RegularExpressions.Regex.Matches(text, "\\{(\\d+)").Select(m => int.Parse(m.Groups[1].Value)).DefaultIfEmpty(-1).Max();
                int supplied = Max(key) + 1 + (key == " 等 {0} 项" ? 1 : 0); // that key passes an extra count for English
                if (Max(english) >= supplied)
                    throw new Exception($"'{english}' needs more values than '{key}' gets");
                _ = string.Format(english, Enumerable.Repeat((object)1, Math.Max(supplied, 1)).ToArray());
            }
        });
        Check("switching to English relabels cards, the stack header and the wheel", () =>
        {
            try
            {
                L.UseForSession("en");
                var stack = new TaskIndicatorWindow(headless: true);
                var a = stack.Add(new ConversionJob(["a.mp4", "b.mp4", "c.mp4"], "convert:webm"));
                stack.Add(new ConversionJob(["d.mp4"], "pack:zip"));
                a.Start();
                stack.Update();
                var texts = Texts(a);
                Require(texts.Contains("Converting") && texts.Contains("a.mp4 and 2 more") && stack.HeaderText == "2 jobs · running 1 · waiting 1");
                var labels = Catalog.Options([Path.Combine(Path.GetTempPath(), "x.png")], true).Select(o => o.Label).ToList();
                Require(labels.Count == 0 || labels.Contains("Compress"));
                Require(Catalog.Definition("trimVideo", "video").Label == "Trim" && JobValidationMessage().Contains("must be"));
                L.UseForSession("zh");
                stack.Relabel();
                Require(Texts(a).Contains("正在转换格式") && Texts(a).Contains("a.mp4 等 3 项") && stack.HeaderText.StartsWith("2 个任务"));
            }
            finally { L.UseForSession("zh"); }
        });
        Check("settings keep each other's values and survive a damaged file", () =>
        {
            string file = Path.Combine(Path.GetTempPath(), "zestdrop-settings-" + Guid.NewGuid().ToString("N") + ".json");
            Settings.PathOverride = file;
            try
            {
                Require(Settings.GetString("language") == null && Settings.GetBool("hideWelcome", false) == false);
                Settings.Set("language", "en");
                Settings.Set("hideWelcome", true);
                Settings.Set("language", "zh");
                Require(Settings.GetString("language") == "zh" && Settings.GetBool("hideWelcome", false));
                File.WriteAllText(file, "{ this is not json");
                Require(Settings.GetString("language") == null && Settings.GetBool("hideWelcome", true));
                Settings.Set("hideWelcome", false);
                Require(!Settings.GetBool("hideWelcome", true) && !File.Exists(file + ".tmp"));
            }
            finally
            {
                Settings.PathOverride = null;
                try { File.Delete(file); } catch (IOException) { }
            }
        });
        Check("the welcome window explains the app in both languages and remembers the startup choice", () =>
        {
            string file = Path.Combine(Path.GetTempPath(), "zestdrop-welcome-" + Guid.NewGuid().ToString("N") + ".json");
            Settings.PathOverride = file;
            try
            {
                L.UseForSession("en");
                var window = new WelcomeWindow();
                var english = Texts(window);
                Require(english.Contains("Welcome to ZestDrop") && english.Contains("Drag files") && english.Contains("Press Shift") && english.Contains("Got it"));
                Require(english.Any(t => t.Contains("notification area")) && english.Any(t => t.Contains("never connects to the internet")));
                Require(english.Contains("English") && english.Contains("简体中文"));
                var box = FindAll<CheckBox>(window).Single(c => Equals(c.Content, L.T("启动时不再显示此窗口")));
                Require(box.IsChecked == false);
                box.IsChecked = true;
                box.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Require(Settings.GetBool(WelcomeWindow.HideAtStartKey, false));
                // Changing the language rebuilds the open window, and the checkbox still reflects the saved choice.
                L.Language = "zh"; // the same setter the language buttons and the tray menu use
                var chinese = Texts(window);
                Require(chinese.Contains("欢迎使用 ZestDrop") && chinese.Contains("知道了") && FindAll<CheckBox>(window).Single(c => Equals(c.Content, L.T("启动时不再显示此窗口"))).IsChecked == true);
                window.Close();
            }
            finally
            {
                Settings.PathOverride = null;
                L.UseForSession("zh");
                try { File.Delete(file); } catch (IOException) { }
            }
        });
        Check("welcome startup option synchronizes with its provider and keeps the welcome preference separate", () =>
        {
            var startupProvider = new MemoryStartupProvider();
            StartupManager.ProviderOverride = startupProvider;
            bool hideWelcome = Settings.GetBool(WelcomeWindow.HideAtStartKey, false);
            var window = new WelcomeWindow();
            try
            {
                var checkbox = FindAll<CheckBox>(window).Single(c => Equals(c.Content, L.T("开机自启")));
                Require(checkbox.IsChecked == false && checkbox.IsEnabled);
                checkbox.IsChecked = true; checkbox.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Require(startupProvider.Enabled && checkbox.IsChecked == true);
                Require(Settings.GetBool(WelcomeWindow.HideAtStartKey, false) == hideWelcome);
                StartupManager.SetAsync(false).GetAwaiter().GetResult();
                Require(checkbox.IsChecked == false);
                startupProvider.Blocked = true;
                StartupManager.SetAsync(true).GetAwaiter().GetResult();
                Require(checkbox.IsChecked == false && !checkbox.IsEnabled);
                Require(Texts(window).Any(t => t.Contains("Windows 已禁用")));
            }
            finally { window.Close(); StartupManager.ProviderOverride = null; }
        });
        string[] ratioNames = ["4:3", "16:9", "1:1", "9:16", "3:2", "21:9", "2.35:1", "7:3", "5:4"];
        var allHandles = new[] { CropHandle.Left, CropHandle.Right, CropHandle.Top, CropHandle.Bottom, CropHandle.TopLeft, CropHandle.TopRight, CropHandle.BottomLeft, CropHandle.BottomRight };
        bool Inside(CropRect r, int w, int h) => r.X >= 0 && r.Y >= 0 && r.Width >= 1 && r.Height >= 1 && r.Right <= w && r.Bottom <= h;
        // Fixed seed: the sweep is large but always the same, so a failure can be reproduced.
        CropRect RandomCrop(Random rng, int w, int h, CropRatio? ratio, out int min)
        {
            min = CropMath.MinSize(w, h);
            var raw = new CropRect(rng.Next(0, w), rng.Next(0, h), rng.Next(min, w + 1), rng.Next(min, h + 1));
            return CropMath.Constrain(raw, w, h, ratio, min);
        }
        Check("crop moves keep their size and stay inside the picture", () =>
        {
            var rng = new Random(11);
            for (int i = 0; i < 4000; i++)
            {
                int w = rng.Next(40, 5000), h = rng.Next(40, 5000);
                var start = RandomCrop(rng, w, h, null, out _);
                var moved = CropMath.Move(start, rng.NextDouble() * 9000 - 4500, rng.NextDouble() * 9000 - 4500, w, h);
                if (moved.Width != start.Width || moved.Height != start.Height || !Inside(moved, w, h)) throw new Exception($"{start} in {w}x{h} became {moved}");
            }
        });
        Check("free resizing moves only the dragged sides, never leaves the picture and never gets too small", () =>
        {
            var rng = new Random(12);
            for (int i = 0; i < 6000; i++)
            {
                int w = rng.Next(40, 5000), h = rng.Next(40, 5000);
                var start = RandomCrop(rng, w, h, null, out int min);
                var handle = allHandles[rng.Next(allHandles.Length)];
                var next = CropMath.Resize(start, handle, rng.NextDouble() * 9000 - 4500, rng.NextDouble() * 9000 - 4500, w, h, null, min);
                bool left = handle is CropHandle.Left or CropHandle.TopLeft or CropHandle.BottomLeft, right = handle is CropHandle.Right or CropHandle.TopRight or CropHandle.BottomRight;
                bool top = handle is CropHandle.Top or CropHandle.TopLeft or CropHandle.TopRight, bottom = handle is CropHandle.Bottom or CropHandle.BottomLeft or CropHandle.BottomRight;
                string why = $"{handle} on {start} in {w}x{h} gave {next}";
                if (!Inside(next, w, h) || next.Width < min || next.Height < min) throw new Exception(why);
                if (!left && next.X != start.X || !right && next.Right != start.Right || !top && next.Y != start.Y || !bottom && next.Bottom != start.Bottom) throw new Exception("moved a side it was not holding: " + why);
            }
        });
        Check("resizing with a locked ratio keeps the ratio, the opposite side or corner, and the picture bounds", () =>
        {
            var rng = new Random(13);
            for (int i = 0; i < 9000; i++)
            {
                int w = rng.Next(60, 5000), h = rng.Next(60, 5000);
                var ratio = CropRatio.Parse(ratioNames[rng.Next(ratioNames.Length)])!.Value;
                var start = RandomCrop(rng, w, h, ratio, out int min);
                var handle = allHandles[rng.Next(allHandles.Length)];
                var next = CropMath.Resize(start, handle, rng.NextDouble() * 9000 - 4500, rng.NextDouble() * 9000 - 4500, w, h, ratio, min);
                bool left = handle is CropHandle.Left or CropHandle.TopLeft or CropHandle.BottomLeft, right = handle is CropHandle.Right or CropHandle.TopRight or CropHandle.BottomRight;
                bool top = handle is CropHandle.Top or CropHandle.TopLeft or CropHandle.TopRight, bottom = handle is CropHandle.Bottom or CropHandle.BottomLeft or CropHandle.BottomRight;
                bool corner = (left || right) && (top || bottom);
                string why = $"{handle} @ {ratio.A}:{ratio.B} on {start} in {w}x{h} gave {next}";
                if (!Inside(next, w, h)) throw new Exception("left the picture: " + why);
                if (!CropMath.Conforms(next.Width, next.Height, ratio)) throw new Exception("lost the ratio: " + why);
                if (left && next.Right != start.Right || right && next.X != start.X) throw new Exception("moved the fixed side: " + why);
                if (corner && (top && next.Bottom != start.Bottom || bottom && next.Y != start.Y)) throw new Exception("moved the fixed corner: " + why);
                if (!corner && (left || right) && Math.Abs(next.Y + next.Height / 2.0 - (start.Y + start.Height / 2.0)) > 1 && next.Y != 0 && next.Bottom != h) throw new Exception("drifted off centre: " + why);
                if (!corner && (top || bottom) && Math.Abs(next.X + next.Width / 2.0 - (start.X + start.Width / 2.0)) > 1 && next.X != 0 && next.Right != w) throw new Exception("drifted off centre: " + why);
                if (next.Width < min - 1 || next.Height < min - 1) throw new Exception("too small: " + why);
            }
        });
        Check("typed values and ratio changes always give a valid crop; a typed side dictates the other", () =>
        {
            var rng = new Random(14);
            for (int i = 0; i < 6000; i++)
            {
                int w = rng.Next(60, 5000), h = rng.Next(60, 5000), min = CropMath.MinSize(w, h);
                var ratio = rng.Next(3) == 0 ? (CropRatio?)null : CropRatio.Parse(ratioNames[rng.Next(ratioNames.Length)]);
                var typed = new CropRect(rng.Next(-300, w + 300), rng.Next(-300, h + 300), rng.Next(min, w + 300), rng.Next(min, h + 300));
                var keep = (CropKeep)rng.Next(3);
                var result = CropMath.Constrain(typed, w, h, ratio, min, keep);
                string why = $"{typed} in {w}x{h} keep {keep} ratio {ratio} gave {result}";
                if (!Inside(result, w, h)) throw new Exception("outside: " + why);
                if (ratio is { } r && !CropMath.Conforms(result.Width, result.Height, r)) throw new Exception("lost the ratio: " + why);
                if (ratio == null && (result.Width != Math.Clamp(typed.Width, min, w) || result.Height != Math.Clamp(typed.Height, min, h))) throw new Exception("changed a free size: " + why);
            }
            // Typing a width keeps the width and follows with the height.
            var typedWidth = CropMath.Constrain(new CropRect(100, 100, 800, 999), 2560, 1440, CropRatio.Parse("4:3"), 16, CropKeep.Width);
            Require(typedWidth.Width == 800 && typedWidth.Height == 600 && typedWidth.X == 100 && typedWidth.Y == 100);
            // Choosing a ratio shrinks the frame about its own centre.
            var shrunk = CropMath.Constrain(new CropRect(300, 100, 1200, 800), 1920, 1080, CropRatio.Parse("4:3"), 16);
            Require(shrunk.Height == 800 && shrunk.Width == 1067 && shrunk.X == 366);
        });
        Check("the frame handles are found at the corners, sides and inside, and nowhere else", () =>
        {
            var frame = new CropFrame();
            frame.Measure(new Size(600, 350));
            frame.Arrange(new Rect(0, 0, 600, 350));
            frame.SetSource(1920, 1080);
            var image = frame.ImageViewRect;
            Require(Math.Abs(image.Width - 600) < .01 && Math.Abs(image.Height - 337.5) < .01 && Math.Abs(image.Top - 6.25) < .01);
            frame.SetCrop(new CropRect(480, 270, 960, 540));
            var c = frame.CropViewRect;
            Require(Math.Abs(c.Left - 150) < .01 && Math.Abs(c.Width - 300) < .01);
            Require(frame.HitTest(new Point(c.Left + c.Width / 2, c.Top + c.Height / 2)) == CropHandle.Move);
            Require(frame.HitTest(new Point(c.Left + 3, c.Top + 3)) == CropHandle.TopLeft);
            Require(frame.HitTest(new Point(c.Right - 3, c.Bottom - 3)) == CropHandle.BottomRight);
            Require(frame.HitTest(new Point(c.Left + 3, c.Bottom - 3)) == CropHandle.BottomLeft);
            Require(frame.HitTest(new Point(c.Left + c.Width / 2, c.Top + 3)) == CropHandle.Top);
            Require(frame.HitTest(new Point(c.Right - 3, c.Top + c.Height / 2)) == CropHandle.Right);
            Require(frame.HitTest(new Point(c.Left - 4, c.Top + c.Height / 2)) == CropHandle.Left);
            Require(frame.HitTest(new Point(5, 5)) == CropHandle.None && frame.HitTest(new Point(c.Left - 40, c.Top + 50)) == CropHandle.None);
            // Only the frame takes mouse hits; clicks elsewhere fall through to what is underneath.
            Require(VisualTreeHelper.HitTest(frame, new Point(c.Left + 20, c.Top + 20)) != null && VisualTreeHelper.HitTest(frame, new Point(5, 5)) == null);
        });
        Check("dragging the frame moves and resizes it, reports changes, and keeps a locked ratio", () =>
        {
            var frame = new CropFrame();
            frame.Measure(new Size(600, 350));
            frame.Arrange(new Rect(0, 0, 600, 350));
            frame.SetSource(1920, 1080);
            int started = 0, changed = 0, ended = 0;
            frame.DragStarted += () => started++;
            frame.Changed += () => changed++;
            frame.DragEnded += () => ended++;
            Require(frame.Crop == new CropRect(0, 0, 1920, 1080) && !frame.IsDragging);
            Require(!frame.BeginDrag(new Point(300, 1)) || frame.IsDragging); // the top edge of the picture is grabbable
            frame.EndDrag();
            started = changed = ended = 0;
            // Pull the bottom-right corner in by 100 x 50 view pixels (320 x 160 source pixels).
            var corner = new Point(frame.CropViewRect.Right - 3, frame.CropViewRect.Bottom - 3);
            Require(frame.BeginDrag(corner) && frame.IsDragging);
            frame.DragTo(new Point(corner.X - 100, corner.Y - 50));
            Require(frame.Crop == new CropRect(0, 0, 1600, 920));
            frame.DragTo(new Point(corner.X - 100, corner.Y - 50));
            Require(changed == 1); // dragging to the same place changes nothing
            frame.EndDrag();
            Require(started == 1 && ended == 1 && !frame.IsDragging);
            // Move it: grab the middle and drag right by 62.5 view pixels (200 source pixels; there is room for 320).
            var middle = new Point(frame.CropViewRect.Left + frame.CropViewRect.Width / 2, frame.CropViewRect.Top + frame.CropViewRect.Height / 2);
            Require(frame.BeginDrag(middle));
            frame.DragTo(new Point(middle.X + 62.5, middle.Y));
            frame.EndDrag();
            Require(frame.Crop == new CropRect(200, 0, 1600, 920));
            // Locked ratio: resize and the result keeps 4:3 to within a pixel.
            frame.Ratio = CropRatio.Parse("4:3");
            frame.SetCrop(CropMath.Constrain(frame.Crop, 1920, 1080, frame.Ratio, frame.MinSize));
            var br = new Point(frame.CropViewRect.Right - 3, frame.CropViewRect.Bottom - 3);
            Require(frame.BeginDrag(br));
            frame.DragTo(new Point(br.X - 37, br.Y - 11));
            frame.EndDrag();
            Require(CropMath.Conforms(frame.Crop.Width, frame.Crop.Height, frame.Ratio!.Value) && Inside(frame.Crop, 1920, 1080));
        });
        Check("the busy line stays away from quick work, shows for slow work, and never flashes", () =>
        {
            var line = new BusyLine { ShowDelay = TimeSpan.FromMilliseconds(120), MinVisible = TimeSpan.FromMilliseconds(260) };
            using (line.Begin())
                Pump(40);
            Pump(300);
            Require(!line.IsShown && line.Visibility == Visibility.Collapsed); // finished before the delay: nothing was ever shown
            var slow = line.Begin();
            Pump(60);
            Require(!line.IsShown);
            Pump(160);
            Require(line.IsShown && line.Visibility == Visibility.Visible);
            slow.Dispose();
            Require(line.IsShown); // it has only been up a moment, so it stays a little longer
            Pump(450);
            Require(!line.IsShown && line.Visibility == Visibility.Collapsed);
        });
        Check("overlapping work shares one busy line, and ending a session twice is harmless", () =>
        {
            var line = new BusyLine { ShowDelay = TimeSpan.Zero, MinVisible = TimeSpan.FromMilliseconds(30) };
            var first = line.Begin();
            var second = line.Begin();
            Require(line.IsShown);
            first.Dispose();
            first.Dispose();
            Pump(120);
            Require(line.IsShown); // the second job is still running
            second.Dispose();
            Pump(300);
            Require(!line.IsShown);
            using (line.Begin())
                Require(line.IsShown); // a fresh job after a double dispose still works
        });
        Check("batch progress turns the busy line into a bar; a single job keeps it moving", () =>
        {
            var line = new BusyLine { ShowDelay = TimeSpan.Zero };
            line.Measure(new Size(400, 3));
            line.Arrange(new Rect(0, 0, 400, 3));
            using var session = line.Begin();
            Require(line.Fraction == null);
            session.Report(new JobProgress("processing", 0, 4, "a"));
            Require(line.Fraction == null); // nothing finished yet
            session.Report(new JobProgress("processing", 2, 4, "b"));
            Require(line.Fraction is { } half && Math.Abs(half - .5) < .001);
            session.Report(new JobProgress("processing", 0, 1, "c"));
            Require(line.Fraction == null);
        });
        // End to end with the real worker and FFmpeg. Needs the portable runtime: set ZESTDROP_APP to outputs/ZestDrop.
        string? app = Environment.GetEnvironmentVariable("ZESTDROP_APP");
        if (app != null) Check("a real conversion pauses mid-encode, resumes, and still produces a valid file", () =>
        {
            string root = Path.Combine(Path.GetTempPath(), "zestdrop-e2e-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            void Must(bool ok, string what) { if (!ok) throw new Exception(what); }
            // The directory listing lags while FFmpeg writes, so read the live size through a handle.
            static long Live(string path) { using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); return stream.Length; }
            System.Diagnostics.Process? worker = null;
            try
            {
                string ffmpeg = Path.Combine(app, "runtime", "ffmpeg", "ffmpeg.exe"), source = Path.Combine(root, "clip.mp4");
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ffmpeg, $"-hide_banner -loglevel error -f lavfi -i testsrc2=size=1280x720:rate=30:duration=25 -c:v libx264 -preset ultrafast \"{source}\"") { CreateNoWindow = true, UseShellExecute = false })!.WaitForExit(60000);
                Must(File.Exists(source), "source video was not created");
                string request = Path.Combine(root, "request.json"), response = Path.Combine(root, "result.json");
                File.WriteAllText(request, JsonSerializer.Serialize(new ConversionJob([source], "convert:webm")));
                var start = new System.Diagnostics.ProcessStartInfo(Path.Combine(app, "runtime", "python", "python.exe")) { CreateNoWindow = true, UseShellExecute = false };
                foreach (var arg in new[] { Path.Combine(app, "backend", "worker.py"), "--job", request, response })
                    start.ArgumentList.Add(arg);
                start.Environment["ZESTDROP_JOB"] = "e2etoken";
                worker = System.Diagnostics.Process.Start(start)!;
                string? staging = null;
                for (int i = 0; i < 300 && staging == null; i++)
                {
                    Thread.Sleep(100);
                    staging = Directory.GetFiles(root, ".zestdrop-e2etoken-*").FirstOrDefault(f => Live(f) > 0);
                }
                Must(staging != null, "no tagged staging file appeared"); // the job tags its staging file with its own id
                using (var pause = ProcessTreePause.Suspend(worker))
                {
                    Must(pause.Count >= 2, "pause did not catch python + ffmpeg"); // python + ffmpeg
                    long frozen = Live(staging!);
                    Thread.Sleep(3000);
                    Must(Live(staging!) == frozen && !worker.HasExited, "staging file grew while paused (or worker died)");
                }
                Must(worker.WaitForExit(180000) && worker.ExitCode == 0, "worker failed after resume");
                using var result = JsonDocument.Parse(File.ReadAllText(response));
                var file = result.RootElement.GetProperty("Files")[0];
                string? output = file.GetProperty("Output").GetString();
                Must(output != null && new FileInfo(output).Length > 10000 && !Directory.GetFiles(root, ".zestdrop-*").Any(), "bad output or leftover staging files");
            }
            finally
            {
                try { if (worker is { HasExited: false }) worker.Kill(true); } catch (InvalidOperationException) { }
                try { Directory.Delete(root, true); } catch (IOException) { }
            }
        });
        // The frame must be saved as shown. This feeds frames the UI can produce to the real backend (needs the portable runtime).
        if (app != null) Check("the backend saves every frame the crop UI can produce at exactly the size shown", () =>
        {
            string root = Path.Combine(Path.GetTempPath(), "zestdrop-cropcheck-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var rng = new Random(21);
                var cases = new List<object[]>();
                var shown = new List<(int Width, int Height)>();
                void Add(CropRect crop, string name, int imageWidth, int imageHeight) { cases.Add([crop.X, crop.Y, crop.Width, crop.Height, name]); shown.Add((crop.Width, crop.Height)); }
                for (int i = 0; i < 2400; i++)
                {
                    int w = rng.Next(60, 6000), h = rng.Next(60, 6000);
                    string name = ratioNames[rng.Next(ratioNames.Length)];
                    var ratio = CropRatio.Parse(name)!.Value;
                    int min = CropMath.MinSize(w, h);
                    var start = RandomCrop(rng, w, h, ratio, out _);
                    CropRect crop = (i % 4) switch
                    {
                        0 => start,
                        1 => CropMath.Resize(start, allHandles[rng.Next(allHandles.Length)], rng.NextDouble() * 6000 - 3000, rng.NextDouble() * 6000 - 3000, w, h, ratio, min),
                        2 => CropMath.Constrain(new CropRect(rng.Next(0, w), rng.Next(0, h), rng.Next(min, w + 1), rng.Next(min, h + 1)), w, h, ratio, min, CropKeep.Width),
                        _ => CropMath.Constrain(new CropRect(rng.Next(0, w), rng.Next(0, h), rng.Next(min, w + 1), rng.Next(min, h + 1)), w, h, ratio, min, CropKeep.Height)
                    };
                    Add(crop, name, w, h);
                }
                // The case from the report: typing 856 x 657 at 4:3 used to show 657 but save 642.
                Add(CropMath.Constrain(new CropRect(0, 0, 856, 657), 2560, 1440, CropRatio.Parse("4:3"), 16), "4:3", 2560, 1440);
                Require(shown[^1] == (856, 642));
                string script = Path.Combine(root, "check.py"), input = Path.Combine(root, "cases.json"), output = Path.Combine(root, "sizes.json");
                File.WriteAllText(script, """
                    import sys, json
                    sys.path.insert(0, sys.argv[1])
                    from common import fit_ratio
                    import images
                    from PIL import Image
                    cases = json.load(open(sys.argv[2]))
                    fitted, saved = [], []
                    for index, (x, y, w, h, ratio) in enumerate(cases):
                        fitted.append(list(fit_ratio(w, h, ratio)))
                        if index % 8 == 0:  # the real crop function, on a stand-in picture just big enough
                            picture = Image.new('L', (x + w, y + h))
                            saved.append(list(images.crop(picture, {'x': str(x), 'y': str(y), 'width': str(w), 'height': str(h), 'ratio': ratio}).size))
                        else:
                            saved.append(None)
                    json.dump({'fitted': fitted, 'saved': saved}, open(sys.argv[3], 'w'))
                    """);
                File.WriteAllText(input, JsonSerializer.Serialize(cases));
                var start2 = new System.Diagnostics.ProcessStartInfo(Path.Combine(app, "runtime", "python", "python.exe")) { CreateNoWindow = true, UseShellExecute = false, RedirectStandardError = true };
                foreach (string argument in new[] { script, Path.Combine(app, "backend"), input, output })
                    start2.ArgumentList.Add(argument);
                using (var python = System.Diagnostics.Process.Start(start2)!)
                {
                    string error = python.StandardError.ReadToEnd();
                    if (!python.WaitForExit(180000) || python.ExitCode != 0)
                        throw new Exception("python failed: " + error);
                }
                using var result = JsonDocument.Parse(File.ReadAllText(output));
                int saves = 0;
                for (int i = 0; i < shown.Count; i++)
                {
                    var fit = result.RootElement.GetProperty("fitted")[i];
                    if (fit[0].GetInt32() != shown[i].Width || fit[1].GetInt32() != shown[i].Height)
                        throw new Exception($"case {i} {JsonSerializer.Serialize(cases[i])}: the frame shows {shown[i]} but the backend fits it to {fit}");
                    var saved = result.RootElement.GetProperty("saved")[i];
                    if (saved.ValueKind == JsonValueKind.Array)
                    {
                        saves++;
                        if (saved[0].GetInt32() != shown[i].Width || saved[1].GetInt32() != shown[i].Height)
                            throw new Exception($"case {i} {JsonSerializer.Serialize(cases[i])}: the frame shows {shown[i]} but crop saved {saved}");
                    }
                }
                Require(saves > 250);
            }
            finally { try { Directory.Delete(root, true); } catch (IOException) { } }
        });
        File.WriteAllText(args[0],JsonSerializer.Serialize(new{passed=checks.Count-failed,failed,checks},new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine($"passed={checks.Count-failed}, failed={failed}");return failed==0?0:1;
    }
    private static string JobValidationMessage()
    {
        try { JobValidation.Check(new ConversionJob(["a.mp4"], "trimVideo", new Dictionary<string, string> { ["start"] = "5", ["end"] = "2" }), 0, 0, 10); return ""; }
        catch (ArgumentException ex) { return ex.Message; }
    }
    private static List<T> FindAll<T>(DependencyObject root) where T : DependencyObject
    {
        var found = new List<T>();
        if (root is T match) found.Add(match);
        foreach (var child in LogicalTreeHelper.GetChildren(root)) if (child is DependencyObject node) found.AddRange(FindAll<T>(node));
        return found;
    }
    // Lets timers and animations run for a while without blocking, as the real window does.
    private static void Pump(int milliseconds)
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }
    private static List<string> Texts(DependencyObject root)
    {
        var found = new List<string>();
        if (root is TextBlock text) found.Add(text.Text);
        if (root is ContentControl { Content: string label }) found.Add(label);
        foreach (var child in LogicalTreeHelper.GetChildren(root)) if (child is DependencyObject node) found.AddRange(Texts(node));
        return found;
    }
    private sealed class MemoryStartupProvider : IStartupProvider
    {
        public bool Enabled, Blocked;
        public System.Threading.Tasks.Task<StartupStatus> GetAsync() => System.Threading.Tasks.Task.FromResult(new StartupStatus(Enabled, !Blocked, Blocked ? "Windows 已禁用自启" : null));
        public System.Threading.Tasks.Task<StartupStatus> SetAsync(bool enabled) { Enabled = !Blocked && enabled; return GetAsync(); }
    }
    private sealed class Reporter(Action<JobProgress> callback):IProgress<JobProgress>{public void Report(JobProgress value)=>callback(value);}
}
