using System;
using System.Collections;
using System.IO;
using System.Linq;
using LmpClient;
using LmpClient.Systems.SettingsSys;
using UnityEngine;

namespace RailgunSandbox
{
    // Explicit loopback-only validation. Never connects to a public or configured server.
    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public sealed class MultiplayerValidation : MonoBehaviour
    {
        bool enabledTest, connected, tracking, flying;
        string originalPlayer;
        float next;
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
            if (connected && !tracking && HighLogic.LoadedScene == GameScenes.SPACECENTER) { tracking = true; HighLogic.LoadScene(GameScenes.TRACKSTATION); }
            if (connected && !flying && HighLogic.LoadedScene == GameScenes.TRACKSTATION) {
                var vessel = FlightGlobals.Vessels.FirstOrDefault(v => v.vesselName == "Railgun Multiplayer Test Station");
                if (vessel != null) {
                    var game = HighLogic.CurrentGame;
                    int index = game.flightState.protoVessels.FindIndex(p => p.vesselID == vessel.id);
                    if (index < 0) { game.flightState.protoVessels.Add(vessel.BackupVessel()); index = game.flightState.protoVessels.Count - 1; }
                    flying = true; FlightDriver.StartAndFocusVessel(game, index);
                }
            }
            string command = Path.Combine(directory, "command.txt");
            if (File.Exists(command) && File.ReadAllText(command).Trim() == "quit") { File.Delete(command); Application.Quit(); }
        }
        void OnApplicationQuit() { if (originalPlayer != null) SettingsSystem.CurrentSettings.PlayerName = originalPlayer; }
    }
}
