// Copyright (c) Meta Platforms, Inc. and affiliates.

using System.Collections;
using PassthroughCameraSamples;
using UnityEngine;
using UnityEngine.UI;

public sealed class EfficientNetSnapshotPredictor : MonoBehaviour
{
    [Tooltip("Reference the WebCamTextureManager in your scene. If left null, the script will try to find one at runtime.")]
    public WebCamTextureManager webcamManager;

    [Tooltip("Reference a component that implements IAgeRegressorRunner in your scene. If left null, the script will try to find one at runtime.")]
    public MonoBehaviour modelRunner;

    [Header("Output")]
    [SerializeField] private Text m_resultText;
    public float LastMean { get; private set; }
    public float LastLogVariance { get; private set; }
    public float LastInferenceMs { get; private set; }
    public float LastBrightness { get; private set; }
    public float LastMaskCoverage { get; private set; }
    public bool HasResult { get; private set; }
    public bool IsCapturing => m_isCapturing;
    public int ResultVersion { get; private set; }

    [Header("Capture")]
    [SerializeField] private bool m_captureInputPng;
    public bool CaptureInputPng
    {
        get => m_captureInputPng;
        set => m_captureInputPng = value;
    }

    [Header("Mask")]
    [SerializeField] private bool m_useBinaryMask;
    [SerializeField] private DepthReprojectBaker m_depthMaskSource;
    [SerializeField, Range(0f, 1f)] private float m_maskThreshold = 0.5f;
    [SerializeField] private bool m_invertMask;
    [SerializeField] private Material m_maskMaterial;

    [Header("Brightness")]
    [SerializeField, Range(0f, 1f)] private float m_minBrightness;

    [Header("Mask Filters")]
    [SerializeField, Range(0f, 1f)] private float m_minMaskCoverage;

    [Header("Landmark Overlay")]
    [SerializeField] private bool m_overlayLandmarks;
    [SerializeField] private MaskLandmarkRunner m_landmarkRunner;
    [SerializeField, Range(1, 9)] private int m_landmarkPixelSize = 3;
    [SerializeField] private Color m_landmarkColor = Color.red;

    [Header("Debug")]
    [SerializeField] private Renderer m_debugRenderer;
    [SerializeField] private RawImage m_debugRawImage;

    private bool m_isCapturing;
    private RenderTexture m_debugTexture;
    private MaterialPropertyBlock m_debugPropertyBlock;
    private Material m_runtimeMaskMaterial;
    private byte[] m_lastInputPng;
    private int m_lastInputVersion;
    private const float BrightnessSentinel = -1f;
    private const float MaskCoverageSentinel = -1f;

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
        m_lastInputPng = null;
        m_lastInputVersion = 0;
        LastBrightness = BrightnessSentinel;
        LastMaskCoverage = MaskCoverageSentinel;
        RenderTexture inputTexture = null;
        RenderTexture maskTexture = null;
        RenderTexture maskedInputTexture = null;
        RenderTexture overlayTexture = null;
        Texture2D maskTextureCpu = null;

