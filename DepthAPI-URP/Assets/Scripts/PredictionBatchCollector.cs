using System;
using System.Collections.Generic;
using UnityEngine;
using HandSelection = HandScore.HandSelection;

public sealed class PredictionBatchCollector : MonoBehaviour
{
    public const float BrightnessSentinel = -1f;

    private const int BatchIdLength = 8;

    public sealed class PredictionSample
    {
        public HandSelection Hand { get; }
        public float Mean { get; private set; }
        public float LogVariance { get; private set; }
        public float InferenceMs { get; private set; }
        public float Brightness { get; }
        public float Timestamp { get; }
        public string ImagePath { get; }
        public bool HasInference { get; private set; }

        public PredictionSample(
            HandSelection hand,
            float mean,
            float logVariance,
            float inferenceMs,
            float timestamp,
            float brightness = BrightnessSentinel,
            string imagePath = null,
            bool hasInference = true)
        {
            Hand = hand;
            Mean = mean;
            LogVariance = logVariance;
            InferenceMs = inferenceMs;
            Brightness = brightness;
            Timestamp = timestamp;
            ImagePath = imagePath;
            HasInference = hasInference;
        }

        public void SetInference(float mean, float logVariance, float inferenceMs)
        {
            Mean = mean;
            LogVariance = logVariance;
            InferenceMs = inferenceMs;
            HasInference = true;
        }
    }

    public readonly struct PredictionBatch
    {
        public readonly IReadOnlyList<PredictionSample> Samples;
        public readonly IReadOnlyList<byte[]> ImagePngs;
        public readonly string BatchId;
        public readonly string BatchTimestamp;
        public readonly int LeftCount;
        public readonly int RightCount;
        public readonly int LeftTargetCount;
        public readonly int RightTargetCount;
        public readonly int TargetCount;

        public int Count => Samples?.Count ?? 0;

        public PredictionBatch(
            IReadOnlyList<PredictionSample> samples,
            IReadOnlyList<byte[]> imagePngs,
            string batchId,
            string batchTimestamp,
            int leftCount,
            int rightCount,
            int leftTargetCount,
            int rightTargetCount,
            int targetCount)
        {
            Samples = samples;
            ImagePngs = imagePngs;
            BatchId = batchId;
            BatchTimestamp = batchTimestamp;
            LeftCount = leftCount;
            RightCount = rightCount;
            LeftTargetCount = leftTargetCount;
            RightTargetCount = rightTargetCount;
            TargetCount = targetCount;
        }
    }

    [Header("Images")]
    [SerializeField] private bool m_savePredictionImages;

    private readonly List<PredictionSample> m_samples = new List<PredictionSample>();
    private readonly List<byte[]> m_sampleImagePngs = new List<byte[]>();
    private bool m_hasBegun;
    private bool m_batchReadyRaised;
    private bool m_deferBatchReady;
    private bool m_pendingBatchReady;

    public IReadOnlyList<PredictionSample> Samples => m_samples;
    public IReadOnlyList<byte[]> ImagePngs => m_sampleImagePngs;
    public int TargetCount { get; private set; }
    public int LeftTargetCount { get; private set; }
    public int RightTargetCount { get; private set; }
    public int LeftCount { get; private set; }
    public int RightCount { get; private set; }
    public bool IsCollecting { get; private set; }
    public bool SavePredictionImages => m_savePredictionImages;
    public string BatchId { get; private set; }
    public string BatchTimestamp { get; private set; }
    public event Action<PredictionBatch> BatchReady;
    public bool HasLastBatch => m_batchReadyRaised;
    public bool DeferBatchReady
    {
        get => m_deferBatchReady;
        set => m_deferBatchReady = value;
    }

    public int Count => m_samples.Count;

    public void Begin(int targetCount)
    {
        Begin(targetCount, targetCount);
    }

    public void Begin(int leftTargetCount, int rightTargetCount)
    {
        Clear();
        LeftTargetCount = Mathf.Max(0, leftTargetCount);
        RightTargetCount = Mathf.Max(0, rightTargetCount);
        TargetCount = LeftTargetCount + RightTargetCount;
        IsCollecting = TargetCount > 0;
        m_hasBegun = IsCollecting;
        m_batchReadyRaised = false;
    }

    public void Clear()
    {
        m_samples.Clear();
        m_sampleImagePngs.Clear();
        BatchId = GenerateBatchId();
        BatchTimestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff", System.Globalization.CultureInfo.InvariantCulture);
        TargetCount = 0;
        LeftTargetCount = 0;
        RightTargetCount = 0;
        LeftCount = 0;
        RightCount = 0;
        IsCollecting = false;
        m_hasBegun = false;
        m_batchReadyRaised = false;
        m_pendingBatchReady = false;
    }

