using System;
using System.Collections.Generic;
using System.Linq;

namespace RailgunSandbox
{
    public struct AeroKnot
    {
        public float Time, Value, InTangent, OutTangent;
        public AeroKnot(float time,float value,float incoming,float outgoing) { Time=time;Value=value;InTangent=incoming;OutTangent=outgoing; }
    }

    // Immutable unweighted Hermite data. Worker forecasts never access a live
    // Unity Transform, PartModule or mutable AnimationCurve.
    public sealed class AeroCurve
    {
        readonly AeroKnot[] keys;
        public AeroCurve(IEnumerable<AeroKnot> values)
        {
            keys=values.ToArray();
            for(int i=0;i<keys.Length;i++) {
                var k=keys[i];
                if(!FlightRules.Finite(k.Time)||!FlightRules.Finite(k.Value)||float.IsNaN(k.InTangent)||float.IsNaN(k.OutTangent)||
                    i>0&&k.Time<=keys[i-1].Time)throw new ArgumentException("Unsupported aerodynamic curve keys");
            }
        }
        public float Evaluate(float value)
        {
            if(!FlightRules.Finite(value))throw new ArgumentException("Invalid aerodynamic curve input");
            if(keys.Length==0)return 0;
            if(value<=keys[0].Time)return keys[0].Value;
            if(value>=keys[keys.Length-1].Time)return keys[keys.Length-1].Value;
            for(int i=1;i<keys.Length;i++)if(value<keys[i].Time) {
                var a=keys[i-1];var b=keys[i];
                if(float.IsInfinity(a.OutTangent)||float.IsInfinity(b.InTangent))return a.Value;
                float span=b.Time-a.Time,t=(value-a.Time)/span,t2=t*t,t3=t2*t;
                return (2*t3-3*t2+1)*a.Value+(t3-2*t2+t)*span*a.OutTangent+(-2*t3+3*t2)*b.Value+(t3-t2)*span*b.InTangent;
            }
            throw new InvalidOperationException("Aerodynamic curve interval missing");
        }
    }
}
