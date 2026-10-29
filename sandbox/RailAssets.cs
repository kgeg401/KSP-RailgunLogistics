using System;
using System.Collections.Generic;
using UnityEngine;

namespace RailgunSandbox
{
    // Original modular artwork. No colliders, rigidbodies or guidance changes.
    public sealed class RailAssets : IDisposable
    {
        const float Start=-24, Bay=8, Chunk=32;
        public const float ShellBore=3.85f,BareBore=2.15f;
        const int Steel=0,Dark=1,Copper=2,Ceramic=3,Paint=4,Glow=5,Concrete=6;
        readonly Transform depot,guide;
        public readonly Transform Frame;
        readonly double length;
        readonly float bore,track;
        readonly Material[] materials;
        readonly List<Mesh> meshes=new List<Mesh>();
        readonly List<Texture2D> textures=new List<Texture2D>();
        readonly Dictionary<int,GameObject> detail=new Dictionary<int,GameObject>();
        readonly Stack<GameObject> pool=new Stack<GameObject>();
        readonly HashSet<int> needed=new HashSet<int>();
        readonly List<int> remove=new List<int>();
        readonly Mesh module,mediumModule;
        Mesh tail,mediumTail,supportMesh;
        GameObject piers;
        int lastNear=int.MinValue;
        bool lastNearVisible;
        Color lastPower;
        double lastHeight=double.NaN,lastHeading=double.NaN;
        public int ActiveDetailChunks=>detail.Count;
        public int PooledDetailChunks=>pool.Count;
        public int CreatedDetailChunks {get;private set;}
        public int DetailRebuilds {get;private set;}
        public int OwnedMeshCount=>meshes.Count+(supportMesh==null?0:1);
        public int ModuleVertices=>module.vertexCount;
        public int MediumModuleVertices=>mediumModule.vertexCount;
        public double Length=>length;
        public float Bore=>bore;
        public IEnumerable<Mesh> RailMeshes=>meshes;
        public IEnumerable<Mesh> OwnedMeshes { get {foreach(var mesh in meshes)yield return mesh;if(supportMesh!=null)yield return supportMesh;} }
        public float MinimumDetailRadius {get;private set;}=float.PositiveInfinity;
        public RailAssets(Transform depot,Transform guide,double length,bool shell)
        {
            this.depot=depot;this.guide=guide;this.length=length;bore=shell?ShellBore:BareBore;track=bore+.65f;
            Frame=new GameObject("KRL high fidelity accelerator / open loading channel").transform;Frame.SetParent(guide,false);
            materials=new[]{Material("Brushed rail steel",new Color(.36f,.4f,.43f),.55f,0),Material("Graphite structural coating",new Color(.075f,.095f,.11f),.28f,1),
                Material("Wound copper and busbars",new Color(.52f,.25f,.11f),.65f,0),Material("Ceramic electrical isolation",new Color(.78f,.8f,.74f),.2f,1),
                Material("Safety yellow hardware",new Color(.78f,.47f,.06f),.28f,1),Material("Recessed accelerator status lights",new Color(.06f,.7f,.85f),.2f,2),
                Material("Reinforced concrete plinths",new Color(.35f,.36f,.34f),.06f,1)};
            var backboneCache=new Dictionary<float,Mesh>();
            for(float y=Start;y<length;y+=4096) {
                float span=(float)Math.Min(4096,length-y);
                if(!backboneCache.TryGetValue(span,out var mesh)){mesh=Backbone(span);meshes.Add(mesh);backboneCache.Add(span,mesh);}
                var obj=Object("Continuous chamfered rail backbone",mesh,Frame);obj.transform.localPosition=Vector3.up*y;
            }
            module=Detailed(Chunk);meshes.Add(module);
            mediumModule=Detailed(Chunk,false);meshes.Add(mediumModule);
            foreach(var v in module.vertices)MinimumDetailRadius=Math.Min(MinimumDetailRadius,Mathf.Sqrt(v.x*v.x+v.z*v.z));
            if(MinimumDetailRadius<bore-.001f)throw new InvalidOperationException("Rail detail intrudes into the payload clearance envelope.");
            UpdateDetail(0,true);SetPower(null,false);
        }
        Material Material(string name,Color color,float shininess,int finish)
        {
            var shader=(finish==2?Shader.Find("KSP/Emissive/Diffuse"):Shader.Find("KSP/Specular"))??Shader.Find("KSP/Diffuse")??Shader.Find("Standard");
            var m=new Material(shader){name=name,color=color,enableInstancing=true};
            if(m.HasProperty("_Shininess"))m.SetFloat("_Shininess",shininess);
            if(m.HasProperty("_SpecColor"))m.SetColor("_SpecColor",new Color(.5f,.53f,.56f));
            if(m.HasProperty("_Metallic"))m.SetFloat("_Metallic",finish==0?.8f:.15f);
            if(m.HasProperty("_Glossiness"))m.SetFloat("_Glossiness",shininess);
            if(finish==2){if(m.HasProperty("_EmissiveColor"))m.SetColor("_EmissiveColor",color);return m;}
            var t=new Texture2D(128,128,TextureFormat.RGBA32,true){name=name+" / original micro surface",wrapMode=TextureWrapMode.Repeat,filterMode=FilterMode.Trilinear,anisoLevel=8};
            var pixels=new Color[128*128];var random=new System.Random(3190+finish);
            for(int y=0;y<128;y++)for(int x=0;x<128;x++) {
                float variation=(float)(.91+random.NextDouble()*.09)+(finish==0?(float)Math.Sin(x*1.71)*.035f:0);
                if(finish==0 && x%37==0 && y%19<12)variation=.74f;
                pixels[y*128+x]=new Color(variation,variation,variation,1);
            }
            t.SetPixels(pixels);t.Apply(true,false);textures.Add(t);m.mainTexture=t;return m;
        }
        GameObject Object(string name,Mesh mesh,Transform parent)
        {
            var obj=new GameObject(name);obj.transform.SetParent(parent,false);obj.AddComponent<MeshFilter>().sharedMesh=mesh;
            var renderer=obj.AddComponent<MeshRenderer>();renderer.sharedMaterials=materials;renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.On;return obj;
        }
        Mesh Backbone(float span)
        {
            var b=new RailMesh();
            for(int side=-1;side<=1;side+=2) {
                float x=side*track;
                b.Chamfer(Steel,new Vector3(x,span/2,-.9f),new Vector3(1.25f,span,.25f),.09f);
                b.Chamfer(Steel,new Vector3(x,span/2,.55f),new Vector3(1.25f,span,.25f),.09f);
                b.Chamfer(Dark,new Vector3(x,span/2,-.18f),new Vector3(.38f,span,1.3f),.06f);
                b.Chamfer(Copper,new Vector3(side*(bore+.045f),span/2,-.1f),new Vector3(.09f,span,.55f),.025f);
                b.Chamfer(Ceramic,new Vector3(side*(bore+.17f),span/2,-.1f),new Vector3(.12f,span,.78f),.035f);
                b.Chamfer(Dark,new Vector3(side*(track+2.1f),span/2,-2),new Vector3(1.25f,span,.2f),.04f);
                b.Tube(Steel,new Vector3(side*(track+2.75f),0,-.6f),new Vector3(side*(track+2.75f),span,-.6f),.07f);
            }
            return b.Finish("Rail backbone / "+span+" m");
        }
        void Bolt(RailMesh b,Vector3 center,Vector3 normal,float radius=.09f) {b.Tube(Steel,center,center+normal*.1f,radius,6,false);}
        Mesh Detailed(float span,bool fine=true)
        {
            var b=new RailMesh();
            for(float y=0;y<span-.01f;y+=Bay) {
                float size=Math.Min(Bay,span-y);
                b.Hoop(Steel,y+.35f,bore+.15f,bore+1.05f,.42f,fine?28:8);
                for(int side=-1;side<=1;side+=2) {
                    for(int end=0;end<2;end++) {
                        float h=y+(end==0?.65f:size-.65f);
                        b.Chamfer(Dark,new Vector3(side*(track+.14f),h,0),new Vector3(1.5f,.5f,2.1f),.12f);
                        for(int n=0;n<(fine?3:1);n++)b.Chamfer(Ceramic,new Vector3(side*(track+.2f),h+(n-1)*.13f,.95f),new Vector3(1.25f,.09f,.18f),.025f);
                        if(fine)for(int z=-1;z<=1;z+=2)Bolt(b,new Vector3(side*(track+.9f),h,z*.7f),Vector3.right*side);
                    }
                    for(int n=0;n<4;n++) {
                        float h=y+1+n*2;
                        b.Chamfer(Dark,new Vector3(side*(track+.48f),h,.05f),new Vector3(.75f,1.6f,1.25f),.12f);
                        for(int winding=0;winding<(fine?5:1);winding++) {
                            float py=h-.55f+winding*.27f;
                            var a=new Vector3(side*(track-.02f),py,-.74f);var c=new Vector3(side*(track+.98f),py,.84f);
                            if(fine)b.RoundedWinding(Copper,a.x,c.x,py,a.z,c.z,.18f,.055f);
                            else b.Chamfer(Copper,new Vector3(side*(track+.48f),h,.77f),new Vector3(1,.9f,.12f),.03f);
                        }
                        b.Chamfer(Steel,new Vector3(side*(track+1.1f),h,.12f),new Vector3(.28f,.85f,1.4f),.07f);
                        b.Chamfer(Glow,new Vector3(side*(bore+.02f),h,.62f),new Vector3(.06f,.5f,.1f),.015f);
                    }
                    for(int z=-1;z<=1;z+=2) {
                        var a=new Vector3(side*(track+1.1f),y,z*.65f);var c=new Vector3(a.x,y+size,a.z);
                        b.Tube(Steel,a,c,.11f);b.Tube(Copper,a+Vector3.right*side*.23f,c+Vector3.right*side*.23f,.075f);
                        b.Tube(Steel,new Vector3(a.x,y+size/2,a.z),new Vector3(side*(track+.8f),y+size/2,a.z),.08f);
                    }
                    b.Chamfer(Dark,new Vector3(side*(track+1.8f),y+size/2,-1.3f),new Vector3(.65f,2,.6f),.1f);
                    b.Chamfer(Paint,new Vector3(side*(track+2.16f),y+size/2,-1.3f),new Vector3(.08f,1.5f,.45f),.02f);
                    if(fine)for(float h=y;h<y+size;h+=.5f)b.Box(Steel,new Vector3(side*(track+2.1f),h,-1.86f),new Vector3(1.2f,.09f,.12f));
                    for(int n=0;n<3;n++) {
                        float h=y+n*size/2;
                        b.Tube(Steel,new Vector3(side*(track+2.75f),h,-1.9f),new Vector3(side*(track+2.75f),h,-.6f),.06f);
                        b.Beam(Dark,new Vector3(side*(track+.6f),h,-.9f),new Vector3(side*(track+2.7f),h,-2),.14f);
                    }
                    b.Beam(Dark,new Vector3(side*(bore+1),y+.5f,-(bore+.5f)),new Vector3(side*(bore+1),y+size-.5f,-(bore+.5f)),.25f);
                }
            }
            return b.Finish("Detailed accelerator bays / coils, ceramics, fasteners and services");
        }
        void UpdateDetail(double along,bool nearVisible)
        {
            int last=(int)Math.Ceiling((length-Start)/Chunk)-1;
            int near=(int)Math.Floor((along-Start)/Chunk);
            if(near==lastNear && nearVisible==lastNearVisible)return;
            lastNear=near;lastNearVisible=nearVisible;DetailRebuilds++;needed.Clear();remove.Clear();
            for(int i=0;i<=Math.Min(7,last);i++)needed.Add(i);
            if(nearVisible)for(int i=Math.Max(0,near-2);i<=Math.Min(last,near+3);i++)needed.Add(i);
            foreach(var pair in detail)if(!needed.Contains(pair.Key))remove.Add(pair.Key);
            foreach(int i in remove){detail[i].SetActive(false);pool.Push(detail[i]);detail.Remove(i);}
            foreach(int i in needed)if(!detail.ContainsKey(i)) {
                float y=Start+i*Chunk;Mesh mesh=module,medium=mediumModule;
                if(y+Chunk>length){
                    if(tail==null){tail=Detailed((float)(length-y));mediumTail=Detailed((float)(length-y),false);meshes.Add(tail);meshes.Add(mediumTail);}
                    mesh=tail;medium=mediumTail;
                }
                var obj=pool.Count>0?pool.Pop():DetailObject();
                obj.transform.GetChild(0).GetComponent<MeshFilter>().sharedMesh=mesh;obj.transform.GetChild(1).GetComponent<MeshFilter>().sharedMesh=medium;
                obj.transform.localPosition=Vector3.up*y;obj.SetActive(true);obj.GetComponent<LODGroup>().RecalculateBounds();detail.Add(i,obj);
            }
        }
        GameObject DetailObject()
        {
            var obj=new GameObject("Pooled accelerator bays / distance detail");obj.transform.SetParent(Frame,false);
            var high=Object("Machined near detail",module,obj.transform).GetComponent<MeshRenderer>();
            var medium=Object("Distant structure",mediumModule,obj.transform).GetComponent<MeshRenderer>();
            var lod=obj.AddComponent<LODGroup>();lod.SetLODs(new[]{new LOD(.22f,new Renderer[]{high}),new LOD(.015f,new Renderer[]{medium})});lod.RecalculateBounds();
            CreatedDetailChunks++;return obj;
        }
        public void SetPower(LoadingPose pose,bool firing)
        {
            var color=firing?new Color(.55f,.9f,1):pose==null||pose.Stage==LoadingStage.Aligning?new Color(.012f,.025f,.032f):
                Color.Lerp(new Color(.55f,.16f,.015f),new Color(.06f,.7f,.85f),(float)pose.Charge);
            if((color-lastPower).maxColorComponent<.002f && (lastPower-color).maxColorComponent<.002f)return;
            lastPower=color;materials[Glow].color=color;
            if(materials[Glow].HasProperty("_EmissiveColor"))materials[Glow].SetColor("_EmissiveColor",color);
        }
        public void Configure(double height,double heading)
        {
            var axis=guide.up;var up=Vector3.ProjectOnPlane(depot.up,axis);
            if(up.sqrMagnitude<.001f)up=depot.forward;
            Frame.rotation=Quaternion.LookRotation(up.normalized,axis);
            if(Math.Abs(height-lastHeight)<.02 && Math.Abs(heading-lastHeading)<.02)return;
            lastHeight=height;lastHeading=heading;
            if(piers!=null)UnityEngine.Object.Destroy(piers);
            if(supportMesh!=null)UnityEngine.Object.Destroy(supportMesh);
            var b=new RailMesh();
            for(float y=Start;y<=Math.Min(96,length);y+=32) {
                var origin=depot.InverseTransformPoint(Frame.TransformPoint(new Vector3(0,y,-bore-1.1f)));
                var sideways=depot.InverseTransformDirection(Frame.right);
                for(int side=-1;side<=1;side+=2) {
                    var top=origin+sideways*side*(bore+1.4f);if(top.y<3)continue;
                    var foot=new Vector3(top.x,.5f,top.z);
                    b.Box(Concrete,foot,new Vector3(3.5f,1,3.5f));b.Chamfer(Dark,foot+Vector3.up*.65f,new Vector3(2.2f,.2f,2.2f),.15f);
                    for(int x=-1;x<=1;x+=2)for(int z=-1;z<=1;z+=2) {
                        var p=foot+new Vector3(x*.65f,.8f,z*.65f);b.Tube(Steel,p,new Vector3(p.x,top.y,p.z),.12f);
                        Bolt(b,foot+new Vector3(x*.9f,.78f,z*.9f),Vector3.up,.13f);
                    }
                    for(float h=1.4f;h<top.y-3;h+=6) {
                        b.Beam(Steel,new Vector3(top.x-.65f,h,top.z-.65f),new Vector3(top.x+.65f,Math.Min(h+6,top.y),top.z+.65f),.16f);
                        b.Beam(Steel,new Vector3(top.x+.65f,h,top.z-.65f),new Vector3(top.x-.65f,Math.Min(h+6,top.y),top.z+.65f),.16f);
                    }
                    b.Box(Paint,top,new Vector3(2.2f,.4f,2.2f));b.Beam(Steel,top,origin,.4f);
                }
            }
            supportMesh=b.Finish("Triangulated launch trestles and bolted foundation plates");piers=Object("Launcher support trestles",supportMesh,depot);
        }
        public void Update(Vector3 viewer)
        {
            var local=Frame.InverseTransformPoint(viewer);double along=Math.Max(Start,Math.Min(length,local.y));
            double distance=(local-new Vector3(0,(float)along,0)).sqrMagnitude;
            UpdateDetail(along,distance<1200*1200);
        }
        public void Dispose(){if(Frame!=null)UnityEngine.Object.Destroy(Frame.gameObject);if(piers!=null)UnityEngine.Object.Destroy(piers);if(supportMesh!=null)UnityEngine.Object.Destroy(supportMesh);foreach(var mesh in meshes)UnityEngine.Object.Destroy(mesh);foreach(var m in materials)UnityEngine.Object.Destroy(m);foreach(var t in textures)UnityEngine.Object.Destroy(t);}
    }
}
