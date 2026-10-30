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
        void Awake()
        {
            if(!Environment.GetCommandLineArgs().Contains("-railgun-steering-probe"))return;
            file=Path.Combine(KSPUtil.ApplicationRootPath,"saves","Railgun-Reentry-Test","SteeringEvidence.tsv");
            new Harmony("local.railgun.private-steering-probe").Patch(AccessTools.Method(typeof(MechJebModuleAttitudeController),"SetFlightCtrlState"),
                prefix:new HarmonyMethod(typeof(SteeringProbe),nameof(Before)),postfix:new HarmonyMethod(typeof(SteeringProbe),nameof(After)));
        }
        static void Before(MechJebModuleAttitudeController __instance,Vector3d act,Vector3d deltaEuler,FlightCtrlState s,ref string __state)
        {
            if(!SandboxFlight.ReentryTestSave||NetworkFleet.IsMultiplayer||__instance.core.vessel!=FlightGlobals.ActiveVessel||__instance.core.vessel.altitude>10000)return;
            double now=Planetarium.GetUniversalTime();if(now<next)return;next=now+.1;
            var a=__instance;var v=a.core.vessel;
            __state=FormattableString.Invariant($"{now:R}\t{v.altitude:R}\t{a.attitudeReference}\t{a.attitudeKILLROT}\t{a.attitudeError:R}\t{s.pitch:R}\t{s.yaw:R}\t{s.roll:R}\t{s.pitchTrim:R}\t{s.yawTrim:R}\t{act.x:R}\t{act.y:R}\t{act.z:R}\t{deltaEuler.x:R}\t{deltaEuler.y:R}\t{deltaEuler.z:R}\t{a.torque.x:R}\t{a.torque.y:R}\t{a.torque.z:R}\t{a.AxisControl}\t{a.ActuationControl}");
        }
        static void After(FlightCtrlState s,string __state)
        {
            if(__state==null)return;
            try {
                if(!File.Exists(file))File.WriteAllText(file,"ut\taltitude\treference\tkillrot\terror\tinPitch\tinYaw\tinRoll\tpitchTrim\tyawTrim\tactX\tactY\tactZ\tdeltaX\tdeltaY\tdeltaZ\ttorqueX\ttorqueY\ttorqueZ\taxes\tactuation\toutPitch\toutYaw\toutRoll\n");
                File.AppendAllText(file,__state+FormattableString.Invariant($"\t{s.pitch:R}\t{s.yaw:R}\t{s.roll:R}\n"));
            } catch(Exception e) {Debug.LogWarning("[RailgunPrivateProbe] "+e.Message);}
        }
    }
}