    public bool AddSample(HandSelection hand, float mean, float logVariance, float inferenceMs)
    {
        return AddSample(hand, mean, logVariance, inferenceMs, BrightnessSentinel, null);
    }

    public bool AddSample(HandSelection hand, float mean, float logVariance, float inferenceMs, float brightness)
    {
        return AddSample(hand, mean, logVariance, inferenceMs, brightness, null);
    }

    public bool AddSample(HandSelection hand, float mean, float logVariance, float inferenceMs, float brightness, byte[] imagePng)
    {
        return AddSampleInternal(hand, mean, logVariance, inferenceMs, brightness, imagePng, true, out _);
    }

    public bool AddSampleDeferred(HandSelection hand, float brightness, byte[] imagePng, out int sampleIndex)
    {
        return AddSampleInternal(hand, 0f, 0f, 0f, brightness, imagePng, false, out sampleIndex);
    }

    public bool UpdateSampleInference(int index, float mean, float logVariance, float inferenceMs)
    {
        if (index < 0 || index >= m_samples.Count)
        {
            return false;
        }

        var sample = m_samples[index];
        sample.SetInference(mean, logVariance, inferenceMs);
        return true;
    }

    private bool AddSampleInternal(HandSelection hand, float mean, float logVariance, float inferenceMs, float brightness, byte[] imagePng, bool hasInference, out int sampleIndex)
    {
        if (!IsCollecting)
        {
            sampleIndex = -1;
            return false;
        }

        if (hand == HandSelection.Left && LeftCount >= LeftTargetCount)
        {
            sampleIndex = -1;
            return false;
        }

        if (hand == HandSelection.Right && RightCount >= RightTargetCount)
        {
            sampleIndex = -1;
            return false;
        }

        m_samples.Add(new PredictionSample(hand, mean, logVariance, inferenceMs, Time.realtimeSinceStartup, brightness, null, hasInference));
        m_sampleImagePngs.Add(imagePng);
        sampleIndex = m_samples.Count - 1;
        if (hand == HandSelection.Left)
        {
            LeftCount++;
        }
        else
        {
            RightCount++;
        }

        if ((LeftCount >= LeftTargetCount) && (RightCount >= RightTargetCount))
        {
            IsCollecting = false;
            TryRaiseBatchReady();
        }

        return true;
    }

    public void Complete()
    {
        IsCollecting = false;
        TryRaiseBatchReady();
    }

    public float AverageMean => GetAverage(m_samples, s => s.Mean);
    public float AverageLogVariance => Mathf.Log(Mathf.Max(1e-6f, AverageVariance));
    public float AverageInferenceMs => GetAverage(m_samples, s => s.InferenceMs);
    public float AverageVariance => GetAverage(m_samples, s => Mathf.Exp(s.LogVariance));
    public float AverageStdDev => Mathf.Sqrt(Mathf.Max(0f, AverageVariance));

    private static float GetAverage(IReadOnlyList<PredictionSample> samples, System.Func<PredictionSample, float> selector)
    {
        if (samples == null || samples.Count == 0)
        {
            return 0f;
        }

        var sum = 0f;
        for (var i = 0; i < samples.Count; i++)
        {
            sum += selector(samples[i]);
        }

        return sum / samples.Count;
    }

    private void TryRaiseBatchReady()
    {
        if (!m_hasBegun || m_batchReadyRaised)
        {
            return;
        }

        if (m_deferBatchReady)
        {
            m_pendingBatchReady = true;
            return;
        }

        RaiseBatchReadyNow();
    }

    public void ReleaseBatchReady()
    {
        if (m_pendingBatchReady && !m_batchReadyRaised)
        {
            m_pendingBatchReady = false;
            RaiseBatchReadyNow();
        }
    }

    private void RaiseBatchReadyNow()
    {
        if (m_batchReadyRaised)
        {
            return;
        }

        m_batchReadyRaised = true;
        BatchReady?.Invoke(new PredictionBatch(
            m_samples,
            m_sampleImagePngs,
            BatchId,
            BatchTimestamp,
            LeftCount,
            RightCount,
            LeftTargetCount,
            RightTargetCount,
            TargetCount));
    }

    public bool TryGetLastBatch(out PredictionBatch batch)
    {
        if (!m_batchReadyRaised)
        {
            batch = default;
            return false;
        }

        batch = new PredictionBatch(
            m_samples,
            m_sampleImagePngs,
            BatchId,
            BatchTimestamp,
            LeftCount,
            RightCount,
            LeftTargetCount,
            RightTargetCount,
            TargetCount);
        return true;
    }

    private static string GenerateBatchId()
    {
        var guid = Guid.NewGuid().ToString("N");
        return guid.Substring(0, BatchIdLength);
    }
}
