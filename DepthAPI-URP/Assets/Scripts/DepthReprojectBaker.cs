using System.Collections;
using System.IO;
using PassthroughCameraSamples;
using UnityEngine;
using UnityEngine.Experimental.Rendering; // for GraphicsFormat

public class DepthReprojectBaker : MonoBehaviour
{
    [Header("Inputs")]
    public Transform quad;                  // the plane you want to sample over
    public MeshFilter quadMeshFilter;       // its mesh (to get size)
    public Material blitMat;                // material using Hidden/DepthReprojectMeters_Blit

    [Header("Passthrough source")]
    public WebCamTextureManager webcamMgr;     // Assign in Inspector
    public int resolutionW = 512;            // output texture size (square)
    public int resolutionH = 512;            // output texture size (square)

    [Header("Eye Selection")]
    [Range(0, 1)] public int eyeIndex = 0;   // choose 0 or 1
    public bool usePreprocessed = true;

    [Header("Output")]
    public RenderTexture metersRT;          // holds meters in R
    public Transform outQuad;               // assign in Inspector
    [Min(0f)] public float outQuadScale = 1f; // uniform multiplier for size

    [Header("Controls")]
    [SerializeField] private OVRInput.RawButton _saveSnapshotButton = OVRInput.RawButton.A;
    [SerializeField] private OVRInput.RawButton _hidUnhide = OVRInput.RawButton.B;


    private void Update()
    {
        if (OVRInput.GetDown(_saveSnapshotButton)) SaveEXR();
        if (OVRInput.GetDown(_hidUnhide)) ToggleVisibility();
    }

    public void ToggleVisibility()
    {
        if (!quad) { Debug.LogWarning("ToggleVisibility: quad is null"); return; }
        var r = quad.GetComponent<Renderer>();
        if (!r) { Debug.LogWarning($"ToggleVisibility: no Renderer on {quad.name}"); return; }

        r.enabled = !r.enabled;
        Debug.Log($"Quad {quad.name} visibility is now: {r.enabled}");
    }

    void LateUpdate()
    {
        if (!quad || !quadMeshFilter || !blitMat) return;

        var mesh = quadMeshFilter.sharedMesh;
        var bounds = mesh.bounds; // local AABB, for Unity Quad usually size (1,1,0)

        // World center of the plane
        Vector3 centerWS = quad.TransformPoint(bounds.center);

        // World-space HALF-extents along the plane axes (includes rotation & scale)
        Vector3 rightHalfWS = quad.TransformVector(Vector3.right * bounds.extents.x);
        Vector3 upHalfWS = quad.TransformVector(Vector3.up * bounds.extents.y);

        // Push to material
        blitMat.SetVector("_PlaneCenterWS", centerWS);
        blitMat.SetVector("_PlaneRightHalfWS", rightHalfWS);
        blitMat.SetVector("_PlaneUpHalfWS", upHalfWS);

        // Stereo control for this offscreen blit
        blitMat.SetFloat("_UseStereo", 0f);
        blitMat.SetFloat("_EyeIndex", eyeIndex);
        blitMat.SetFloat("_UsePreprocessed", usePreprocessed ? 1f : 0f);

        // Run the pass directly into the RT
        Graphics.Blit(null, metersRT, blitMat, 0);
        // In URP/HDRP you can also do a CommandBuffer.Blit + Graphics.ExecuteCommandBuffer

        // ---- Copy world size to outQuad (scaled) ----
        if (outQuad)
        {
            // World width/height of the source quad
            float worldWidth = (rightHalfWS * 2f).magnitude;
            float worldHeight = (upHalfWS * 2f).magnitude;

            // Apply uniform scale factor
            float targetW = worldWidth * outQuadScale;
            float targetH = worldHeight * outQuadScale;

            // Convert to localScale for outQuad, compensating for any parent lossy scale
            Vector3 parentLossy = outQuad.parent ? outQuad.parent.lossyScale : Vector3.one;
            float sx = parentLossy.x != 0 ? targetW / parentLossy.x : targetW;
            float sy = parentLossy.y != 0 ? targetH / parentLossy.y : targetH;

            // For a primitive Quad, Z scale isn't meaningful; keep it at 1
            outQuad.localScale = new Vector3(sx, sy, 1f);

        }
    }

    // Call this whenever you want to save
    public void SaveEXR()
    {
        if (!metersRT || !metersRT.IsCreated()) { Debug.LogWarning("SaveEXR: metersRT not ready"); return; }

        var prev = RenderTexture.active;
        RenderTexture.active = metersRT;

        var tex = new Texture2D(metersRT.width, metersRT.height, TextureFormat.RGBAFloat, false, true);
        tex.ReadPixels(new Rect(0, 0, metersRT.width, metersRT.height), 0, 0);
        tex.Apply();

        string exrPath = Path.Combine(Application.persistentDataPath, $"depth_lin_slice_{eyeIndex}_{Time.frameCount}_blit.exr");

        var bytes = tex.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat); // preserves float meters
        System.IO.File.WriteAllBytes(exrPath, bytes);

        RenderTexture.active = prev;
        Destroy(tex); // cleanup if you like
        Debug.Log($"Saved EXR to: {exrPath}");
    }

    void OnDisable()
    {
        if (metersRT)
        {
            if (metersRT.IsCreated()) metersRT.Release();
            Destroy(metersRT);
            metersRT = null;
        }
    }
}
