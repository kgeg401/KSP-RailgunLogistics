using System;
using System.Collections.Generic;
using UnityEngine;

namespace RailgunSandbox
{
    // Facility artwork is ours. Flight pods are exclusively assemblies of installed KSP parts.
    public sealed class DepotModel : IDisposable
    {
        public readonly GameObject Root;
        public readonly LoadingMechanism Loader;
        readonly Transform rail;
        readonly double guideLength, guideElevation;
        readonly List<Material> materials = new List<Material>();
        Material steel, dark, concrete, copper, cyan, white;
        public DepotModel(bool experimentalRailOnly = false)
        {
            guideLength = experimentalRailOnly ? RailLaunch.Length : 112;
            guideElevation = experimentalRailOnly ? RailLaunch.ElevationDegrees : 90;
            Root = new GameObject("KRL Rail-Only Mass Driver / Offline Sandbox");
            rail = new GameObject("Angled electromagnetic launch guide").transform;
            rail.SetParent(Root.transform, false);
            steel = Material(new Color(.38f, .43f, .48f)); dark = Material(new Color(.09f, .12f, .15f));
            concrete = Material(new Color(.39f, .4f, .39f)); copper = Material(new Color(.65f, .33f, .12f));
            cyan = Material(new Color(.1f, .72f, .85f)); white = Material(new Color(.86f, .86f, .8f));
            Box("Foundation", new Vector3(0, -.5f, 0), new Vector3(16, 1, 18), concrete, true);
            for (int side = -1; side <= 1; side += 2) {
                float length = (float)guideLength;
                Box("Accelerator rail", new Vector3(side * 1.8f, length / 2, 0), new Vector3(.45f, length, .6f), steel, false, rail);
                Box("Inner energized rail", new Vector3(side * 1.52f, length / 2, 0), new Vector3(.12f, length, .2f), cyan, false, rail);
                for (int n = 0; n < 64; n++) {
                    Box("Copper coil bank", new Vector3(side * 2.35f, (n + .5f) * length / 64, 0), new Vector3(.8f, 2.2f, 1.4f), copper, false, rail);
                }
            }
            ConfigureRail(28, 90);
            Box("Launch cradle deck", new Vector3(0, 1, 0), new Vector3(2.6f, 1, 3), dark, false);
            Box("Control building", new Vector3(-13, 2.2f, -7), new Vector3(8, 4.4f, 6), white, true);
            Box("Control room windows", new Vector3(-13, 3, -3.95f), new Vector3(6.5f, 1.1f, .08f), cyan, false);
            for (int i = 0; i < 7; i++) Box("Capacitor bank", new Vector3(-9 + i * 3, 1.1f, -13), new Vector3(2, 2.2f, 3), dark, true);
            for (int number = 1; number <= 2; number++) {
                float x = PadX(number);
                Box("Pod " + number + " landing pad", new Vector3(x, .03f, 6), new Vector3(20, .06f, 20), concrete, false);
                Box("Landing centre stripe", new Vector3(x, .075f, 6), new Vector3(12, .02f, .5f), white, false);
                Box("Landing centre stripe", new Vector3(x, .075f, 6), new Vector3(.5f, .02f, 12), white, false);
                for (int side = -1; side <= 1; side += 2)
                    Box("Pad edge marker", new Vector3(x + side * 8, .1f, 6), new Vector3(.3f, .1f, 16), cyan, false);
                Lamp(new Vector3(x, 22, -8), new Vector3(x, 0, 6));
            }
            Lamp(new Vector3(-12, 45, 10), new Vector3(0, 55, 0));
            Lamp(new Vector3(12, 90, 10), new Vector3(0, 80, 0));
            Loader = new LoadingMechanism(Root.transform);
        }
        public static float PadX(int number) => number == 1 ? -36 : 36;
        Material Material(Color color) {
            var m = new Material(Shader.Find("KSP/Bumped Specular") ?? Shader.Find("KSP/Diffuse") ?? Shader.Find("Standard"));
            m.color = color; materials.Add(m); return m;
        }
        public void ConfigureRail(double cradleHeight, double heading) {
            double h = heading * Math.PI / 180, p = guideElevation * Math.PI / 180;
            var axis = new Vector3((float)(Math.Sin(h) * Math.Cos(p)), (float)Math.Sin(p), (float)(Math.Cos(h) * Math.Cos(p)));
            rail.localPosition = new Vector3(0, (float)cradleHeight, 0);
            rail.localRotation = Quaternion.FromToRotation(Vector3.up, axis);
        }
        void Box(string name, Vector3 position, Vector3 scale, Material material, bool collider, Transform parent = null) {
            var obj = GameObject.CreatePrimitive(PrimitiveType.Cube); obj.name = name;
            obj.transform.SetParent(parent ?? Root.transform, false); obj.transform.localPosition = position; obj.transform.localScale = scale;
            obj.GetComponent<Renderer>().sharedMaterial = material;
            if (!collider) UnityEngine.Object.Destroy(obj.GetComponent<Collider>());
        }
        void Beam(string name, Vector3 a, Vector3 b, float thickness, Material material) {
            var obj = GameObject.CreatePrimitive(PrimitiveType.Cube); obj.name = name; obj.transform.SetParent(Root.transform, false);
            obj.transform.localPosition = (a + b) / 2; obj.transform.localRotation = Quaternion.FromToRotation(Vector3.up, b - a);
            obj.transform.localScale = new Vector3(thickness, (b - a).magnitude, thickness); obj.GetComponent<Renderer>().sharedMaterial = material;
            UnityEngine.Object.Destroy(obj.GetComponent<Collider>());
        }
        void Lamp(Vector3 position, Vector3 target) {
            var obj = new GameObject("Depot floodlight"); obj.transform.SetParent(Root.transform, false);
            obj.transform.localPosition = position; obj.transform.localRotation = Quaternion.LookRotation(target - position);
            var light = obj.AddComponent<Light>(); light.type = LightType.Spot; light.spotAngle = 85;
            light.range = 140; light.intensity = 1.5f; light.color = new Color(.87f, .92f, 1);
        }
        public void Place(CelestialBody body, double lat, double lon, double height) {
            var pos = body.GetWorldSurfacePosition(lat, lon, height);
            var up = (pos - body.position).normalized;
            var north = (body.GetWorldSurfacePosition(lat + .01, lon, height) - pos).normalized;
            Root.transform.position = (Vector3)pos; Root.transform.rotation = Quaternion.LookRotation((Vector3)north, (Vector3)up);
            Root.SetActive(FlightGlobals.ActiveVessel != null && (FlightGlobals.ActiveVessel.GetWorldPos3D() - pos).magnitude < guideLength + 15000);
        }
        public void Dispose() { Loader.Dispose(); UnityEngine.Object.Destroy(Root); foreach (var m in materials) UnityEngine.Object.Destroy(m); }
    }
}
