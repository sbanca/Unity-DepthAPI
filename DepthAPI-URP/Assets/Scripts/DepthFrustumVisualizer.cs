using PassthroughCameraSamples;
using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class DepthFrustumVisualizer : MonoBehaviour
{
    [Header("Inputs")]
    [SerializeField] private WebCamTextureManager m_webCamTextureManager;
    [SerializeField] private HandCaptureSettings m_settings;
    [SerializeField] private bool m_alignPose = true;
    [SerializeField] private bool m_rebuildOnEnable = true;
    [SerializeField] private bool m_autoRebuild = true;
    [SerializeField] private bool m_requireCameraStreaming = true;
    [SerializeField] private bool m_useMainCameraInEditor = true;
    [SerializeField] private Vector2Int m_editorResolution = new Vector2Int(1280, 960);
    [SerializeField, Min(1f)] private float m_editorHFovDegrees = 90f;
    [SerializeField, Min(1f)] private float m_editorVFovDegrees = 60f;
    [SerializeField] private bool m_matchOutputAspect = true;

    [Header("Output")]
    [SerializeField] private MeshFilter m_meshFilter;
    [SerializeField] private MeshRenderer m_meshRenderer;

    private Mesh m_mesh;
    private readonly Vector3[] m_frustumCorners = new Vector3[8];
    private float m_lastMinMeters = -1f;
    private float m_lastMaxMeters = -1f;
    private Vector2Int m_lastOutputResolution;
    private PassthroughCameraEye m_lastEye;
    private float m_lastHFovRad;
    private float m_lastVFovRad;
    private bool m_hasBuilt;

    private void Awake()
    {
        if (m_meshFilter == null)
        {
            m_meshFilter = GetComponent<MeshFilter>();
        }

        if (m_meshRenderer == null)
        {
            m_meshRenderer = GetComponent<MeshRenderer>();
        }
    }

    private void OnEnable()
    {
        if (m_rebuildOnEnable)
        {
            RebuildIfNeeded(true);
        }
    }

    [ContextMenu("Build Frustum")]
    public void BuildFrustum()
    {
        if (m_alignPose)
        {
            UpdatePose();
        }

        RebuildIfNeeded(true);
    }

    private void LateUpdate()
    {
        if (m_alignPose)
        {
            UpdatePose();
        }

        if (m_autoRebuild)
        {
            RebuildIfNeeded(false);
        }
    }

    private void UpdatePose()
    {
        if (!CanUsePassthroughApi())
        {
#if UNITY_EDITOR
            if (m_useMainCameraInEditor)
            {
                var mainCamera = Camera.main;
                if (mainCamera != null)
                {
                    transform.SetPositionAndRotation(mainCamera.transform.position, mainCamera.transform.rotation);
                }
            }
#endif
            return;
        }

        if (m_requireCameraStreaming && !IsCameraStreaming())
        {
            return;
        }

        var eye = ResolveEye();
        var cameraPose = PassthroughCameraUtils.GetCameraPoseInWorld(eye);
        transform.SetPositionAndRotation(cameraPose.position, cameraPose.rotation);
    }

    public void RebuildIfNeeded(bool force)
    {
        if (!TryGetDepthRange(out var minMeters, out var maxMeters))
        {
            return;
        }

        var eye = ResolveEye();
        if (!TryGetFov(eye, out var hFovRad, out var vFovRad, out var outputResolution))
        {
            return;
        }

        if (!force && !NeedsRebuild(minMeters, maxMeters, eye, hFovRad, vFovRad, outputResolution))
        {
            return;
        }

        BuildMesh(minMeters, maxMeters, hFovRad, vFovRad);

        m_lastMinMeters = minMeters;
        m_lastMaxMeters = maxMeters;
        m_lastEye = eye;
        m_lastHFovRad = hFovRad;
        m_lastVFovRad = vFovRad;
        m_lastOutputResolution = outputResolution;
        m_hasBuilt = true;
    }

    private bool NeedsRebuild(float minMeters, float maxMeters, PassthroughCameraEye eye, float hFovRad, float vFovRad, Vector2Int outputResolution)
    {
        if (!m_hasBuilt)
        {
            return true;
        }

        if (!Mathf.Approximately(minMeters, m_lastMinMeters) || !Mathf.Approximately(maxMeters, m_lastMaxMeters))
        {
            return true;
        }

        if (eye != m_lastEye)
        {
            return true;
        }

        if (!Mathf.Approximately(hFovRad, m_lastHFovRad) || !Mathf.Approximately(vFovRad, m_lastVFovRad))
        {
            return true;
        }

        if (outputResolution != m_lastOutputResolution)
        {
            return true;
        }

        return false;
    }

    private void BuildMesh(float minMeters, float maxMeters, float hFovRad, float vFovRad)
    {
        EnsureMesh();
        m_mesh.Clear();

        var halfNearWidth = Mathf.Tan(hFovRad * 0.5f) * minMeters;
        var halfNearHeight = Mathf.Tan(vFovRad * 0.5f) * minMeters;
        var halfFarWidth = Mathf.Tan(hFovRad * 0.5f) * maxMeters;
        var halfFarHeight = Mathf.Tan(vFovRad * 0.5f) * maxMeters;

        m_frustumCorners[0] = new Vector3(-halfNearWidth, halfNearHeight, minMeters);
        m_frustumCorners[1] = new Vector3(halfNearWidth, halfNearHeight, minMeters);
        m_frustumCorners[2] = new Vector3(halfNearWidth, -halfNearHeight, minMeters);
        m_frustumCorners[3] = new Vector3(-halfNearWidth, -halfNearHeight, minMeters);
        m_frustumCorners[4] = new Vector3(-halfFarWidth, halfFarHeight, maxMeters);
        m_frustumCorners[5] = new Vector3(halfFarWidth, halfFarHeight, maxMeters);
        m_frustumCorners[6] = new Vector3(halfFarWidth, -halfFarHeight, maxMeters);
        m_frustumCorners[7] = new Vector3(-halfFarWidth, -halfFarHeight, maxMeters);

        m_mesh.vertices = m_frustumCorners;
        m_mesh.triangles = new[]
        {
            0, 1, 2, 0, 2, 3, // near
            4, 6, 5, 4, 7, 6, // far
            0, 3, 7, 0, 7, 4, // left
            1, 5, 6, 1, 6, 2, // right
            0, 4, 5, 0, 5, 1, // top
            3, 2, 6, 3, 6, 7  // bottom
        };
        m_mesh.RecalculateNormals();
        m_mesh.RecalculateBounds();

        if (m_meshFilter != null)
        {
            m_meshFilter.sharedMesh = m_mesh;
        }
    }

    private void EnsureMesh()
    {
        if (m_mesh != null)
        {
            return;
        }

        m_mesh = new Mesh { name = "DepthFrustumMesh" };
    }

    private bool TryGetDepthRange(out float minMeters, out float maxMeters)
    {
        if (m_settings == null)
        {
            minMeters = 0f;
            maxMeters = 0f;
            return false;
        }

        minMeters = m_settings.minMeters;
        maxMeters = m_settings.maxMeters;
        if (maxMeters < minMeters)
        {
            maxMeters = minMeters;
        }

        return minMeters > 0f && maxMeters > 0f;
    }

    private PassthroughCameraEye ResolveEye()
    {
        if (m_webCamTextureManager != null)
        {
            return m_webCamTextureManager.Eye;
        }

        return m_settings != null ? m_settings.eye : PassthroughCameraEye.Left;
    }

    private bool TryGetFov(PassthroughCameraEye eye, out float hFovRad, out float vFovRad, out Vector2Int outputResolution)
    {
        if (!TryGetOutputResolution(out outputResolution))
        {
            hFovRad = 0f;
            vFovRad = 0f;
            return false;
        }

        if (!CanUsePassthroughApi())
        {
            hFovRad = m_editorHFovDegrees * Mathf.Deg2Rad;
            vFovRad = m_matchOutputAspect
                ? CalculateVFovFromHFov(hFovRad, outputResolution)
                : m_editorVFovDegrees * Mathf.Deg2Rad;
            return hFovRad > 0f && vFovRad > 0f;
        }

        var intrinsics = PassthroughCameraUtils.GetCameraIntrinsics(eye);
        if (intrinsics.FocalLength.x <= 0f || intrinsics.FocalLength.y <= 0f ||
            intrinsics.Resolution.x <= 0 || intrinsics.Resolution.y <= 0)
        {
            hFovRad = 0f;
            vFovRad = 0f;
            return false;
        }

        hFovRad = 2f * Mathf.Atan(intrinsics.Resolution.x / (2f * intrinsics.FocalLength.x));
        vFovRad = m_matchOutputAspect
            ? CalculateVFovFromHFov(hFovRad, outputResolution, intrinsics.FocalLength.y, intrinsics.Resolution.x)
            : 2f * Mathf.Atan(intrinsics.Resolution.y / (2f * intrinsics.FocalLength.y));

        return hFovRad > 0f && vFovRad > 0f;
    }

    public bool TryGetFrustumParameters(out float minMeters, out float maxMeters, out float hFovRad, out float vFovRad)
    {
        hFovRad = 0f;
        vFovRad = 0f;
        if (!TryGetDepthRange(out minMeters, out maxMeters))
        {
            return false;
        }

        var eye = ResolveEye();
        return TryGetFov(eye, out hFovRad, out vFovRad, out _);
    }

    private bool TryGetOutputResolution(out Vector2Int outputResolution)
    {
        outputResolution = Vector2Int.zero;
        if (m_webCamTextureManager != null)
        {
            var webCamTexture = m_webCamTextureManager.WebCamTexture;
            if (webCamTexture != null && webCamTexture.width > 0 && webCamTexture.height > 0)
            {
                outputResolution = new Vector2Int(webCamTexture.width, webCamTexture.height);
            }
            else if (m_webCamTextureManager.RequestedResolution != Vector2Int.zero)
            {
                outputResolution = m_webCamTextureManager.RequestedResolution;
            }
        }

        if (outputResolution == Vector2Int.zero)
        {
            outputResolution = m_editorResolution;
        }

        return outputResolution.x > 0 && outputResolution.y > 0;
    }

    private static float CalculateVFovFromHFov(float hFovRad, Vector2Int outputResolution)
    {
        if (outputResolution.x <= 0 || outputResolution.y <= 0)
        {
            return 0f;
        }

        var aspect = (float)outputResolution.x / outputResolution.y;
        return 2f * Mathf.Atan(Mathf.Tan(hFovRad * 0.5f) / aspect);
    }

    private static float CalculateVFovFromHFov(float hFovRad, Vector2Int outputResolution, float focalLengthY, float intrinsicsWidth)
    {
        if (outputResolution.x <= 0 || outputResolution.y <= 0 || focalLengthY <= 0f || intrinsicsWidth <= 0f)
        {
            return 0f;
        }

        var aspect = (float)outputResolution.x / outputResolution.y;
        var cropHeight = intrinsicsWidth / aspect;
        return 2f * Mathf.Atan(cropHeight / (2f * focalLengthY));
    }

    private bool TryGetEditorFallbackFov(out float hFovRad, out float vFovRad, out Vector2Int intrinsicsResolution)
    {
        intrinsicsResolution = m_webCamTextureManager != null && m_webCamTextureManager.RequestedResolution != Vector2Int.zero
            ? m_webCamTextureManager.RequestedResolution
            : m_editorResolution;

        if (intrinsicsResolution.x <= 0 || intrinsicsResolution.y <= 0 ||
            m_editorHFovDegrees <= 0f || m_editorVFovDegrees <= 0f)
        {
            hFovRad = 0f;
            vFovRad = 0f;
            return false;
        }

        hFovRad = m_editorHFovDegrees * Mathf.Deg2Rad;
        vFovRad = m_matchOutputAspect
            ? CalculateVFovFromHFov(hFovRad, intrinsicsResolution)
            : m_editorVFovDegrees * Mathf.Deg2Rad;
        return true;
    }

    private bool IsCameraStreaming()
    {
        if (m_webCamTextureManager == null)
        {
            return false;
        }

        return m_webCamTextureManager.WebCamTexture != null && m_webCamTextureManager.WebCamTexture.isPlaying;
    }

    private bool CanUsePassthroughApi()
    {
#if UNITY_ANDROID
        return Application.isPlaying && PassthroughCameraUtils.IsSupported;
#else
        return false;
#endif
    }
}
