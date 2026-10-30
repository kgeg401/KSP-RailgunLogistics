using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using MuMech;
using UnityEngine;

namespace RailgunSandbox
{
    public struct SurfaceForces { public Vector3d Drag, Lift; }

    public sealed class FrozenAirbrake
    {
        readonly AeroCurve drag,dragMach,lift,liftMach;
        readonly float coefficient,dragMultiplier,liftMultiplier;
        readonly Vector3 normal,closed,opened;
        readonly bool internalDrag,omnidirectional,perpendicular,shielded,plannedOpen;
        static readonly FieldInfo deflection=AccessTools.Field(typeof(ModuleControlSurface),"deflection");
        public static float NativeDeflection(ModuleAeroSurface panel)
        {
            if(deflection?.FieldType!=typeof(float))throw new InvalidOperationException("Native airbrake actuator field is incompatible");
            return (float)deflection.GetValue(panel);
        }
        static AeroCurve Freeze(FloatCurve curve)
        {
            var keys=curve.Curve.keys;
            if(keys.Any(k=>k.weightedMode!=WeightedMode.None))throw new InvalidOperationException("Weighted stock airbrake curves require a validated adapter");
            return new AeroCurve(keys.Select(k=>new AeroKnot(k.time,k.value,k.inTangent,k.outTangent)));
        }
        public FrozenAirbrake(ModuleAeroSurface panel)
        {
            if(deflection==null||deflection.FieldType!=typeof(float)||panel.nodeEnabled||panel.displaceVelocity||
                !panel.ignorePitch||!panel.ignoreYaw||!panel.ignoreRoll)
                throw new InvalidOperationException("Unsupported native airbrake geometry or control mode");
            var flap=panel.part.FindModelTransform(panel.transformName);
            if(flap==null||flap.parent==null)throw new InvalidOperationException("Native airbrake flap transform missing");
            panel.SetupCoefficients(Vector3.up,out var unused,out var nativeNormal,out var unusedDot,out var unusedAbs);
            var root=panel.vessel.GetTransform();
            normal=root.InverseTransformDirection(nativeNormal);
            var axis=root.InverseTransformDirection(flap.parent.TransformDirection(Vector3.right));
            closed=Quaternion.AngleAxis(-NativeDeflection(panel),axis)*normal;
            opened=Quaternion.AngleAxis((panel.deployInvert?-1:1)*panel.aeroDeployAngle,axis)*closed;
            coefficient=panel.deflectionLiftCoeff;dragMultiplier=PhysicsGlobals.LiftDragMultiplier;liftMultiplier=PhysicsGlobals.LiftMultiplier;
            internalDrag=panel.useInternalDragModel;omnidirectional=panel.omnidirectional;perpendicular=panel.perpendicularOnly;
            shielded=panel.part.ShieldedFromAirstream;plannedOpen=panel.deploy;
            drag=Freeze(panel.dragCurve);dragMach=Freeze(panel.dragMachCurve);lift=Freeze(panel.liftCurve);liftMach=Freeze(panel.liftMachCurve);
        }
        public SurfaceForces Evaluate(Vector3d localVelocity,double pressureKpa,float mach,double altitude,bool currentPose=false)
        {
            if(!FlightRules.Finite(pressureKpa)||pressureKpa<0||!FlightRules.Finite(mach)||mach<0||!FlightRules.Finite(altitude))
                throw new ArgumentException("Invalid airbrake forecast state");
            if(shielded||localVelocity.sqrMagnitude==0)return default;
            Vector3 direction=((Vector3)localVelocity).normalized;
            Vector3 axis=currentPose?normal:plannedOpen&&altitude>=1500&&localVelocity.magnitude>=150?opened:closed;
            float dot=Vector3.Dot(direction,axis),absolute=omnidirectional?Mathf.Abs(dot):Mathf.Clamp01(dot);
            float dragScalar=drag.Evaluate(absolute)*dragMach.Evaluate(mach)*coefficient;
            float liftScalar=Mathf.Sign(dot)*lift.Evaluate(absolute)*liftMach.Evaluate(mach)*coefficient;
            // KSP's calibrated lifting-surface multipliers already produce kN,
            // despite Q being passed in pascals. Part/FlightIntegrator forward
            // these vectors unchanged to rigidbodies whose masses are in tonnes.
            // MJ uses those same force/mass units: do not divide by 1000 again.
            var force=new SurfaceForces {
                Drag=internalDrag?(Vector3d)(-direction*(float)(pressureKpa*1000*(dragScalar*dragMultiplier))):Vector3d.zero,
                Lift=(Vector3d)(-axis*(float)(pressureKpa*1000*(liftMultiplier*liftScalar)))
            };
            if(perpendicular)force.Lift=Vector3d.Exclude(direction,force.Lift);
            return force;
        }
    }

