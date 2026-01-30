// Copyright (c) Meta Platforms, Inc. and affiliates.

using System;
using Unity.InferenceEngine;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

public sealed class MaskLandmarkRunner : MonoBehaviour
{
    [Header("Model")]
    [SerializeField] private ModelAsset m_modelAsset;
    [SerializeField] private BackendType m_backend = BackendType.CPU;
    [SerializeField, Min(0)] private int m_overrideInputSize;

    private Worker m_worker;
    private Tensor<float> m_input;
    private Vector2Int m_inputSize;
    private RenderTexture m_resizeBuffer;
    private Texture2D m_readback;

    public Vector2Int InputSize => m_inputSize;
    public bool IsReady => m_worker != null;
    public int LandmarkCount { get; private set; }
    public float LastInferenceMs { get; private set; } = -1f;

    private void Awake()
    {
        if (m_modelAsset != null)
        {
            Initialize(m_modelAsset);
        }
    }

    private void OnDestroy()
    {
        DisposeResources();
    }

    public void Initialize(ModelAsset modelAsset)
    {
        if (modelAsset == null)
        {
            Debug.LogError($"{nameof(MaskLandmarkRunner)}.{nameof(Initialize)}: modelAsset is null.");
            return;
        }

        DisposeResources();

        m_modelAsset = modelAsset;
        var model = ModelLoader.Load(modelAsset);
        if (model == null)
        {
            Debug.LogError($"{nameof(MaskLandmarkRunner)}.{nameof(Initialize)}: failed to load model asset.");
            return;
        }

        m_inputSize = ResolveInputSize(model);
        if (m_inputSize.x <= 0 || m_inputSize.y <= 0)
        {
            Debug.LogError($"{nameof(MaskLandmarkRunner)}.{nameof(Initialize)}: could not resolve input size.");
            return;
        }

        m_input = new Tensor<float>(new TensorShape(1, 1, m_inputSize.y, m_inputSize.x));
        m_worker = new Worker(model, m_backend);

        LandmarkCount = 0;
    }

    public bool TryPredict(Texture mask, out Vector2[] landmarks, out float inferenceMs)
    {
        return TryPredict(mask, out landmarks, out _, out inferenceMs);
    }

    public bool TryPredict(Texture mask, out Vector2[] landmarks, out Vector2[] processedLandmarks, out float inferenceMs)
    {
        landmarks = Array.Empty<Vector2>();
        processedLandmarks = Array.Empty<Vector2>();
        inferenceMs = -1f;
        LastInferenceMs = -1f;

        if (mask == null)
        {
            Debug.LogError($"{nameof(MaskLandmarkRunner)}.{nameof(TryPredict)}: input mask is null.");
            return false;
        }

        if (m_worker == null || m_input == null)
        {
            Debug.LogError($"{nameof(MaskLandmarkRunner)}.{nameof(TryPredict)}: call Initialize first.");
            return false;
        }

        if (!TryPrepareInput(mask))
        {
            Debug.LogError($"{nameof(MaskLandmarkRunner)}.{nameof(TryPredict)}: failed to prepare mask input.");
            return false;
        }

        var stopwatch = Stopwatch.StartNew();
        m_worker.Schedule(m_input);

        using var heatmaps = (m_worker.PeekOutput(0) as Tensor<float>)?.ReadbackAndClone();
        if (heatmaps == null)
        {
            Debug.LogError($"{nameof(MaskLandmarkRunner)}.{nameof(TryPredict)}: output tensor is null.");
            return false;
        }

        var decoded = DecodeHeatmaps(heatmaps);
        LandmarkCount = decoded.Length;
        processedLandmarks = decoded;
        landmarks = MapToOriginal(decoded, mask.width, mask.height);

        stopwatch.Stop();
        inferenceMs = (float)stopwatch.Elapsed.TotalMilliseconds;
        LastInferenceMs = inferenceMs;
        return true;
    }

    private bool TryPrepareInput(Texture mask)
    {
        if (m_inputSize.x <= 0 || m_inputSize.y <= 0)
        {
            return false;
        }

        EnsureResizeBuffer();
        Graphics.Blit(mask, m_resizeBuffer);

        var textureTransform = new TextureTransform().SetDimensions(m_resizeBuffer.width, m_resizeBuffer.height, 1);
        try
        {
            TextureConverter.ToTensor(m_resizeBuffer, m_input, textureTransform);
            return true;
        }
        catch (Exception)
        {
            return TryPopulateInputFromReadback();
        }
    }

