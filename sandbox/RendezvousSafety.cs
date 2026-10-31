using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using HarmonyLib;
using MuMech;

namespace RailgunSandbox
{
    public static class RendezvousSafety
    {
        sealed class Registration { public object Owner; public Func<PodRecord> Current; public Func<bool> Authorized; public Action<PodRecord,string> Hold; }
        static readonly Dictionary<MechJebCore,Registration> registered = new Dictionary<MechJebCore,Registration>();
        static readonly Dictionary<string,string> held = new Dictionary<string,string>();
        static readonly HashSet<string> recoveryAttempted = new HashSet<string>();
        static bool installed;
        public static bool Held(string identity, out string reason) => held.TryGetValue(identity, out reason);
        public static void Register(MechJebCore core, object owner, Func<PodRecord> current, Func<bool> authorized, Action<PodRecord,string> hold)
        {
            if (!installed) {
                var method = AccessTools.Method(typeof(MechJebModuleNodeExecutor), "OnFixedUpdate");
                if (method == null) throw new InvalidOperationException("Installed native maneuver executor API is incompatible.");
                new Harmony("local.railgun.rendezvous-budget").Patch(method, prefix: new HarmonyMethod(typeof(RendezvousSafety), nameof(BeforeNode)));
                installed = true;
            }
            registered[core] = new Registration { Owner=owner, Current=current, Authorized=authorized, Hold=hold };
        }
        public static void RemoveOwner(object owner)
        {
            foreach (var item in registered.Where(p=>ReferenceEquals(p.Value.Owner,owner)).ToArray()) registered.Remove(item.Key);
        }
        static bool BeforeNode(MechJebModuleNodeExecutor __instance)
        {
            var core = __instance.core;
            if (!registered.TryGetValue(core,out var scope) || !scope.Authorized()) return true;
            var pod=scope.Current();
            if (pod==null || pod.Phase!=FlightPhase.Rendezvous) return true;
            string reason;
            if (!held.TryGetValue(pod.VesselId,out reason)) {
                var node=core.vessel.patchedConicSolver?.maneuverNodes.FirstOrDefault();
                if (node==null) return true;
                if (node.nextPatch==null) { core.thrust.ThrustOff(); return false; }
                var original=core.vessel.parts.Where(p=>pod.PartIds.Contains(p.flightID)).ToArray();
                var engine=original.SelectMany(p=>p.FindModulesImplementing<ModuleEngines>()).SingleOrDefault();
                var liquid=engine?.propellants.SingleOrDefault(p=>p.name=="LiquidFuel");
                var oxidizer=engine?.propellants.SingleOrDefault(p=>p.name=="Oxidizer");
                var resources=original.Where(p=>p.partInfo.name!=StockPods.FuelCargo&&p.partInfo.name!=StockPods.MonoCargo)
                    .SelectMany(p=>p.Resources.Cast<PartResource>()).Where(r=>r.flowState).ToArray();
                double available=liquid==null||oxidizer==null?double.NaN:RendezvousPolicy.AvailableDeltaV(core.vessel.GetTotalMass(),
                    resources.Where(r=>r.resourceName=="LiquidFuel").Sum(r=>r.amount),resources.Where(r=>r.resourceName=="Oxidizer").Sum(r=>r.amount),
                    liquid.ratio,oxidizer.ratio,liquid.resourceDef.density,oxidizer.resourceDef.density,engine.atmosphereCurve.Evaluate(0),PhysicsGlobals.GravitationalAcceleration);
                reason=RendezvousPolicy.Reject(node.DeltaV.magnitude,node.GetBurnVector(core.vessel.orbit).magnitude,available,
                    node.nextPatch.PeA,node.nextPatch.eccentricity,core.vessel.mainBody.atmosphereDepth);
                if (reason==null) return true;
                string rejectedDetails=FormattableString.Invariant($" / planned {node.DeltaV.magnitude:F1} m/s, remaining {node.GetBurnVector(core.vessel.orbit).magnitude:F1} m/s, available {available:F1} m/s, planned Pe {node.nextPatch.PeA:F0} m");
                if (TryGuardedRecovery(core,scope,pod,node,available,reason)) return false;
                reason+=rejectedDetails;
                UnityEngine.Debug.LogWarning("[RailgunSandbox] "+reason+" / original pod "+pod.VesselId);
                held[pod.VesselId]=reason;
                scope.Hold(pod,reason);
            }
            __instance.Abort();
            core.GetComputerModule<MechJebModuleRendezvousAutopilot>().users.Remove(scope.Owner);
            core.thrust.ThrustOff();
            return false;
        }
        static bool TryGuardedRecovery(MechJebCore core,Registration scope,PodRecord pod,ManeuverNode rejected,double available,string reason)
        {
            // One guarded native intercept per authenticated delivery order.
            // Every candidate retains the orbit, delta-v and return-fuel limits.
            var args=Environment.GetCommandLineArgs();
            bool playerFlight=!SandboxFlight.ReentryTestSave;
            bool constrained=playerFlight||args.Contains("-railgun-rendezvous-intercept-lab");
            string attemptKey=RendezvousPolicy.RecoveryAttemptKey(pod.VesselId,pod.OrderId);
            bool fixture=SandboxFlight.ReentryTestSave&&SandboxFlight.RendezvousFixtureReady;
            bool loopback=constrained&&RecoveryGuidance.PrivateLoopbackEligible;
            if(!(fixture||loopback||playerFlight)||
                !constrained&&!args.Contains("-railgun-rendezvous-recovery-lab")||
                reason!="Rendezvous held: planned orbit intersects the recovery safety boundary"||
                core.node.burnTriggered||core.vessel.patchedConicSolver.maneuverNodes.Count!=1||
                !core.target.NormalTargetExists||core.target.TargetOrbit.referenceBody!=core.vessel.mainBody||
                !FlightRules.Finite(core.target.Distance)||core.target.Distance<=0||
                core.target.Distance>=core.vessel.orbit.radius/25||attemptKey==null||recoveryAttempted.Contains(attemptKey))return false;
            recoveryAttempted.Add(attemptKey);
            double now=Planetarium.GetUniversalTime(),at=now+15;
            var dv=OrbitalManeuverCalculator.DeltaVToMatchVelocities(core.vessel.orbit,at,core.target.TargetOrbit);
            var predicted=core.vessel.orbit.PerturbedOrbit(at,dv);
            double selectedDuration=double.NaN;
            if(constrained) {
                // The native horizon oracle demonstrated that the short100s
                // intercept can violate the orbit floor while200s is safe.
                // Bracket only to one native orbit, without flying trial paths.
                bool found=false;
                double maximum=core.vessel.orbit.period;
                if(!FlightRules.Finite(maximum)||maximum<=0)return false;
                double duration=100;
                for(int query=0;query<16&&duration<=maximum;query++,duration*=2) {
                    var candidate=OrbitalManeuverCalculator.DeltaVToInterceptAtTime(core.vessel.orbit,at,core.target.TargetOrbit,duration);
                    var orbit=core.vessel.orbit.PerturbedOrbit(at,candidate);
                    if(RendezvousPolicy.Reject(candidate.magnitude,candidate.magnitude,available,orbit.PeA,orbit.eccentricity,core.vessel.mainBody.atmosphereDepth)!=null)continue;
                    dv=candidate;predicted=orbit;selectedDuration=duration;found=true;break;
                }
                if(!found)return false;
            }
            string refusal=RendezvousPolicy.Reject(dv.magnitude,dv.magnitude,available,predicted.PeA,predicted.eccentricity,core.vessel.mainBody.atmosphereDepth);
            string directory=NetworkFleet.IsMultiplayer?Path.Combine(KSPUtil.ApplicationRootPath,"Railgun-MP-Test","roundtrip"):
                Path.Combine(KSPUtil.ApplicationRootPath,"saves","Railgun-Reentry-Test");
            Directory.CreateDirectory(directory);
            File.AppendAllText(Path.Combine(directory,"RendezvousRecovery.tsv"),FormattableString.Invariant($"{now:R}\t{pod.VesselId}\t{rejected.UT:R}\t{rejected.DeltaV.magnitude:R}\t{rejected.nextPatch.PeA:R}\t{at:R}\t{dv.magnitude:R}\t{predicted.PeA:R}\t{predicted.eccentricity:R}\t{available:R}\t{refusal??"CandidateAccepted"}\t{selectedDuration:R}\n"));
            if(refusal!=null)return false;
            core.node.Abort();core.thrust.ThrustOff();core.vessel.RemoveAllManeuverNodes();
            var actual=core.vessel.PlaceManeuverNode(core.vessel.orbit,dv,at);
            // The installed patched-conic solver is authoritative too. Failure
            // retains the hold instead of executing a speculative candidate.
            if(actual==null||actual.nextPatch==null||RendezvousPolicy.Reject(actual.DeltaV.magnitude,actual.GetBurnVector(core.vessel.orbit).magnitude,
                available,actual.nextPatch.PeA,actual.nextPatch.eccentricity,core.vessel.mainBody.atmosphereDepth)!=null)return false;
            core.node.ExecuteOneNode(scope.Owner);
            return true;
        }
    }
}
