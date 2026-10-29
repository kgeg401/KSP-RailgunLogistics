using System;

namespace RailgunSandbox
{
    public static class GantrySafety
    {
        // Keep the entire tower/diagonal interval outside the horizontal
        // projection of the launch cylinder, including the 1.4 m tower width.
        public static double HalfDepth(double east,double north,double bore,double baseline)
        {
            if(!FlightRules.Finite(east)||!FlightRules.Finite(north)||!FlightRules.Finite(bore)||!FlightRules.Finite(baseline)||bore<=0||baseline<=0)throw new InvalidOperationException("Invalid gantry clearance inputs.");
            double magnitude=Math.Sqrt(east*east+north*north);
            if(magnitude<1e-6)return baseline;
            east=Math.Abs(east/magnitude);north=Math.Abs(north/magnitude);
            double point=baseline*east,lo=39*north,hi=46*north;
            double distance=point<lo?lo-point:point>hi?point-hi:0;
            if(distance>=bore+1.1)return baseline;
            return Math.Ceiling(((hi+bore+1.1)/east+.5)*2)/2;
        }
    }
}