    private bool TryPopulateInputFromReadback()
    {
        EnsureReadbackTexture();
        var prev = RenderTexture.active;
        RenderTexture.active = m_resizeBuffer;
        m_readback.ReadPixels(new Rect(0f, 0f, m_resizeBuffer.width, m_resizeBuffer.height), 0, 0, false);
        m_readback.Apply(false, false);
        RenderTexture.active = prev;

        var pixels = m_readback.GetPixels32();
        var width = m_inputSize.x;
        var height = m_inputSize.y;
        if (pixels.Length != width * height)
        {
            return false;
        }

        var idx = 0;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var v = pixels[idx].r * (1f / 255f);
                m_input[0, 0, y, x] = Mathf.Clamp01(v);
                idx++;
            }
        }

        return true;
    }

    private static Vector2[] DecodeHeatmaps(Tensor<float> heatmaps)
    {
        var shape = heatmaps.shape;
        if (shape.rank != 3 && shape.rank != 4)
        {
            throw new InvalidOperationException($"Unexpected heatmap rank {shape.rank}; expected 3 or 4.");
        }

        int batch = shape.rank == 4 ? shape[0] : 1;
        if (batch > 1)
        {
            Debug.LogWarning($"MaskLandmarkRunner: heatmap batch {batch} detected; using first batch only.");
        }

        int landmarks = shape.rank == 4 ? shape[1] : shape[0];
        int height = shape.rank == 4 ? shape[2] : shape[1];
        int width = shape.rank == 4 ? shape[3] : shape[2];

        var coords = new Vector2[landmarks];
        for (var l = 0; l < landmarks; l++)
        {
            var bestIndex = 0;
            var bestValue = float.NegativeInfinity;
            var idx = 0;
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var value = shape.rank == 4 ? heatmaps[0, l, y, x] : heatmaps[l, y, x];
                    if (value > bestValue)
                    {
                        bestValue = value;
                        bestIndex = idx;
                    }
                    idx++;
                }
            }

            var bestY = bestIndex / width;
            var bestX = bestIndex % width;
            coords[l] = new Vector2(bestX, bestY);
        }

        return coords;
    }

    private Vector2[] MapToOriginal(Vector2[] coords, int originalWidth, int originalHeight)
    {
        if (coords == null || coords.Length == 0)
        {
            return Array.Empty<Vector2>();
        }

        if (originalWidth <= 0 || originalHeight <= 0)
        {
            return (Vector2[])coords.Clone();
        }

        var ax = m_inputSize.x > 0 ? m_inputSize.x / (float)originalWidth : 1f;
        var ay = m_inputSize.y > 0 ? m_inputSize.y / (float)originalHeight : 1f;

        var mapped = new Vector2[coords.Length];
        for (var i = 0; i < coords.Length; i++)
        {
            var x = ax != 0f ? coords[i].x / ax : coords[i].x;
            var y = ay != 0f ? coords[i].y / ay : coords[i].y;
            x = Mathf.Clamp(x, 0f, Mathf.Max(0, originalWidth - 1));
            y = Mathf.Clamp(y, 0f, Mathf.Max(0, originalHeight - 1));
            mapped[i] = new Vector2(x, y);
        }

        return mapped;
    }

    private Vector2Int ResolveInputSize(Model model)
    {
        if (model.inputs.Count > 0)
        {
            var shape = model.inputs[0].shape;
            var height = shape.Get(2);
            var width = shape.Get(3);
            if (width > 0 && height > 0)
            {
                return new Vector2Int(width, height);
            }
        }

        if (m_overrideInputSize > 0)
        {
            return new Vector2Int(m_overrideInputSize, m_overrideInputSize);
        }

        return new Vector2Int(0, 0);
    }

    private static int GuessLandmarkCount(TensorShape shape)
    {
        if (shape.rank == 4)
        {
            return shape[1];
        }

        if (shape.rank == 3)
        {
            return shape[0];
        }

        return 0;
    }

    private void EnsureResizeBuffer()
    {
        if (m_resizeBuffer != null && m_resizeBuffer.width == m_inputSize.x && m_resizeBuffer.height == m_inputSize.y)
        {
            return;
        }

        if (m_resizeBuffer != null)
        {
            m_resizeBuffer.Release();
            Destroy(m_resizeBuffer);
        }

        m_resizeBuffer = new RenderTexture(m_inputSize.x, m_inputSize.y, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
        {
            name = "MaskLandmarkInput",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
    }

    private void EnsureReadbackTexture()
    {
        if (m_readback != null && m_readback.width == m_inputSize.x && m_readback.height == m_inputSize.y)
        {
            return;
        }

        if (m_readback != null)
        {
            Destroy(m_readback);
        }

        m_readback = new Texture2D(m_inputSize.x, m_inputSize.y, TextureFormat.RGBA32, false, true);
    }

    private void DisposeResources()
    {
        m_worker?.Dispose();
        m_worker = null;
        m_input?.Dispose();
        m_input = null;

        if (m_resizeBuffer != null)
        {
            m_resizeBuffer.Release();
            Destroy(m_resizeBuffer);
            m_resizeBuffer = null;
        }

        if (m_readback != null)
        {
            Destroy(m_readback);
            m_readback = null;
        }
    }
}
