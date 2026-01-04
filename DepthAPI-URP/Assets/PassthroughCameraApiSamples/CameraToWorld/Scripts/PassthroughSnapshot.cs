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

using System.Collections;
using System.IO;
using Meta.XR;
using PassthroughCameraSamples;
using Unity.Collections;
using UnityEngine;

public class PassthroughSnapshot : MonoBehaviour
{
    [Tooltip("Reference the WebCamTextureManager in your scene. " +
             "If left null, the script will try to find one at runtime.")]
    public WebCamTextureManager webcamManager;

    [Tooltip("Optional PassthroughCameraAccess for CPU snapshots; if assigned, it is used instead of WebCamTexture.")]
    public PassthroughCameraAccess cameraAccess;

    [Header("Input")]
    [SerializeField] private OVRInput.RawButton _saveSnapshotButton = OVRInput.RawButton.A;
    private bool _isCapturing;

    private void Update()
    {
        if (OVRInput.GetDown(_saveSnapshotButton)) SaveCurrentFrame();
    }

    public void SaveCurrentFrame()
    {
        if (_isCapturing)
        {
            return;
        }
        StartCoroutine(CaptureFrameCoroutine());
    }

    private IEnumerator CaptureFrameCoroutine()
    {
        _isCapturing = true;

        if (cameraAccess == null)
        {
            cameraAccess = FindAnyObjectByType<PassthroughCameraAccess>();
        }

        if (cameraAccess != null && cameraAccess.IsPlaying)
        {
            const int maxWaitFramesAccess = 5;
            var waitedFramesAccess = 0;
            do
            {
                yield return new WaitForEndOfFrame();
                waitedFramesAccess++;
            } while (!cameraAccess.IsUpdatedThisFrame && waitedFramesAccess < maxWaitFramesAccess);

            if (!cameraAccess.IsUpdatedThisFrame)
            {
                Debug.LogWarning("PassthroughSnapshot: PassthroughCameraAccess did not update this frame; capture may be stale.");
            }

            var size = cameraAccess.CurrentResolution;
            if (size.x <= 0 || size.y <= 0)
            {
                Debug.LogWarning("PassthroughSnapshot: PassthroughCameraAccess resolution is not ready yet.");
                _isCapturing = false;
                yield break;
            }

            var pixels = cameraAccess.GetColors();
            var pixelCount = size.x * size.y;
            if (!pixels.IsCreated || pixels.Length < pixelCount)
            {
                Debug.LogWarning("PassthroughSnapshot: PassthroughCameraAccess pixel buffer is not ready yet.");
                _isCapturing = false;
                yield break;
            }

            var managedPixels = new Color32[pixelCount];
            NativeArray<Color32>.Copy(pixels, managedPixels, pixelCount);

            var accessTex = new Texture2D(size.x, size.y, TextureFormat.RGBA32, false);
            accessTex.SetPixels32(managedPixels);
            accessTex.Apply(false, false);

            byte[] accessPng = accessTex.EncodeToPNG();
            Destroy(accessTex);

            string accessFilename = $"PassthroughSnapshot_{cameraAccess.CameraPosition}_{Time.frameCount}.png";
            string accessFullPath = Path.Combine(Application.persistentDataPath, accessFilename);
            File.WriteAllBytes(accessFullPath, accessPng);

            Debug.Log($"PassthroughSnapshot: Saved to {accessFullPath} (PassthroughCameraAccess)");
            _isCapturing = false;
            yield break;
        }

        if (webcamManager == null)
        {
            webcamManager = FindAnyObjectByType<WebCamTextureManager>();
        }

        if (webcamManager == null || webcamManager.WebCamTexture == null)
        {
            Debug.LogWarning("PassthroughSnapshot: WebCamTextureManager or WebCamTexture is missing.");
            _isCapturing = false;
            yield break;
        }

        var webCamTexture = webcamManager.WebCamTexture;
        if (webCamTexture.width <= 0 || webCamTexture.height <= 0)
        {
            Debug.LogWarning("PassthroughSnapshot: WebCamTexture is not ready yet.");
            _isCapturing = false;
            yield break;
        }

        if (!webCamTexture.isPlaying)
        {
            webCamTexture.Play();
        }

        const int maxWaitFramesWebCam = 5;
        var waitedFramesWebCam = 0;
        do
        {
            yield return new WaitForEndOfFrame();
            waitedFramesWebCam++;
        } while (!webCamTexture.didUpdateThisFrame && waitedFramesWebCam < maxWaitFramesWebCam);

        if (!webCamTexture.didUpdateThisFrame)
        {
            Debug.LogWarning("PassthroughSnapshot: WebCamTexture did not update this frame; capture may be stale.");
        }

        var outputSize = webcamManager.RequestedResolution;
        if (outputSize == Vector2Int.zero)
        {
            outputSize = new Vector2Int(webCamTexture.width, webCamTexture.height);
        }

        Debug.Log($"PassthroughSnapshot: Manager '{webcamManager.name}', requested {webcamManager.RequestedResolution}, webCam {webCamTexture.width}x{webCamTexture.height}, output {outputSize.x}x{outputSize.y}");

        // Copy pixels into a Texture2D via GPU to handle external/YUV camera textures.
        var rt = RenderTexture.GetTemporary(outputSize.x, outputSize.y, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
        var previous = RenderTexture.active;
        Graphics.Blit(webCamTexture, rt);
        RenderTexture.active = rt;
        var tex = new Texture2D(outputSize.x, outputSize.y, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, outputSize.x, outputSize.y), 0, 0, false);
        tex.Apply(false, false);
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(rt);

        // Encode to PNG
        byte[] png = tex.EncodeToPNG();
        Destroy(tex);

        // Build a timestamped filename inside persistentDataPath
        string filename = $"PassthroughSnapshot_{webcamManager.Eye}_{Time.frameCount}.png";
        string fullPath = Path.Combine(Application.persistentDataPath, filename);

        // Write the file
        File.WriteAllBytes(fullPath, png);

        Debug.Log($"PassthroughSnapshot: Saved to {fullPath}");
        _isCapturing = false;
    }
}
