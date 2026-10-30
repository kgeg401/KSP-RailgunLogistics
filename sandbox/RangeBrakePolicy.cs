using System;

namespace RailgunSandbox
{
    public static class RangeBrakePolicy
    {
        // Native deployment-angle command, in degrees. Positive range error
        // means impact is short: reduce braking. Never increase the stock range.
        public static double Next(double angle,double maximum,double signedMiss,double predictedBodyHeat)
        {
            foreach(double value in new[]{angle,maximum,signedMiss,predictedBodyHeat})
                if(!FlightRules.Finite(value))throw new ArgumentException("Invalid native range-braking sample");
            if(maximum<=0||angle<0||angle>maximum||predictedBodyHeat<0)throw new ArgumentException("Invalid native range-braking envelope");
            if(predictedBodyHeat>=.70)return maximum;
            if(Math.Abs(signedMiss)<=250)return angle;
            return Math.Max(0,Math.Min(maximum,angle-Math.Max(-5,Math.Min(5,signedMiss/500))));
        }
    }
}
