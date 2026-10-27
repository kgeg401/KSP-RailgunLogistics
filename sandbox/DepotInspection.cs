using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;

namespace RailgunSandbox
{
    public static class DepotInspection
    {
        // Render the actual local KSP scene; informational desktop popups are not part of this camera.
        // This is an asset inspection under added work lighting, not a gameplay screenshot.
        public static IEnumerator Capture(DepotModel model, string path, Vector3 podPosition)
        {
            yield return new WaitForEndOfFrame();
            var obj = new GameObject("KRL inspection camera");
            var camera = obj.AddComponent<Camera>(); camera.enabled = false;
            var texture = new RenderTexture(1600, 1200, 24);
            var image = new Texture2D(1600, 1200, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            var workLight = new GameObject("KRL inspection work light");
            var floodlights = model.Root.GetComponentsInChildren<Light>().Where(light => light.enabled).ToArray();
            try {
                foreach (var floodlight in floodlights) floodlight.enabled = false;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.025f, .04f, .065f);
                camera.nearClipPlane = .3f; camera.farClipPlane = 1000; camera.fieldOfView = 53;
                camera.cullingMask = FlightCamera.fetch.mainCamera.cullingMask;
                var root = model.Root.transform;
                camera.transform.position = root.TransformPoint(new Vector3(145, 95, 175));
                camera.transform.rotation = Quaternion.LookRotation(root.TransformPoint(new Vector3(0, 45, 0)) - camera.transform.position, root.up);
                var light = workLight.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = .4f; light.color = Color.white;
                workLight.transform.rotation = Quaternion.LookRotation(root.TransformDirection(new Vector3(-1, -1, -1)));
                camera.targetTexture = texture; texture.Create(); camera.Render();
                RenderTexture.active = texture; image.ReadPixels(new Rect(0, 0, 1600, 1200), 0, 0); image.Apply();
                File.WriteAllBytes(path, image.EncodeToPNG());
                Debug.Log("[RailgunSandbox] Native asset inspection rendered " + path);
                camera.transform.position = podPosition + root.TransformDirection(new Vector3(21, 6, 27));
                camera.transform.rotation = Quaternion.LookRotation(podPosition - camera.transform.position, root.up);
                camera.Render(); RenderTexture.active = texture;
                image.ReadPixels(new Rect(0, 0, 1600, 1200), 0, 0); image.Apply();
                string closeup = Path.Combine(Path.GetDirectoryName(path), "PodInspection.png");
                File.WriteAllBytes(closeup, image.EncodeToPNG());
                Debug.Log("[RailgunSandbox] Native pod inspection rendered " + closeup);
            } finally {
                foreach (var floodlight in floodlights) if (floodlight != null) floodlight.enabled = true;
                RenderTexture.active = previous; camera.targetTexture = null;
                texture.Release(); Object.Destroy(texture); Object.Destroy(image); Object.Destroy(workLight); Object.Destroy(obj);
            }
        }
    }
}
