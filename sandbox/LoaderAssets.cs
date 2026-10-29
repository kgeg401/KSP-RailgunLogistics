using System;
using System.Collections.Generic;
using UnityEngine;

namespace RailgunSandbox
{
    // Original, batched mechanical artwork. No part or collision components.
    // Local Y follows the payload axis; the existing animator owns every pose.
    public sealed class LoaderAssets : IDisposable
    {
        const int Steel=0,Dark=1,Paint=2,Chrome=3;
        public const float CollarTravel=2.8f,PinHalfLength=1.4f;
        readonly float bore;
        readonly Material[] palette;
        readonly Dictionary<string,Mesh> meshes=new Dictionary<string,Mesh>();
        public IEnumerable<Mesh> OwnedMeshes=>meshes.Values;
        public LoaderAssets(Material steel,Material dark,Material yellow,Material chrome,Material amber,Material cyan,float bore)
        { palette=new[]{steel,dark,yellow,chrome,amber,cyan,steel};this.bore=bore; }

        Transform Create(string key,Transform parent,Func<RailMesh> build)
        {
            if(!meshes.TryGetValue(key,out var mesh)){mesh=build().Finish(key);meshes.Add(key,mesh);}
            var obj=new GameObject(key);obj.transform.SetParent(parent,false);
            obj.AddComponent<MeshFilter>().sharedMesh=mesh;obj.AddComponent<MeshRenderer>().sharedMaterials=palette;return obj.transform;
        }
        static string Key(string name,float radius,int side)=>name+" / "+radius.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+" / "+side;
        public static float PinSide(float radius)=>radius+.15f;
        public static float PinContact(float radius)=>Mathf.Sqrt((radius+.3f)*(radius+.3f)-PinSide(radius)*PinSide(radius));
        public static float SocketFront(float radius,float bore)=>Mathf.Sqrt(Math.Max(0,(bore+.1f)*(bore+.1f)-Math.Max(0,PinSide(radius)-.42f)*Math.Max(0,PinSide(radius)-.42f)))+.12f;
        public static Vector3 PinPosition(float radius,int side,float y,double engaged,float bore)
        {
            float tip=Mathf.Lerp(SocketFront(radius,bore)+.25f,PinContact(radius)-.15f,(float)engaged);
            return new Vector3(side*PinSide(radius),y,tip+PinHalfLength);
        }
        static void Bolt(RailMesh b,Vector3 p,Vector3 normal,float radius=.07f)
        {
            b.Tube(Dark,p,p+normal*.025f,radius*1.35f,12);
            b.Tube(Chrome,p+normal*.025f,p+normal*.105f,radius,6,false);
        }
        static void Band(RailMesh b,float radius,int side,float depth)
        {
            float start=side==1?-80:100;
            b.ArcBand(Paint,0,radius+.025f,radius+.36f,depth,start,160,32,.055f);
            // Separate replaceable pads sit just outside the measured casing.
            for(int n=0;n<8;n++)b.ArcBand(Dark,0,radius-.12f,radius+.035f,depth*.65f,start+2+n*20,16,4,.025f);
            for(int face=-1;face<=1;face+=2) {
                b.ArcBand(Steel,face*(depth/2-.07f),radius+.08f,radius+.3f,.08f,start,160,32,.018f);
                for(int n=0;n<9;n++) {
                    float angle=(start+5+n*18.75f)*Mathf.Deg2Rad;
                    Bolt(b,new Vector3(Mathf.Cos(angle)*(radius+.19f),face*depth/2,Mathf.Sin(angle)*(radius+.19f)),Vector3.up*face,.065f);
                }
            }
            foreach(float angle in new[]{start,start+160}) {
                float a=angle*Mathf.Deg2Rad;var p=new Vector3(Mathf.Cos(a)*(radius+.18f),0,Mathf.Sin(a)*(radius+.18f));
                b.Chamfer(Steel,p,new Vector3(.5f,depth+.15f,.25f),.045f);
            }
        }
        public Transform Collar(float radius,int side,Transform parent)
        {
            return Create(Key("Continuous split rail collar",radius,side),parent,()=>{
                var b=new RailMesh();Band(b,radius,side,1.05f);
                b.Chamfer(Dark,new Vector3(side*(radius+.55f),0,0),new Vector3(1.05f,1.5f,1.35f),.12f);
                for(int face=-1;face<=1;face+=2)for(int z=-1;z<=1;z+=2)Bolt(b,new Vector3(side*(radius+.6f),face*.77f,z*.4f),Vector3.up*face,.085f);
                b.Tube(Chrome,new Vector3(side*(radius+.78f),0,0),new Vector3(side*(radius+3),0,0),.145f,20);
                return b;
            });
        }
        public Transform Actuator(float radius,int side,Transform parent)
        {
            return Create(Key("Flanged hydraulic actuator",radius,side),parent,()=>{
                var b=new RailMesh();
                b.Chamfer(Dark,new Vector3(side*(radius+4.9f),-.85f,0),new Vector3(2.1f,.6f,1.4f),.12f);
                b.Chamfer(Steel,new Vector3(side*(radius+4.9f),-1.15f,0),new Vector3(2.4f,.25f,2.9f),.07f);
                for(int z=-1;z<=1;z+=2)for(int x=-1;x<=1;x+=2)Bolt(b,new Vector3(side*(radius+4.9f)+x*.85f,-.98f,z*1.15f),Vector3.up,.11f);
                b.Sleeve(Steel,new Vector3(side*(radius+2.55f),0,0),new Vector3(side*(radius+5.9f),0,0),.18f,.47f);
                for(int end=0;end<2;end++) {
                    float x=radius+(end==0?2.55f:5.75f);
                    b.Sleeve(Dark,new Vector3(side*(x-.12f),0,0),new Vector3(side*(x+.12f),0,0),.18f,.63f);
                    for(int n=0;n<6;n++) {
                        float a=n*60*Mathf.Deg2Rad;
                        Bolt(b,new Vector3(side*(x-.14f),Mathf.Cos(a)*.52f,Mathf.Sin(a)*.52f),Vector3.right*-side,.07f);
                    }
                }
                b.Sleeve(Chrome,new Vector3(side*(radius+2.4f),0,0),new Vector3(side*(radius+2.6f),0,0),.17f,.29f);
                b.Sleeve(Dark,new Vector3(side*(radius+2.35f),0,0),new Vector3(side*(radius+2.42f),0,0),.15f,.205f);
                b.Tube(Steel,new Vector3(side*(radius+5.9f),0,0),new Vector3(side*(radius+6.05f),0,0),.48f,20);
                for(int saddle=0;saddle<2;saddle++)b.Chamfer(Steel,new Vector3(side*(radius+4.35f+saddle*.95f),-.49f,0),new Vector3(.32f,.26f,1.15f),.045f);
                for(int z=-1;z<=1;z+=2) {
                    var points=new[]{new Vector3(side*(radius+2.85f),.35f,z*.37f),new Vector3(side*(radius+3),.82f,z*.8f),
                        new Vector3(side*(radius+5.3f),.82f,z*.8f),new Vector3(side*(radius+5.55f),.35f,z*.37f)};
                    b.Pipe(Dark,points,.065f,10);
                    for(int n=0;n<2;n++)b.Tube(Chrome,points[n==0?0:3]-Vector3.up*.08f,points[n==0?0:3]+Vector3.up*.08f,.12f,6,false);
                }
                b.Chamfer(Paint,new Vector3(side*(radius+5.2f),.55f,0),new Vector3(.8f,.3f,1.05f),.06f);
                return b;
            });
        }
        public Transform PinSocket(float radius,int side,Transform parent)
        {
            return Create(Key("Aligned locking pin socket",radius,side),parent,()=>{
                var b=new RailMesh();float x=side*PinSide(radius),z=SocketFront(radius,bore);
                var end=new Vector3(x,0,z+1.55f);
                b.Beam(Dark,new Vector3(side*(radius+4.9f),-.6f,.9f),end,.32f);
                b.Sleeve(Steel,new Vector3(x,0,z+.05f),new Vector3(x,0,z+3.1f),.13f,.32f);
                b.Sleeve(Dark,new Vector3(x,0,z),new Vector3(x,0,z+.15f),.13f,.4f);
                b.Chamfer(Dark,new Vector3(x,0,z+3.25f),new Vector3(.7f,.7f,.4f),.08f);
                for(int n=0;n<4;n++) {
                    float a=(45+n*90)*Mathf.Deg2Rad;
                    Bolt(b,new Vector3(x+Mathf.Cos(a)*.32f,Mathf.Sin(a)*.32f,z-.04f),Vector3.back,.07f);
                }
                return b;
            });
        }
        public Transform Pin(Transform parent)
        {
            return Create("Machined driven locking pin",parent,()=>{
                var b=new RailMesh();b.Tube(Chrome,new Vector3(0,0,-PinHalfLength),new Vector3(0,0,PinHalfLength),.115f,20);
                b.Tube(Dark,new Vector3(0,0,-PinHalfLength+.02f),new Vector3(0,0,-PinHalfLength+.14f),.15f,20);
                b.Chamfer(Paint,new Vector3(0,0,PinHalfLength-.4f),new Vector3(.42f,.42f,.18f),.045f);
                b.Tube(Steel,new Vector3(0,0,PinHalfLength-.08f),new Vector3(0,0,PinHalfLength+.12f),.19f,6,false);return b;
            });
        }
        public Transform TransportGrip(float radius,int side,Transform parent)
        {
            return Create(Key("Profile fitted continuous transport grip",radius,side),parent,()=>{
                var b=new RailMesh();Band(b,radius,side,1.15f);
                b.Beam(Dark,new Vector3(side*(radius+.3f),0,-radius-1.3f),new Vector3(side*(radius+.3f),0,0),.36f);
                b.Chamfer(Steel,new Vector3(side*(radius+.3f),0,-radius-1.25f),new Vector3(.8f,1.4f,1.3f),.09f);
                b.Tube(Chrome,new Vector3(side*(radius+.3f),0,-radius-1.3f),new Vector3(side*(radius+2.1f),0,-radius-1.3f),.13f,16);
                for(int face=-1;face<=1;face+=2)Bolt(b,new Vector3(side*(radius+.3f),face*.73f,-radius-1.25f),Vector3.up*face,.11f);
                return b;
            });
        }
        public void Dispose(){foreach(var mesh in meshes.Values)UnityEngine.Object.Destroy(mesh);meshes.Clear();}
    }
}
