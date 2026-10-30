using System;
using System.IO;
using System.Linq;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace RailgunSandbox
{
    // Private receipt observer. Capture committed server messages, rather than
    // slowing a short physical phase to accommodate filesystem polling.
    [KSPAddon(KSPAddon.Startup.MainMenu,true)]
    public sealed class AuthorityTransitionProbe : MonoBehaviour
    {
        static readonly object gate=new object();
        static string directory,previous;
        static bool installed;
        static int count;
        void Awake()
        {
            if(installed||!Environment.GetCommandLineArgs().Any(s=>s=="-railgun-mp-test"||s=="-railgun-mp-test-b"))return;
            directory=Path.Combine(KSPUtil.ApplicationRootPath,"Railgun-MP-Test","roundtrip","authority");
            new Harmony("local.railgun.private-authority-observer").Patch(AccessTools.Method(typeof(NetworkFleet),"Receive"),
                prefix:new HarmonyMethod(typeof(AuthorityTransitionProbe),nameof(Observe)));
            installed=true;
        }
        static void Observe(string name,byte[] bytes)
        {
            if(name!=FleetWire.Channel)return;
            try {
                var message=FleetWire.Decode(bytes,bytes.Length);
                if(message.Fleet==null)return;
                message.Fleet.Validate();
                var fleet=message.Fleet;
                string key=fleet.Epoch+"/"+string.Join("/",fleet.Slots.Select(s=>s.Pod.Number+":"+s.Pod.Phase+":"+s.Pod.OrderId+":"+s.Pod.CargoTransferred+":"+s.Pod.FlightsCompleted));
                lock(gate) {
                    if(key==previous)return;
                    var take=Path.Combine(directory,(count++).ToString("D6")+"-"+DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffff"));
                    Directory.CreateDirectory(take);
                    File.WriteAllText(Path.Combine(take,"Fleet.xml"),FleetWire.Text(fleet),Encoding.Unicode);
                    File.WriteAllText(Path.Combine(take,"receipt.txt"),"kind="+message.Kind+"\nserverNow="+message.ServerNow+"\nsource=received-server-message\nchangesGameplay=False\n");
                    previous=key;
                }
            } catch(Exception error) {
                // Logging must never prevent the real receiver from consuming
                // its original bytes, even if independent evidence fails.
                try { Directory.CreateDirectory(directory);File.AppendAllText(Path.Combine(directory,"errors.txt"),error+"\n"); } catch { }
            }
        }
    }
}