        try
        {
            var runner = ResolveModelRunner();

            if (webcamManager == null)
            {
                webcamManager = FindAnyObjectByType<WebCamTextureManager>();
            }

            if (runner == null)
            {
                Debug.LogWarning("EfficientNetSnapshotPredictor: IAgeRegressorRunner is missing.");
                yield break;
            }

            if (!runner.IsReady)
            {
                Debug.LogWarning("EfficientNetSnapshotPredictor: IAgeRegressorRunner is not ready yet.");
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

            var targetSize = runner.InputSize;
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

            var finalInputTexture = inputTexture;
            if (m_useBinaryMask)
            {
                if (m_depthMaskSource == null)
                {
                    m_depthMaskSource = FindAnyObjectByType<DepthReprojectBaker>();
                }

                if (m_depthMaskSource == null)
                {
                    Debug.LogWarning("EfficientNetSnapshotPredictor: DepthReprojectBaker is missing, skipping mask.");
                }
                else
                {
                    maskTextureCpu = m_depthMaskSource.BuildBinaryMaskFromGlobals(m_invertMask);
                    if (maskTextureCpu == null)
                    {
                        Debug.LogWarning("EfficientNetSnapshotPredictor: Failed to build binary mask, skipping mask.");
                    }
                    else
                    {
                        maskTexture = CropAndResizeCenter(maskTextureCpu, targetSize.x, targetSize.y);
                        if (maskTexture == null)
                        {
                            Debug.LogWarning("EfficientNetSnapshotPredictor: Failed to resize mask, skipping mask.");
                        }
                        else
                        {
                            maskedInputTexture = ApplyBinaryMask(inputTexture, maskTexture);
                            if (maskedInputTexture != null)
                            {
                                finalInputTexture = maskedInputTexture;
                            }
                        }
                    }
                }
            }

            LastBrightness = ComputeBrightness(inputTexture, maskTexture, m_useBinaryMask, m_maskThreshold);
            if (m_minBrightness > 0f && (LastBrightness <= BrightnessSentinel || LastBrightness < m_minBrightness))
            {
                Debug.LogWarning($"EfficientNetSnapshotPredictor: Brightness {LastBrightness:0.###} below threshold {m_minBrightness:0.###}, skipping inference.");
                yield break;
            }

            if (m_minMaskCoverage > 0f)
            {
                if (!m_useBinaryMask || maskTexture == null)
                {
                    Debug.LogWarning("EfficientNetSnapshotPredictor: Mask coverage threshold set but binary mask is disabled or missing.");
                }
                else
                {
                    LastMaskCoverage = ComputeMaskCoverage(maskTexture, m_maskThreshold);
                    if (LastMaskCoverage <= MaskCoverageSentinel || LastMaskCoverage < m_minMaskCoverage)
                    {
                        Debug.LogWarning($"EfficientNetSnapshotPredictor: Mask coverage {LastMaskCoverage:0.###} below threshold {m_minMaskCoverage:0.###}, skipping inference.");
                        yield break;
                    }
                }
            }

            var debugTextureSource = finalInputTexture;
            if (m_overlayLandmarks && m_landmarkRunner != null && maskTexture != null)
            {
                overlayTexture = RenderTexture.GetTemporary(targetSize.x, targetSize.y, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
                Graphics.Blit(finalInputTexture, overlayTexture);
                if (TryOverlayLandmarks(overlayTexture, maskTexture, targetSize))
                {
                    debugTextureSource = overlayTexture;
                }
            }

            UpdateDebugTexture(debugTextureSource, targetSize);

            if (runner.TryPredict(finalInputTexture, out var mean, out var logVar, out var inferenceMs))
            {
                LastMean = mean;
                LastLogVariance = logVar;
                LastInferenceMs = inferenceMs;
                HasResult = true;
                ResultVersion++;
                if (m_captureInputPng)
                {
                    m_lastInputPng = EncodeToPng(debugTextureSource);
                    m_lastInputVersion = ResultVersion;
                }
                UpdateResultText();
            }
        }
        finally
        {
            if (inputTexture != null)
            {
                RenderTexture.ReleaseTemporary(inputTexture);
            }

            if (maskTexture != null)
            {
                RenderTexture.ReleaseTemporary(maskTexture);
            }

            if (maskedInputTexture != null)
            {
                RenderTexture.ReleaseTemporary(maskedInputTexture);
            }

            if (overlayTexture != null)
            {
                RenderTexture.ReleaseTemporary(overlayTexture);
            }

            if (maskTextureCpu != null)
            {
                Destroy(maskTextureCpu);
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

        if (m_runtimeMaskMaterial != null)
        {
            Destroy(m_runtimeMaskMaterial);
            m_runtimeMaskMaterial = null;
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

    private RenderTexture ApplyBinaryMask(RenderTexture source, RenderTexture mask)
    {
        if (source == null || mask == null)
        {
            return null;
        }

        var mat = GetMaskMaterial();
        if (mat == null)
        {
            Debug.LogWarning("EfficientNetSnapshotPredictor: Missing mask material, skipping mask.");
            return null;
        }

        mat.SetTexture("_MaskTex", mask);
        mat.SetFloat("_Threshold", m_maskThreshold);

        var rt = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
        Graphics.Blit(source, rt, mat);
        return rt;
    }

    private Material GetMaskMaterial()
    {
        if (m_maskMaterial != null)
        {
            return m_maskMaterial;
        }

        if (m_runtimeMaskMaterial != null)
        {
            return m_runtimeMaskMaterial;
        }

        var shader = Shader.Find("Hidden/ApplyBinaryMask");
        if (shader == null)
        {
            return null;
        }

        m_runtimeMaskMaterial = new Material(shader)
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        return m_runtimeMaskMaterial;
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

    public bool TryConsumeLastInputPng(int expectedVersion, out byte[] png)
    {
        if (m_lastInputPng != null && m_lastInputVersion == expectedVersion)
        {
            png = m_lastInputPng;
            m_lastInputPng = null;
            return true;
        }

        png = null;
        return false;
    }

    private static byte[] EncodeToPng(RenderTexture source)
    {
        if (source == null)
        {
            return null;
        }

        var previous = RenderTexture.active;
        RenderTexture.active = source;
        var tex = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0f, 0f, source.width, source.height), 0, 0, false);
        tex.Apply(false, false);
        RenderTexture.active = previous;
        var png = tex.EncodeToPNG();
        Destroy(tex);
        return png;
    }

    private float ComputeBrightness(RenderTexture colorTexture, RenderTexture maskTexture, bool useMask, float threshold)
    {
        if (colorTexture == null)
        {
            return BrightnessSentinel;
        }

        if (!useMask)
        {
            return ComputeAverageLuminance(colorTexture);
        }

        if (maskTexture == null)
        {
            return BrightnessSentinel;
        }

        if (colorTexture.width != maskTexture.width || colorTexture.height != maskTexture.height)
        {
            return BrightnessSentinel;
        }

        var colorTex = ReadbackTexture(colorTexture, TextureFormat.RGBA32, false);
        var maskTex = ReadbackTexture(maskTexture, TextureFormat.RGBA32, true);
        try
        {
            var colors = colorTex.GetPixels32();
            var masks = maskTex.GetPixels32();
            return ComputeAverageLuminanceMasked(colors, masks, threshold);
        }
        finally
        {
            Destroy(colorTex);
            Destroy(maskTex);
        }
    }

    private float ComputeMaskCoverage(RenderTexture maskTexture, float threshold)
    {
        if (maskTexture == null)
        {
            return MaskCoverageSentinel;
        }

        var maskTex = ReadbackTexture(maskTexture, TextureFormat.RGBA32, true);
        try
        {
            var masks = maskTex.GetPixels32();
            if (masks == null || masks.Length == 0)
            {
                return MaskCoverageSentinel;
            }

            var thresholdByte = (byte)Mathf.Clamp(Mathf.RoundToInt(threshold * 255f), 0, 255);
            var valid = 0;
            for (var i = 0; i < masks.Length; i++)
            {
                if (masks[i].r >= thresholdByte)
                {
                    valid++;
                }
            }

            return (float)valid / masks.Length;
        }
        finally
        {
            Destroy(maskTex);
        }
    }

    private IAgeRegressorRunner ResolveModelRunner()
    {
        if (modelRunner != null)
        {
            var runner = modelRunner as IAgeRegressorRunner;
            if (runner == null)
            {
                Debug.LogWarning("EfficientNetSnapshotPredictor: Assigned modelRunner does not implement IAgeRegressorRunner.");
                return FindAnyModelRunner();
            }
            return runner;
        }

        return FindAnyModelRunner();
    }

    private IAgeRegressorRunner FindAnyModelRunner()
    {
        var behaviours = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
        for (var i = 0; i < behaviours.Length; i++)
        {
            if (behaviours[i] is IAgeRegressorRunner candidate)
            {
                modelRunner = behaviours[i];
                return candidate;
            }
        }

        return null;
    }

    private static float ComputeAverageLuminance(RenderTexture source)
    {
        var tex = ReadbackTexture(source, TextureFormat.RGBA32, false);
        try
        {
            var colors = tex.GetPixels32();
            return ComputeAverageLuminance(colors);
        }
        finally
        {
            Destroy(tex);
        }
    }

    private static float ComputeAverageLuminance(Color32[] colors)
    {
        if (colors == null || colors.Length == 0)
        {
            return BrightnessSentinel;
        }

        double sum = 0.0;
        for (var i = 0; i < colors.Length; i++)
        {
            var c = colors[i];
            var r = c.r / 255f;
            var g = c.g / 255f;
            var b = c.b / 255f;
            sum += 0.2126f * r + 0.7152f * g + 0.0722f * b;
        }

        return (float)(sum / colors.Length);
    }

    private static float ComputeAverageLuminanceMasked(Color32[] colors, Color32[] masks, float threshold)
    {
        if (colors == null || masks == null || colors.Length == 0)
        {
            return BrightnessSentinel;
        }

        var count = Mathf.Min(colors.Length, masks.Length);
        var t = Mathf.Clamp01(threshold);
        double sum = 0.0;
        var valid = 0;
        for (var i = 0; i < count; i++)
        {
            var mask = masks[i].r / 255f;
            if (mask < t)
            {
                continue;
            }

            var c = colors[i];
            var r = c.r / 255f;
            var g = c.g / 255f;
            var b = c.b / 255f;
            sum += 0.2126f * r + 0.7152f * g + 0.0722f * b;
            valid++;
        }

        if (valid <= 0)
        {
            return BrightnessSentinel;
        }

        return (float)(sum / valid);
    }

    private static Texture2D ReadbackTexture(RenderTexture source, TextureFormat format, bool linear)
    {
        var prev = RenderTexture.active;
        RenderTexture.active = source;
        var tex = new Texture2D(source.width, source.height, format, false, linear);
        tex.ReadPixels(new Rect(0f, 0f, source.width, source.height), 0, 0, false);
        tex.Apply(false, false);
        RenderTexture.active = prev;
        return tex;
    }

    private bool TryOverlayLandmarks(RenderTexture target, Texture maskTexture, Vector2Int targetSize)
    {
        if (target == null || maskTexture == null || m_landmarkRunner == null)
        {
            return false;
        }

        if (!m_landmarkRunner.IsReady)
        {
            return false;
        }

        if (!m_landmarkRunner.TryPredict(maskTexture, out var landmarks, out _))
        {
            return false;
        }

        if (landmarks == null || landmarks.Length == 0)
        {
            return false;
        }

        var tex = ReadbackTexture(target, TextureFormat.RGBA32, false);
        var drewAny = false;
        try
        {
            var size = Mathf.Clamp(m_landmarkPixelSize, 1, 9);
            var color = (Color32)m_landmarkColor;
            for (var i = 0; i < landmarks.Length; i++)
            {
                var point = landmarks[i];
                var targetPixel = new Vector2Int(Mathf.RoundToInt(point.x), Mathf.RoundToInt(point.y));
                DrawSquare(tex, targetPixel, size, color, targetSize.y);
                drewAny = true;
            }

            if (drewAny)
            {
                tex.Apply(false, false);
                Graphics.Blit(tex, target);
            }
        }
        finally
        {
            Destroy(tex);
        }

        return drewAny;
    }

    private static void DrawSquare(Texture2D tex, Vector2Int center, int size, Color32 color, int textureHeight)
    {
        if (tex == null || size <= 0)
        {
            return;
        }

        center.y = textureHeight - 1 - center.y;
        var half = size / 2;
        var xMin = Mathf.Clamp(center.x - half, 0, tex.width - 1);
        var xMax = Mathf.Clamp(center.x + half, 0, tex.width - 1);
        var yMin = Mathf.Clamp(center.y - half, 0, tex.height - 1);
        var yMax = Mathf.Clamp(center.y + half, 0, tex.height - 1);

        for (var y = yMin; y <= yMax; y++)
        {
            for (var x = xMin; x <= xMax; x++)
            {
                tex.SetPixel(x, y, color);
            }
        }
    }
}
