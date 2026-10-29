using UnityEngine;

namespace RailgunSandbox
{
    // Apply the anchor after vessel/render interpolation and before the stock
    // flight camera's LateUpdate. The object stays outside depot culling.
    [DefaultExecutionOrder(-50)]
    public sealed class InspectionTargetFollower : MonoBehaviour
    {
        public Transform Reference;
        public Vector3 LocalPoint;
        void LateUpdate()
        {
            if(Reference==null)return;
            transform.position=Reference.TransformPoint(LocalPoint);
            var camera=FlightCamera.fetch;
            // A native vessel/world shift may translate the persistent camera
            // pivot independently of our custom target. Its Transform target
            // anchor is zero; remove that drift only while we own its parent.
            if(camera!=null&&camera.GetPivot()!=null&&camera.GetPivot().parent==transform)camera.GetPivot().localPosition=Vector3.zero;
        }
    }
}
