using System;
using System.IO;
using System.Linq;
using RailgunLogistics;
using Server.Client;
using Server.Context;
using Server.Log;
using Server.Plugin;
using Server.System;
using LmpCommon.Locks;
using LmpCommon.Message.Data;
using LmpCommon.Message.Server;
using Server.Server;

namespace RailgunLogisticsServer
{
    public sealed class LogisticsPlugin : LmpPlugin
    {
        readonly object gate=new object();
        Depot depot;
        public override void OnServerStart()
        {
            try
            {
                string dir=Path.Combine(ServerContext.UniverseDirectory,"RailgunLogistics");
                Directory.CreateDirectory(dir);
                var options=XmlFile.Read<Options>(Path.Combine(dir,"Settings.xml"));
                options.Validate();
                if(!File.Exists(Path.Combine(dir,"Settings.xml")))XmlFile.Save(Path.Combine(dir,"Settings.xml"),options);
                string ledger=Path.Combine(dir,"Orders.xml");
                depot=new Depot(XmlFile.Read<DepotState>(ledger),options,s=>XmlFile.Save(ledger,s));
                if(!LmpModInterface.RegisterModHandler(Rules.Channel,Receive))throw new InvalidOperationException("Duplicate mod handler");
                LunaLog.Normal("[RailgunLogistics] v0.2.0 ready; persistent physical cargo pods enabled.");
            }
            catch(Exception ex){depot=null;LunaLog.Error("[RailgunLogistics] Disabled: "+ex);}
        }
        public override void OnServerStop(){LmpModInterface.UnregisterModHandler(Rules.Channel);}
        static bool Owns(ClientStructure client,Guid vessel)=>LockSystem.LockQuery.ControlLockBelongsToPlayer(vessel,client.PlayerName);
        static Server.System.Vessel.Classes.Part Receiver(Guid vessel,uint anchor)
        {
            if(!VesselStoreSystem.CurrentVessels.TryGetValue(vessel,out var v))return null;
            return v.Parts.GetAllValues().FirstOrDefault(p=>p.Fields.GetSingle("uid")?.Value==anchor.ToString() &&
                p.Modules.GetAll().Any(m=>m.Key=="RailgunDeliveryReceipt"));
        }
        static bool Receipt(Guid vessel,Order order)
        {
            var part=Receiver(vessel,order.Anchor);
            var mod=part?.Modules.GetAll().FirstOrDefault(m=>m.Key=="RailgunDeliveryReceipt")?.Value;
            if(!Rules.HasReceipt(mod?.GetValue("receipts")?.Value,order.Id))return false;
            return VesselStoreSystem.CurrentVessels[vessel].Parts.GetAllValues().Any(p=>
            {
                var cargo=p.Modules.GetAll().FirstOrDefault(m=>m.Key=="RailgunCargoPod")?.Value;
                var dock=p.Modules.GetAll().FirstOrDefault(m=>m.Key=="ModuleDockingNode")?.Value;
                return cargo?.GetValue("orderId")?.Value==order.Id && cargo.GetValue("unloaded")?.Value=="True" &&
                    dock?.GetValue("dockUId")?.Value==order.Port.ToString() && (dock.GetValue("state")?.Value??"").StartsWith("Docked");
            });
        }
        void Send(ClientStructure client,Packet packet)
        {
            // Luna 0.29.2's convenience sender omits NumBytes. Set it explicitly.
            byte[] bytes=Wire.Encode(packet);
            var data=ServerContext.ServerMessageFactory.CreateNewMessageData<ModMsgData>();
            data.ModName=Rules.Channel;data.Data=bytes;data.NumBytes=bytes.Length;data.Reliable=true;data.Relay=false;
            MessageQueuer.SendToClient<ModSrvMsg>(client,data);
        }
        void Snapshot(ClientStructure client)=>Send(client,new Packet {Kind="State", ServerNow=Rules.Now,Settings=depot.Settings,
            Orders=depot.State.Orders.Where(o=>o.Status!="Delivered" && o.Status!="Cancelled")
                .Concat(depot.State.Orders.Where(o=>o.Status=="Delivered" || o.Status=="Cancelled").Reverse().Take(20)).Take(150).ToList()});
        void Receive(ClientStructure client,byte[] bytes,int count)
        {
            lock(gate)
            {
                try
                {
                    if(depot==null)throw new IOException("Logistics service unavailable");
                    var message=Wire.Decode(bytes,count);
                    if(message.Kind=="List"){Snapshot(client);return;}
                    var request=message.Order;
                    if(request==null)throw new InvalidDataException("Missing dispatch");
                    if(message.Kind=="Cancel")depot.Cancel(request.Id,client.PlayerName);
                    else
                    {
                        if(!Guid.TryParse(request.VesselId,out var vessel) || !Owns(client,vessel))
                            throw new InvalidDataException("Take control of the receiving ship first");
                        if(Receiver(vessel,request.Anchor)==null)throw new InvalidDataException("Receiver not synced yet; wait and retry");
                        if(message.Kind=="Order")
                        {
                            request.Owner=client.PlayerName;
                            // Body and available resource names are verified against the vessel snapshot.
                            var stored=VesselStoreSystem.CurrentVessels[vessel];
                            request.Body=stored.GetOrbitingBodyName();
                            request.VesselName=stored.Fields.GetSingle("name")?.Value??"Ship";
                            if(!stored.Parts.GetAllValues().Any(p=>p.Fields.GetSingle("uid")?.Value==request.Port.ToString() &&
                                p.Modules.GetAll().Any(m=>m.Key=="ModuleDockingNode" && m.Value.GetValue("state")?.Value=="Ready")))
                                throw new InvalidDataException("The selected docking port is not available in the server snapshot.");
                            foreach(var cargo in request.Cargo)
                                if(!stored.Parts.GetAllValues().Any(p=>p.Resources.GetAll().Any(r=>r.Key==cargo.Resource)))
                                    throw new InvalidDataException("The ship has no tank for "+cargo.Resource);
                            var order=depot.Submit(request,Rules.Now);
                            LunaLog.Normal("[RailgunLogistics] Dispatch "+order.Id+" to "+order.VesselName+" by "+client.PlayerName);
                        }
                        else if(message.Kind=="Claim")
                        {
                            var o=depot.Claim(request.Id,client.PlayerName,request.VesselId,request.Anchor,Rules.Now);
                            if(o!=null)Send(client,new Packet {Kind="Grant",Order=o,ServerNow=Rules.Now});
                        }
                        else if(message.Kind=="Pod")
                        {
                            var o=depot.Get(request.Id);
                            if(o==null || o.Owner!=client.PlayerName || o.Anchor!=request.Anchor || o.PodId!=request.PodId ||
                                !Guid.TryParse(request.PodId,out var podId))throw new InvalidDataException("Pod does not match dispatch");
                            if(o.PodSpawned){Snapshot(client);return;}
                            if(!VesselStoreSystem.CurrentVessels.TryGetValue(podId,out var pod))throw new IOException("Waiting for cargo pod snapshot");
                            if(!pod.Parts.GetAllValues().Any(p=>p.Modules.GetAll().Any(m=>m.Key=="RailgunCargoPod" &&
                                m.Value.GetValue("orderId")?.Value==o.Id && m.Value.GetValue("receiverAnchor")?.Value==o.Anchor.ToString() &&
                                m.Value.GetValue("portId")?.Value==o.Port.ToString())))throw new InvalidDataException("Cargo pod identity mismatch");
                            VesselStoreSystem.BackupVessels();
                            string podPath=Path.Combine(VesselStoreSystem.VesselsPath,podId+VesselStoreSystem.VesselFileFormat);
                            if(!File.ReadAllText(podPath).Contains(o.Id))throw new IOException("Waiting for durable cargo pod snapshot");
                            depot.ConfirmPod(o.Id,true);
                            LunaLog.Normal("[RailgunLogistics] Physical pod persisted "+o.Id+" as "+podId);
                        }
                        else if(message.Kind=="Receipt")
                        {
                            var o=depot.Get(request.Id);
                            if(o==null || o.Owner!=client.PlayerName || o.Anchor!=request.Anchor || o.VesselId!=request.VesselId)
                                throw new InvalidDataException("Receipt does not match dispatch");
                            if(Receipt(vessel,o))
                            {
                                // Persist the standard snapshot (resources + receipt) before committing the queue.
                                VesselStoreSystem.BackupVessels();
                                // Verify disk too, not merely a volatile cache entry.
                                var path=Path.Combine(VesselStoreSystem.VesselsPath,vessel+VesselStoreSystem.VesselFileFormat);
                                string disk=File.ReadAllText(path);
                                if(!disk.Contains(request.Id))throw new IOException("Vessel receipt not durable yet");
                                depot.Confirm(o.Id,true,request.Delivered);
                                LunaLog.Normal("[RailgunLogistics] Confirmed "+o.Id+" on "+o.VesselName);
                            }
                        }
                        else throw new InvalidDataException("Unknown logistics request");
                    }
                    Snapshot(client);
                }
                catch(Exception ex)
                {
                    LunaLog.Debug("[RailgunLogistics] "+ex.Message);
                    Send(client,new Packet {Kind="Error",Error=ex.Message,ServerNow=Rules.Now});
                }
            }
        }
    }
}
