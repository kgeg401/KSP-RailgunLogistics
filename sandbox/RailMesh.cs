using System;
using System.Collections.Generic;
using UnityEngine;

namespace RailgunSandbox
{
    // Original engine-ready geometry: local Y is along the guide, +Z is the
    // open loading side. One mesh batches all geometry by material.
    public sealed class RailMesh
    {
        readonly List<Vector3> vertices=new List<Vector3>(), normals=new List<Vector3>();
        readonly List<Vector2> uv=new List<Vector2>();
        readonly List<int>[] triangles;
        public RailMesh(int materials=7) {triangles=new List<int>[materials];for(int i=0;i<materials;i++)triangles[i]=new List<int>();}
        int Vertex(Vector3 p,Vector3 n,Vector2 tex)
        {
            int index=vertices.Count;vertices.Add(p);normals.Add(n);uv.Add(tex);return index;
        }
        void TriangleIndices(int m,int a,int b,int c) {triangles[m].Add(a);triangles[m].Add(b);triangles[m].Add(c);}
        public void Triangle(int material,Vector3 a,Vector3 b,Vector3 c)
        {
            var normal=Vector3.Cross(b-a,c-a).normalized;
            int first=Vertex(a,normal,Vector2.zero);Vertex(b,normal,new Vector2((b-a).magnitude*.5f,0));Vertex(c,normal,new Vector2(0,(c-a).magnitude*.5f));
            TriangleIndices(material,first,first+1,first+2);
        }
        void SmoothQuad(int material,Vector3 a,Vector3 b,Vector3 c,Vector3 d,Vector3 na,Vector3 nb,Vector3 nc,Vector3 nd)
        {
            int first=vertices.Count;float w=(b-a).magnitude*.5f,h=(d-a).magnitude*.5f;
            Vertex(a,na,Vector2.zero);Vertex(b,nb,new Vector2(w,0));Vertex(c,nc,new Vector2(w,h));Vertex(d,nd,new Vector2(0,h));
            TriangleIndices(material,first,first+1,first+2);TriangleIndices(material,first,first+2,first+3);
        }
        public void Quad(int material,Vector3 a,Vector3 b,Vector3 c,Vector3 d)
        {
            var normal=Vector3.Cross(b-a,c-a).normalized;
            SmoothQuad(material,a,b,c,d,normal,normal,normal,normal);
        }
        public void Box(int m,Vector3 center,Vector3 size,Quaternion rotation)
        {
            var p=new Vector3[8];for(int i=0;i<8;i++)p[i]=center+rotation*Vector3.Scale(size,new Vector3((i&1)==0?-.5f:.5f,(i&2)==0?-.5f:.5f,(i&4)==0?-.5f:.5f));
            Quad(m,p[0],p[4],p[6],p[2]);Quad(m,p[1],p[3],p[7],p[5]);
            Quad(m,p[0],p[1],p[5],p[4]);Quad(m,p[2],p[6],p[7],p[3]);
            Quad(m,p[0],p[2],p[3],p[1]);Quad(m,p[4],p[5],p[7],p[6]);
        }
        public void Box(int m,Vector3 center,Vector3 size)=>Box(m,center,size,Quaternion.identity);
        // An octagonal extrusion provides real chamfer highlights, not painted edges.
        public void Chamfer(int m,Vector3 center,Vector3 size,float bevel)
        {
            float x=size.x/2,z=size.z/2,b=Math.Min(bevel,Math.Min(x,z)*.45f);
            var ring=new[]{new Vector3(-x+b,0,-z),new Vector3(x-b,0,-z),new Vector3(x,0,-z+b),new Vector3(x,0,z-b),new Vector3(x-b,0,z),new Vector3(-x+b,0,z),new Vector3(-x,0,z-b),new Vector3(-x,0,-z+b)};
            for(int i=0;i<8;i++) {
                int j=(i+1)%8;var a=center+ring[i]-Vector3.up*size.y/2;var c=center+ring[j]+Vector3.up*size.y/2;
                Quad(m,a,a+Vector3.up*size.y,c,c-Vector3.up*size.y);
                Triangle(m,center-Vector3.up*size.y/2,a,center+ring[j]-Vector3.up*size.y/2);
                Triangle(m,center+Vector3.up*size.y/2,center+ring[j]+Vector3.up*size.y/2,center+ring[i]+Vector3.up*size.y/2);
            }
        }
        public void Tube(int m,Vector3 a,Vector3 b,float radius,int sides=12,bool smooth=true)
        {
            var axis=(b-a).normalized;var side=Vector3.Cross(axis,Math.Abs(axis.y)>.9f?Vector3.right:Vector3.up).normalized;
            var other=Vector3.Cross(axis,side);
            int first=vertices.Count;
            if(smooth)for(int i=0;i<=sides;i++) {
                float t=i*2*Mathf.PI/sides;var normal=side*Mathf.Cos(t)+other*Mathf.Sin(t);
                Vertex(a+normal*radius,normal,new Vector2(i/(float)sides,0));Vertex(b+normal*radius,normal,new Vector2(i/(float)sides,(b-a).magnitude*.5f));
            }
            for(int i=0;i<sides;i++) {
                float t=i*2*Mathf.PI/sides,u=(i+1)*2*Mathf.PI/sides;
                var p=(side*Mathf.Cos(t)+other*Mathf.Sin(t))*radius;var q=(side*Mathf.Cos(u)+other*Mathf.Sin(u))*radius;
                if(smooth) {int n=first+i*2;TriangleIndices(m,n,n+2,n+3);TriangleIndices(m,n,n+3,n+1);}
                else Quad(m,a+p,a+q,b+q,b+p);
                Triangle(m,a,a+q,a+p);Triangle(m,b,b+p,b+q);
            }
        }
        // Shared circular rings keep rounded copper windings smooth without
        // four separate, intersecting capped cylinders at every corner.
        public void Pipe(int m,IReadOnlyList<Vector3> points,float radius,int sides=8,bool closed=false)
        {
            if(points.Count<2)throw new ArgumentException("A pipe needs two or more points.");
            int first=vertices.Count,segments=closed?points.Count:points.Count-1;
            float distance=0;
            for(int i=0;i<=segments;i++) {
                int index=i%points.Count;var p=points[index];
                var previous=points[index==0?(closed?points.Count-1:0):index-1];
                var next=points[index+1==points.Count?(closed?0:index):index+1];
                var axis=(next-previous).normalized;var side=Vector3.Cross(axis,Math.Abs(axis.y)>.9f?Vector3.right:Vector3.up).normalized;var other=Vector3.Cross(axis,side);
                if(i>0)distance+=Vector3.Distance(p,points[(i-1)%points.Count]);
                for(int n=0;n<=sides;n++) {
                    float t=n*2*Mathf.PI/sides;var normal=side*Mathf.Cos(t)+other*Mathf.Sin(t);
                    Vertex(p+normal*radius,normal,new Vector2(n/(float)sides,distance*.5f));
                }
            }
            for(int i=0;i<segments;i++)for(int n=0;n<sides;n++) {
                int a=first+i*(sides+1)+n,b=a+sides+1;
                TriangleIndices(m,a,a+1,b+1);TriangleIndices(m,a,b+1,b);
            }
        }
        public void RoundedWinding(int m,float x0,float x1,float y,float z0,float z1,float corner,float wire)
        {
            float left=Math.Min(x0,x1),right=Math.Max(x0,x1);
            var path=new List<Vector3>(16);
            for(int c=0;c<4;c++) {
                float cx=(c==0||c==3?right:left)+(c==0||c==3?-corner:corner);
                float cz=(c<2?z1:z0)+(c<2?-corner:corner);
                for(int step=0;step<4;step++) {
                    float angle=(c*90+step*30)*Mathf.Deg2Rad;
                    path.Add(new Vector3(cx+Mathf.Cos(angle)*corner,y,cz+Mathf.Sin(angle)*corner));
                }
            }
            Pipe(m,path,wire,8,true);
        }
        public void Beam(int m,Vector3 a,Vector3 b,float thickness)=>Box(m,(a+b)/2,new Vector3(thickness,(b-a).magnitude,thickness),Quaternion.FromToRotation(Vector3.up,b-a));
        public void Sleeve(int m,Vector3 a,Vector3 b,float inner,float outer,int sides=24)
        {
            int first=vertices.Count;ArcBand(m,0,inner,outer,(b-a).magnitude,0,360,sides,.035f);
            var rotation=Quaternion.FromToRotation(Vector3.up,b-a);var center=(a+b)/2;
            for(int i=first;i<vertices.Count;i++){vertices[i]=center+rotation*vertices[i];normals[i]=rotation*normals[i];}
        }
        public void Hoop(int m,float y,float inner,float outer,float depth,int steps=28)
        {
            // A 150 degree loading aperture clears the 7 m shell and carrier.
            ArcBand(m,y,inner,outer,depth,165,210,steps,.055f);
        }
        public void ArcBand(int m,float y,float inner,float outer,float depth,float start,float sweep,int steps,float bevel=.03f)
        {
            float edge=Math.Min(bevel,Math.Min((outer-inner)/3,depth/3));
            var section=new[]{new Vector2(inner+edge,-depth/2),new Vector2(outer-edge,-depth/2),new Vector2(outer,-depth/2+edge),new Vector2(outer,depth/2-edge),
                new Vector2(outer-edge,depth/2),new Vector2(inner+edge,depth/2),new Vector2(inner,depth/2-edge),new Vector2(inner,-depth/2+edge)};
            for(int i=0;i<steps;i++) {
                float a=(start+sweep*i/steps)*Mathf.Deg2Rad,b=(start+sweep*(i+1)/steps)*Mathf.Deg2Rad;
                var ra=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a));var rb=new Vector3(Mathf.Cos(b),0,Mathf.Sin(b));
                for(int n=0;n<8;n++) {
                    var p=section[n];var q=section[(n+1)%8];var d=(q-p).normalized;
                    var na=ra*d.y-Vector3.up*d.x;var nb=rb*d.y-Vector3.up*d.x;
                    SmoothQuad(m,ra*p.x+Vector3.up*(y+p.y),ra*q.x+Vector3.up*(y+q.y),rb*q.x+Vector3.up*(y+q.y),rb*p.x+Vector3.up*(y+p.y),na,na,nb,nb);
                    var ca=ra*((inner+outer)/2)+Vector3.up*y;var cb=rb*((inner+outer)/2)+Vector3.up*y;
                    if(Math.Abs(sweep)<359.99f) {
                        if(i==0)Triangle(m,ca,ra*q.x+Vector3.up*(y+q.y),ra*p.x+Vector3.up*(y+p.y));
                        if(i==steps-1)Triangle(m,cb,rb*p.x+Vector3.up*(y+p.y),rb*q.x+Vector3.up*(y+q.y));
                    }
                }
            }
        }
        public Mesh Finish(string name)
        {
            var mesh=new Mesh {name=name,indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};
            mesh.SetVertices(vertices);mesh.SetNormals(normals);mesh.SetUVs(0,uv);mesh.subMeshCount=triangles.Length;
            for(int i=0;i<triangles.Length;i++)mesh.SetTriangles(triangles[i],i,false);
            mesh.RecalculateBounds();mesh.RecalculateTangents();return mesh;
        }
    }
}
