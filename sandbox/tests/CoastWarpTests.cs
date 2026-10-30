using System;
using RailgunSandbox;

int checks=0;
void Check(bool value,string message){if(!value)throw new Exception(message);checks++;}
string epoch=Guid.NewGuid().ToString("N");string[] players={"A","B"};
CoastWarpMessage Presence(bool enabled=true,bool safe=true,int space=0)=>new CoastWarpMessage {Kind="Presence",Epoch=epoch,Enabled=enabled,Safe=safe,Subspace=space};
CoastWarpMessage Request()=>new CoastWarpMessage {Kind="Request",Epoch=epoch,Number=1,Subspace=0,StopUt=1000};
var c=new CoastWarpCoordinator();
c.Receive("A",Presence(),players,epoch,100,false);c.Receive("B",Presence(false),players,epoch,100,false);
c.Receive("A",Request(),players,epoch,100,true);Check(c.State.Stage=="Idle","Another player cannot be fast-forwarded without opting in");
c.Receive("B",Presence(),players,epoch,100,false);c.Receive("A",Request(),players,epoch,104,true);Check(c.State.Stage=="Idle","Stale consent cannot start a new coast");
c.Receive("A",Presence(),players,epoch,105,false);c.Receive("B",Presence(),players,epoch,105,false);
c.Receive("A",Request(),players,epoch,105,false);Check(c.State.Stage=="Idle","Warp requires the native pod pilot and live authority");
c.Receive("A",Request(),players,epoch,105,true);Check(c.State.Stage=="Preparing","All opted-in peers still require session acknowledgements");
string session=c.State.Session;
var ack=new CoastWarpMessage {Kind="Ack",Epoch=epoch,Session="wrong"};c.Receive("A",ack,players,epoch,105,false);Check(c.State.Stage=="Preparing","A stale session cannot acknowledge this coast");
ack.Session=session;c.Receive("A",ack,players,epoch,105,false);Check(c.State.Stage=="Preparing","One acknowledgement cannot move two players");
c.Receive("B",ack,players,epoch,105,false);Check(c.State.Stage=="Warping","Matching acknowledgements start the bounded coast");
c.Receive("A",Presence(space:-1),players,epoch,110,false);c.Receive("B",Presence(),players,epoch,110,false);c.Tick(players,113);
Check(c.State.Stage=="Stopping","The coast pulse has a wall-clock limit");
c.Receive("B",new CoastWarpMessage {Kind="Finished",Epoch=epoch,Session=session,Destination=2},players,epoch,113,false);Check(c.State.Stage=="Stopping","A follower cannot choose the destination timeline");
c.Receive("A",new CoastWarpMessage {Kind="Finished",Epoch=epoch,Session=session,Destination=2},players,epoch,113,false);Check(c.State.Stage=="Synchronizing","The leader reports its native destination after slowing down");
c.Receive("A",Presence(space:2),players,epoch,114,false);
var done=new CoastWarpMessage {Kind="Done",Epoch=epoch,Session=session};c.Receive("A",done,players,epoch,114,false);Check(c.State.Stage=="Synchronizing","Guidance cannot resume while a peer remains on the old timeline");
c.Receive("B",Presence(space:2),players,epoch,114,false);c.Receive("A",done,players,epoch,114,false);Check(c.State.Stage=="Idle","Shared guidance resumes only after all players arrive");
foreach(FlightPhase phase in Enum.GetValues<FlightPhase>())
 if(phase!=FlightPhase.Rendezvous&&phase!=FlightPhase.Landing)Check(!CoastWarpRules.Envelope(phase,true,true,true,true,true,false,100),"Protected phase stays at 1x: "+phase);
Check(!CoastWarpRules.Envelope(FlightPhase.Rendezvous,true,true,true,true,true,true,100),"Close receiver approach cannot auto warp");
Check(!CoastWarpRules.Envelope(FlightPhase.Landing,true,true,true,true,true,false,30),"Maneuver lead time retains a 1x margin");
Check(!CoastWarpRules.Envelope(FlightPhase.Landing,true,true,true,true,true,false,double.NaN),"Missing event timing cannot auto warp");
Check(!CoastWarpRules.Envelope(FlightPhase.Landing,true,true,false,true,true,false,100),"Atmospheric or unsafe orbit cannot auto warp");
Check(CoastWarpRules.Envelope(FlightPhase.Landing,true,true,true,true,true,false,100),"Safe native orbital coast is eligible");
Console.WriteLine($"PASS {checks} coast-warp consent, phase, expiry and synchronization checks. Native warp remains a separate test.");
