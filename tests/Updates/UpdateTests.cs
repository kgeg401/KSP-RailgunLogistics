using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using RailgunLogistics.Updates;

static class UpdateTests
{
    static int count;
    static void Assert(bool value,string name) { if(!value) throw new Exception(name); count++; Console.WriteLine("PASS "+name); }
    static void Reject(Action action,string name) { bool failed=false; try { action(); } catch(InvalidDataException) { failed=true; } catch(System.Xml.XmlException) { failed=true; } Assert(failed,name); }
    static int Main(string[] args)
    {
        string source=Path.GetFullPath(args[0]);
        string lab=Path.Combine(Path.GetTempPath(),"RailgunUpdaterTests-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(lab);
        var release=Release.Read(Path.Combine(source,"friends.xml"));
        string zip=Directory.GetFiles(source,"RailgunLogistics-Client-*.zip").Single();
        string good=Path.Combine(lab,"good"); release.ExtractVerified(zip,good);
        Assert(release.Matches(good),"valid payload extracts and matches all files");
        File.AppendAllText(Path.Combine(good,"Receiver.cfg"),"corrupt"); Assert(!release.Matches(good),"local changed file triggers repair");
        foreach(var path in new[]{"../saves/persistent.sfs","Parts/../../x.cfg","Parts\\x.cfg","/Parts/x.cfg","C:/x.cfg","Parts/x.cfg:other","Parts//x.cfg","Parts/x.cfg.","Settings.xml","Server/Plugins/x.dll","Updater/evil.exe"}) Assert(!Release.SafePath(path),"reject "+path);
        string badZip=Path.Combine(lab,"bad.zip"); File.Copy(zip,badZip); using(var output=new FileStream(badZip,FileMode.Open)) output.WriteByte(123);
        Reject(()=>release.ExtractVerified(badZip,Path.Combine(lab,"bad")),"reject archive checksum mismatch");
        Action<string,string> badManifest=(attribute,value)=> {
            var doc=XDocument.Load(Path.Combine(source,"friends.xml")); doc.Root.SetAttributeValue(attribute,value);
            string path=Path.Combine(lab,"manifest.xml"); doc.Save(path); Reject(()=>Release.Read(path),"reject manifest "+attribute+"="+value);
        };
        badManifest("PackageUrl","https://evil.example/release.zip"); badManifest("PackageUrl","http://github.com/"+Release.Repository+"/releases/download/v1/p.zip");
        badManifest("PackageUrl","https://github.com/other/repo/releases/download/v1/p.zip"); badManifest("KspBuild","3189"); badManifest("LunaVersion","0.29.1.0"); badManifest("Sha256","garbage");
        var duplicate=XDocument.Load(Path.Combine(source,"friends.xml")); duplicate.Root.Add(new XElement(duplicate.Root.Elements("File").First()));
        string duplicatePath=Path.Combine(lab,"duplicate.xml"); duplicate.Save(duplicatePath); Reject(()=>Release.Read(duplicatePath),"reject duplicate manifest entries");
        foreach(string injected in new[]{"../outside.cfg","Receiver.cfg"}) {
            string malicious=Path.Combine(lab,Guid.NewGuid()+".zip");
            File.Copy(zip,malicious); using(var a=ZipFile.Open(malicious,ZipArchiveMode.Update)) using(var w=new StreamWriter(a.CreateEntry(injected).Open())) w.Write("evil");
            string before=release.Sha256; release.Sha256=Release.Hash(malicious);
            Reject(()=>release.ExtractVerified(malicious,Path.Combine(lab,Guid.NewGuid().ToString("N"))),"reject ZIP injected "+injected); release.Sha256=before;
        }
        string missing=Path.Combine(lab,"missing.zip"); File.Copy(zip,missing); using(var a=ZipFile.Open(missing,ZipArchiveMode.Update)) a.Entries.First().Delete();
        release.Sha256=Release.Hash(missing); Reject(()=>release.ExtractVerified(missing,Path.Combine(lab,"missing")),"reject missing required payload file");
        string tampered=Path.Combine(lab,"tampered.zip"); File.Copy(zip,tampered);
        using(var a=ZipFile.Open(tampered,ZipArchiveMode.Update)) { a.GetEntry("Receiver.cfg").Delete(); using(var w=new StreamWriter(a.CreateEntry("Receiver.cfg").Open())) w.Write("changed"); }
        release.Sha256=Release.Hash(tampered); Reject(()=>release.ExtractVerified(tampered,Path.Combine(lab,"tampered")),"reject file checksum even when archive checksum matches");
        string xmlBomb=Path.Combine(lab,"dtd.xml"); File.WriteAllText(xmlBomb,"<!DOCTYPE a [<!ENTITY test SYSTEM 'file:///C:/Windows/win.ini'>]><Release>&test;</Release>");
        Reject(()=>Release.Read(xmlBomb),"reject external XML entities");
        Console.WriteLine(count+" updater checks passed. Fixtures: "+lab); return 0;
    }
}
