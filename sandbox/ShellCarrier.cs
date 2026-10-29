using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace RailgunSandbox
{
    // Authored loader art. The real shell remains an independent native part.
    // Read its actual saved fairing profile to place grips on the cylindrical body.
    public sealed class ShellCarrier : IDisposable
    {
        readonly Transform depot, rig;
        readonly Material steel, dark, yellow, chrome, cyan;
        readonly List<Transform> geometry = new List<Transform>();
        readonly Transform[] halves = new Transform[4], wires = new Transform[2];
        readonly Renderer[] indicators = new Renderer[4];
        readonly LoaderAssets assets;
        uint shellId;
        float radius, centerHeight, bandSpan;
        public Vector3 HoistLocal { get; private set; }=new Vector3(-36,0,-10);
        public bool Built => shellId != 0;
        public bool Positioned { get; private set; }
        public ShellCarrier(Transform parent, Material steel, Material dark, Material yellow, Material chrome, Material cyan,LoaderAssets assets)
        {
            depot = parent; this.steel=steel;this.dark=dark;this.yellow=yellow;this.chrome=chrome;this.cyan=cyan;this.assets=assets;
            rig = Node("Shell transport carrier / twin trunnion bands", depot);
            rig.localPosition=new Vector3(-36,40,-10);
            for (int i=0;i<2;i++) wires[i]=Box("Load-bearing hoist cable "+i,depot,Vector3.zero,new Vector3(.11f,1,.11f),chrome);
            rig.gameObject.SetActive(false); foreach(var wire in wires) wire.gameObject.SetActive(false);
        }
        static Transform Node(string name,Transform parent) {var t=new GameObject(name).transform;t.SetParent(parent,false);return t;}
        static Transform Box(string name,Transform parent,Vector3 p,Vector3 size,Material material) {
            var t=GameObject.CreatePrimitive(PrimitiveType.Cube).transform;t.name=name;t.SetParent(parent,false);t.localPosition=p;t.localScale=size;
            t.GetComponent<Renderer>().sharedMaterial=material;UnityEngine.Object.Destroy(t.GetComponent<Collider>());return t;
        }
        static Transform Beam(string name,Transform parent,Vector3 a,Vector3 b,float thickness,Material material) {
            var t=Box(name,parent,(a+b)/2,new Vector3(thickness,(b-a).magnitude,thickness),material);t.localRotation=Quaternion.FromToRotation(Vector3.up,b-a);return t;
        }
        void Build(Part shell)
        {
            foreach(var t in geometry) if(t!=null) UnityEngine.Object.Destroy(t.gameObject);geometry.Clear();
            var saved=new ConfigNode("MODULE");shell.FindModuleImplementing<ModuleProceduralFairing>().Save(saved);
            var sections=saved.GetNodes("XSECTION").Select(n=>new Vector2(float.Parse(n.GetValue("h"),CultureInfo.InvariantCulture),float.Parse(n.GetValue("r"),CultureInfo.InvariantCulture))).ToArray();
            if(sections.Length<3) throw new InvalidOperationException("Shell loader needs the actual closed fairing profile.");
            radius=sections.Max(s=>s.y)+.14f;
            var cylinder=sections.Where(s=>Math.Abs(s.y-(radius-.14f))<.02f).OrderBy(s=>s.x).ToArray();
            if(cylinder.Length<2 || cylinder.Last().x-cylinder.First().x<5) throw new InvalidOperationException("Shell has no supported cylindrical grip span.");
            centerHeight=(cylinder.First().x+cylinder.Last().x)/2+.17f;
            bandSpan=(cylinder.Last().x-cylinder.First().x)*.8f;
            var frame=Node("Profile-fitted shell lifting frame",rig);geometry.Add(frame);
            Box("Dorsal structural spine",frame,new Vector3(0,0,-radius-1.3f),new Vector3(1,bandSpan+4,1.2f),dark);
            for(int side=-1;side<=1;side+=2) Box("Twin lifting spine",frame,new Vector3(side*.9f,0,-radius-1.3f),new Vector3(.32f,bandSpan+4,.65f),steel);
            for(int tier=0;tier<2;tier++) {
                float h=(tier==0?-.5f:.5f)*bandSpan;
                Box("Transport crosshead",frame,new Vector3(0,h,-radius-1.3f),new Vector3(2*radius+5,.7f,1.2f),yellow);
                for(int side=-1;side<=1;side+=2) {
                    int i=tier*2+(side==1?1:0);
                    halves[i]=Node("Transport grip half "+i,frame);halves[i].localPosition=new Vector3(side*3,h,0);
                    assets.TransportGrip(radius,side,halves[i]);
                    indicators[i]=Box("Grip lock indicator",halves[i],new Vector3(side*(radius+.4f),.85f,-radius-1.25f),new Vector3(.3f,.18f,.35f),dark).GetComponent<Renderer>();
                }
            }
            Box("Upper lift spreader",frame,new Vector3(0,bandSpan/2+1,-radius-1.3f),new Vector3(2*radius+2.8f,.8f,1.3f),steel);
            shellId=shell.flightID;rig.gameObject.SetActive(true);
        }
        void Grips(double grip) {for(int i=0;i<4;i++)if(halves[i]!=null){halves[i].localPosition=new Vector3((i%2==0?-1:1)*(float)(3*(1-grip)),(i<2?-.5f:.5f)*bandSpan,0);indicators[i].sharedMaterial=grip>.98?cyan:dark;}}
        void Wires(float gantryHeight)
        {
            Vector3 sum=Vector3.zero;
            for(int i=0;i<2;i++) {
                Vector3 bottom=rig.TransformPoint(new Vector3((i==0?-1:1)*(radius+.8f),bandSpan/2+1,-radius-1.3f));
                Vector3 local=depot.InverseTransformPoint(bottom);sum+=local;
                Vector3 top=depot.TransformPoint(new Vector3(local.x,gantryHeight-1,local.z));
                var wire=wires[i];wire.gameObject.SetActive(true);wire.position=(top+bottom)/2;wire.rotation=Quaternion.FromToRotation(Vector3.up,top-bottom);wire.localScale=new Vector3(.11f,(top-bottom).magnitude,.11f);
            }
            HoistLocal=sum/2;
        }
        public void Update(Vessel pod,ShellLoadingRoute route,LoadingPose pose,float gantryHeight)
        {
            var shell=pod.parts.FirstOrDefault(p=>p.partInfo.name==LaunchShell.PartName);
            if(shell==null) throw new InvalidOperationException("Cannot animate shell loading without the actual casing.");
            if(shellId!=shell.flightID) Build(shell);
            var axis=shell.transform.up;
            // Hoist cables stay on the upper side of the tilted casing. Vertical
            // cables from this side move farther away from its cylinder axis.
            var dorsal=Vector3.ProjectOnPlane(depot.up,axis);
            if(dorsal.sqrMagnitude<.0001f) dorsal=Vector3.ProjectOnPlane(-depot.right,axis);
            dorsal.Normalize();
            float retreat=(float)pose.CarrierRetraction;
            Vector3 target=shell.transform.TransformPoint(new Vector3(0,centerHeight,0))+dorsal*(retreat*12)+depot.up*(retreat*6);
            Quaternion rotation=Quaternion.LookRotation(-dorsal,axis);
            if(route!=null && route.Stage==ShellTransferStage.Gripping) {
                rig.position=Vector3.MoveTowards(rig.position,target,Math.Max(0,Time.deltaTime)*8);
                rig.rotation=Quaternion.RotateTowards(rig.rotation,rotation,Math.Max(0,Time.deltaTime)*35);
            } else {rig.position=target;rig.rotation=rotation;}
            Positioned=Vector3.Distance(rig.position,target)<.1f && Quaternion.Angle(rig.rotation,rotation)<1;
            Grips(route!=null && !route.Seated?route.Grip:pose.CarrierGrip);
            Wires(gantryHeight);
        }
        public void Park(float gantryHeight,float dt)
        {
            if(!Built)return;
            rig.localPosition=Vector3.MoveTowards(rig.localPosition,new Vector3(-36,40,-10),Math.Max(0,dt)*5);
            rig.localRotation=Quaternion.RotateTowards(rig.localRotation,Quaternion.identity,Math.Max(0,dt)*25);
            Grips(0);Wires(gantryHeight);
        }
        public void Dispose()
        {
            // Scene changes can dispose the depot before a shell has populated
            // its carrier wires, or after Unity has already removed a child.
            foreach(var wire in wires)if(wire!=null)UnityEngine.Object.Destroy(wire.gameObject);
            if(rig!=null)UnityEngine.Object.Destroy(rig.gameObject);
        }
    }
}