    public static class AirbrakeForecast
    {
        [ThreadStatic] static double altitude;
        static bool installed;
        static Func<Vessel,bool> registered;
        static long snapshots,executedSnapshots;
        public static long ExecutedModels=>Interlocked.Read(ref executedSnapshots);
        static string lastVessel="";
        public static string Evidence=>"snapshots="+Interlocked.Read(ref snapshots)+" workerModels="+Interlocked.Read(ref executedSnapshots)+" vessel="+lastVessel;
        sealed class ForecastPart : SimulatedPart
        {
            static readonly ConcurrentBag<ForecastPart> pool=new ConcurrentBag<ForecastPart>();
            FrozenAirbrake surface;
            double pressure,dragScale;
            FloatCurve pseudoReynolds;
            float mach;
            bool hasDragSample;
            bool executed;
            public static ForecastPart Capture(Part part,ReentrySimulation.SimCurves curves)
            {
                if(!pool.TryTake(out var model))model=new ForecastPart();
                model.cubes.BodyLiftCurve=new PhysicsGlobals.LiftingSurfaceCurve();
                model.Init(part,curves);
                model.surface=new FrozenAirbrake(part.FindModuleImplementing<ModuleAeroSurface>());
                model.pseudoReynolds=curves.DragCurvePseudoReynolds;
                model.dragScale=PhysicsGlobals.DragCubeMultiplier*PhysicsGlobals.DragMultiplier;
                if(model.dragScale<=0)throw new InvalidOperationException("Unsupported native drag multiplier");
                model.hasDragSample=false;
                model.executed=false;lastVessel=part.vessel.id.ToString("N");Interlocked.Increment(ref snapshots);
                return model;
            }
            public override Vector3d Drag(Vector3d velocity,double factor,float sampleMach)
            {
                if(!executed) { executed=true;Interlocked.Increment(ref executedSnapshots); }
                pressure=factor/dragScale;mach=sampleMach;hasDragSample=true;
                // TotalAccel multiplies all DragForce by the drag-cube
                // pseudo-Reynolds curve. Native ModuleLiftingSurface does not.
                // Cancel that downstream multiplier for this internal force only.
                double reynolds=velocity.magnitude>0?2000*pressure/velocity.magnitude:0;
                double correction=pseudoReynolds.Evaluate((float)reynolds);
                if(!FlightRules.Finite(correction)||correction<=0)throw new InvalidOperationException("Unsupported native pseudo-Reynolds correction");
                return base.Drag(velocity,factor,sampleMach)+surface.Evaluate(velocity,pressure,mach,altitude).Drag/correction;
            }
            public override Vector3d Lift(Vector3d velocity,double factor)
            {
                // Installed ReentrySimulation.TotalAccel evaluates DragForce
                // immediately before LiftForce for the same pressure/Mach sample.
                if(!hasDragSample)throw new InvalidOperationException("Airbrake forecast requires the validated native drag/lift update order");
                return base.Lift(velocity,factor)+surface.Evaluate(velocity,pressure,mach,altitude).Lift;
            }
            public override void Release()
            {
                foreach(var cube in cubes.Cubes)DragCubePool.Instance.Release(cube);
                cubes.Cubes.Clear();surface=null;pseudoReynolds=null;hasDragSample=false;pool.Add(this);
                // Never insert this derived type into MJ's base-part pool.
            }
        }
        public static void Install(Func<Vessel,bool> ownsLanding)
        {
            if(installed)return;
            var borrow=AccessTools.Method(typeof(SimulatedPart),nameof(SimulatedPart.Borrow),new[]{typeof(Part),typeof(ReentrySimulation.SimCurves)});
            var drag=AccessTools.Method(typeof(ReentrySimulation),"DragForce");
            var lift=AccessTools.Method(typeof(ReentrySimulation),"LiftForce");
            var radius=AccessTools.Field(typeof(ReentrySimulation),"bodyRadius");
            if(borrow==null||borrow.ReturnType!=typeof(SimulatedPart)||drag==null||lift==null||radius?.FieldType!=typeof(double))
                throw new InvalidOperationException("Installed native airbrake prediction API is incompatible");
            registered=ownsLanding;
            var harmony=new Harmony("local.railgun.native-airbrake-forecast");
            harmony.Patch(borrow,prefix:new HarmonyMethod(typeof(AirbrakeForecast),nameof(BorrowPrefix)));
            var prefix=new HarmonyMethod(typeof(AirbrakeForecast),nameof(ForcePrefix));
            harmony.Patch(drag,prefix:prefix);harmony.Patch(lift,prefix:prefix);
            installed=true;
        }
        static bool BorrowPrefix(Part p,ReentrySimulation.SimCurves simCurve,ref SimulatedPart __result)
        {
            if(p.partInfo.name!="airbrake1"||registered==null||!registered(p.vessel))return true;
            __result=ForecastPart.Capture(p,simCurve);return false;
        }
        static void ForcePrefix(Vector3d pos,double ___bodyRadius) { altitude=pos.magnitude-___bodyRadius; }
    }
}
