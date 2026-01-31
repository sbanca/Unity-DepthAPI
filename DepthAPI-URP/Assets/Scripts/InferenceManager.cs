using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using TMPro;
using UnityEngine.Serialization;
using UnityEngine.UI;

public sealed class InferenceManager : MonoBehaviour
{
    [System.Serializable]
    public class BatchMeansEvent : UnityEvent<float, float> { }

    [Header("Inputs")]
    [SerializeField] private HandScore m_handScore;
    [SerializeField] private EfficientNetSnapshotPredictor m_predictor;
    [SerializeField] private PredictionBatchCollector m_collector;

    [Header("Trigger")]
    [SerializeField, Range(0f, 1f)] private float m_scoreThreshold = 0.8f;
    [SerializeField] private bool m_requireScoreDropToRetrigger = true;

    [Header("Batch")]
    [SerializeField, Min(0), FormerlySerializedAs("m_predictionsPerBatch")] private int m_leftPredictionsPerBatch = 3;
    [SerializeField, Min(0)] private int m_rightPredictionsPerBatch = 3;
    [SerializeField, Min(0f)] private float m_delayMs = 200f;
    [SerializeField, Min(0f)] private float m_predictionTimeoutMs = 2000f;
    [SerializeField, Min(0)] private int m_maxAttemptsPerHand;
    [SerializeField, Min(0f)] private float m_retryDelayMs = 200f;
    [SerializeField] private bool m_disableAfterBatch = true;

    [Header("Output")]
    [SerializeField] private Text m_batchText;
    [SerializeField] private TMP_Text m_batchTmpText;
    [SerializeField] private BatchMeansEvent m_onBatchMeansReady;
    [SerializeField] private UnityEvent m_onBatchCaptured;
    [SerializeField] private UnityEvent m_onBatchInferenceCompleted;

    private bool m_isCollecting;
    private bool m_leftArmed = true;
    private bool m_rightArmed = true;
    private bool m_lastCaptureSucceeded;
    private readonly System.Collections.Generic.List<PendingSample> m_pendingSamples = new System.Collections.Generic.List<PendingSample>();
    private const bool k_DeferInference = true;

    private struct PendingSample
    {
        public HandScore.HandSelection Hand;
        public byte[] Png;
        public float Brightness;
        public int CollectorIndex;
    }

    private void Update()
    {
        if (m_handScore == null || m_predictor == null || m_collector == null)
        {
            return;
        }

        if (m_isCollecting)
        {
            return;
        }

        var leftNeeded = m_leftPredictionsPerBatch > 0;
        var rightNeeded = m_rightPredictionsPerBatch > 0;
        if (!leftNeeded && !rightNeeded)
        {
            return;
        }

        var nextHand = leftNeeded ? HandScore.HandSelection.Left : HandScore.HandSelection.Right;
        m_handScore.SetHandSelection(nextHand);
        var score = m_handScore.Score;

        if (m_requireScoreDropToRetrigger)
        {
            if (nextHand == HandScore.HandSelection.Left && score < m_scoreThreshold)
            {
                m_leftArmed = true;
            }

            if (nextHand == HandScore.HandSelection.Right && score < m_scoreThreshold)
            {
                m_rightArmed = true;
            }
        }

        var handArmed = nextHand == HandScore.HandSelection.Left ? m_leftArmed : m_rightArmed;
        var ready = score >= m_scoreThreshold && (!m_requireScoreDropToRetrigger || handArmed);
        if (ready)
        {
            if (nextHand == HandScore.HandSelection.Left)
            {
                m_leftArmed = false;
            }
            else
            {
                m_rightArmed = false;
            }

            StartCoroutine(CollectBatch());
        }
    }

