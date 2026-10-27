using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace RailgunLogistics.Updates
{
    public sealed class Release
    {
        public string Version, Url, Sha256;
        public readonly Dictionary<string,string> Files = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        public const string Repository = "kgeg401/KSP-RailgunLogistics";
        public const string Channel = "https://raw.githubusercontent.com/" + Repository + "/main/channel/friends.xml";
        public const int MaxDownload = 30 * 1024 * 1024;
        public static string Hash(string path) { using(var s=File.OpenRead(path)) using(var h=SHA256.Create()) return BitConverter.ToString(h.ComputeHash(s)).Replace("-", "").ToLowerInvariant(); }
        public static bool Hex(string s) => s != null && Regex.IsMatch(s, "\\A[0-9a-fA-F]{64}\\z");
        public static bool SafePath(string path)
        {
            if(string.IsNullOrEmpty(path) || path.Contains('\\') || path.Contains(':') || path.StartsWith("/") ||
                path.Split('/').Any(p=>p=="." || p==".." || p.Length==0 || p.EndsWith(".") || p.EndsWith(" ") || p.IndexOfAny(Path.GetInvalidFileNameChars())>=0)) return false;
            if(path=="Receiver.cfg" || path=="RailgunLogistics.restockwhitelist") return true;
            return (path.StartsWith("Parts/",StringComparison.Ordinal) && path.EndsWith(".cfg",StringComparison.Ordinal)) ||
                path=="Plugins/RailgunLogistics.dll" || path=="Updater/RailgunLogistics.Updater.dll" || path=="Updater/ApplyUpdate.exe";
        }
        public static Release Read(string path)
        {
            XDocument doc;
            using(var r=XmlReader.Create(path,new XmlReaderSettings { DtdProcessing=DtdProcessing.Prohibit, XmlResolver=null, MaxCharactersInDocument=65536 })) doc=XDocument.Load(r);
            var root=doc.Root;
            if(root==null || root.Name!="Release" || (string)root.Attribute("KspBuild")!="3190" || (string)root.Attribute("LunaVersion")!="0.29.2.0") throw new InvalidDataException("Unsupported release/game version.");
            var release=new Release { Version=(string)root.Attribute("Version"), Url=(string)root.Attribute("PackageUrl"), Sha256=(string)root.Attribute("Sha256") };
            System.Version parsed;
            if(!System.Version.TryParse(release.Version,out parsed) || !Hex(release.Sha256)) throw new InvalidDataException("Invalid release identity.");
            Uri uri;
            if(!Uri.TryCreate(release.Url,UriKind.Absolute,out uri) || uri.Scheme!="https" || uri.Host!="github.com" || !uri.AbsolutePath.StartsWith("/"+Repository+"/releases/download/",StringComparison.Ordinal) || !uri.AbsolutePath.EndsWith(".zip") || uri.UserInfo!="" || !uri.IsDefaultPort || uri.Query!="" || uri.Fragment!="") throw new InvalidDataException("Package is not a release from the configured repository.");
            foreach(var f in root.Elements("File"))
            {
                string name=(string)f.Attribute("Path"), hash=(string)f.Attribute("Sha256");
                if(!SafePath(name) || !Hex(hash) || release.Files.ContainsKey(name)) throw new InvalidDataException("Invalid/duplicate package file.");
                release.Files.Add(name,hash);
            }
            foreach(var required in new[]{"Plugins/RailgunLogistics.dll","Parts/CargoPod.cfg","Updater/RailgunLogistics.Updater.dll","Updater/ApplyUpdate.exe"})
                if(!release.Files.ContainsKey(required)) throw new InvalidDataException("Incomplete client release.");
            if(release.Files.Count>100) throw new InvalidDataException("Too many files.");
            return release;
        }
        public bool Matches(string folder) => Files.All(f=>File.Exists(Path.Combine(folder,f.Key)) && string.Equals(Hash(Path.Combine(folder,f.Key)),f.Value,StringComparison.OrdinalIgnoreCase));
        public void ExtractVerified(string zipPath,string destination)
        {
            if(!string.Equals(Hash(zipPath),Sha256,StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Download checksum mismatch.");
            var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using(var zip=ZipFile.OpenRead(zipPath))
            {
                long size=0;
                foreach(var e in zip.Entries)
                {
                    string expected;
                    if(!SafePath(e.FullName) || !Files.TryGetValue(e.FullName,out expected) || !seen.Add(e.FullName) || (size+=e.Length)>100*1024*1024) throw new InvalidDataException("Unexpected or unsafe ZIP entry.");
                    string target=Path.Combine(destination,e.FullName);
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    using(var input=e.Open()) using(var output=new FileStream(target,FileMode.CreateNew)) input.CopyTo(output);
                    if(!string.Equals(Hash(target),expected,StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Package file checksum mismatch.");
                }
            }
            if(seen.Count!=Files.Count) throw new InvalidDataException("Package is missing files.");
        }
        public static void Download(string url,string destination,int limit)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var request=(HttpWebRequest)WebRequest.Create(url);
            request.UserAgent="RailgunLogistics-Updater/1.0"; request.Timeout=20000; request.ReadWriteTimeout=20000;
            using(var response=(HttpWebResponse)request.GetResponse())
            {
                if(response.ResponseUri.Scheme!="https") throw new InvalidDataException("Insecure download redirect.");
                using(var input=response.GetResponseStream()) using(var output=new FileStream(destination,FileMode.Create))
                { var buffer=new byte[16384]; int count,total=0; while((count=input.Read(buffer,0,buffer.Length))>0) { total+=count; if(total>limit) throw new InvalidDataException("Download too large."); output.Write(buffer,0,count); } }
            }
        }
        public static void CheckGame(string root)
        {
            if(!File.Exists(Path.Combine(root,"KSP_x64.exe")) || !Regex.IsMatch(File.ReadAllText(Path.Combine(root,"buildID64.txt")),@"build id = 0?3190\b")) throw new InvalidDataException("KSP 1.12.5 build 3190 required.");
            string lmp=Path.Combine(root,"GameData/LunaMultiplayer/Plugins/LmpClient.dll");
            if(System.Reflection.AssemblyName.GetAssemblyName(lmp).Version.ToString()!="0.29.2.0") throw new InvalidDataException("Luna 0.29.2 required.");
        }
        public static void NoLinks(string path)
        {
            for(var dir=new DirectoryInfo(Path.GetFullPath(path));dir!=null;dir=dir.Parent)
                if(dir.Exists && (dir.Attributes&FileAttributes.ReparsePoint)!=0) throw new IOException("Update paths cannot be junctions or links.");
        }
    }
}
