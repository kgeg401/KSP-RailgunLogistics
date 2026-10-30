using System;

namespace RailgunSandbox
{
    public static class AirbrakePolicy
    {
        // Engineering envelope, not a change to stock drag, actuator speed or
        // heat limits. Fold all four symmetrically before an exposed panel heats.
        public static bool Deploy(FlightPhase phase,bool previouslyOpen,double altitude,double verticalSpeed,double surfaceSpeed,
            double pressureKpa,double predictedExposedHeatRatio)
        {
            foreach(double value in new[]{altitude,verticalSpeed,surfaceSpeed,pressureKpa,predictedExposedHeatRatio})
                if(!FlightRules.Finite(value))return false;
            // Open in orbital return preparation, so the forecast can use the
            // actual settled flap pose before deorbit. Stock panels have no
            // specified dynamic-pressure cutoff; retain their real heat/load physics.
            if(phase!=FlightPhase.Landing||altitude<1500||surfaceSpeed<150||pressureKpa<0||predictedExposedHeatRatio<0)return false;
            return predictedExposedHeatRatio < (previouslyOpen ? .78 : .60);
        }
    }
}
