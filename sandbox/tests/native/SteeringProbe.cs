using System;
using System.IO;
using System.Linq;
using HarmonyLib;
using MuMech;
using UnityEngine;

namespace RailgunSandbox
{
    // Read-only trace of native attitude input/output, private fixture only.
    [KSPAddon(KSPAddon.Startup.MainMenu,true)]
    public sealed class SteeringProbe : MonoBehaviour
    {
        static double next;
        static string file;
        static int readErrors;
        static double previousUt=double.NaN;
        void Awake()
        {
            if(!Environment.GetCommandLineArgs().Contains("-railgun-steering-probe"))return;
            bool multiplayer=Environment.GetCommandLineArgs().Any(s=>s=="-railgun-mp-test"||s=="-railgun-mp-test-b");
            file=multiplayer?Path.Combine(KSPUtil.ApplicationRootPath,"Railgun-MP-Test","SteeringEvidence.tsv"):
                Path.Combine(KSPUtil.ApplicationRootPath,"saves","Railgun-Reentry-Test","SteeringEvidence.tsv");
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            new Harmony("local.railgun.private-steering-probe").Patch(AccessTools.Method(typeof(MechJebModuleAttitudeController),"SetFlightCtrlState"),
                prefix:new HarmonyMethod(typeof(SteeringProbe),nameof(Before)),postfix:new HarmonyMethod(typeof(SteeringProbe),nameof(After)));
        }
        static void Before(MechJebModuleAttitudeController __instance,Vector3d act,Vector3d deltaEuler,FlightCtrlState s,ref string __state)
        {
            if(readErrors>=3)return;
            try {
            var candidate=__instance.core.vessel;
            if(candidate!=FlightGlobals.ActiveVessel||candidate.altitude>10000)return;
            bool ownReturn=NetworkFleet.IsMultiplayer&&NetworkFleet.Connected&&NetworkFleet.Shared.Ready&&
                NetworkFleet.Shared.Local.Pods.Any(p=>p.Phase==FlightPhase.Landing&&StockPods.Vessel(p)==candidate&&NetworkFleet.Shared.NativeOwner(p,false)&&NetworkFleet.Shared.LeaseValid(p.Number));
            if(!SandboxFlight.ReentryTestSave&&!ownReturn)return;
            double now=Planetarium.GetUniversalTime();
            // An explicit fixture scene reload can rewind UT. Do not suppress
            // the new experiment until it catches the previous run's clock.
            if(!double.IsNaN(previousUt)&&now<previousUt-1)next=0;
            previousUt=now;if(now<next)return;next=now+.1;
            var a=__instance;var v=a.core.vessel;
            var target=a.attitudeGetReferenceRotation(a.attitudeReference)*a.attitudeTarget*Vector3.forward;
            var up=(v.GetWorldPos3D()-v.mainBody.position).normalized;
            __state=FormattableString.Invariant($"{now:R}\t{v.altitude:R}\t{a.attitudeReference}\t{a.attitudeKILLROT}\t{a.attitudeError:R}\t{s.pitch:R}\t{s.yaw:R}\t{s.roll:R}\t{s.pitchTrim:R}\t{s.yawTrim:R}\t{act.x:R}\t{act.y:R}\t{act.z:R}\t{deltaEuler.x:R}\t{deltaEuler.y:R}\t{deltaEuler.z:R}\t{a.torque.x:R}\t{a.torque.y:R}\t{a.torque.z:R}\t{a.AxisControl}\t{a.ActuationControl}\t{Vector3d.Angle(target,v.ReferenceTransform.up):R}\t{Vector3d.Angle(target,up):R}\t{Vector3d.Angle(v.ReferenceTransform.up,up):R}\t{a.activeController}");
            string drivers=Path.Combine(Path.GetDirectoryName(file),"SteeringDrivers.txt");
            if(!File.Exists(drivers)) {
                var lines=new System.Collections.Generic.List<string>();
                foreach(var field in typeof(Vessel).GetFields(System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic).Where(f=>typeof(Delegate).IsAssignableFrom(f.FieldType))) {
                    var callback=field.GetValue(v) as Delegate;
                    if(callback!=null)foreach(var handler in callback.GetInvocationList())lines.Add(field.Name+" : "+handler.Method.DeclaringType?.FullName+"."+handler.Method.Name);
                }
                File.WriteAllLines(drivers,lines);
            }
            } catch(Exception e) { __state=null;readErrors++;Debug.LogWarning("[RailgunPrivateProbe] Read-only steering capture failed: "+e.Message); }
        }
        static void After(FlightCtrlState s,string __state)
        {
            if(__state==null)return;
            try {
                if(!File.Exists(file))File.WriteAllText(file,"ut\taltitude\treference\tkillrot\terror\tinPitch\tinYaw\tinRoll\tpitchTrim\tyawTrim\tactX\tactY\tactZ\tdeltaX\tdeltaY\tdeltaZ\ttorqueX\ttorqueY\ttorqueZ\taxes\tactuation\tphysicalTargetError\trequestedTilt\tuprightTilt\tcontroller\toutPitch\toutYaw\toutRoll\n");
                File.AppendAllText(file,__state+FormattableString.Invariant($"\t{s.pitch:R}\t{s.yaw:R}\t{s.roll:R}\n"));
            } catch(Exception e) {Debug.LogWarning("[RailgunPrivateProbe] "+e.Message);}
        }
    }
}
