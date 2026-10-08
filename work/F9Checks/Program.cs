using ZestDrop;
using System.Text.Json;

L.UseForSession("zh"); // assertions use Chinese messages; never touch the saved choice
var checks=new List<object>();int failed=0;
void Check(string name,Action body)
{
    try{body();checks.Add(new{test=name,passed=true});}
    catch(Exception ex){failed++;checks.Add(new{test=name,passed=false,error=ex.Message});}
}
void Require(bool value){if(!value)throw new Exception("Unexpected result");}
ConversionJob Job(string id,string ext,params (string,string)[] fields)
{
    var p=Catalog.Definition(id,Catalog.Category("sample."+ext)).Fields!.ToDictionary(f=>f.Name,f=>f.Default);
    foreach(var (key,value) in fields)p[key]=value;
    return new(["sample."+ext],id,p);
}
void Reject(ConversionJob job,int w=160,int h=120,double seconds=10,int pages=3)
{
    try{JobValidation.ResolveRatio(job.Parameters!);JobValidation.Check(job,w,h,seconds,pages);}
    catch(ArgumentException){return;}
    throw new Exception("Invalid parameters were accepted");
}
foreach(string tool in new[]{"cropImage","cropVideo"})
{
    Check(tool+" offers custom aspect ratio",()=>Require(Catalog.Definition(tool,"image").Fields!.First(f=>f.Name=="ratio").Choices!.Contains("custom")));
    Check(tool+" resolves decimal custom ratio for the real worker",()=>{var j=Job(tool,tool=="cropImage"?"png":"mp4",("width","150"),("height","100"),("ratio","custom"),("ratioWidth","2.35"),("ratioHeight","1"));JobValidation.ResolveRatio(j.Parameters!);JobValidation.Check(j,160,120);Require(j.Parameters!["ratio"]=="2.35:1");});
    foreach(string invalid in new[]{"0","-1","NaN","Infinity","abc",""})Check(tool+" rejects custom ratio width "+invalid,()=>Reject(Job(tool,"png",("ratio","custom"),("ratioWidth",invalid),("width","150"),("height","100"))));
}
foreach(string tool in new[]{"rotateImage","rotateVideo"})
{
    Check(tool+" offers quarter turns and flips, starting unchanged",()=>
    {
        var fields=Catalog.Definition(tool,tool=="rotateImage"?"image":"video").Fields!;
        Require(fields.Single(f=>f.Name=="angle").Kind=="number"&&fields.Single(f=>f.Name=="angle").Default=="0");
        Require(fields.Single(f=>f.Name=="expand").Choices!.SequenceEqual(tool=="rotateImage" ? ["crop","expand","keep"] : ["expand","keep"]));
        Require(fields.Single(f=>f.Name=="expand").Default == (tool=="rotateImage" ? "crop" : "expand"));
        Require(fields.Single(f=>f.Name=="flip").Choices!.SequenceEqual(["none","horizontal","vertical"])&&fields.Single(f=>f.Name=="flip").Default=="none");
    });
    Check(tool+" is available for pictures and videos of any size",()=>{var j=Job(tool,tool=="rotateImage"?"png":"mp4",("angle","90"));JobValidation.Check(j,160,120,10,0);});
}
foreach(string angle in new[]{"-1","361","abc"})
    Check("a rotation of "+angle+" degrees is refused",()=>Reject(Job("rotateImage","png",("angle",angle))));
foreach(string angle in new[]{"0","0.5","45","89.9","123.45","270","360"})
    Check("a rotation of "+angle+" degrees is accepted",()=>JobValidation.Check(Job("rotateVideo","mp4",("angle",angle)),160,120,10,0));
Check("crop coordinates cannot extend outside original image",()=>Reject(Job("cropImage","png",("x","120"),("width","50"),("height","30"))));
Check("zero crop dimensions cannot be saved",()=>Reject(Job("cropVideo","mp4",("width","0"),("height","30"))));
Check("fractional pixel dimensions rejected",()=>Reject(Job("cropImage","png",("width","30.5"),("height","30"))));
Check("non-finite edits rejected",()=>Reject(Job("editImage","png",("exposure","NaN"))));
Check("invalid JSON regions rejected",()=>Reject(Job("redactImage","png",("width","30"),("height","30"),("regions","{bad}"))));
Check("backwards time range rejected",()=>Reject(Job("trimVideo","mp4",("start","8"),("end","2"))));
Check("time outside source duration rejected",()=>Reject(Job("trimAudio","wav",("start","1"),("end","20"))));
Check("invalid clock seconds rejected",()=>Reject(Job("trimVideo","mp4",("start","0:99"),("end","0"))));
Check("clock format and end-zero supported",()=>JobValidation.Check(Job("trimAudio","wav",("start","0:01.5"),("end","0")),0,0,10));
Check("out-of-range mosaic block size rejected",()=>Reject(Job("redactImage","png",("width","40"),("height","30"),("blockSize","1"))));
Check("mosaic is the default for image and video redaction",()=>Require(new[]{"redactImage","redactVideo"}.All(id=>Catalog.Definition(id,"image").Fields!.First(f=>f.Name=="style").Default=="pixelate")));
Check("PDF page numbers beyond source rejected",()=>Reject(Job("organizePDF","pdf",("pageOrder","1,4"))));
Check("PDF repeated/reordered pages allowed",()=>JobValidation.Check(Job("organizePDF","pdf",("pageOrder","3,1,1")),0,0,0,3));
Check("negative snapshot frame offset retained",()=>JobValidation.Check(Job("videoSnapshots","mp4",("time","1"),("frame","-1")),160,120,10));
var report=new{passed=checks.Count-failed,failed,checks};File.WriteAllText(args[0],JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine($"passed={checks.Count-failed}, failed={failed}");return failed==0?0:1;
