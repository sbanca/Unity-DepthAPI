using PassthroughCameraSamples;
using UnityEngine;

public class PassthroughResolutionGlobals : MonoBehaviour
{
    private static readonly int ResolutionId = Shader.PropertyToID("_PassthroughResolution");
    private static readonly int TexelSizeId = Shader.PropertyToID("_PassthroughTexelSize");

    [SerializeField] private WebCamTextureManager m_webCamTextureManager;
    [SerializeField] private bool m_applyOnEnable = true;

    private Vector2Int m_lastResolution;

    private void OnEnable()
    {
        if (m_applyOnEnable)
        {
            ApplyIfReady();
        }
    }

    private void Update()
    {
        ApplyIfReady();
    }

    private void ApplyIfReady()
    {
        if (m_webCamTextureManager == null)
        {
            m_webCamTextureManager = FindAnyObjectByType<WebCamTextureManager>();
        }

        if (m_webCamTextureManager == null)
        {
            return;
        }

        var webCamTexture = m_webCamTextureManager.WebCamTexture;
        if (webCamTexture == null || webCamTexture.width <= 0 || webCamTexture.height <= 0)
        {
            return;
        }

        var resolution = new Vector2Int(webCamTexture.width, webCamTexture.height);
        if (resolution == m_lastResolution)
        {
            return;
        }

        m_lastResolution = resolution;
        Shader.SetGlobalVector(ResolutionId, new Vector4(resolution.x, resolution.y, 0f, 0f));
        Shader.SetGlobalVector(TexelSizeId, new Vector4(1f / resolution.x, 1f / resolution.y, resolution.x, resolution.y));
    }
}
