using System.Collections.Generic;
using UnityEngine;

public sealed class PredictionBatchCollector : MonoBehaviour
{
    public readonly struct PredictionSample
    {
        public readonly float Mean;
        public readonly float LogVariance;
        public readonly float InferenceMs;
        public readonly float Timestamp;

        public PredictionSample(float mean, float logVariance, float inferenceMs, float timestamp)
        {
            Mean = mean;
            LogVariance = logVariance;
            InferenceMs = inferenceMs;
            Timestamp = timestamp;
        }
    }

    private readonly List<PredictionSample> m_samples = new List<PredictionSample>();

    public IReadOnlyList<PredictionSample> Samples => m_samples;
    public int TargetCount { get; private set; }
    public bool IsCollecting { get; private set; }

    public int Count => m_samples.Count;

    public void Begin(int targetCount)
    {
        Clear();
        TargetCount = Mathf.Max(1, targetCount);
        IsCollecting = true;
    }

    public void Clear()
    {
        m_samples.Clear();
        TargetCount = 0;
        IsCollecting = false;
    }

    public bool AddSample(float mean, float logVariance, float inferenceMs)
    {
        if (!IsCollecting)
        {
            return false;
        }

        m_samples.Add(new PredictionSample(mean, logVariance, inferenceMs, Time.realtimeSinceStartup));
        if (m_samples.Count >= TargetCount)
        {
            IsCollecting = false;
        }

        return true;
    }

    public void Complete()
    {
        IsCollecting = false;
    }

    public float AverageMean => GetAverage(m_samples, s => s.Mean);
    public float AverageLogVariance => GetAverage(m_samples, s => s.LogVariance);
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
}
