/*
 *  PassthroughSnapshot.cs
 *  Attach this to any active GameObject in the scene that also contains
 *  � or has a reference to � a WebCamTextureManager component.
 *
 *  Requires:
 *  � Quest 3 / 3 S running Horizon OS 74+
 *  � android.permission.CAMERA  +  horizonos.permission.HEADSET_CAMERA
 *    (already handled by the sample�s manifest) :contentReference[oaicite:0]{index=0}
 */

using System.IO;
using PassthroughCameraSamples;
using UnityEngine;

public class PassthroughSnapshot : MonoBehaviour
{
    [Tooltip("Reference the WebCamTextureManager in your scene. " +
             "If left null, the script will try to find one at runtime.")]
    public WebCamTextureManager webcamManager;

    [Header("Input")]
    [SerializeField] private OVRInput.RawButton _saveSnapshotButton = OVRInput.RawButton.A;

    private void Update()
    {
        if (OVRInput.GetDown(_saveSnapshotButton)) SaveCurrentFrame();
    }

    public void SaveCurrentFrame()
    {
        if (webcamManager == null)
        {
            webcamManager = FindAnyObjectByType<WebCamTextureManager>();
        }

        if (webcamManager == null || webcamManager.WebCamTexture == null)
        {
            Debug.LogWarning("PassthroughSnapshot: WebCamTextureManager or WebCamTexture is missing.");
            return;
        }

        var webCamTexture = webcamManager.WebCamTexture;
        if (webCamTexture.width <= 0 || webCamTexture.height <= 0)
        {
            Debug.LogWarning("PassthroughSnapshot: WebCamTexture is not ready yet.");
            return;
        }

        var outputSize = webcamManager.RequestedResolution;
        if (outputSize == Vector2Int.zero)
        {
            outputSize = new Vector2Int(webCamTexture.width, webCamTexture.height);
        }

        Debug.Log($"PassthroughSnapshot: Manager '{webcamManager.name}', requested {webcamManager.RequestedResolution}, webCam {webCamTexture.width}x{webCamTexture.height}, output {outputSize.x}x{outputSize.y}");

        // Copy pixels into a Texture2D, scaling if the requested size differs.
        Texture2D tex;
        if (outputSize.x == webCamTexture.width && outputSize.y == webCamTexture.height)
        {
            tex = new Texture2D(webCamTexture.width, webCamTexture.height, TextureFormat.RGBA32, false);
            tex.SetPixels32(webCamTexture.GetPixels32());
            tex.Apply(false, false);
        }
        else
        {
            var rt = RenderTexture.GetTemporary(outputSize.x, outputSize.y, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
            var previous = RenderTexture.active;
            Graphics.Blit(webCamTexture, rt);
            RenderTexture.active = rt;
            tex = new Texture2D(outputSize.x, outputSize.y, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, outputSize.x, outputSize.y), 0, 0, false);
            tex.Apply(false, false);
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
        }

        // Encode to PNG
        byte[] png = tex.EncodeToPNG();
        Destroy(tex);

        // Build a timestamped filename inside persistentDataPath
        string filename = $"PassthroughSnapshot_{webcamManager.Eye}_{Time.frameCount}.png";
        string fullPath = Path.Combine(Application.persistentDataPath, filename);

        // Write the file
        File.WriteAllBytes(fullPath, png);

        Debug.Log($"PassthroughSnapshot: Saved to {fullPath}");
    }
}
