
using System.IO;
using UnityEngine;

public class DepthReprojectBaker : MonoBehaviour
{
    [Header("Inputs")]
    public Transform quad;                  // the plane you want to sample over
    public MeshFilter quadMeshFilter;       // its mesh (to get size)
    public Material blitMat;                // material using Hidden/DepthReprojectMeters_Blit
    public bool usePreprocessed = true;

    [Header("Output")]
    public RenderTexture metersRT;          // holds meters in R
    public Transform outQuad;               // assign in Inspector
    [Min(0f)] public float outQuadScale = 1f; // uniform multiplier for size

    public void ToggleVisibility()
    {
        if (!quad) { Debug.LogWarning("ToggleVisibility: quad is null"); return; }
        var r = quad.GetComponent<Renderer>();
        if (!r) { Debug.LogWarning($"ToggleVisibility: no Renderer on {quad.name}"); return; }

        r.enabled = !r.enabled;
        Debug.Log($"Quad {quad.name} visibility is now: {r.enabled}");
    }

    private void LateUpdate()
    {
        if (!quad || !quadMeshFilter || !blitMat) return;

        var mesh = quadMeshFilter.sharedMesh;
        var bounds = mesh.bounds; // local AABB, for Unity Quad usually size (1,1,0)

        // World center of the plane
        var centerWS = quad.TransformPoint(bounds.center);

        // World-space HALF-extents along the plane axes (includes rotation & scale)
        var rightHalfWS = quad.TransformVector(Vector3.right * bounds.extents.x);
        var upHalfWS = quad.TransformVector(Vector3.up * bounds.extents.y);

        // Push to material
        blitMat.SetVector("_PlaneCenterWS", centerWS);
        blitMat.SetVector("_PlaneRightHalfWS", rightHalfWS);
        blitMat.SetVector("_PlaneUpHalfWS", upHalfWS);

        // Stereo control for this offscreen blit
        blitMat.SetFloat("_UseStereo", 0f);
        blitMat.SetFloat("_EyeIndex", HandCaptureGlobals.EyeIndex);
        blitMat.SetFloat("_UsePreprocessed", usePreprocessed ? 1f : 0f);

        // Run the pass directly into the RT
        Graphics.Blit(null, metersRT, blitMat, 0);
        // In URP/HDRP you can also do a CommandBuffer.Blit + Graphics.ExecuteCommandBuffer

        // ---- Copy world size to outQuad (scaled) ----
        if (outQuad)
        {
            // World width/height of the source quad
            var worldWidth = (rightHalfWS * 2f).magnitude;
            var worldHeight = (upHalfWS * 2f).magnitude;

            // Apply uniform scale factor
            var targetW = worldWidth * outQuadScale;
            var targetH = worldHeight * outQuadScale;

            // Convert to localScale for outQuad, compensating for any parent lossy scale
            var parentLossy = outQuad.parent ? outQuad.parent.lossyScale : Vector3.one;
            var sx = parentLossy.x != 0 ? targetW / parentLossy.x : targetW;
            var sy = parentLossy.y != 0 ? targetH / parentLossy.y : targetH;

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

        var exrPath = Path.Combine(Application.persistentDataPath, $"depth_lin_slice_{HandCaptureGlobals.EyeIndex}_{Time.frameCount}_blit.exr");

        var bytes = tex.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat); // preserves float meters
        File.WriteAllBytes(exrPath, bytes);

        RenderTexture.active = prev;
        Destroy(tex); // cleanup if you like
        Debug.Log($"Saved EXR to: {exrPath}");
    }

    public Texture2D BuildBinaryMask(float minMeters, float maxMeters, bool invert = false)
    {
        if (!metersRT || !metersRT.IsCreated())
        {
            Debug.LogWarning("BuildBinaryMask: metersRT not ready");
            return null;
        }

        if (maxMeters <= minMeters)
        {
            Debug.LogWarning("BuildBinaryMask: maxMeters must be greater than minMeters");
            return null;
        }

        var prev = RenderTexture.active;
        RenderTexture.active = metersRT;

        var src = new Texture2D(metersRT.width, metersRT.height, TextureFormat.RGBAFloat, false, true);
        src.ReadPixels(new Rect(0, 0, metersRT.width, metersRT.height), 0, 0);
        src.Apply();

        RenderTexture.active = prev;

        var srcPixels = src.GetPixels();
        var maskPixels = new Color32[srcPixels.Length];
        for (int i = 0; i < srcPixels.Length; i++)
        {
            float r = srcPixels[i].r;
            bool inRange = r >= minMeters && r <= maxMeters;
            byte v = inRange ? (byte)255 : (byte)0;
            if (invert)
            {
                v = (byte)(255 - v);
            }
            maskPixels[i] = new Color32(v, v, v, 255);
        }

        var mask = new Texture2D(metersRT.width, metersRT.height, TextureFormat.RGBA32, false, true);
        mask.SetPixels32(maskPixels);
        mask.Apply();

        Destroy(src);
        return mask;
    }

    public Texture2D BuildBinaryMaskFromGlobals(bool invert = false)
    {
        float minMeters = Shader.GetGlobalFloat("_DepthMinMeters");
        float maxMeters = Shader.GetGlobalFloat("_DepthMaxMeters");
        return BuildBinaryMask(minMeters, maxMeters, invert);
    }

    private void OnDisable()
    {
        if (metersRT)
        {
            if (metersRT.IsCreated()) metersRT.Release();
            Destroy(metersRT);
            metersRT = null;
        }
    }
}