    private IEnumerator CollectBatch()
    {
        m_isCollecting = true;
        m_pendingSamples.Clear();

        var previousDefer = m_predictor != null ? m_predictor.DeferInference : false;
        if (m_predictor != null)
        {
            m_predictor.DeferInference = k_DeferInference;
        }

        if (m_collector != null)
        {
            m_collector.DeferBatchReady = k_DeferInference;
            m_collector.Begin(m_leftPredictionsPerBatch, m_rightPredictionsPerBatch);
        }

        var delaySeconds = Mathf.Max(0f, m_delayMs) * 0.001f;
        var retryDelaySeconds = Mathf.Max(0f, m_retryDelayMs) * 0.001f;

        if (m_leftPredictionsPerBatch > 0)
        {
            m_handScore.SetHandSelection(HandScore.HandSelection.Left);
            yield return WaitForHandReady(HandScore.HandSelection.Left);
            yield return CaptureHandBatch(HandScore.HandSelection.Left, m_leftPredictionsPerBatch, delaySeconds, retryDelaySeconds);
        }

        if (m_rightPredictionsPerBatch > 0)
        {
            m_handScore.SetHandSelection(HandScore.HandSelection.Right);
            yield return WaitForHandReady(HandScore.HandSelection.Right);
            yield return CaptureHandBatch(HandScore.HandSelection.Right, m_rightPredictionsPerBatch, delaySeconds, retryDelaySeconds);
        }

        m_onBatchCaptured?.Invoke();

        if (k_DeferInference && m_pendingSamples.Count > 0)
        {
            yield return RunDeferredInference();
        }

        m_onBatchInferenceCompleted?.Invoke();
        m_collector.Complete();
        m_isCollecting = false;

        UpdateBatchText();
        InvokeBatchMeansEvent();

        if (m_disableAfterBatch)
        {
            enabled = false;
        }
    }

    private IEnumerator CaptureOnce(HandScore.HandSelection hand)
    {
        if (m_predictor == null || m_collector == null)
        {
            yield break;
        }

        m_lastCaptureSucceeded = false;
        while (m_predictor.IsCapturing)
        {
            yield return null;
        }

        var startVersion = m_predictor.ResultVersion;
        var previousCapturePng = m_predictor.CaptureInputPng;
        m_predictor.CaptureInputPng = true;
        m_predictor.CaptureAndPredict();

        var timeoutSeconds = Mathf.Max(0f, m_predictionTimeoutMs) * 0.001f;
        var startTime = Time.realtimeSinceStartup;
        while (m_predictor.IsCapturing && (timeoutSeconds <= 0f || Time.realtimeSinceStartup - startTime < timeoutSeconds))
        {
            yield return null;
        }

        if (m_predictor.ResultVersion != startVersion)
        {
            byte[] inputPng = null;
            if (m_predictor.CaptureInputPng)
            {
                m_predictor.TryConsumeLastInputPng(m_predictor.ResultVersion, out inputPng);
            }

            if (inputPng == null || inputPng.Length == 0)
            {
                m_lastCaptureSucceeded = false;
            }
            else if (m_collector.AddSampleDeferred(hand, m_predictor.LastBrightness, inputPng, out var sampleIndex))
            {
                m_pendingSamples.Add(new PendingSample
                {
                    Hand = hand,
                    Png = inputPng,
                    Brightness = m_predictor.LastBrightness,
                    CollectorIndex = sampleIndex
                });
                m_lastCaptureSucceeded = true;
            }
        }

        m_predictor.CaptureInputPng = previousCapturePng;
    }

    private IEnumerator CaptureHandBatch(HandScore.HandSelection hand, int count, float delaySeconds, float retryDelaySeconds)
    {
        if (count <= 0)
        {
            yield break;
        }

        var consecutiveFailures = 0;
        while (m_collector != null && m_collector.IsCollecting && GetHandCount(hand) < count)
        {
            if (m_maxAttemptsPerHand > 0 && consecutiveFailures >= m_maxAttemptsPerHand)
            {
                Debug.LogWarning($"InferenceManager: Max consecutive failed attempts reached for {hand} hand; batch may be incomplete.");
                yield break;
            }

            yield return CaptureOnce(hand);
            consecutiveFailures = m_lastCaptureSucceeded ? 0 : consecutiveFailures + 1;

            if (m_collector == null || !m_collector.IsCollecting)
            {
                yield break;
            }

            if (m_lastCaptureSucceeded && delaySeconds > 0f)
            {
                yield return new WaitForSeconds(delaySeconds);
            }
            else if (!m_lastCaptureSucceeded && retryDelaySeconds > 0f)
            {
                yield return new WaitForSeconds(retryDelaySeconds);
            }
        }
    }

