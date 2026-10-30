using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MuMech;

namespace RailgunSandbox
{
    public sealed class AirbrakeRecovery
    {
        readonly Dictionary<uint,double> temperatures=new Dictionary<uint,double>();
        readonly Dictionary<uint,double> bodyTemperatures=new Dictionary<uint,double>();
        double previousUt=-1,nextLog;
        double rangeAngle=double.NaN,nextRangeSample;
        bool previousCommand;
        public string Evidence { get; private set; }="waiting";
        public bool ReadyForForecast { get; private set; }
        public void Update(PodRecord pod,Vessel vessel,MechJebCore core,double now,string folder)
        {
            var panels=vessel.parts.Where(p=>pod.PartIds.Contains(p.flightID)&&p.partInfo.name=="airbrake1")
                .SelectMany(p=>p.FindModulesImplementing<ModuleAeroSurface>()).ToArray();
            if(panels.Length==0)return; // Older native assemblies are not silently rebuilt.
            double maximum=panels.Min(p=>(double)Math.Abs(p.ctrlSurfaceRange));
            if(!FlightRules.Finite(rangeAngle))rangeAngle=maximum;
            double dt=now-previousUt,predicted=0;
            foreach(var panel in panels) {
                double limit=Math.Min(panel.uncasedTemp,panel.part.maxTemp);
                double skin=panel.part.skinTemperature;
                double rate=dt>0&&temperatures.TryGetValue(panel.part.flightID,out var before)?Math.Max(0,(skin-before)/dt):double.PositiveInfinity;
                // Native stock panel opens/folds 70 degrees at 20 degrees/sec.
                // Include its measured actuator time and one guidance interval.
                double response=Math.Abs(panel.aeroDeployAngle)/Math.Max(.01,panel.actuatorSpeed)+.5;
                predicted=Math.Max(predicted,(skin+rate*response)/Math.Max(1,limit));
                temperatures[panel.part.flightID]=skin;
            }
            double bodyHeat=0;
            foreach(var part in vessel.parts.Where(p=>pod.PartIds.Contains(p.flightID))) {
                double skin=part.skinTemperature;
                double rate=dt>0&&bodyTemperatures.TryGetValue(part.flightID,out var before)?Math.Max(0,(skin-before)/dt):0;
                double response=maximum/Math.Max(.01,panels.Min(p=>p.actuatorSpeed))+.5;
                bodyHeat=Math.Max(bodyHeat,Math.Max(part.temperature/Math.Max(1,part.maxTemp),(skin+rate*response)/Math.Max(1,part.maxTemp)));
                bodyTemperatures[part.flightID]=skin;
            }
            previousUt=now;
            bool deploy=panels.Length==4&&AirbrakePolicy.Deploy(pod.Phase,previousCommand,vessel.altitude,
                vessel.verticalSpeed,vessel.srfSpeed,vessel.dynamicPressurekPa,predicted);
            double miss=double.NaN;string rangeState="holding native angle";
            if(pod.Phase!=FlightPhase.Landing||vessel.altitude>vessel.mainBody.atmosphereDepth)rangeAngle=maximum;
            if(deploy&&bodyHeat>=.70) { rangeAngle=maximum;nextRangeSample=now+2;rangeState="body thermal protection"; }
            else if(Environment.GetCommandLineArgs().Contains("-railgun-range-test")&&deploy&&vessel.altitude>5000&&vessel.altitude<vessel.mainBody.atmosphereDepth&&vessel.horizontalSrfSpeed>100&&core.landing.PredictionReady&&AirbrakeForecast.ExecutedModels>0) {
                double age=now-core.landing.Prediction.input_UT;
                if(age>=0&&age<=6) {
                    Vector3d up=(vessel.GetWorldPos3D()-vessel.mainBody.position).normalized;
                    Vector3d along=Vector3d.Exclude(up,vessel.srf_velocity).normalized;
                    miss=Vector3d.Dot(core.target.GetPositionTargetPosition()-core.landing.LandingSite,along);
                    if(now>=nextRangeSample) {
                        rangeAngle=RangeBrakePolicy.Next(rangeAngle,maximum,miss,bodyHeat);nextRangeSample=now+2;
                    }
                    rangeState="native predicted range control";
                } else rangeState="holding for a fresh native prediction";
            }
            if(core!=null&&RecoveryGuidance.PoweredApproachActive(core.landing)&&bodyHeat<.70) {rangeAngle=0;rangeState="powered intercept: native flaps folded";}
            // ModuleAeroSurface consumes this native field, animates the stock
            // flaps and applies its normal aerodynamic and thermal behavior.
            // aeroDeployAngle is the game's normal deployment-angle slider;
            // mass, force curves, actuator speed and heat limits are unchanged.
            foreach(var panel in panels) { panel.aeroDeployAngle=(float)rangeAngle;panel.deploy=deploy&&rangeAngle>.01; }
            Evidence=FormattableString.Invariant($"angle={rangeAngle:F2} deg / alongMiss={miss:F1} m / bodyHeatForecast={bodyHeat:F3} / {rangeState}");
            ReadyForForecast=dt>0&&FlightRules.Finite(predicted)&&panels.All(p=>Math.Abs(FrozenAirbrake.NativeDeflection(p)-(deploy?(p.deployInvert?-1:1)*p.aeroDeployAngle:0))<.1);
            if(deploy!=previousCommand||now>=nextLog) {
                string path=Path.Combine(folder,"AirbrakeEvidence-v2.tsv");
                if(!File.Exists(path))File.WriteAllText(path,"ut\tpod\tphase\tdeploy\tpanels\taltitude\tvz\tspeed\tqKpa\tpredictedHeatRatio\tmaxSkinK\tangle\talongMiss\tbodyHeatForecast\trangeState\n");
                File.AppendAllText(path,FormattableString.Invariant($"{now:R}\t{pod.VesselId}\t{pod.Phase}\t{deploy}\t{panels.Length}\t{vessel.altitude:R}\t{vessel.verticalSpeed:R}\t{vessel.srfSpeed:R}\t{vessel.dynamicPressurekPa:R}\t{predicted:R}\t{panels.Max(p=>p.part.skinTemperature):R}\t{rangeAngle:R}\t{miss:R}\t{bodyHeat:R}\t{rangeState}\n"));
                nextLog=now+2;
            }
            previousCommand=deploy;
        }
    }
}
