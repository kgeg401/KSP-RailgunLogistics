using System;
using System.Collections;
using System.IO;
using System.Linq;
using LmpClient;
using LmpClient.Systems.SettingsSys;
using LmpClient.Systems.Warp;
using UnityEngine;

namespace RailgunSandbox
{
    // Explicit loopback-only validation. Never connects to a public or configured server.
    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public sealed class MultiplayerValidation : MonoBehaviour
    {
        bool enabledTest, connected, tracking, flying, splitIssued;
        string originalPlayer;
        float next;
        float stationReadyAt;
        IEnumerator Start()
        {
            if (!Environment.GetCommandLineArgs().Contains("-railgun-mp-test") && !Environment.GetCommandLineArgs().Contains("-railgun-mp-test-b")) yield break;
            enabledTest = true;
            DontDestroyOnLoad(gameObject);
            while (MainSystem.Singleton == null || HighLogic.LoadedScene != GameScenes.MAINMENU) yield return null;
            yield return new WaitForSeconds(3);
            if (!SettingsSystem.CurrentSettings.DisclaimerAccepted) { Debug.LogError("[RailgunMPTest] Accept the existing Luna disclaimer before testing."); yield break; }
            originalPlayer = SettingsSystem.CurrentSettings.PlayerName;
            SettingsSystem.CurrentSettings.PlayerName = Environment.GetCommandLineArgs().Contains("-railgun-mp-test-b") ? "RG-Test-B" : "RG-Test-A";
            MainSystem.CommandLineServer = new ServerEntry { Address = "127.0.0.1", Port = 8817 };
            Application.runInBackground = true;
        }
        void Update()
        {
            if (!enabledTest || Time.realtimeSinceStartup < next) return; next = Time.realtimeSinceStartup + 1;
            string directory = Path.Combine(KSPUtil.ApplicationRootPath, "Railgun-MP-Test"); Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "status.txt"), "network=" + MainSystem.NetworkState + " scene=" + HighLogic.LoadedScene +
                " save=" + HighLogic.SaveFolder + " active=" + FlightGlobals.ActiveVessel?.vesselName + " controller=" + (SandboxFlight.Instance != null));
            if (NetworkFleet.Connected) connected = true;
            if(NetworkFleet.Shared.Ready&&SandboxFlight.Instance!=null) {
                var owned=typeof(SandboxFlight).GetField("model",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)?.GetValue(SandboxFlight.Instance) as DepotModel;
                foreach(var pod in NetworkFleet.Shared.Local.Pods.Where(p=>p.Phase==FlightPhase.Reserved)) {
                    var jaw=owned?.Loader.CradleFrame.Find("Sliding collar half 0");
                    var pin=owned?.Loader.CradleFrame.Find("Driven locking pin 0");
                    if(jaw!=null&&pin!=null) {
                        var path=Path.Combine(directory,"mechanism.tsv");
                        if(!File.Exists(path))File.WriteAllText(path,"ut\torder\tpod\tpilot\tjawX\tpinZ\n");
                        File.AppendAllText(path,FormattableString.Invariant($"{Planetarium.GetUniversalTime():R}\t{pod.OrderId}\t{pod.Number}\t{NetworkFleet.Shared.Pilot(pod.Number)}\t{jaw.localPosition.x:R}\t{pin.localPosition.z:R}\n"));
                    }
                }
            }
            if (!splitIssued && Environment.GetCommandLineArgs().Contains("-railgun-rebase-check") && NetworkFleet.Shared.Ready &&
                NetworkFleet.Shared.Local?.Pods[0].Phase == FlightPhase.Rendezvous && FlightGlobals.ActiveVessel?.vesselName == "KRL Reusable Pod 1") {
                splitIssued = true;
                Debug.Log("[RailgunMPTest] Automatic native timeline split immediately after orbit recovery; no clock, pose, velocity or resource edit.");
                WarpSystem.Singleton.CurrentSubspace = -1;
            }
            if (connected && !tracking && HighLogic.LoadedScene == GameScenes.SPACECENTER) { tracking = true; HighLogic.LoadScene(GameScenes.TRACKSTATION); }
            if (connected && !flying && HighLogic.LoadedScene == GameScenes.TRACKSTATION) {
                var vessel = FlightGlobals.Vessels.FirstOrDefault(v => v.vesselName == "Railgun Multiplayer Test Station");
                if (vessel != null && vessel.protoVessel != null && vessel.orbitDriver != null) {
                    // Luna populates tracking-station vessels asynchronously.
                    // Do not enter Flight in the same update as first discovery.
                    if(stationReadyAt==0)stationReadyAt=Time.realtimeSinceStartup+8;
                    if(Time.realtimeSinceStartup<stationReadyAt)return;
                    var game = HighLogic.CurrentGame;
                    int index = game.flightState.protoVessels.FindIndex(p => p.vesselID == vessel.id);
                    if (index < 0) { game.flightState.protoVessels.Add(vessel.BackupVessel()); index = game.flightState.protoVessels.Count - 1; }
                    flying = true; FlightDriver.StartAndFocusVessel(game, index);
                } else stationReadyAt=0;
            }
            string command = Path.Combine(directory, "command.txt");
            if (File.Exists(command)) {
                string action = File.ReadAllText(command).Trim();
                if (action == "quit") { File.Delete(command); Application.Quit(); }
                else if(action=="vessels" && NetworkFleet.Shared.Ready) {
                    File.Delete(command);
                    foreach(var pod in NetworkFleet.Shared.Local.Pods) {
                        var vessel=StockPods.Vessel(pod);
                        var report=new System.Text.StringBuilder($"pod={pod.Number} settled={StockPods.PlacementSettled(pod)} loaded={vessel?.loaded}\n");
                        if(vessel?.protoVessel!=null) {
                            foreach(var part in vessel.protoVessel.protoPartSnapshots)foreach(var module in part.modules.Where(m=>m.moduleName.Contains("Railgun")))
                                report.AppendLine($"part={part.flightID} module={module.moduleName} fleet={module.moduleValues.GetValue("fleetId")} settled={module.moduleValues.GetValue("placementSettled")}");
                            var node=new ConfigNode("VESSEL");vessel.protoVessel.Save(node);
                            File.WriteAllText(Path.Combine(directory,"pod"+pod.Number+"-native.txt"),node.ToString());
                        }
                        File.WriteAllText(Path.Combine(directory,"pod"+pod.Number+"-modules.txt"),report.ToString());
                    }
                }
                else if(action=="tracking" && NetworkFleet.Connected && HighLogic.LoadedSceneIsFlight) {
                    File.Delete(command);flying=false;stationReadyAt=0;HighLogic.LoadScene(GameScenes.TRACKSTATION);
                }
                else if((action=="audit"||action=="frame") && NetworkFleet.Connected && SandboxFlight.Instance!=null) {
                    var field=typeof(SandboxFlight).GetField("model",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
                    var model=field?.GetValue(SandboxFlight.Instance) as DepotModel;
                    if(model!=null) {
                        File.Delete(command);
                        StartCoroutine(action=="audit"?RailInspection.Audit(model,directory):RailInspection.GameFrame(Path.Combine(directory,"Multiplayer-frame.png")));
                    }
                }
                else if (action == "split-subspace" && NetworkFleet.Connected && HighLogic.LoadedSceneIsFlight) {
                    File.Delete(command);
                    Debug.Log("[RailgunMPTest] Native new-subspace recovery test; no pose, velocity, fuel or clock edit.");
                    WarpSystem.Singleton.CurrentSubspace = -1;
                }
            }
        }
        void OnApplicationQuit() { if (originalPlayer != null) SettingsSystem.CurrentSettings.PlayerName = originalPlayer; }
    }
}
