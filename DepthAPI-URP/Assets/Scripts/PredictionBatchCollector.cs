using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using HandSelection = HandScore.HandSelection;

public sealed class PredictionBatchCollector : MonoBehaviour
{

    public readonly struct PredictionSample
    {
        public readonly HandSelection Hand;
        public readonly float Mean;
        public readonly float LogVariance;
        public readonly float InferenceMs;
        public readonly float Timestamp;
        public readonly string ImagePath;

        public PredictionSample(HandSelection hand, float mean, float logVariance, float inferenceMs, float timestamp)
            : this(hand, mean, logVariance, inferenceMs, timestamp, null)
        {
        }

        public PredictionSample(HandSelection hand, float mean, float logVariance, float inferenceMs, float timestamp, string imagePath)
        {
            Hand = hand;
            Mean = mean;
            LogVariance = logVariance;
            InferenceMs = inferenceMs;
            Timestamp = timestamp;
            ImagePath = imagePath;
        }
    }

    public readonly struct PredictionBatch
    {
        public readonly IReadOnlyList<PredictionSample> Samples;
        public readonly IReadOnlyList<byte[]> ImagePngs;
        public readonly string ImageDirectory;
        public readonly int LeftCount;
        public readonly int RightCount;
        public readonly int LeftTargetCount;
        public readonly int RightTargetCount;
        public readonly int TargetCount;

        public int Count => Samples?.Count ?? 0;

        public PredictionBatch(
            IReadOnlyList<PredictionSample> samples,
            IReadOnlyList<byte[]> imagePngs,
            string imageDirectory,
            int leftCount,
            int rightCount,
            int leftTargetCount,
            int rightTargetCount,
            int targetCount)
        {
            Samples = samples;
            ImagePngs = imagePngs;
            ImageDirectory = imageDirectory;
            LeftCount = leftCount;
            RightCount = rightCount;
            LeftTargetCount = leftTargetCount;
            RightTargetCount = rightTargetCount;
            TargetCount = targetCount;
        }
    }

    [Header("Images")]
    [SerializeField] private bool m_savePredictionImages;
    [SerializeField] private string m_imageFolder = "PredictionBatch";
    [SerializeField] private string m_imagePrefix = "Prediction";

    private readonly List<PredictionSample> m_samples = new List<PredictionSample>();
    private readonly List<byte[]> m_sampleImagePngs = new List<byte[]>();
    private string m_currentImageDirectory;
    private int m_imageIndex;
    private bool m_hasBegun;
    private bool m_batchReadyRaised;

    public IReadOnlyList<PredictionSample> Samples => m_samples;
    public IReadOnlyList<byte[]> ImagePngs => m_sampleImagePngs;
    public int TargetCount { get; private set; }
    public int LeftTargetCount { get; private set; }
    public int RightTargetCount { get; private set; }
    public int LeftCount { get; private set; }
    public int RightCount { get; private set; }
    public bool IsCollecting { get; private set; }
    public bool SavePredictionImages => m_savePredictionImages;
    public string ImageDirectory => m_currentImageDirectory;
    public event Action<PredictionBatch> BatchReady;
    public bool HasLastBatch => m_batchReadyRaised;

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
        if (IsCollecting)
        {
            PrepareImageOutputDirectory();
        }
    }

    public void Clear()
    {
        m_samples.Clear();
        m_sampleImagePngs.Clear();
        TargetCount = 0;
        LeftTargetCount = 0;
        RightTargetCount = 0;
        LeftCount = 0;
        RightCount = 0;
        IsCollecting = false;
        m_currentImageDirectory = null;
        m_imageIndex = 0;
        m_hasBegun = false;
        m_batchReadyRaised = false;
    }

    public bool AddSample(HandSelection hand, float mean, float logVariance, float inferenceMs)
    {
        return AddSample(hand, mean, logVariance, inferenceMs, null);
    }

    public bool AddSample(HandSelection hand, float mean, float logVariance, float inferenceMs, byte[] imagePng)
    {
        if (!IsCollecting)
        {
            return false;
        }

        if (hand == HandSelection.Left && LeftCount >= LeftTargetCount)
        {
            return false;
        }

        if (hand == HandSelection.Right && RightCount >= RightTargetCount)
        {
            return false;
        }

        var imagePath = TrySaveImage(hand, imagePng);
        m_samples.Add(new PredictionSample(hand, mean, logVariance, inferenceMs, Time.realtimeSinceStartup, imagePath));
        m_sampleImagePngs.Add(imagePng);
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

        m_batchReadyRaised = true;
        BatchReady?.Invoke(new PredictionBatch(
            m_samples,
            m_sampleImagePngs,
            m_currentImageDirectory,
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
            m_currentImageDirectory,
            LeftCount,
            RightCount,
            LeftTargetCount,
            RightTargetCount,
            TargetCount);
        return true;
    }

    private void PrepareImageOutputDirectory()
    {
        if (!m_savePredictionImages)
        {
            m_currentImageDirectory = null;
            return;
        }

        var folder = string.IsNullOrWhiteSpace(m_imageFolder) ? "PredictionBatch" : m_imageFolder;
        var batchName = $"Batch_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}";
        m_currentImageDirectory = Path.Combine(Application.persistentDataPath, folder, batchName);
        Directory.CreateDirectory(m_currentImageDirectory);
        m_imageIndex = 0;
    }

    private string TrySaveImage(HandSelection hand, byte[] imagePng)
    {
        if (!m_savePredictionImages || imagePng == null || imagePng.Length == 0)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(m_currentImageDirectory))
        {
            PrepareImageOutputDirectory();
        }

        if (string.IsNullOrWhiteSpace(m_currentImageDirectory))
        {
            return null;
        }

        Directory.CreateDirectory(m_currentImageDirectory);
        var prefix = string.IsNullOrWhiteSpace(m_imagePrefix) ? "Prediction" : m_imagePrefix;
        var filename = $"{prefix}_{m_imageIndex:000}_{hand}.png";
        var fullPath = Path.Combine(m_currentImageDirectory, filename);

        try
        {
            File.WriteAllBytes(fullPath, imagePng);
            m_imageIndex++;
            return fullPath;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"PredictionBatchCollector: Failed to save image to {fullPath}. {ex.Message}");
            return null;
        }
    }
}
