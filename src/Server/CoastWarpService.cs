using System;
using System.Linq;
using LmpCommon.Message.Data;
using LmpCommon.Message.Server;
using RailgunLogistics;
using RailgunSandbox;
using Server.Client;
using Server.Context;
using Server.Plugin;
using Server.Server;
using Server.System;

namespace RailgunLogisticsServer
{
    public sealed class CoastWarpService
    {
        readonly object gate;
        readonly CoastWarpCoordinator coordinator=new CoastWarpCoordinator();
        readonly Func<SharedFleet> fleet;
        readonly Func<ClientStructure,int,bool> authorized;
        public CoastWarpService(object authorityGate,Func<SharedFleet> snapshot,Func<ClientStructure,int,bool> pilot){gate=authorityGate;fleet=snapshot;authorized=pilot;}
        public void Start(){if(!LmpModInterface.RegisterModHandler(CoastWarpRules.Channel,Receive))throw new InvalidOperationException("Duplicate coast-warp service");}
        public void Stop()=>LmpModInterface.UnregisterModHandler(CoastWarpRules.Channel);
        void Receive(ClientStructure client,byte[] bytes,int count)
        {
            if(client==null||!client.Authenticated||bytes==null||count<1||count>8192||count>bytes.Length)return;
            lock(gate)try {
                var message=FleetWire.Read<CoastWarpMessage>(new System.Text.UTF8Encoding(false,true).GetString(bytes,0,count));
                if(message.Kind=="Presence")message.Subspace=client.Subspace;
                if(message.Kind=="Request")message.Subspace=client.Subspace;
                if(message.Kind=="Finished"&&message.Destination!=client.Subspace)return;
                var peers=ClientRetriever.GetAuthenticatedClients().ToArray();var state=fleet();
                bool permitted=message.Kind=="Request"&&authorized(client,message.Number);
                coordinator.Receive(client.PlayerName,message,peers.Select(c=>c.PlayerName),state.Epoch,Rules.Now,permitted);
                string text=FleetWire.Text(coordinator.State);byte[] encoded=System.Text.Encoding.UTF8.GetBytes(text);
                foreach(var peer in peers) {
                    var data=ServerContext.ServerMessageFactory.CreateNewMessageData<ModMsgData>();
                    data.ModName=CoastWarpRules.Channel;data.Data=encoded;data.NumBytes=encoded.Length;data.Reliable=true;data.Relay=false;
                    MessageQueuer.SendToClient<ModSrvMsg>(peer,data);
                }
            } catch(Exception e) {Server.Log.LunaLog.Debug("[RailgunCoastWarp] "+e.Message);}
        }
    }
}