    private IEnumerator RunDeferredInference()
    {
        if (m_predictor == null || m_collector == null || m_pendingSamples.Count == 0)
        {
            yield break;
        }

        for (var i = 0; i < m_pendingSamples.Count; i++)
        {
            var pending = m_pendingSamples[i];
            if (pending.Png == null || pending.Png.Length == 0)
            {
                continue;
            }

            Texture2D tex = null;
            try
            {
                tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!tex.LoadImage(pending.Png, false))
                {
                    continue;
                }

                if (m_predictor.TryPredictTexture(tex, out var mean, out var logVar, out var inferenceMs))
                {
                    m_collector.UpdateSampleInference(pending.CollectorIndex, mean, logVar, inferenceMs);
                }
            }
            finally
            {
                if (tex != null)
                {
                    Object.Destroy(tex);
                }
            }

            // Yield to keep main thread responsive during post-batch processing.
            yield return null;
        }

        m_pendingSamples.Clear();
        m_collector.DeferBatchReady = false;
        m_collector.ReleaseBatchReady();
    }

    private int GetHandCount(HandScore.HandSelection hand)
    {
        return hand == HandScore.HandSelection.Left ? m_collector.LeftCount : m_collector.RightCount;
    }

    private IEnumerator WaitForHandReady(HandScore.HandSelection hand)
    {
        if (m_handScore == null)
        {
            yield break;
        }

        while (m_handScore.Score < m_scoreThreshold)
        {
            yield return null;
        }
    }

    public void ResetInference()
    {
        StopAllCoroutines();
        m_isCollecting = false;
        m_leftArmed = true;
        m_rightArmed = true;

        if (m_collector != null)
        {
            m_collector.Clear();
        }

        UpdateBatchText();
        if (m_handScore != null)
        {
            var nextHand = m_leftPredictionsPerBatch > 0 ? HandScore.HandSelection.Left : HandScore.HandSelection.Right;
            m_handScore.SetHandSelection(nextHand);
        }
        enabled = true;
    }

    private void UpdateBatchText()
    {
        if ((m_batchText == null && m_batchTmpText == null) || m_collector == null)
        {
            return;
        }

        var count = m_collector.Count;
        if (count <= 0)
        {
            SetBatchText("Batch: No samples");
            return;
        }

        var leftCount = m_collector.LeftCount;
        var rightCount = m_collector.RightCount;
        var leftTarget = m_collector.LeftTargetCount;
        var rightTarget = m_collector.RightTargetCount;
        var mean = m_collector.AverageMean;
        var logVar = m_collector.AverageLogVariance;
        var stdDev = m_collector.AverageStdDev;
        var inferMs = m_collector.AverageInferenceMs;

        var text =
            $"Batch: {count} (L {leftCount}/{leftTarget}, R {rightCount}/{rightTarget})\n" +
            $"Mean: {mean:0.###}\n" +
            $"LogVar: {logVar:0.###}\n" +
            $"StdDev: {stdDev:0.###}\n" +
            $"Inference: {inferMs:0.##} ms";
        SetBatchText(text);
    }

    private void InvokeBatchMeansEvent()
    {
        if (m_collector == null || m_collector.Count <= 0)
        {
            return;
        }

        var mean = m_collector.AverageMean;
        var logVar = m_collector.AverageLogVariance;
        m_onBatchMeansReady?.Invoke(mean, logVar);
    }

    private void SetBatchText(string text)
    {
        if (m_batchTmpText != null)
        {
            m_batchTmpText.text = text;
        }

        if (m_batchText != null)
        {
            m_batchText.text = text;
        }
    }
}
