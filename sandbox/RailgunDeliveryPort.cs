using System;
using System.Linq;
using UnityEngine;

namespace RailgunSandbox
{
    // Native Part Action Window entry, attached to ports by ModuleManager.
    // Selecting a receiver never dispatches a pod or substitutes another port.
    public sealed class RailgunDeliveryPort : PartModule
    {
        [KSPEvent(guiActive = true, guiName = "Request pod delivery here", active = true)]
        public void RequestDelivery()
        {
            var controller = SandboxFlight.Instance;
            if (controller == null) return;
            try { controller.SelectDeliveryPort(part); }
            catch (Exception error) { ScreenMessages.PostScreenMessage(error.Message, 7, ScreenMessageStyle.UPPER_CENTER); }
        }
        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            bool allowed = HighLogic.LoadedSceneIsFlight && HighLogic.SaveFolder == SandboxFlight.SaveName &&
                vessel != null && !vessel.parts.Any(p => p.FindModuleImplementing<RailgunFlightPod>() != null);
            Events[nameof(RequestDelivery)].active = allowed;
            Events[nameof(RequestDelivery)].guiActive = allowed;
        }
    }
}
