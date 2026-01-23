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
    [SerializeField] private bool m_disableAfterBatch = true;

    [Header("Output")]
    [SerializeField] private Text m_batchText;
    [SerializeField] private TMP_Text m_batchTmpText;
    [SerializeField] private BatchMeansEvent m_onBatchMeansReady;
    [SerializeField] private UnityEvent m_onBatchCompleted;

    private bool m_isCollecting;
    private bool m_leftArmed = true;
    private bool m_rightArmed = true;

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
        m_collector.Begin(m_leftPredictionsPerBatch, m_rightPredictionsPerBatch);

        var delaySeconds = Mathf.Max(0f, m_delayMs) * 0.001f;

        if (m_leftPredictionsPerBatch > 0)
        {
            m_handScore.SetHandSelection(HandScore.HandSelection.Left);
            yield return WaitForHandReady(HandScore.HandSelection.Left);
            yield return CaptureHandBatch(HandScore.HandSelection.Left, m_leftPredictionsPerBatch, delaySeconds);
        }

        if (m_rightPredictionsPerBatch > 0)
        {
            m_handScore.SetHandSelection(HandScore.HandSelection.Right);
            yield return WaitForHandReady(HandScore.HandSelection.Right);
            yield return CaptureHandBatch(HandScore.HandSelection.Right, m_rightPredictionsPerBatch, delaySeconds);
        }

        m_collector.Complete();
        m_isCollecting = false;

        UpdateBatchText();
        InvokeBatchMeansEvent();
        m_onBatchCompleted?.Invoke();

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

        while (m_predictor.IsCapturing)
        {
            yield return null;
        }

        var startVersion = m_predictor.ResultVersion;
        m_predictor.CaptureInputPng = m_collector.SavePredictionImages;
        m_predictor.CaptureAndPredict();

        var timeoutSeconds = Mathf.Max(0f, m_predictionTimeoutMs) * 0.001f;
        var startTime = Time.realtimeSinceStartup;
        while (m_predictor.IsCapturing && (timeoutSeconds <= 0f || Time.realtimeSinceStartup - startTime < timeoutSeconds))
        {
            yield return null;
        }

        if (m_predictor.ResultVersion != startVersion && m_predictor.HasResult)
        {
            byte[] inputPng = null;
            if (m_collector.SavePredictionImages)
            {
                m_predictor.TryConsumeLastInputPng(m_predictor.ResultVersion, out inputPng);
            }

            m_collector.AddSample(hand, m_predictor.LastMean, m_predictor.LastLogVariance, m_predictor.LastInferenceMs, m_predictor.LastBrightness, inputPng);
        }
    }

    private IEnumerator CaptureHandBatch(HandScore.HandSelection hand, int count, float delaySeconds)
    {
        if (count <= 0)
        {
            yield break;
        }

        for (var i = 0; i < count; i++)
        {
            if (!m_collector.IsCollecting)
            {
                yield break;
            }

            yield return CaptureOnce(hand);

            if (i < count - 1 && delaySeconds > 0f)
            {
                yield return new WaitForSeconds(delaySeconds);
            }
        }
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
