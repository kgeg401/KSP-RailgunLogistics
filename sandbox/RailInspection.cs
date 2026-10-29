using System;
using System.Collections;
using System.IO;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;

namespace RailgunSandbox
{
    public static class RailInspection
    {
        static string Number(double n)=>n.ToString("R",CultureInfo.InvariantCulture);
        static bool Finite(float n)=>!float.IsNaN(n)&&!float.IsInfinity(n);
        static float Cross(Vector2 a,Vector2 b)=>a.x*b.y-a.y*b.x;
        static float SegmentDistance(Vector2 a,Vector2 b)
        {
            var d=b-a;float t=d.sqrMagnitude<1e-12f?0:Mathf.Clamp01(-Vector2.Dot(a,d)/d.sqrMagnitude);return (a+d*t).magnitude;
        }
        static float TriangleRadius(Vector3 va,Vector3 vb,Vector3 vc)
        {
            var a=new Vector2(va.x,va.z);var b=new Vector2(vb.x,vb.z);var c=new Vector2(vc.x,vc.z);
            if(Math.Abs(Cross(b-a,c-a))>1e-8f) {
                float ab=Cross(a,b),bc=Cross(b,c),ca=Cross(c,a);
                if(ab>=0&&bc>=0&&ca>=0||ab<=0&&bc<=0&&ca<=0)return 0;
            }
            return Math.Min(SegmentDistance(a,b),Math.Min(SegmentDistance(b,c),SegmentDistance(c,a)));
        }
        static bool MeshReport(IEnumerable<Mesh> source,StringBuilder output,string label,bool radial,out float minimum)
        {
            bool pass=true;minimum=float.PositiveInfinity;
            foreach(var mesh in new HashSet<Mesh>(source)) {
                var vertices=mesh.vertices;var normals=mesh.normals;var tex=mesh.uv;var tangents=mesh.tangents;var triangles=mesh.triangles;
                int invalid=0,degenerate=0,reversed=0;
                for(int i=0;i<vertices.Length;i++) {
                    var v=vertices[i];var n=normals[i];var uv=tex[i];var t=tangents[i];
                    if(!Finite(v.x)||!Finite(v.y)||!Finite(v.z)||!Finite(n.x)||!Finite(n.y)||!Finite(n.z)||Math.Abs(n.sqrMagnitude-1)>.01f||
                        !Finite(uv.x)||!Finite(uv.y)||!Finite(t.x)||!Finite(t.y)||!Finite(t.z)||!Finite(t.w))invalid++;
                }
                for(int i=0;i<triangles.Length;i+=3) {
                    int a=triangles[i],b=triangles[i+1],c=triangles[i+2];var normal=Vector3.Cross(vertices[b]-vertices[a],vertices[c]-vertices[a]);
                    if(normal.sqrMagnitude<1e-14f)degenerate++;
                    else if(Vector3.Dot(normal,normals[a]+normals[b]+normals[c])<=0)reversed++;
                    if(radial)minimum=Math.Min(minimum,TriangleRadius(vertices[a],vertices[b],vertices[c]));
                }
                output.AppendLine(label+"Mesh="+mesh.name+" | vertices="+vertices.Length+" | triangles="+triangles.Length/3+" | invalid="+invalid+" | degenerate="+degenerate+" | reversed="+reversed);
                pass&=invalid==0&&degenerate==0&&reversed==0;
            }
            return pass;
        }
        public static IEnumerator Audit(DepotModel model,string folder)
        {
            var report=new StringBuilder("Native rail and loader asset audit\nschema=2\nunits=metres\nclientMvid="+typeof(RailAssets).Assembly.ManifestModule.ModuleVersionId+"\ncapturedUtc="+DateTime.UtcNow.ToString("O",CultureInfo.InvariantCulture)+"\n");
            bool pass=true;int warmedMeshes=-1,warmedObjects=-1;
            var samples=new StringBuilder("lap\talong\tactiveChunks\tpooledChunks\tcreatedChunks\townedMeshes\tdetailRebuilds\n");
            var art=model.RailArt;
            try {
                for(int lap=0;lap<4;lap++) {
                    foreach(double along in new[]{-24,0,24,200,480,10000,art.Length-1,art.Length+30,10000,0}) {
                        art.Update(art.Frame.TransformPoint(new Vector3(0,(float)along,12)));
                        samples.AppendLine(lap+"\t"+Number(along)+"\t"+art.ActiveDetailChunks+"\t"+art.PooledDetailChunks+"\t"+art.CreatedDetailChunks+"\t"+art.OwnedMeshCount+"\t"+art.DetailRebuilds);
                        pass&=art.ActiveDetailChunks<=14&&art.CreatedDetailChunks<=14&&art.OwnedMeshCount<=7;
                        yield return null;
                    }
                    if(lap==0){warmedMeshes=art.OwnedMeshCount;warmedObjects=art.CreatedDetailChunks;}
                    else pass&=warmedMeshes==art.OwnedMeshCount&&warmedObjects==art.CreatedDetailChunks;
                }
                var same=art.Frame.TransformPoint(new Vector3(0,12,12));art.Update(same);int rebuilds=art.DetailRebuilds;
                for(int i=0;i<2000;i++)art.Update(same);
                bool cached=art.DetailRebuilds==rebuilds;pass&=cached;
                report.AppendLine("steadyViewerUsesCache="+cached+"\ncreatedDetailChunks="+art.CreatedDetailChunks+"\nownedMeshes="+art.OwnedMeshCount+"\nmediumVertices="+art.MediumModuleVertices);
                pass&=MeshReport(art.RailMeshes,report,"rail",true,out float minimum);
                pass&=MeshReport(art.OwnedMeshes.Except(art.RailMeshes),report,"support",false,out _);
                report.AppendLine("shellBore="+Number(art.Bore)+"\nminimumTriangleRadius="+Number(minimum));
                pass&=minimum>=art.Bore-.001f;
                pass&=MeshReport(model.Loader.OwnedMeshes,report,"loader",false,out _);
                bool engages=true,clears=true;
                foreach(float radius in new[]{.88f,1.48f,3.62f}) {
                    float bore=radius>3?RailAssets.ShellBore:RailAssets.BareBore;
                    var locked=LoaderAssets.PinPosition(radius,1,0,1,bore);var open=LoaderAssets.PinPosition(radius,1,0,0,bore);
                    float tip=new Vector2(locked.x,locked.z-LoaderAssets.PinHalfLength).magnitude;float retracted=new Vector2(open.x,open.z-LoaderAssets.PinHalfLength).magnitude;
                    engages&=tip>radius+.025f&&tip<radius+.36f;clears&=retracted>bore+.1f;
                    report.AppendLine("pinRadius="+Number(radius)+" | engagedTip="+Number(tip)+" | retractedTip="+Number(retracted));
                }
                report.AppendLine("lockingPinsEngageCollar="+engages+"\nlockingPinsClearCollar="+clears);pass&=engages&&clears;
                pass&=art.Frame.GetComponentsInChildren<Collider>(true).Length==0;
                var isolated=new GameObject("Inactive bare-rail asset test");isolated.SetActive(false);
                RailAssets bare=null;
                try {
                    var guide=new GameObject("Bare guide").transform;guide.SetParent(isolated.transform,false);
                    bare=new RailAssets(isolated.transform,guide,112,false);bare.Configure(28,90);
                    bare.Update(bare.Frame.TransformPoint(new Vector3(0,111,4)));
                    pass&=MeshReport(bare.RailMeshes,report,"bare",true,out float bareMinimum);
                    report.AppendLine("bareBore="+Number(bare.Bore)+"\nbareMinimumTriangleRadius="+Number(bareMinimum));pass&=bareMinimum>=bare.Bore-.001f;
                    int owned=bare.OwnedMeshCount;
                    for(int i=0;i<20;i++){bare.Configure(28+i*.25,90);yield return null;pass&=bare.OwnedMeshCount==owned;}
                    report.AppendLine("bareSupportRebuildKeepsMeshCount="+(bare.OwnedMeshCount==owned));
                } finally {bare?.Dispose();UnityEngine.Object.Destroy(isolated);}
                report.AppendLine("success="+pass);
            } finally {
                art.Update(FlightCamera.fetch.mainCamera.transform.position);
                File.WriteAllText(Path.Combine(folder,"RailAssets-audit.txt"),report.ToString());
                File.WriteAllText(Path.Combine(folder,"RailAssets-cache.tsv"),samples.ToString());
            }
        }
        public static IEnumerator Capture(DepotModel model,string folder)
        {
            yield return new WaitForEndOfFrame();
            var obj=new GameObject("Rail asset inspection camera");var cam=obj.AddComponent<Camera>();cam.enabled=false;
            var lightObject=new GameObject("Rail asset inspection key light");var light=lightObject.AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.1f;light.color=new Color(.94f,.96f,1);
            var texture=new RenderTexture(1600,1000,24);var pixels=new Texture2D(1600,1000,TextureFormat.RGB24,false);var previous=RenderTexture.active;
            var frame=model.RailArt.Frame;
            try {
                cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.025f,.04f,.065f);
                cam.nearClipPlane=.15f;cam.farClipPlane=2000;cam.fieldOfView=53;cam.cullingMask=FlightCamera.fetch.mainCamera.cullingMask;cam.targetTexture=texture;
                var eyes=new[]{new Vector3(16,-9,13),new Vector3(9,14,5),new Vector3(-11,18,10),new Vector3(15,-8,11),new Vector3(13,-18,8)};
                var targets=new[]{new Vector3(0,9,0),new Vector3(5.1f,17,0),new Vector3(0,30,-1),new Vector3(6,-8,0),new Vector3(6,-18,0)};
                for(int i=0;i<eyes.Length;i++) {
                    var reference=i<3?frame:model.Loader.CradleFrame;
                    cam.transform.position=reference.TransformPoint(eyes[i]);var target=reference.TransformPoint(targets[i]);
                    cam.transform.rotation=Quaternion.LookRotation(target-cam.transform.position,model.Root.transform.up);
                    lightObject.transform.rotation=Quaternion.LookRotation((target-cam.transform.position).normalized+Vector3.down*.5f);
                    texture.Create();cam.Render();RenderTexture.active=texture;pixels.ReadPixels(new Rect(0,0,1600,1000),0,0);pixels.Apply();
                    File.WriteAllBytes(Path.Combine(folder,"RailInspection-"+(i+1)+".png"),pixels.EncodeToPNG());
                    yield return null;
                }
                File.WriteAllText(Path.Combine(folder,"RailAssets-diagnostics.txt"),$"Native rail asset inspection under authored work lighting\nprototypeVertices={model.RailArt.ModuleVertices}\nmediumVertices={model.RailArt.MediumModuleVertices}\nactiveDetailChunks={model.RailArt.ActiveDetailChunks}\ncreatedDetailChunks={model.RailArt.CreatedDetailChunks}\nownedMeshes={model.RailArt.OwnedMeshCount}\nminimumDetailRadius={model.RailArt.MinimumDetailRadius:R}\nrailColliders={frame.GetComponentsInChildren<Collider>().Length}\n");
                var geometry=new StringBuilder("Native loader geometry check at captured pose\n");
                bool valid=MeshReport(model.Loader.OwnedMeshes,geometry,"loader",false,out _);geometry.AppendLine("success="+valid);
                File.WriteAllText(Path.Combine(folder,"LoaderMeshes-audit.txt"),geometry.ToString());
            }finally{RenderTexture.active=previous;cam.targetTexture=null;texture.Release();UnityEngine.Object.Destroy(texture);UnityEngine.Object.Destroy(pixels);UnityEngine.Object.Destroy(lightObject);UnityEngine.Object.Destroy(obj);}
        }
        public static IEnumerator Profile(DepotModel model,string folder)
        {
            var vessel=FlightGlobals.ActiveVessel;var samples=new List<float>(300);
            for(int i=0;i<30;i++)yield return new WaitForEndOfFrame();
            float began=Time.realtimeSinceStartup;double ut=Planetarium.GetUniversalTime();long heap=GC.GetTotalMemory(false);
            for(int i=0;i<300;i++) {
                yield return new WaitForEndOfFrame();
                if(FlightGlobals.ActiveVessel!=vessel)break;
                samples.Add(Time.unscaledDeltaTime*1000);
            }
            float elapsed=Time.realtimeSinceStartup-began;samples.Sort();
            var all=model.Root.GetComponentsInChildren<MeshRenderer>(true);var meshes=new HashSet<Mesh>(model.RailArt.OwnedMeshes.Concat(model.Loader.OwnedMeshes));
            var result=new StringBuilder("Native presentation timing sample; not an isolated CPU or GPU benchmark\n");
            result.AppendLine("sameActiveVessel="+(FlightGlobals.ActiveVessel==vessel)+"\nsamples="+samples.Count+"\nwallSeconds="+Number(elapsed)+"\nframesPerSecond="+Number(samples.Count/elapsed));
            result.AppendLine("simulationSeconds="+Number(Planetarium.GetUniversalTime()-ut)+"\nmonoHeapDelta="+(GC.GetTotalMemory(false)-heap));
            if(samples.Count>0)result.AppendLine("medianFrameMs="+Number(samples[samples.Count/2])+"\np95FrameMs="+Number(samples[(int)((samples.Count-1)*.95)]));
            result.AppendLine("facilityMeshRenderers="+all.Length+"\noriginalOwnedMeshes="+meshes.Count+"\noriginalOwnedVertices="+meshes.Sum(m=>m.vertexCount));
            File.WriteAllText(Path.Combine(folder,"RailAssets-profile.txt"),result.ToString());
        }
        public static IEnumerator Aim(Transform reference,Vector3 eye,Vector3 target,string folder)
        {
            // SetTarget establishes the stock pivot before converting a world
            // position through KSP's own camera reference frame.
            yield return null;yield return new WaitForEndOfFrame();
            var camera=FlightCamera.fetch;var position=reference.TransformPoint(eye);var aim=reference.TransformPoint(target);
            camera.SetCamCoordsFromPosition(position);camera.SetDistanceImmediate(Vector3.Distance(position,aim));
            yield return new WaitForEndOfFrame();
            var pixel=camera.mainCamera.WorldToViewportPoint(aim);
            float error=Vector3.Distance(camera.mainCamera.transform.position,reference.TransformPoint(eye));
            bool centred=pixel.z>0&&Math.Abs(pixel.x-.5f)<.01f&&Math.Abs(pixel.y-.5f)<.01f;
            File.WriteAllText(Path.Combine(folder,"RailCamera-state.txt"),"Native FlightCamera positioning\neye="+reference.InverseTransformPoint(camera.mainCamera.transform.position)+
                "\nrigEye="+reference.InverseTransformPoint(camera.transform.position)+"\npivot="+reference.InverseTransformPoint(camera.GetPivot().position)+
                "\npivotLocal="+camera.GetPivot().localPosition+"\nmainLocal="+camera.transform.InverseTransformPoint(camera.mainCamera.transform.position)+"\ntargetViewport="+pixel+"\neyeError="+Number(error)+"\ncentred="+centred+"\n");
        }
        public static IEnumerator GameFrame(string path)
        {
            yield return new WaitForEndOfFrame();
            var previous=RenderTexture.active;var pixels=new Texture2D(Screen.width,Screen.height,TextureFormat.RGB24,false);
            try {RenderTexture.active=null;pixels.ReadPixels(new Rect(0,0,Screen.width,Screen.height),0,0);pixels.Apply();File.WriteAllBytes(path,pixels.EncodeToPNG());}
            finally {RenderTexture.active=previous;UnityEngine.Object.Destroy(pixels);}
        }
        public static void LoadingClearance(DepotModel model,string folder)
        {
            var frame=model.RailArt.Frame;var text=new StringBuilder("Native cleared loader surfaces within the launch span\n");
            float overall=float.PositiveInfinity;string worst="";int checkedTriangles=0;
            foreach(var filter in model.Loader.RootFrame.GetComponentsInChildren<MeshFilter>(true)) {
                var renderer=filter.GetComponent<MeshRenderer>();
                if(renderer==null||!renderer.enabled||!renderer.gameObject.activeInHierarchy||filter.sharedMesh==null)continue;
                var vertices=filter.sharedMesh.vertices;var triangles=filter.sharedMesh.triangles;float minimum=float.PositiveInfinity;
                for(int i=0;i<vertices.Length;i++)vertices[i]=frame.InverseTransformPoint(filter.transform.TransformPoint(vertices[i]));
                for(int i=0;i<triangles.Length;i+=3) {
                    var a=vertices[triangles[i]];var b=vertices[triangles[i+1]];var c=vertices[triangles[i+2]];
                    if(Math.Max(a.y,Math.Max(b.y,c.y)) < -24 || Math.Min(a.y,Math.Min(b.y,c.y))>Math.Min(128,model.RailArt.Length))continue;
                    minimum=Math.Min(minimum,TriangleRadius(a,b,c));checkedTriangles++;
                }
                if(minimum<overall){overall=minimum;worst=filter.name;}
                text.AppendLine(filter.name+" | minimumRadius="+Number(minimum));
            }
            text.AppendLine("checkedTriangles="+checkedTriangles+"\nminimumClearRadius="+Number(overall)+"\nnominalBore="+Number(model.RailArt.Bore)+"\nclosestHardware="+worst+"\nsuccess="+(checkedTriangles>0&&overall>=model.RailArt.Bore-.001f));
            File.WriteAllText(Path.Combine(folder,"LoaderClearance-audit.txt"),text.ToString());
        }
        public static IEnumerator PayloadWhenReady(Vessel vessel,string folder)
        {
            float deadline=Time.realtimeSinceStartup+30;
            while(vessel!=null&&FlightGlobals.ActiveVessel==vessel&&Time.realtimeSinceStartup<deadline&&(!vessel.loaded||vessel.packed||vessel.rootPart==null))yield return null;
            if(vessel==null||FlightGlobals.ActiveVessel!=vessel||!vessel.loaded||vessel.packed||vessel.rootPart==null) {
                File.WriteAllText(Path.Combine(folder,"RailPayload-geometry.txt"),"available=False\nreason=Original active vessel did not become ready within the bounded capture window.\n");yield break;
            }
            Payload(vessel,folder);
        }
        static void Payload(Vessel vessel,string folder)
        {
            if(vessel==null||!vessel.loaded||vessel.packed)throw new InvalidOperationException("Payload geometry needs a loaded native vessel.");
            var marker=vessel.parts.SelectMany(p=>p.FindModulesImplementing<RailgunFlightPod>()).FirstOrDefault();
            if(marker==null)throw new InvalidOperationException("The active vessel is not a reusable pod.");
            var axis=vessel.rootPart.transform.up;var origin=vessel.rootPart.transform.position;
            float maximum=0;int total=0;
            var text=new StringBuilder("Actual visible installed pod geometry, excluding the temporary shell\navailable=True\n");
            foreach(var part in vessel.parts.Where(p=>p.partInfo.name!=LaunchShell.PartName)) {
                float radius=0;int count=0;
                Action<Transform,Mesh> scan=(transform,mesh)=>{
                    foreach(var v in mesh.vertices) {
                        var delta=transform.TransformPoint(v)-origin;
                        radius=Math.Max(radius,Vector3.ProjectOnPlane(delta,axis).magnitude);count++;
                    }
                };
                foreach(var filter in part.FindModelComponents<MeshFilter>()) {
                    var renderer=filter.GetComponent<MeshRenderer>();
                    if(renderer==null||!renderer.enabled||!renderer.gameObject.activeInHierarchy||filter.sharedMesh==null)continue;
                    string shaders=string.Join(",",renderer.sharedMaterials.Where(m=>m!=null).Select(m=>m.shader.name));
                    bool effect=EffectShader(shaders);
                    if(effect){text.AppendLine("excludedEffect="+filter.name+" | shader="+shaders);continue;}
                    float before=radius;scan(filter.transform,filter.sharedMesh);
                    if(radius>before)text.AppendLine("physicalMesh="+part.partInfo.name+"/"+filter.name+" | shader="+shaders+" | radius="+Number(radius));
                }
                foreach(var skin in part.FindModelComponents<SkinnedMeshRenderer>()) {
                    if(!skin.enabled||!skin.gameObject.activeInHierarchy)continue;
                    string shaders=string.Join(",",skin.sharedMaterials.Where(m=>m!=null).Select(m=>m.shader.name));
                    bool effect=EffectShader(shaders);
                    if(effect){text.AppendLine("excludedSkinnedEffect="+skin.name+" | shader="+shaders);continue;}
                    var baked=new Mesh();try{skin.BakeMesh(baked);scan(skin.transform,baked);text.AppendLine("physicalSkin="+part.partInfo.name+"/"+skin.name+" | shader="+shaders+" | maximum="+Number(radius));}finally{UnityEngine.Object.Destroy(baked);}
                }
                text.AppendLine(part.partInfo.name+" | vertices="+count+" | radius="+Number(radius));maximum=Math.Max(maximum,radius);total+=count;
            }
            text.AppendLine("maximumPayloadRadius="+Number(maximum)+"\nvertices="+total+"\nparts="+vessel.parts.Count+"\n");
            File.WriteAllText(Path.Combine(folder,"RailPayload-geometry.txt"),text.ToString());
        }
        static bool EffectShader(string shaders)=>shaders.IndexOf("Firefly",StringComparison.OrdinalIgnoreCase)>=0||shaders.IndexOf("Particle",StringComparison.OrdinalIgnoreCase)>=0||shaders.IndexOf("Waterfall/",StringComparison.OrdinalIgnoreCase)>=0;
    }
}
