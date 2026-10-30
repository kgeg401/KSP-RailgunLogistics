using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using MuMech;
using UnityEngine;

namespace RailgunSandbox
{
    [KSPAddon(KSPAddon.Startup.Flight,false)]
    public sealed class AirbrakeForceOracle : MonoBehaviour
    {
        IEnumerator Start()
        {
            if(!Environment.GetCommandLineArgs().Contains("-railgun-aero-oracle"))yield break;
            if(!SandboxFlight.ReentryTestSave||NetworkFleet.IsMultiplayer)throw new InvalidOperationException("Force oracle requires the private paused-orbit reentry fixture");
            while(!FlightGlobals.ready||SandboxFlight.Instance==null||FlightGlobals.ActiveVessel==null||FlightGlobals.ActiveVessel.packed)yield return null;
            var vessel=FlightGlobals.ActiveVessel;
            var state=typeof(SandboxFlight).GetField("state",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)?.GetValue(SandboxFlight.Instance) as FleetState;
            if(state?.Pods[0].Phase!=FlightPhase.Rendezvous||state.Pods[0].RecoveryTest)
                throw new InvalidOperationException("Force oracle must not run an active return mission");
            if(vessel.altitude<100000||vessel.situation!=Vessel.Situations.ORBITING||vessel.parts.Count!=29||TimeWarp.CurrentRate!=1||
                vessel.parts.SelectMany(p=>p.FindModulesImplementing<ModuleEngines>()).Any(e=>e.finalThrust>0))
                throw new InvalidOperationException("Force oracle requires the intact unpowered orbital pod");
            string directory=Path.Combine(KSPUtil.ApplicationRootPath,"Railgun-Aero-Oracle",DateTime.UtcNow.ToString("yyyyMMddTHHmmss"));Directory.CreateDirectory(directory);
            var panels=vessel.parts.SelectMany(p=>p.FindModulesImplementing<ModuleAeroSurface>()).ToArray();
            if(panels.Length!=4)throw new InvalidOperationException("Expected four native airbrakes");
            var original=panels.Select(p=>p.deploy).ToArray();
            bool scope=false;
            var curves=ReentrySimulation.SimCurves.Borrow(vessel.mainBody);
            // Compile and execute the real vessel-building caller before
            // installing the adapter, as can happen in an existing game.
            var warm=SimulatedVessel.Borrow(vessel,curves,Planetarium.GetUniversalTime(),-1);
            warm.Release();
            AirbrakeForecast.Install(v=>scope&&v==vessel);
            typeof(AirbrakeForecast).GetField("altitude",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static).SetValue(null,10000d);
            var lines=new StringBuilder("open\tuid\tdirection\tmach\tqKpa\tdragErrorKN\tliftErrorKN\tpredictionDragErrorKN\tpredictionLiftErrorKN\tdragMagnitudeKN\tliftMagnitudeKN\tpass\n");
            double maximum=0;int samples=0,failures=0;
            Vector3[] directions={Vector3.up,Vector3.down,Vector3.left,Vector3.right,Vector3.forward,Vector3.back,new Vector3(1,1,1).normalized,new Vector3(-1,.3f,.7f).normalized};
            try { foreach(bool open in new[]{false,true}) {
                foreach(var panel in panels)panel.deploy=open;
                yield return new WaitForSeconds(4.5f);
                scope=true;var pipeline=SimulatedVessel.Borrow(vessel,curves,Planetarium.GetUniversalTime(),-1);scope=false;
                File.AppendAllText(Path.Combine(directory,"pipeline.txt"),"open="+open+" parts="+pipeline.parts.Count+" adapterParts="+pipeline.parts.Count(p=>p.GetType().FullName.Contains("AirbrakeForecast"))+"\n");
                foreach(var panel in panels) {
                    scope=false;var baseline=SimulatedPart.Borrow(panel.part,curves);
                    var prediction=pipeline.parts[vessel.parts.IndexOf(panel.part)];
                    if(baseline.GetType()!=typeof(SimulatedPart))throw new InvalidOperationException("Forecast adapter scope is not isolated");
                    FrozenAirbrake model=null;Exception failure=null;
                    try { model=new FrozenAirbrake(panel); } catch(Exception error) { failure=error; }
                    if(failure!=null) { File.WriteAllText(Path.Combine(directory,"error.txt"),failure.ToString());failures++;break; }
                    for(int index=0;index<directions.Length;index++) {
                        foreach(float mach in new[]{0,.3f,1,2,4,7,10,30})foreach(double pressure in new[]{0,.05,1,10,50,150}) {
                            var local=directions[index]*1000;
                            var world=vessel.GetTransform().TransformDirection(local);
                            float oldLift=panel.liftScalar,oldDrag=panel.dragScalar;
                            panel.SetupCoefficients(world,out var normalVelocity,out var normal,out var dot,out var absolute);
                            Vector3 actualDrag=panel.useInternalDragModel?panel.GetDragVector(normalVelocity,absolute,pressure*1000,mach):Vector3.zero;
                            Vector3 actualLift=panel.GetLiftVector(normal,dot,absolute,pressure*1000,mach);
                            panel.liftScalar=oldLift;panel.dragScalar=oldDrag;
                            var expected=model.Evaluate(local,pressure,mach,10000,true);
                            Vector3d dragLocal=vessel.GetTransform().InverseTransformDirection(actualDrag),liftLocal=vessel.GetTransform().InverseTransformDirection(actualLift);
                            double dragError=(dragLocal-expected.Drag).magnitude,liftError=(liftLocal-expected.Lift).magnitude;
                            double factor=pressure*PhysicsGlobals.DragCubeMultiplier*PhysicsGlobals.DragMultiplier;
                            double reynolds=curves.DragCurvePseudoReynolds.Evaluate((float)(2000*pressure/local.magnitude));
                            var predictedDrag=(prediction.Drag(local,factor,mach)-baseline.Drag(local,factor,mach))*reynolds;
                            double liftFactor=pressure*curves.LiftMachCurve.Evaluate(mach);
                            var predictedLift=prediction.Lift(local,liftFactor)-baseline.Lift(local,liftFactor);
                            double predictionDragError=(dragLocal-predictedDrag).magnitude,predictionLiftError=(liftLocal-predictedLift).magnitude;
                            bool pass=Math.Max(dragError,predictionDragError)<=Math.Max(.00005,dragLocal.magnitude*1e-5)&&Math.Max(liftError,predictionLiftError)<=Math.Max(.00005,liftLocal.magnitude*1e-5);
                            maximum=Math.Max(maximum,Math.Max(Math.Max(dragError,liftError),Math.Max(predictionDragError,predictionLiftError)));samples++;if(!pass)failures++;
                            lines.AppendLine(FormattableString.Invariant($"{open}\t{panel.part.flightID}\t{index}\t{mach:R}\t{pressure:R}\t{dragError:R}\t{liftError:R}\t{predictionDragError:R}\t{predictionLiftError:R}\t{dragLocal.magnitude:R}\t{liftLocal.magnitude:R}\t{pass}"));
                        }
                        yield return null;
                    }
                    baseline.Release();
                }
                pipeline.Release();
            } } finally { scope=false;curves.Release();for(int i=0;i<panels.Length;i++)panels[i].deploy=original[i]; }
            File.WriteAllText(Path.Combine(directory,"forces.tsv"),lines.ToString());
            File.WriteAllText(Path.Combine(directory,"summary.txt"),FormattableString.Invariant($"samples={samples}\nfailures={failures}\nmaximumErrorKN={maximum:R}\nforceUnit=kN\nmassUnit=tonnes\nvesselMass={vessel.GetTotalMass():R}\nrigidbodyMassSum={vessel.parts.Where(p=>p.rb!=null).Sum(p=>(double)p.rb.mass):R}\nphysicalForcesApplied=False\nfullLogisticsRoundTrip=False\n"));
            Debug.Log("[RailgunAeroOracle] Completed "+samples+" native force comparisons; failures="+failures+" maxErrorKN="+maximum.ToString("R"));
        }
    }
}
