namespace RailgunSandbox
{
    public static class LunaDockingPolicy
    {
        // Network replication envelope, not the stock magnetic capture range.
        // Native full-receiver evidence measured31m of peer pose divergence at
        // the real capture event. Exact IDs/ownership/phase are checked by caller.
        public static bool CaptureEnvelope(double age,double metres,double facing,double relativeSpeed)
            =>FlightRules.Finite(age)&&FlightRules.Finite(metres)&&FlightRules.Finite(facing)&&FlightRules.Finite(relativeSpeed)&&
                age>=-.1&&age<=5&&metres>=0&&metres<=100&&facing>=.95&&facing<=1.001&&relativeSpeed>=0&&relativeSpeed<=3;
        public static bool EmptyCargoSnapshot(bool serverConfirmed,bool nativeReceiptMatches,double received,bool receivedFlow,
            double local,bool localFlow,double manifestUnits)
            =>serverConfirmed&&nativeReceiptMatches&&received==0&&!receivedFlow&&!localFlow&&
                FlightRules.Finite(local)&&local>=0&&FlightRules.Finite(manifestUnits)&&manifestUnits>0&&local<=manifestUnits+1e-6;
    }
}
