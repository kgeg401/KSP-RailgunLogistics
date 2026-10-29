using System;
using System.Collections.Generic;
using UnityEngine;

namespace RailgunSandbox
{
    // Cosmetic machinery follows the real native vessel. It never reparents a
    // part, changes a collider, assigns vessel position, or supplies launch force.
    public sealed class LoadingMechanism : IDisposable
    {
        readonly Transform root, trolley, hoistHead, hanger, railMount;
        readonly Transform[] jaws = new Transform[4], bolts = new Transform[4];
        readonly Transform cable;
        readonly Transform[] supports = new Transform[2];
        readonly Transform[] supportFeet = new Transform[2];
        readonly Transform[] runwaySides=new Transform[2],bridgeWheels=new Transform[4],slideGuides=new Transform[2];
        readonly Transform crossCarriage;
        float runwayHalfDepth;
        readonly List<Material> materials = new List<Material>();
        readonly Material steel, dark, yellow, chrome, amber, cyan;
        readonly Renderer[] indicators = new Renderer[4];
        readonly Renderer[] chargeIndicators = new Renderer[8];
        readonly AudioSource motor, impact;
        readonly AudioClip motorClip, lockClip, releaseClip;
        LoadingStage lastStage = LoadingStage.Aligning;
        bool operating;
        double displayedElapsed;
        readonly float shellRadius;
        readonly float clearBore;
        readonly float gantryHeight;
        readonly ShellCarrier shellCarrier;
        readonly Transform railGuide;
        readonly LoaderAssets assets;
        public bool CarrierPositioned => shellCarrier?.Positioned ?? true;
        public IEnumerable<Mesh> OwnedMeshes=>assets.OwnedMeshes;
        public Transform CradleFrame=>railMount;
        public Transform RootFrame=>root;
        public LoadingMechanism(Transform parent, float enclosingRadius = 0, Transform guide = null)
        {
            railGuide = guide;
            shellRadius = enclosingRadius;
            clearBore=shellRadius>0?RailAssets.ShellBore:RailAssets.BareBore;
            gantryHeight = shellRadius > 0 ? 84 : 64;
            root = new GameObject("KRL articulated loading gantry").transform; root.SetParent(parent, false);
            steel = Mat(new Color(.32f,.38f,.43f),false,.45f); dark = Mat(new Color(.055f,.075f,.09f),false,.18f);
            yellow = Mat(new Color(.93f,.57f,.075f),false,.3f); chrome = Mat(new Color(.72f,.8f,.84f),false,.8f);
            amber = Mat(new Color(1,.33f,.035f),true); cyan = Mat(new Color(.08f,.85f,1),true);
            assets=new LoaderAssets(steel,dark,yellow,chrome,amber,cyan,clearBore);
            runwayHalfDepth=shellRadius>0?9:7;
            for(int depth=-1;depth<=1;depth+=2) {
                var runway=runwaySides[depth==1?1:0]=Node("Gantry runway side",root);runway.localPosition=new Vector3(0,0,depth*runwayHalfDepth);
                foreach(int side in new[]{-1,1}) {
                    Box("Gantry tower",runway,new Vector3(side*46,gantryHeight/2,0),new Vector3(1.4f,gantryHeight,1.4f),steel);
                    Beam("Tower diagonal",runway,new Vector3(side*46,3,0),new Vector3(side*39,17,0),.5f,dark);
                }
                Box("Bridge wheel rail",runway,new Vector3(0,gantryHeight,0),new Vector3(94,1,1),steel);
                Box("Safety rail stripe",runway,new Vector3(0,gantryHeight+.65f,0),new Vector3(92,.12f,1.05f),yellow);
                for(int n=0;n<9;n++) Beam("Bridge truss",runway,new Vector3(-44+n*10,gantryHeight-2,0),new Vector3(-39+n*10,gantryHeight+2,0),.22f,chrome);
            }
            trolley=Node("Traveling hoist trolley",root);
            crossCarriage=Box("Cross carriage",trolley,Vector3.zero,new Vector3(shellRadius>0?12:7,1.5f,runwayHalfDepth*2+2),dark);
            hoistHead=Node("Fore-aft hoist slide",trolley);
            Box("Hoist slide carriage",hoistHead,new Vector3(0,-.7f,0),new Vector3(6,.5f,3),steel);
            for(int side=-1;side<=1;side+=2) {
                slideGuides[side==1?1:0]=Box("Hoist slide guide",trolley,new Vector3(side*2.6f,-.5f,0),new Vector3(.3f,.5f,runwayHalfDepth*2+6),chrome);
                Box("Winch housing",hoistHead,new Vector3(side*2.3f,-1,0),new Vector3(1.7f,2,2.8f),yellow);
                for(int depth=-1;depth<=1;depth+=2)bridgeWheels[(side==1?2:0)+(depth==1?1:0)]=Cylinder("Bridge wheel",trolley,new Vector3(side*2,0,depth*runwayHalfDepth),new Vector3(1.4f,.5f,1.4f),chrome,Quaternion.Euler(90,0,0));
            }
            cable=Box("Hoist telescoping column",root,Vector3.zero,new Vector3(.25f,1,.25f),chrome);
            hanger=Node("Pod transport yoke",root);
            Box("Transport spreader",hanger,new Vector3(0,2,0),new Vector3(shellRadius>0?8.8f:5,.65f,2),yellow);
            foreach(int side in new[]{-1,1}) {
                Beam("Sling support",hanger,new Vector3(side*(shellRadius>0?4.1f:2),2,0),new Vector3(side*(shellRadius>0?shellRadius:.8f),-3,0),.16f,chrome);
                Box("Transport jaw",hanger,new Vector3(side*(shellRadius>0?shellRadius:.85f),-3,0),new Vector3(.22f,1.4f,1.6f),dark);
            }
            railMount=Node("Launch cradle actuators",root);
            railMount.localPosition=new Vector3(0,28,0);
            for(int i=0;i<2;i++) {int side=i==0?-1:1;
                supports[i]=Box("Cradle actuator support tower",root,new Vector3(side*5,14,0),new Vector3(.7f,28,.7f),steel);
                supportFeet[i]=Box("Cradle tower footing",root,new Vector3(side*5,.4f,0),new Vector3(3,.8f,3),dark);
            }
            Box("Charge indicator spine",railMount,new Vector3(5.5f,-12,0),new Vector3(.6f,6,1),dark);
            for(int i=0;i<chargeIndicators.Length;i++) chargeIndicators[i]=Box("Capacitor ready segment "+i,railMount,new Vector3(5.5f,-14.4f+i*.65f,.55f),new Vector3(.45f,.4f,.15f),dark).GetComponent<Renderer>();
            for(int tier=0;tier<2;tier++) {
                float y=tier==0?-8:-18, radius=shellRadius>0?shellRadius:tier==0?.88f:1.48f;
                for(int side=-1;side<=1;side+=2) {
                    int i=tier*2+(side==1?1:0);
                    var actuator=assets.Actuator(radius,side,railMount);actuator.localPosition=new Vector3(0,y,0);
                    jaws[i]=Node("Sliding collar half "+i,railMount); jaws[i].localPosition=new Vector3(side*LoaderAssets.CollarTravel,y,0);
                    assets.Collar(radius,side,jaws[i]);
                    bolts[i]=Node("Driven locking pin "+i,railMount);
                    var socket=assets.PinSocket(radius,side,railMount);socket.localPosition=new Vector3(0,y,0);
                    assets.Pin(bolts[i]);
                    bolts[i].localPosition=LoaderAssets.PinPosition(radius,side,y,0,clearBore);
                    indicators[i]=Box("Lock confirmation light",railMount,new Vector3(side*5,y+1.5f,0),new Vector3(.5f,.3f,.6f),amber).GetComponent<Renderer>();
                }
            }
            var audio = new GameObject("Loading mechanism audio"); audio.transform.SetParent(railMount,false);
            motor=audio.AddComponent<AudioSource>(); motor.spatialBlend=1; motor.minDistance=12; motor.maxDistance=180; motor.loop=true;
            impact=audio.AddComponent<AudioSource>(); impact.spatialBlend=1; impact.minDistance=12; impact.maxDistance=180;
            motorClip=Sound("Authored hydraulic motor",1.2f,0); lockClip=Sound("Authored locking clunk",.22f,1); releaseClip=Sound("Authored release latch",.35f,2);
            motor.clip=motorClip; motor.volume=.12f; impact.volume=.32f;
            WorkLight(new Vector3(-22,38,23),new Vector3(0,18,0),new Color(.82f,.9f,1),2.8f);
            WorkLight(new Vector3(23,32,18),new Vector3(0,18,0),new Color(1,.86f,.67f),2.8f);
            WorkLight(new Vector3(0,42,-25),new Vector3(0,20,0),new Color(.67f,.85f,1),2);
            if(shellRadius>0) shellCarrier=new ShellCarrier(root,steel,dark,yellow,chrome,cyan,assets);
            SetIdle();
        }
        void WorkLight(Vector3 position,Vector3 target,Color color,float intensity) {
            var lamp=Node("Gantry work floodlight",root);lamp.localPosition=position;lamp.localRotation=Quaternion.LookRotation(target-position);
            var light=lamp.gameObject.AddComponent<Light>();light.type=LightType.Spot;light.spotAngle=75;light.range=110;light.intensity=intensity;light.color=color;
            Box("Floodlight housing",lamp,Vector3.zero,new Vector3(1.3f,.6f,.5f),dark);
        }
        Material Mat(Color color,bool glow=false,float gloss=.3f) { var m=new Material((glow?Shader.Find("KSP/Emissive/Diffuse"):null)??Shader.Find("KSP/Specular")??Shader.Find("KSP/Diffuse")??Shader.Find("Standard"));m.color=color;if(m.HasProperty("_Shininess"))m.SetFloat("_Shininess",gloss);if(m.HasProperty("_SpecColor"))m.SetColor("_SpecColor",new Color(.45f,.48f,.5f));if(glow){if(m.HasProperty("_EmissiveColor"))m.SetColor("_EmissiveColor",color);if(m.HasProperty("_EmissionColor")){m.SetColor("_EmissionColor",color);m.EnableKeyword("_EMISSION");}}materials.Add(m);return m; }
        static Transform Node(string name,Transform parent) { var t=new GameObject(name).transform;t.SetParent(parent,false);return t; }
        static Transform Primitive(string name,PrimitiveType type,Transform parent,Vector3 pos,Vector3 scale,Material mat) {
            var obj=GameObject.CreatePrimitive(type);obj.name=name;obj.transform.SetParent(parent,false);obj.transform.localPosition=pos;obj.transform.localScale=scale;
            obj.GetComponent<Renderer>().sharedMaterial=mat;UnityEngine.Object.Destroy(obj.GetComponent<Collider>());return obj.transform;
        }
        static Transform Box(string name,Transform parent,Vector3 pos,Vector3 scale,Material mat)=>Primitive(name,PrimitiveType.Cube,parent,pos,scale,mat);
        static Transform Cylinder(string name,Transform parent,Vector3 pos,Vector3 scale,Material mat,Quaternion rot) {var t=Primitive(name,PrimitiveType.Cylinder,parent,pos,scale,mat);t.localRotation=rot;return t;}
        static void Beam(string name,Transform parent,Vector3 a,Vector3 b,float width,Material mat) {var t=Box(name,parent,(a+b)/2,new Vector3(width,(b-a).magnitude,width),mat);t.localRotation=Quaternion.FromToRotation(Vector3.up,b-a);}
        static AudioClip Sound(string name,float seconds,int kind) {
            int rate=22050,count=(int)(seconds*rate);var samples=new float[count];var random=new System.Random(7274+kind);
            for(int n=0;n<count;n++) {double t=(double)n/rate,env=kind==0?1:Math.Exp(-t*(kind==1?24:13));
                samples[n]=(float)(env*(Math.Sin(t*Math.PI*2*(kind==0?90:kind==1?180:110))*.23+(random.NextDouble()*2-1)*(kind==0?.025:.25)));
            }
            var clip=AudioClip.Create(name,count,1,rate,false);clip.SetData(samples,0);return clip;
        }
        public void SetIdle() {
            operating=false;hanger.gameObject.SetActive(false);cable.gameObject.SetActive(false);railMount.gameObject.SetActive(true);
            for(int i=0;i<4;i++) {int side=i%2==0?-1:1;float y=i<2?-8:-18,radius=shellRadius>0?shellRadius:i<2?.88f:1.48f;
                jaws[i].localPosition=new Vector3(side*LoaderAssets.CollarTravel,y,0);bolts[i].localPosition=LoaderAssets.PinPosition(radius,side,y,0,clearBore);indicators[i].sharedMaterial=amber;
            }
            foreach(var indicator in chargeIndicators)indicator.sharedMaterial=dark;
            trolley.localPosition=Vector3.MoveTowards(trolley.localPosition,new Vector3(-38,64,0),Math.Max(0,Time.deltaTime)*8);if(motor.isPlaying)motor.Stop();lastStage=LoadingStage.Aligning;displayedElapsed=0;
            if(shellCarrier!=null) { shellCarrier.Park(gantryHeight,Time.deltaTime); SetShellHoist(); }
        }
        void SetShellHoist() {
            var center=shellCarrier.HoistLocal;
            trolley.localPosition=new Vector3(center.x,gantryHeight,0);hoistHead.localPosition=new Vector3(0,0,center.z);
        }
        void ConfigureRunway()
        {
            if(railGuide==null)return;
            var axis=root.InverseTransformDirection(railGuide.up);
            float depth=(float)GantrySafety.HalfDepth(axis.x,axis.z,clearBore,shellRadius>0?9:7);
            if(Math.Abs(depth-runwayHalfDepth)<.01f)return;
            runwayHalfDepth=depth;
            for(int i=0;i<2;i++){runwaySides[i].localPosition=new Vector3(0,0,(i==0?-1:1)*depth);slideGuides[i].localScale=new Vector3(.3f,.5f,depth*2+6);}
            crossCarriage.localScale=new Vector3(shellRadius>0?12:7,1.5f,depth*2+2);
            for(int i=0;i<4;i++)bridgeWheels[i].localPosition=new Vector3((i<2?-1:1)*2,0,(i%2==0?-1:1)*depth);
        }
        public void Update(Vessel pod,LoadingPose pose,double cradleHeight,bool reserved,float dt,ShellLoadingRoute route=null)
        {
            ConfigureRunway();
            if(pod==null || !pod.loaded || pod.packed) {SetIdle();return;}
            var local=root.InverseTransformPoint((Vector3)pod.GetWorldPos3D());
            if(!reserved && (pose==null || local.y>cradleHeight+10)) {SetIdle();return;}
            bool fresh=!operating;operating=true;
            if(pose==null)pose=LoadingPose.At(LoadingSequence.Duration);
            // Render interpolation only; the physics interlock uses the unsmoothed pose.
            displayedElapsed=fresh?pose.Elapsed:Math.Min(pose.Elapsed,displayedElapsed+Math.Max(0,dt));
            if(pose.Stage==LoadingStage.Aligning)displayedElapsed=0;
            var visual=pose.Stage==LoadingStage.Aligning || pose.LaunchClear?pose:LoadingPose.At(displayedElapsed,pose.ShellMode);
            // Physics guidance is expressed at CoMD; part art is positioned at
            // the actual root probe. These origins differ by many metres.
            Vector3 artOffset=pod.rootPart.transform.position-(Vector3)pod.GetWorldPos3D();
            if(shellCarrier!=null && railGuide!=null) artOffset=railGuide.up*Vector3.Dot(artOffset,pod.transform.up);
            railMount.gameObject.SetActive(true);railMount.localPosition=new Vector3(0,(float)cradleHeight,0)+root.InverseTransformDirection(artOffset);
            railMount.localRotation=Quaternion.Inverse(root.rotation)*(shellCarrier!=null && railGuide!=null?railGuide.rotation:pod.transform.rotation);
            for(int i=0;i<2;i++) {
                var mount=root.InverseTransformPoint(railMount.TransformPoint(new Vector3((i==0?-1:1)*(shellRadius>0?shellRadius+4.9f:5.78f),-8,0)));
                float supportTop=Math.Max(1,mount.y);supports[i].localPosition=new Vector3(mount.x,supportTop/2,mount.z);supports[i].localScale=new Vector3(.7f,supportTop,.7f);
                supportFeet[i].localPosition=new Vector3(mount.x,.4f,mount.z);
            }
            hanger.gameObject.SetActive(reserved && !visual.LaunchClear);
            float clear=visual.Stage>=LoadingStage.Clearing?1-(float)visual.Clamps:0;
            hanger.position=pod.rootPart.transform.position+pod.transform.up*((shellRadius>0?6:0)+clear*6)+root.right*(clear*8);hanger.rotation=pod.transform.rotation;
            Vector3 top=new Vector3(local.x+clear*8,63,local.z),bottom=root.InverseTransformPoint(hanger.TransformPoint(new Vector3(0,2,0)));
            trolley.localPosition=new Vector3(local.x+clear*8,64,0);hoistHead.localPosition=new Vector3(0,0,local.z);
            cable.gameObject.SetActive(hanger.gameObject.activeSelf);cable.localPosition=(top+bottom)/2;cable.localRotation=Quaternion.FromToRotation(Vector3.up,bottom-top);cable.localScale=new Vector3(.25f,(bottom-top).magnitude,.25f);
            if(shellCarrier!=null) {
                hanger.gameObject.SetActive(false);cable.gameObject.SetActive(false);
                if(reserved) shellCarrier.Update(pod,route,visual,gantryHeight); else shellCarrier.Park(gantryHeight,dt);
                SetShellHoist();
            }
            for(int i=0;i<4;i++) {int side=i%2==0?-1:1;float y=i<2?-8:-18,radius=shellRadius>0?shellRadius:i<2?.88f:1.48f;
                jaws[i].localPosition=new Vector3(side*LoaderAssets.CollarTravel*(float)(1-visual.Clamps),y,0);
                bolts[i].localPosition=LoaderAssets.PinPosition(radius,side,y,visual.Pins,clearBore);
                bolts[i].localRotation=Quaternion.Euler(0,0,(float)(visual.Pins*180));
                indicators[i].sharedMaterial=visual.Pins>.98?cyan:amber;
            }
            for(int i=0;i<chargeIndicators.Length;i++)chargeIndicators[i].sharedMaterial=visual.Charge>=(i+1.0)/chargeIndicators.Length?cyan:dark;
            bool moving=reserved && visual.Stage!=LoadingStage.Charging && visual.Stage!=LoadingStage.Ready;
            if(moving && !motor.isPlaying)motor.Play();else if(!moving && motor.isPlaying)motor.Stop();
            motor.volume=.10f*(GameSettings.SHIP_VOLUME);
            impact.volume=.35f*(GameSettings.SHIP_VOLUME);
            if(!fresh && visual.Stage!=lastStage)impact.PlayOneShot(visual.Stage==LoadingStage.ReleasingCarrier || visual.Stage==LoadingStage.RetractingCarrier || visual.Stage>=LoadingStage.Unlocking?releaseClip:lockClip);
            lastStage=visual.Stage;
        }
        public void Dispose() {shellCarrier?.Dispose();if(root!=null)UnityEngine.Object.Destroy(root.gameObject);assets.Dispose();foreach(var m in materials)UnityEngine.Object.Destroy(m);UnityEngine.Object.Destroy(motorClip);UnityEngine.Object.Destroy(lockClip);UnityEngine.Object.Destroy(releaseClip);}
    }
}
