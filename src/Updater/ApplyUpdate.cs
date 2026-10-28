using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace RailgunLogistics.Updates
{
    static class ApplyUpdate
    {
        static void CopyTree(string source,string target)
        {
            Release.NoLinks(source); Directory.CreateDirectory(target);
            foreach(var file in Directory.GetFiles(source))
            { if((File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0) throw new IOException("Linked file refused."); File.Copy(file,Path.Combine(target,Path.GetFileName(file)),true); }
            foreach(var folder in Directory.GetDirectories(source)) CopyTree(folder,Path.Combine(target,Path.GetFileName(folder)));
        }
        static bool GameRunning(string exe)
        {
            foreach(var p in Process.GetProcessesByName("KSP_x64")) using(p)
            { try { if(string.Equals(p.MainModule.FileName,exe,StringComparison.OrdinalIgnoreCase)) return true; } catch { return true; } }
            return false;
        }
        static int Main(string[] args)
        {
            if(args.Length!=2) return 2;
            string root=Path.GetFullPath(args[0]),job=Path.GetFullPath(args[1]);
            string log=Path.Combine(job,"result.txt");
            try
            {
                Release.NoLinks(root); Release.NoLinks(job); Release.CheckGame(root);
                // One writer per game install; the lock is outside the directory being replaced.
                string state=Path.Combine(root,"RailgunLogistics-Updates"); Directory.CreateDirectory(state); Release.NoLinks(state);
                using(var locked=new FileStream(Path.Combine(state,"apply.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None))
                {
                    File.WriteAllText(log,"Waiting for KSP to close.");
                    while(GameRunning(Path.Combine(root,"KSP_x64.exe"))) Thread.Sleep(1000);
                    var release=Release.Read(Path.Combine(job,"release.xml"));
                    release.CheckRequirements(root);
                    string verified=Path.Combine(job,"verified-"+Guid.NewGuid().ToString("N"));
                    release.ExtractVerified(Path.Combine(job,"client.zip"),verified);
                    string target=Path.Combine(root,"GameData/RailgunLogistics"); Release.NoLinks(target);
                    string stamp=DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,8);
                    // Stage OUTSIDE GameData so KSP can never load a second copy of the DLL.
                    string stage=Path.Combine(state,"stage-"+stamp); Directory.CreateDirectory(stage);
                    if(Directory.Exists(target)) CopyTree(target,stage);
                    foreach(var file in release.Files.Keys)
                    { string dest=Path.Combine(stage,file); Directory.CreateDirectory(Path.GetDirectoryName(dest)); File.Copy(Path.Combine(verified,file),dest,true); }
                    File.Copy(Path.Combine(job,"release.xml"),Path.Combine(stage,"InstalledRelease.xml"),true);
                    if(!release.Matches(stage)) throw new IOException("Staging verification failed.");
                    string backup=Path.Combine(root,"RailgunLogistics-Backups","autoupdate-"+stamp);
                    Directory.CreateDirectory(Path.GetDirectoryName(backup)); Release.NoLinks(backup);
                    // Recheck immediately before touching the installed directory.
                    while(GameRunning(Path.Combine(root,"KSP_x64.exe"))) Thread.Sleep(1000);
                    bool moved=false;
                    try
                    {
                        if(Directory.Exists(target)) { Directory.Move(target,backup); moved=true; }
                        Directory.Move(stage,target);
                    }
                    catch { if(moved && !Directory.Exists(target)) Directory.Move(backup,target); throw; }
                    File.WriteAllText(log,"Installed "+release.Version+". Start KSP normally. Backup: "+backup);
                }
                return 0;
            }
            catch(Exception ex) { try { File.WriteAllText(log,"Update failed; existing install retained. "+ex); } catch {} return 1; }
        }
    }
}
