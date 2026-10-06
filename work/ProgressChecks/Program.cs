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
        L.UseForSession("zh"); // assertions below use Chinese text; never touch the saved choice
        var checks=new List<object>();int failed=0;
        void Check(string name,Action action){try{action();checks.Add(new{test=name,passed=true});}catch(Exception ex){failed++;checks.Add(new{test=name,passed=false,error=ex.Message});}}
        void Require(bool value){if(!value)throw new Exception("Unexpected progress result");}
        Check("layered wheel retains each visible sector's drop target",()=>
        {
            string path=Path.Combine(Path.GetTempPath(),"zestdrop-wheel-"+Guid.NewGuid().ToString("N")+".png");File.WriteAllText(path,"fixture");
            var wheel=new DropWheel((_,_)=>{});
            try
            {
                wheel.Preview([path],false,"convert:jpg");var actions=Catalog.Options([path],false);var hit=typeof(DropWheel).GetMethod("Hit",BindingFlags.NonPublic|BindingFlags.Instance)!;
                for(int i=0;i<actions.Count;i++){double angle=-Math.PI/2+i*2*Math.PI/actions.Count;Require((int)hit.Invoke(wheel,[new Point(190+119*Math.Cos(angle),190+119*Math.Sin(angle))])! == i);}
                Require((int)hit.Invoke(wheel,[new Point(190,190)])! == -1);Require((int)hit.Invoke(wheel,[new Point(190,130)])! == -1);Require((int)hit.Invoke(wheel,[new Point(1,1)])! == -1);
            }
            finally{wheel.Close();File.Delete(path);}
        });
        Check("wheel mode changes keep the dragged file and valid tool sectors",()=>
        {
            string path=Path.Combine(Path.GetTempPath(),"zestdrop-wheel-"+Guid.NewGuid().ToString("N")+".png");File.WriteAllText(path,"fixture");var wheel=new DropWheel((_,_)=>{});
            try{wheel.Preview([path],false,"convert:jpg");wheel.ChangeMode(true);Require(wheel.HasFileDrag&&wheel.ToolsMode);var actions=Catalog.Options([path],true);var hit=typeof(DropWheel).GetMethod("Hit",BindingFlags.NonPublic|BindingFlags.Instance)!;for(int i=0;i<actions.Count;i++){double angle=-Math.PI/2+i*2*Math.PI/actions.Count;Require((int)hit.Invoke(wheel,[new Point(190+119*Math.Cos(angle),190+119*Math.Sin(angle))])! == i);}}
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
                var box = FindAll<CheckBox>(window).Single();
                Require(box.IsChecked == false);
                box.IsChecked = true;
                box.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Require(Settings.GetBool(WelcomeWindow.HideAtStartKey, false));
                // Changing the language rebuilds the open window, and the checkbox still reflects the saved choice.
                L.Language = "zh"; // the same setter the language buttons and the tray menu use
                var chinese = Texts(window);
                Require(chinese.Contains("欢迎使用 ZestDrop") && chinese.Contains("知道了") && FindAll<CheckBox>(window).Single().IsChecked == true);
                window.Close();
            }
            finally
            {
                Settings.PathOverride = null;
                L.UseForSession("zh");
                try { File.Delete(file); } catch (IOException) { }
            }
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
    private static List<string> Texts(DependencyObject root)
    {
        var found = new List<string>();
        if (root is TextBlock text) found.Add(text.Text);
        if (root is ContentControl { Content: string label }) found.Add(label);
        foreach (var child in LogicalTreeHelper.GetChildren(root)) if (child is DependencyObject node) found.AddRange(Texts(node));
        return found;
    }
    private sealed class Reporter(Action<JobProgress> callback):IProgress<JobProgress>{public void Report(JobProgress value)=>callback(value);}
}
