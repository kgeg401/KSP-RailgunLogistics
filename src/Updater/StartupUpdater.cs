using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace RailgunLogistics.Updates
{
    [KSPAddon(KSPAddon.Startup.MainMenu,true)]
    public sealed class StartupUpdater : MonoBehaviour
    {
        static bool started;
        readonly ConcurrentQueue<string> notices=new ConcurrentQueue<string>();
        string message;
        Rect window=new Rect(160,160,480,210);
        void Start()
        {
            if(started) { Destroy(this); return; } started=true; DontDestroyOnLoad(gameObject);
            string root=KSPUtil.ApplicationRootPath;
            UnityEngine.Debug.Log("[RailgunUpdater] Checking the Friends server channel on GitHub.");
            // Networking and ZIP work never block Unity's main thread.
            Task.Run(()=>Check(root));
        }
        void Check(string root)
        {
            try
            {
                string mod=Path.Combine(root,"GameData/RailgunLogistics");
                if(File.Exists(Path.Combine(mod,"DisableAutoUpdates.txt"))) return;
                Release.CheckGame(root);
                string state=Path.Combine(root,"RailgunLogistics-Updates"); Release.NoLinks(state); Directory.CreateDirectory(state);
                // Keep diagnostics/staged downloads outside GameData and outside any save.
                string job=Path.Combine(state,Guid.NewGuid().ToString("N")); Directory.CreateDirectory(job);
                string manifest=Path.Combine(job,"release.xml");
                Release.Download(Release.Channel+"?check="+DateTime.UtcNow.Ticks,manifest,65536);
                var release=Release.Read(manifest);
                if(release.Matches(mod)) { File.WriteAllText(Path.Combine(state,"last-check.txt"),DateTime.UtcNow.ToString("o")+" Current: "+release.Version); return; }
                Release.Download(release.Url,Path.Combine(job,"client.zip"),Release.MaxDownload);
                release.ExtractVerified(Path.Combine(job,"client.zip"),Path.Combine(job,"preview"));
                // Execute the already-installed helper, never executable code straight from a download.
                string helper=Path.Combine(job,"ApplyUpdate.exe");
                File.Copy(Path.Combine(mod,"Updater/ApplyUpdate.exe"),helper);
                Process.Start(new ProcessStartInfo(helper,"\""+root.TrimEnd('\\','/')+"\" \""+job+"\"") { UseShellExecute=false, CreateNoWindow=true, WindowStyle=ProcessWindowStyle.Hidden });
                notices.Enqueue("Railgun Logistics "+release.Version+" is downloaded and verified. Quit KSP normally, wait a few seconds, then start it again. The update installs automatically after KSP closes. Your saves and other mods are preserved.");
                File.WriteAllText(Path.Combine(state,"last-check.txt"),DateTime.UtcNow.ToString("o")+" Staged: "+release.Version+" Job: "+job);
            }
            catch(Exception ex)
            {
                notices.Enqueue("Railgun update check could not finish. Your installed mod is unchanged; you can keep playing. Details: "+ex.Message);
            }
        }
        void Update() { string text; while(notices.TryDequeue(out text)) { message=text; UnityEngine.Debug.Log("[RailgunUpdater] "+text); } }
        void OnGUI()
        {
            if(message==null) return;
            GUI.skin=HighLogic.Skin;
            window=GUILayout.Window(GetInstanceID(),window,id=> {
                GUILayout.Label(message);
                if(GUILayout.Button("Understood")) message=null;
                GUI.DragWindow(new Rect(0,0,480,25));
            },"Railgun Logistics update");
        }
    }
}
