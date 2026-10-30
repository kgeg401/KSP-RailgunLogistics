using System;
using System.Collections.Generic;
using System.Linq;

namespace RailgunSandbox
{
    public sealed class CoastWarpMessage
    {
        public string Kind="",Epoch="",Session="",Leader="",Stage="",Reason="";
        public int Number,Subspace=-1,Destination=-1;
        public bool Enabled,Safe;
        public double StopUt;
        public List<string> Members=new List<string>();
    }
    public static class CoastWarpRules
    {
        public const string Channel="Kgeg4.RailgunCoastWarp.v1";
        public const double MaximumRate=100;
        public static bool PhaseAllowed(FlightPhase phase)=>phase==FlightPhase.Rendezvous||phase==FlightPhase.Landing;
        public static bool Envelope(FlightPhase phase,bool active,bool authority,bool orbitSafe,bool thrustOff,bool nativeCoast,bool nearReceiver,double secondsToEvent)
            =>PhaseAllowed(phase)&&active&&authority&&orbitSafe&&thrustOff&&nativeCoast&&!nearReceiver&&FlightRules.Finite(secondsToEvent)&&secondsToEvent>30;
    }
    // Ephemeral coordination only: no vessel, fuel, flight-phase or clock edits.
    // All authenticated peers must opt in and acknowledge this exact session.
    public sealed class CoastWarpCoordinator
    {
        sealed class Presence { public CoastWarpMessage Message;public long At; }
        readonly Dictionary<string,Presence> peers=new Dictionary<string,Presence>();
        readonly HashSet<string> acknowledgements=new HashSet<string>();
        public CoastWarpMessage State {get;private set;}=new CoastWarpMessage {Kind="State",Stage="Idle",Reason="Auto warp ready"};
        long started,changed;
        static bool SameMembers(IEnumerable<string> a,IEnumerable<string> b)=>a.OrderBy(x=>x).SequenceEqual(b.OrderBy(x=>x));
        bool Ready(string name,int space,long now)=>peers.TryGetValue(name,out var p)&&now-p.At>=0&&now-p.At<=3&&p.Message.Enabled&&p.Message.Safe&&p.Message.Subspace==space;
        public void Receive(string player,CoastWarpMessage message,IEnumerable<string> authenticated,string epoch,long now,bool pilotAuthorized)
        {
            var members=authenticated.OrderBy(x=>x).ToArray();
            if(!members.Contains(player)||message==null||message.Epoch!=epoch)return;
            if(message.Kind=="Presence")peers[player]=new Presence {Message=message,At=now};
            bool busy=State.Stage!="Idle";
            if(busy&&(!SameMembers(members,State.Members)||State.Epoch!=epoch))Stop("Player list changed; slowing down",now);
            if(message.Kind=="Request"&&State.Stage=="Idle") {
                if(!pilotAuthorized||message.Subspace<0||!FlightRules.Finite(message.StopUt)||message.StopUt<=0||!members.All(p=>Ready(p,message.Subspace,now))) {
                    State.Reason="Waiting for every player to enable auto warp and be in safe flight";return;
                }
                State=new CoastWarpMessage {Kind="State",Epoch=epoch,Stage="Preparing",Session=Guid.NewGuid().ToString("N"),Leader=player,Number=message.Number,
                    Subspace=message.Subspace,StopUt=message.StopUt,Members=members.ToList(),Reason="Waiting for warp acknowledgements"};
                acknowledgements.Clear();started=changed=now;
            }
            if(State.Stage=="Idle"||message.Session!=State.Session) {Tick(members,now);return;}
            if(message.Kind=="Ack"&&State.Stage=="Preparing"&&Ready(player,State.Subspace,now)) {
                acknowledgements.Add(player);
                if(State.Members.All(acknowledgements.Contains)) {State.Stage="Warping";State.Reason="Fast-forwarding orbital coast";changed=now;}
            }
            if(message.Kind=="Cancel")Stop("Auto warp stopped by a player",now);
            if(message.Kind=="Finished"&&player==State.Leader&&(State.Stage=="Warping"||State.Stage=="Stopping")&&message.Destination>=0) {
                State.Destination=message.Destination;State.Stage="Synchronizing";State.Reason="Synchronizing players with the pod";changed=now;
            }
            if(message.Kind=="Done"&&player==State.Leader&&State.Stage=="Synchronizing"&&State.Members.All(p=>peers.TryGetValue(p,out var v)&&now-v.At<=3&&v.Message.Subspace==State.Destination)) {
                State=new CoastWarpMessage {Kind="State",Stage="Idle",Epoch=epoch,Reason="Coast complete; guidance resumed"};acknowledgements.Clear();
            }
            Tick(members,now);
        }
        void Stop(string reason,long now)
        {
            if(State.Stage=="Idle")return;
            if(State.Stage=="Preparing") {State=new CoastWarpMessage {Kind="State",Stage="Idle",Epoch=State.Epoch,Reason=reason};acknowledgements.Clear();return;}
            if(State.Stage=="Warping") {State.Stage="Stopping";State.Reason=reason;changed=now;}
        }
        public void Tick(IEnumerable<string> authenticated,long now)
        {
            if(State.Stage=="Idle")return;
            if(!SameMembers(authenticated,State.Members))Stop("Player list changed; slowing down",now);
            if(State.Stage=="Preparing"&&(now-started>3||!State.Members.All(p=>Ready(p,State.Subspace,now))))Stop("Warp preparation expired",now);
            if(State.Stage=="Warping"&&(now-changed>=8||State.Members.Any(p=>!peers.TryGetValue(p,out var v)||now-v.At>3||!v.Message.Enabled||!v.Message.Safe)))Stop("Coast pulse complete; slowing down",now);
            if(State.Stage=="Synchronizing"&&now-changed>15)State.Reason="Waiting at 1× for players to synchronize";
        }
    }
}
