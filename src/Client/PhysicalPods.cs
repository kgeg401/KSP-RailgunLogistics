using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using LmpClient.Systems.Lock;
using LmpClient.Systems.SettingsSys;

namespace RailgunLogistics
{
    public static class PhysicalPods
    {
        public const string PartName="kgeg4RailgunCargoPod";
        public static string State(ModuleDockingNode port)=>port?.fsm?.CurrentState?.name??port?.state??"";
        public static List<ModuleDockingNode> Ports(Vessel vessel)=>vessel==null?new List<ModuleDockingNode>():
            vessel.parts.SelectMany(p=>p.FindModulesImplementing<ModuleDockingNode>())
                .Where(p=>p.part.FindModuleImplementing<RailgunCargoPod>()==null && p.nodeTransform!=null &&
                    (p.nodeTypes?.Contains("size1")??p.nodeType=="size1") && !p.gendered && !p.snapRotation && State(p)=="Ready").ToList();
        public static ModuleDockingNode Target(Vessel vessel,uint id)=>vessel?.parts.FirstOrDefault(p=>p.flightID==id)?.FindModuleImplementing<ModuleDockingNode>();
        public static Vessel Find(string id)
        {
            foreach(var v in FlightGlobals.Vessels)
            {
                if(v==null || v.state==Vessel.State.DEAD)continue;
                if(v.loaded)
                {
                    if(v.parts.Any(p=>p.FindModuleImplementing<RailgunCargoPod>()?.orderId==id))return v;
                }
                else if(v.protoVessel?.protoPartSnapshots.Any(p=>p.modules.Any(m=>m.moduleName=="RailgunCargoPod" && m.moduleValues.GetValue("orderId")==id))==true)return v;
            }
            return null;
        }
        public static string Pack(IEnumerable<Cargo> cargo)=>string.Join(";",cargo.Select(c=>c.Resource+":"+c.Units.ToString("R",CultureInfo.InvariantCulture)).ToArray());
        public static List<Cargo> Unpack(string value)=>(value??"").Split(new[]{';'},StringSplitOptions.RemoveEmptyEntries).Select(s=>s.Split(':'))
            .Select(s=>new Cargo {Resource=s[0],Units=double.Parse(s[1],CultureInfo.InvariantCulture)}).ToList();
        public static void CheckOrbit(Vessel v)
        {
            if(v==null || (v.situation!=Vessel.Situations.ORBITING && v.situation!=Vessel.Situations.ESCAPING) ||
                v.altitude<=v.mainBody.atmosphereDepth+2000)
                throw new InvalidOperationException("Docking deliveries require a ship in space, above the atmosphere.");
            if(TimeWarp.CurrentRate!=1 || v.packed)throw new InvalidOperationException("Return to 1x time before the pod approaches.");
        }
        public static bool ClearCorridor(ModuleDockingNode target,Vector3 start,Part pod=null)
        {
            Vector3 end=target.nodeTransform.position+target.nodeTransform.forward*1.5f;
            Vector3 delta=end-start;
            // Inside the final clearance distance, never cast backwards through the docking face.
            if(Vector3.Dot(start-end,target.nodeTransform.forward)<=0)return true;
            foreach(var hit in Physics.SphereCastAll(start,.65f,delta.normalized,delta.magnitude,~0,QueryTriggerInteraction.Ignore))
            {
                var part=hit.collider.GetComponentInParent<Part>();
                if(part!=null && part!=pod && part!=target.part)return false;
            }
            return true;
        }
        public static IEnumerator Spawn(Order order,Vessel receiver,Action<Vessel> ready,Action<string> failed)
        {
            string folder=HighLogic.SaveFolder;bool online=Logistics.Connected;
            Vessel pod=null;ModuleDockingNode port=null;
            try
            {
                CheckOrbit(receiver);
                port=Target(receiver,order.Port);
                if(port==null || !Ports(receiver).Contains(port))throw new InvalidOperationException("The selected Clamp-O-Tron port is unavailable. Clear the port to receive this pod.");
                Vector3 start=port.nodeTransform.position+port.nodeTransform.forward*60;
                if(!ClearCorridor(port,start))throw new InvalidOperationException("Docking corridor obstructed. Clear the approach to the selected port.");
                var part=ProtoVessel.CreatePartNode(PartName,ShipConstruction.GetUniqueFlightID(HighLogic.CurrentGame.flightState));
                // CreatePartNode emits no MODULE snapshots. In particular, ModuleDockingNode.OnLoad
                // initializes nodeTypes; without it a new port looks Ready but cannot acquire anything.
                foreach(PartModule prefabModule in PartLoader.getPartInfoByName(PartName).partPrefab.Modules)
                {
                    var snapshot=new ConfigNode("MODULE");prefabModule.Save(snapshot);part.AddNode(snapshot);
                }
                part.RemoveNodes("RESOURCE");
                foreach(var cargo in order.Cargo)
                {
                    var resource=part.AddNode("RESOURCE");resource.AddValue("name",cargo.Resource);
                    resource.AddValue("amount",cargo.Units);resource.AddValue("maxAmount",cargo.Units);
                    resource.AddValue("flowState",false);resource.AddValue("isTweakable",false);
                }
                var module=part.GetNodes("MODULE").First(n=>n.GetValue("name")=="RailgunCargoPod");
                module.SetValue("orderId",order.Id,true);module.SetValue("owner",order.Owner,true);
                module.SetValue("receiverAnchor",order.Anchor,true);module.SetValue("portId",order.Port,true);
                module.SetValue("cargoSpec",Pack(order.Cargo),true);module.SetValue("unloaded",false,true);
                module.SetValue("inserted",false,true);
                double ut=Planetarium.GetUniversalTime();
                var orbit=new Orbit();
                Vector3d offset=(Vector3d)start-receiver.GetWorldPos3D();
                orbit.UpdateFromStateVectors(receiver.orbit.getRelativePositionAtUT(ut)+offset.xzy,
                    receiver.orbit.getOrbitalVelocityAtUT(ut),receiver.mainBody,ut);
                var node=ProtoVessel.CreateVesselNode("KRL Cargo Pod "+order.Id.Substring(0,6),VesselType.Probe,orbit,0,new[]{part});
                node.SetValue("pid",order.PodId,true);
                var proto=HighLogic.CurrentGame.AddVessel(node);
                pod=proto.vesselRef;
                if(pod==null)throw new InvalidOperationException("KSP did not create the cargo vessel.");
                pod.isPersistent=true;pod.Load();
                Debug.Log("[RailgunLogistics] Physical pod created "+order.Id+" vessel "+pod.id);
            }
            catch(Exception ex){failed(ex.ToString());yield break;}
            float deadline=Time.realtimeSinceStartup+30;
            while(pod!=null && (!pod.loaded || pod.packed || pod.rootPart?.rb==null) && Time.realtimeSinceStartup<deadline)
            {
                if(HighLogic.SaveFolder!=folder || Logistics.Connected!=online){failed("Delivery paused by a session change.");yield break;}
                yield return null;
            }
            try
            {
                if(HighLogic.SaveFolder!=folder || Logistics.Connected!=online)throw new InvalidOperationException("Delivery paused by a session change.");
                if(pod==null || !pod.loaded || pod.packed || pod.rootPart?.rb==null)throw new InvalidOperationException("Cargo pod is waiting for KSP physics. It will not be spawned twice.");
                port=Target(receiver,order.Port);
                if(port==null)throw new InvalidOperationException("Receiver port disappeared during arrival.");
                var own=pod.rootPart.FindModuleImplementing<ModuleDockingNode>();
                var from=Quaternion.LookRotation(own.nodeTransform.forward,own.nodeTransform.up);
                var to=Quaternion.LookRotation(-port.nodeTransform.forward,port.nodeTransform.up);
                pod.SetRotation(to*Quaternion.Inverse(from)*pod.transform.rotation);
                var offset=port.nodeTransform.position+port.nodeTransform.forward*60-own.nodeTransform.position;
                pod.SetPosition(pod.GetWorldPos3D()+(Vector3d)offset);
                // KSP's method writes Rigidbody.velocity directly: use the current floating-origin frame.
                pod.SetWorldVelocity(port.part.rb.GetPointVelocity(port.nodeTransform.position));
                pod.rootPart.rb.angularVelocity=receiver.rootPart.rb.angularVelocity;
                var cargo=pod.rootPart.FindModuleImplementing<RailgunCargoPod>();cargo.inserted=true;
                cargo.deliveryStatus="Approaching port";
                ready(pod);
            }
            catch(Exception ex){failed(ex.ToString());}
        }
    }

