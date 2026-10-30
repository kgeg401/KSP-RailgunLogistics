using System;
using System.IO;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace RailgunSandbox
{
    // Test-only observer. Does not change the transfer, its arguments, or exceptions.
    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public sealed class CargoTransferProbe : MonoBehaviour
    {
        static bool installed;
        sealed class Capture { public string Before, Order, Resource; public int Pod; public double Ut, Cargo, Receiver; public uint Source; }
        void Awake()
        {
            if(installed||!Environment.GetCommandLineArgs().Any(s=>s=="-railgun-mp-test"||s=="-railgun-mp-test-b"))return;
            new Harmony("local.railgun.private-cargo-observer").Patch(AccessTools.Method(typeof(SandboxFlight),"Transfer"),
                prefix:new HarmonyMethod(typeof(CargoTransferProbe),nameof(Before)),
                finalizer:new HarmonyMethod(typeof(CargoTransferProbe),nameof(After)));
            installed=true;
        }
        static double ReceiverAmount(PodRecord pod,Vessel vessel)=>vessel.parts.Where(p=>!pod.PartIds.Contains(p.flightID))
            .SelectMany(p=>p.Resources.Cast<PartResource>()).Where(r=>r.resourceName==pod.Resource).Sum(r=>r.amount);
        static string Snapshot(Vessel vessel) { var node=new ConfigNode("VESSEL");vessel.BackupVessel().Save(node);return node.ToString(); }
        static void Before(PodRecord p,Vessel v,ref Capture __state)
        {
            try {
                if(!NetworkFleet.IsMultiplayer||!NetworkFleet.Connected||p.CargoTransferred)return;
                var source=v.parts.FirstOrDefault(part=>part.flightID==p.CargoPart)?.Resources[p.Resource];
                if(source==null)return;
                __state=new Capture { Before=Snapshot(v),Order=p.OrderId,Resource=p.Resource,Pod=p.Number,Ut=Planetarium.GetUniversalTime(),
                    Cargo=source.amount,Receiver=ReceiverAmount(p,v),Source=p.CargoPart };
            } catch(Exception error) { Error(error); }
        }
        static Exception After(PodRecord p,Vessel v,Capture __state,Exception __exception)
        {
            try {
                if(__state==null)return __exception;
                var source=v.parts.FirstOrDefault(part=>part.flightID==__state.Source)?.Resources[__state.Resource];
                double cargo=source?.amount??double.NaN,receiver=ReceiverAmount(p,v);
                if(cargo==__state.Cargo&&receiver==__state.Receiver&&__exception==null)return __exception;
                var root=Path.Combine(KSPUtil.ApplicationRootPath,"Railgun-MP-Test","roundtrip","transactions");Directory.CreateDirectory(root);
                var take=Path.Combine(root,__state.Order+"-"+__state.Ut.ToString("F3",System.Globalization.CultureInfo.InvariantCulture));Directory.CreateDirectory(take);
                File.WriteAllText(Path.Combine(take,"before.txt"),__state.Before);File.WriteAllText(Path.Combine(take,"after.txt"),Snapshot(v));
                File.WriteAllText(Path.Combine(take,"fleet.xml"),FleetWire.Text(NetworkFleet.Shared.Local),System.Text.Encoding.Unicode);
                File.WriteAllText(Path.Combine(take,"transaction.tsv"),"ut\tpod\torder\tresource\tsource\tcargoBefore\tcargoAfter\treceiverBefore\treceiverAfter\texception\n"+
                    FormattableString.Invariant($"{__state.Ut:R}\t{__state.Pod}\t{__state.Order}\t{__state.Resource}\t{__state.Source}\t{__state.Cargo:R}\t{cargo:R}\t{__state.Receiver:R}\t{receiver:R}\t{__exception?.GetType().FullName??""}\n"));
            } catch(Exception error) { Error(error); }
            return __exception;
        }
        static void Error(Exception error)
        {
            try { var root=Path.Combine(KSPUtil.ApplicationRootPath,"Railgun-MP-Test");Directory.CreateDirectory(root);File.AppendAllText(Path.Combine(root,"cargo-probe-errors.txt"),error+"\n"); }
            catch { Debug.LogWarning("[RailgunMPTest] Cargo observer failed; independent evidence incomplete."); }
        }
    }
}
