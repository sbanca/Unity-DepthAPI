// Copyright (c) Meta Platforms, Inc. and affiliates.

using System;
using System.Collections.Generic;
using Unity.InferenceEngine;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

public sealed class EfficientNetAgeRegressorRunner : MonoBehaviour, IAgeRegressorRunner
{
    [Header("Model")]
    [SerializeField] private ModelAsset m_modelAsset;
    [SerializeField] private BackendType m_backend = BackendType.CPU;
    [SerializeField] private string m_variant = "b2";
    [SerializeField, Min(0)] private int m_overrideInputSize;

    private Worker m_worker;
    private Tensor<float> m_input;
    private Vector2Int m_inputSize;
    private bool m_outputsSplit;

    private static readonly Dictionary<string, int> s_variantInputSizes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        { "b0", 224 },
        { "b1", 240 },
        { "b2", 260 },
        { "b3", 300 },
        { "b4", 380 },
        { "b5", 456 },
        { "b6", 528 },
        { "b7", 600 },
        { "v2_s", 384 },
        { "v2_m", 480 },
        { "v2_l", 480 },
    };

    public Vector2Int InputSize => m_inputSize;
    public int OutputSize => 2;
    public bool IsReady => m_worker != null;
    public float LastInferenceMs { get; private set; } = -1f;

    private void Awake()
    {
        if (m_modelAsset != null)
        {
            Initialize(m_modelAsset, m_variant);
        }
    }

    private void OnDestroy()
    {
        DisposeResources();
    }

    public void Initialize(ModelAsset modelAsset, string variant = null)
    {
        if (modelAsset == null)
        {
            Debug.LogError($"{nameof(EfficientNetAgeRegressorRunner)}.{nameof(Initialize)}: modelAsset is null.");
            return;
        }

        DisposeResources();

        m_modelAsset = modelAsset;
        if (!string.IsNullOrWhiteSpace(variant))
        {
            m_variant = variant.Trim();
        }

        var model = ModelLoader.Load(modelAsset);
        if (model == null)
        {
            Debug.LogError($"{nameof(EfficientNetAgeRegressorRunner)}.{nameof(Initialize)}: failed to load model asset.");
            return;
        }

        m_outputsSplit = model.outputs.Count >= 2;
        m_inputSize = ResolveInputSize(model, m_variant);
        if (m_inputSize.x <= 0 || m_inputSize.y <= 0)
        {
            Debug.LogError($"{nameof(EfficientNetAgeRegressorRunner)}.{nameof(Initialize)}: could not resolve input size.");
            return;
        }

        m_input = new Tensor<float>(new TensorShape(1, 3, m_inputSize.y, m_inputSize.x));
        m_worker = new Worker(model, m_backend);
    }

    public bool TryPredict(Texture input, out float mean, out float logVariance)
    {
        return TryPredict(input, out mean, out logVariance, out _);
    }

    public bool TryPredict(Texture input, out float mean, out float logVariance, out float inferenceMs)
    {
        mean = 0f;
        logVariance = 0f;
        inferenceMs = -1f;
        LastInferenceMs = -1f;

        if (input == null)
        {
            Debug.LogError($"{nameof(EfficientNetAgeRegressorRunner)}.{nameof(TryPredict)}: input texture is null.");
            return false;
        }

        if (m_worker == null || m_input == null)
        {
            Debug.LogError($"{nameof(EfficientNetAgeRegressorRunner)}.{nameof(TryPredict)}: call Initialize first.");
            return false;
        }

        var stopwatch = Stopwatch.StartNew();
        TextureConverter.ToTensor(input, m_input);
        m_worker.Schedule(m_input);

        if (m_outputsSplit)
        {
            using var meanTensor = (m_worker.PeekOutput(0) as Tensor<float>)?.ReadbackAndClone();
            using var logVarTensor = (m_worker.PeekOutput(1) as Tensor<float>)?.ReadbackAndClone();
            if (meanTensor == null || logVarTensor == null || meanTensor.shape.length < 1 || logVarTensor.shape.length < 1)
            {
                Debug.LogError($"{nameof(EfficientNetAgeRegressorRunner)}.{nameof(TryPredict)}: invalid output tensors.");
                return false;
            }

            mean = meanTensor[0];
            logVariance = logVarTensor[0];
            stopwatch.Stop();
            inferenceMs = (float)stopwatch.Elapsed.TotalMilliseconds;
            LastInferenceMs = inferenceMs;
            return true;
        }

        using var outputTensor = (m_worker.PeekOutput(0) as Tensor<float>)?.ReadbackAndClone();
        if (outputTensor == null || outputTensor.shape.length < 2)
        {
            Debug.LogError($"{nameof(EfficientNetAgeRegressorRunner)}.{nameof(TryPredict)}: expected output size 2.");
            return false;
        }

        mean = outputTensor[0];
        logVariance = outputTensor[1];
        stopwatch.Stop();
        inferenceMs = (float)stopwatch.Elapsed.TotalMilliseconds;
        LastInferenceMs = inferenceMs;
        return true;
    }

    public static bool TryGetInputSize(string variant, out int size)
    {
        size = 0;
        if (string.IsNullOrWhiteSpace(variant))
        {
            return false;
        }

        return s_variantInputSizes.TryGetValue(variant.Trim(), out size);
    }

    private Vector2Int ResolveInputSize(Model model, string variant)
    {
        var inputShape = model.inputs[0].shape;
        var height = inputShape.Get(2);
        var width = inputShape.Get(3);
        if (width > 0 && height > 0)
        {
            return new Vector2Int(width, height);
        }

        if (m_overrideInputSize > 0)
        {
            return new Vector2Int(m_overrideInputSize, m_overrideInputSize);
        }

        if (!string.IsNullOrWhiteSpace(variant) && TryGetInputSize(variant, out var size))
        {
            return new Vector2Int(size, size);
        }

        return new Vector2Int(224, 224);
    }

    private void DisposeResources()
    {
        m_worker?.Dispose();
        m_worker = null;
        m_input?.Dispose();
        m_input = null;
    }
}
