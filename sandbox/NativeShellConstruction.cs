using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace RailgunSandbox
{
    public static class NativeShellConstruction
    {
        static readonly HashSet<Part> pending = new HashSet<Part>();
        static bool installed;
        public static void Register(Part part)
        {
            if (!installed) {
                var method = AccessTools.Method(typeof(ModuleProceduralFairing), "OnStart", new[] { typeof(PartModule.StartState) });
                if (method == null) throw new InvalidOperationException("Installed native fairing construction API is incompatible.");
                new Harmony("local.railgun.shell-construction").Patch(method,
                    postfix: new HarmonyMethod(typeof(NativeShellConstruction), nameof(Started)));
                installed = true;
            }
            pending.Add(part);
        }
        static void Started(ModuleProceduralFairing __instance)
        {
            var part = __instance.part;
            if (part == null || !pending.Remove(part) || part.vessel == null) return;
            // OnAttachFlight excludes the colliders present at attachment time.
            // Native fairing OnStart creates its panels afterwards. Apply the
            // same native vessel collision rule to those newly built panels;
            // world/terrain and foreign-vessel collisions remain native.
            if (!part.sameVesselCollision) CollisionManager.IgnoreCollidersOnVessel(part.vessel, part.GetPartColliders());
            part.SetCollisionIgnores();
            Debug.Log("[RailgunSandbox] Native replacement shell panels initialized; vessel collision exclusions refreshed for " + part.flightID);
        }
    }
}