    // A stock part with real resources, mass, colliders and a stock ModuleDockingNode.
    public sealed class RailgunCargoPod : PartModule
    {
        [KSPField(isPersistant=true)]public string orderId="",owner="",cargoSpec="";
        [KSPField(isPersistant=true)]public uint receiverAnchor,portId;
        [KSPField(isPersistant=true)]public bool unloaded,inserted;
        [KSPField(guiActive=true,guiName="Delivery")]public string deliveryStatus="Cargo aboard";
        GameObject body;
        readonly List<Material> materials=new List<Material>();
        float nextLog;
        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            if(body!=null)return;
            body=new GameObject("KRL physical cargo bus");body.transform.SetParent(part.transform,false);
            Shape("Cargo tank",PrimitiveType.Cylinder,new Vector3(0,-.95f,0),new Vector3(1.1f,.9f,1.1f),new Color(.62f,.48f,.18f),true);
            Shape("Aft avionics",PrimitiveType.Cylinder,new Vector3(0,-1.9f,0),new Vector3(.8f,.15f,.8f),new Color(.12f,.2f,.25f),true);
            for(int i=0;i<4;i++)
            {
                float a=i*Mathf.PI/2;
                Shape("Guidance light",PrimitiveType.Cube,new Vector3(Mathf.Cos(a)*.55f,-.7f,Mathf.Sin(a)*.55f),new Vector3(.12f,1.1f,.12f),new Color(.1f,.8f,1),false);
            }
        }
        void Shape(string name,PrimitiveType type,Vector3 position,Vector3 scale,Color color,bool solid)
        {
            var obj=GameObject.CreatePrimitive(type);obj.name=name;obj.transform.SetParent(body.transform,false);
            obj.transform.localPosition=position;obj.transform.localScale=scale;
            if(!solid)Destroy(obj.GetComponent<Collider>());
            var material=new Material(Shader.Find("KSP/Diffuse")??Shader.Find("Standard"));material.color=color;
            obj.GetComponent<Renderer>().material=material;materials.Add(material);
        }
        void OnDestroy(){foreach(var mat in materials)Destroy(mat);if(body!=null)Destroy(body);}
        public bool DockedToReceiver(out Vessel receiver)
        {
            receiver=null;
            if(part?.vessel==null || !part.vessel.loaded || part.vessel.packed)return false;
            var own=part.FindModuleImplementing<ModuleDockingNode>();
            var other=own?.otherNode;
            if(other==null || other.part.flightID!=portId || own.dockedPartUId!=portId ||
                !PhysicalPods.State(own).StartsWith("Docked") || !PhysicalPods.State(other).StartsWith("Docked") ||
                part.vessel!=other.part.vessel || !part.vessel.parts.Any(p=>p.flightID==receiverAnchor))return false;
            receiver=part.vessel;return true;
        }
        void FixedUpdate()
        {
            if(!HighLogic.LoadedSceneIsFlight || !inserted || unloaded || string.IsNullOrEmpty(orderId) ||
                part?.vessel==null || !part.vessel.loaded || part.vessel.packed || part.rb==null)return;
            if(DockedToReceiver(out _)){deliveryStatus="Docked — unloading cargo";return;}
            // Never steer the station after KSP has coupled the two vessels, even before its FSM settles.
            if(part.vessel.parts.Any(p=>p.flightID==receiverAnchor))return;
            var receiver=FlightGlobals.ActiveVessel;
            if(receiver==null || !receiver.parts.Any(p=>p.flightID==receiverAnchor) || receiver.packed || !receiver.loaded)return;
            if(Logistics.Connected && (owner!=SettingsSystem.CurrentSettings.PlayerName ||
                !LockSystem.LockQuery.ControlLockBelongsToPlayer(receiver.id,owner) ||
                !LockSystem.LockQuery.UpdateLockBelongsToPlayer(part.vessel.id,owner)))return;
            var target=PhysicalPods.Target(receiver,portId);var own=part.FindModuleImplementing<ModuleDockingNode>();
            if(target?.nodeTransform==null || own?.nodeTransform==null || target.part.rb==null)return;
            if(TimeWarp.CurrentRate!=1){deliveryStatus="Holding — return to 1x";return;}
            Vector3 axis=target.nodeTransform.forward;
            Vector3 delta=own.nodeTransform.position-target.nodeTransform.position;
            float distance=delta.magnitude,along=Vector3.Dot(delta,axis);
            Vector3 velocity=target.part.rb.GetPointVelocity(target.nodeTransform.position);
            if(PhysicalPods.State(own).StartsWith("Acquire"))
            {deliveryStatus="Stock port magnetic capture";return;}
            bool clear=PhysicalPods.State(target)=="Ready" && PhysicalPods.ClearCorridor(target,own.nodeTransform.position,part);
            if(!clear || along<-.5f)
            {deliveryStatus="Holding — port blocked or unavailable";}
            else
            {
                Vector3 lateral=delta-axis*along;
                float closing=distance<2?.10f:Mathf.Clamp((along-.02f)*.18f,.10f,1.2f);
                if(lateral.magnitude>1 || receiver.angularVelocity.magnitude>.06f)closing=0;
                velocity-=axis*closing+Vector3.ClampMagnitude(lateral*.5f,.6f);
                deliveryStatus="Approaching: "+distance.ToString("F1")+" m";
            }
            // Assisted thrusters apply bounded acceleration. They do not teleport, Couple(), or set docking states.
            part.rb.AddForce(Vector3.ClampMagnitude((velocity-part.rb.velocity)*1.8f,.3f),ForceMode.Acceleration);
            var desiredFrame=Quaternion.LookRotation(-axis,target.nodeTransform.up);
            var currentFrame=Quaternion.LookRotation(own.nodeTransform.forward,own.nodeTransform.up);
            var error=desiredFrame*Quaternion.Inverse(currentFrame);
            error.ToAngleAxis(out float angle,out Vector3 rotationAxis);
            if(angle>180)angle-=360;
            Vector3 angular=target.part.rb.angularVelocity+rotationAxis*Mathf.Clamp(angle*Mathf.Deg2Rad*1.5f,-.25f,.25f);
            if(!float.IsNaN(angular.x))part.rb.AddTorque(Vector3.ClampMagnitude((angular-part.rb.angularVelocity)*3,.6f),ForceMode.Acceleration);
            if(Time.realtimeSinceStartup>=nextLog)
            {
                nextLog=Time.realtimeSinceStartup+5;
                Debug.Log("[RailgunLogistics] Pod "+orderId+" "+deliveryStatus+" / "+PhysicalPods.State(own)+" / target "+PhysicalPods.State(target)+
                    " / types "+string.Join(",",own.nodeTypes)+" / distance "+distance.ToString("F3")+" / along "+along.ToString("F3")+" / angle "+angle.ToString("F2"));
            }
        }
    }
}
