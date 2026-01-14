using System.Collections.Generic;
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

        public PredictionSample(HandSelection hand, float mean, float logVariance, float inferenceMs, float timestamp)
        {
            Hand = hand;
            Mean = mean;
            LogVariance = logVariance;
            InferenceMs = inferenceMs;
            Timestamp = timestamp;
        }
    }

    private readonly List<PredictionSample> m_samples = new List<PredictionSample>();

    public IReadOnlyList<PredictionSample> Samples => m_samples;
    public int TargetCount { get; private set; }
    public int LeftTargetCount { get; private set; }
    public int RightTargetCount { get; private set; }
    public int LeftCount { get; private set; }
    public int RightCount { get; private set; }
    public bool IsCollecting { get; private set; }

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
    }

    public void Clear()
    {
        m_samples.Clear();
        TargetCount = 0;
        LeftTargetCount = 0;
        RightTargetCount = 0;
        LeftCount = 0;
        RightCount = 0;
        IsCollecting = false;
    }

    public bool AddSample(HandSelection hand, float mean, float logVariance, float inferenceMs)
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

        m_samples.Add(new PredictionSample(hand, mean, logVariance, inferenceMs, Time.realtimeSinceStartup));
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
        }

        return true;
    }

    public void Complete()
    {
        IsCollecting = false;
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
}
