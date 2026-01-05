// Copyright (c) Meta Platforms, Inc. and affiliates.

using System.Collections;
using PassthroughCameraSamples;
using UnityEngine;
using UnityEngine.UI;

public sealed class EfficientNetSnapshotPredictor : MonoBehaviour
{
    [Tooltip("Reference the WebCamTextureManager in your scene. If left null, the script will try to find one at runtime.")]
    public WebCamTextureManager webcamManager;

    [Tooltip("Reference the EfficientNetAgeRegressorRunner in your scene. If left null, the script will try to find one at runtime.")]
    public EfficientNetAgeRegressorRunner modelRunner;

    [Header("Output")]
    [SerializeField] private Text m_resultText;
    public float LastMean { get; private set; }
    public float LastLogVariance { get; private set; }
    public float LastInferenceMs { get; private set; }
    public bool HasResult { get; private set; }

    [Header("Debug")]
    [SerializeField] private Renderer m_debugRenderer;
    [SerializeField] private RawImage m_debugRawImage;

    private bool m_isCapturing;
    private RenderTexture m_debugTexture;
    private MaterialPropertyBlock m_debugPropertyBlock;

    public void CaptureAndPredict()
    {
        if (m_isCapturing)
        {
            return;
        }

        StartCoroutine(CaptureAndPredictCoroutine());
    }

    private IEnumerator CaptureAndPredictCoroutine()
    {
        m_isCapturing = true;
        RenderTexture inputTexture = null;

        try
        {
            if (modelRunner == null)
            {
                modelRunner = FindAnyObjectByType<EfficientNetAgeRegressorRunner>();
            }

            if (webcamManager == null)
            {
                webcamManager = FindAnyObjectByType<WebCamTextureManager>();
            }

            if (modelRunner == null)
            {
                Debug.LogWarning("EfficientNetSnapshotPredictor: EfficientNetAgeRegressorRunner is missing.");
                yield break;
            }

            if (!modelRunner.IsReady)
            {
                Debug.LogWarning("EfficientNetSnapshotPredictor: EfficientNetAgeRegressorRunner is not ready yet.");
                yield break;
            }

            if (webcamManager == null || webcamManager.WebCamTexture == null)
            {
                Debug.LogWarning("EfficientNetSnapshotPredictor: WebCamTextureManager or WebCamTexture is missing.");
                yield break;
            }

            var webCamTexture = webcamManager.WebCamTexture;
            if (webCamTexture.width <= 0 || webCamTexture.height <= 0)
            {
                Debug.LogWarning("EfficientNetSnapshotPredictor: WebCamTexture is not ready yet.");
                yield break;
            }

            if (!webCamTexture.isPlaying)
            {
                webCamTexture.Play();
            }

            const int maxWaitFrames = 5;
            var waitedFrames = 0;
            do
            {
                yield return new WaitForEndOfFrame();
                waitedFrames++;
            } while (!webCamTexture.didUpdateThisFrame && waitedFrames < maxWaitFrames);

            if (!webCamTexture.didUpdateThisFrame)
            {
                Debug.LogWarning("EfficientNetSnapshotPredictor: WebCamTexture did not update this frame; capture may be stale.");
            }

            var targetSize = modelRunner.InputSize;
            if (targetSize.x <= 0 || targetSize.y <= 0)
            {
                Debug.LogWarning("EfficientNetSnapshotPredictor: Model input size is not ready.");
                yield break;
            }

            inputTexture = CropAndResizeCenter(webCamTexture, targetSize.x, targetSize.y);
            if (inputTexture == null)
            {
                Debug.LogWarning("EfficientNetSnapshotPredictor: Failed to prepare input texture.");
                yield break;
            }

            UpdateDebugTexture(inputTexture, targetSize);

            if (modelRunner.TryPredict(inputTexture, out var mean, out var logVar, out var inferenceMs))
            {
                LastMean = mean;
                LastLogVariance = logVar;
                LastInferenceMs = inferenceMs;
                HasResult = true;
                UpdateResultText();
            }
        }
        finally
        {
            if (inputTexture != null)
            {
                RenderTexture.ReleaseTemporary(inputTexture);
            }

            m_isCapturing = false;
        }
    }

    private void OnDestroy()
    {
        if (m_debugTexture != null)
        {
            m_debugTexture.Release();
            Destroy(m_debugTexture);
            m_debugTexture = null;
        }
    }

    private static RenderTexture CropAndResizeCenter(Texture source, int targetWidth, int targetHeight)
    {
        var sourceWidth = source.width;
        var sourceHeight = source.height;
        if (sourceWidth <= 0 || sourceHeight <= 0 || targetWidth <= 0 || targetHeight <= 0)
        {
            return null;
        }

        var sourceAspect = (float)sourceWidth / sourceHeight;
        var targetAspect = (float)targetWidth / targetHeight;

        var scale = Vector2.one;
        var offset = Vector2.zero;

        if (sourceAspect > targetAspect)
        {
            var newWidth = sourceHeight * targetAspect;
            var xOffset = (sourceWidth - newWidth) * 0.5f;
            scale = new Vector2(newWidth / sourceWidth, 1f);
            offset = new Vector2(xOffset / sourceWidth, 0f);
        }
        else if (sourceAspect < targetAspect)
        {
            var newHeight = sourceWidth / targetAspect;
            var yOffset = (sourceHeight - newHeight) * 0.5f;
            scale = new Vector2(1f, newHeight / sourceHeight);
            offset = new Vector2(0f, yOffset / sourceHeight);
        }

        var rt = RenderTexture.GetTemporary(targetWidth, targetHeight, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
        Graphics.Blit(source, rt, scale, offset);
        return rt;
    }

    private void UpdateDebugTexture(RenderTexture source, Vector2Int targetSize)
    {
        if (m_debugRenderer == null && m_debugRawImage == null)
        {
            return;
        }

        if (m_debugTexture == null || m_debugTexture.width != targetSize.x || m_debugTexture.height != targetSize.y)
        {
            if (m_debugTexture != null)
            {
                m_debugTexture.Release();
                Destroy(m_debugTexture);
            }

            m_debugTexture = new RenderTexture(targetSize.x, targetSize.y, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default)
            {
                name = "EfficientNetDebugInput"
            };
        }

        Graphics.Blit(source, m_debugTexture);

        if (m_debugRenderer != null)
        {
            m_debugPropertyBlock ??= new MaterialPropertyBlock();
            m_debugRenderer.GetPropertyBlock(m_debugPropertyBlock);
            m_debugPropertyBlock.SetTexture("_MainTex", m_debugTexture);
            m_debugRenderer.SetPropertyBlock(m_debugPropertyBlock);
        }

        if (m_debugRawImage != null)
        {
            m_debugRawImage.texture = m_debugTexture;
        }
    }

    private void UpdateResultText()
    {
        if (m_resultText == null || !HasResult)
        {
            return;
        }

        m_resultText.text =
            $"Mean: {LastMean:0.###}\n" +
            $"LogVar: {LastLogVariance:0.###}\n" +
            $"Inference: {LastInferenceMs:0.##} ms";
    }
}
