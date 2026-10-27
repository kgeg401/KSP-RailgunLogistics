using System;
using System.Collections;
using UnityEngine;

namespace RailgunLogistics
{
    public sealed class RailgunLaunchTerminal : PartModule
    {
        GameObject rig;
        [KSPEvent(guiActive=true,guiName="Open Kerbin logistics",active=true)]
        public void OpenLogistics(){Logistics.Instance?.Open();}
        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            if(rig!=null)return;
            rig=new GameObject("Railgun rails");rig.transform.SetParent(part.transform,false);
            Shape("Base",PrimitiveType.Cube,new Vector3(0,.05f,0),new Vector3(2.8f,.3f,6),new Color(.15f,.19f,.23f));
            for(int side=-1;side<=1;side+=2)
            {
                Shape("Rail",PrimitiveType.Cube,new Vector3(side*.8f,.55f,0),new Vector3(.24f,.7f,6),new Color(.3f,.42f,.5f));
                Shape("Coil",PrimitiveType.Cube,new Vector3(side*.64f,.9f,0),new Vector3(.08f,.08f,5.8f),new Color(.15f,.8f,1));
            }
            Shape("Cargo cradle",PrimitiveType.Cube,new Vector3(0,.5f,-1.8f),new Vector3(1,.6f,.8f),new Color(.8f,.65f,.2f));
        }
        void Shape(string name,PrimitiveType type,Vector3 pos,Vector3 scale,Color color)
        {
            var obj=GameObject.CreatePrimitive(type);obj.name=name;obj.transform.SetParent(rig.transform,false);
            obj.transform.localPosition=pos;obj.transform.localScale=scale;
            Destroy(obj.GetComponent<Collider>());
            var mat=new Material(Shader.Find("KSP/Diffuse")??Shader.Find("Standard"));mat.color=color;
            obj.GetComponent<Renderer>().material=mat;
        }
        void OnDestroy()
        {
            if(rig==null)return;
            foreach(var renderer in rig.GetComponentsInChildren<Renderer>())Destroy(renderer.material);
            Destroy(rig);
        }
    }
}
