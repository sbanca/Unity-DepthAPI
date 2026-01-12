using System.Collections;
using UnityEngine;

public sealed class InferenceManager : MonoBehaviour
{
    [Header("Inputs")]
    [SerializeField] private HandScore m_handScore;
    [SerializeField] private EfficientNetSnapshotPredictor m_predictor;
    [SerializeField] private PredictionBatchCollector m_collector;

    [Header("Trigger")]
    [SerializeField, Range(0f, 1f)] private float m_scoreThreshold = 0.8f;
    [SerializeField] private bool m_requireScoreDropToRetrigger = true;

    [Header("Batch")]
    [SerializeField, Min(1)] private int m_predictionsPerBatch = 3;
    [SerializeField, Min(0f)] private float m_delayMs = 200f;
    [SerializeField, Min(0f)] private float m_predictionTimeoutMs = 2000f;
    [SerializeField] private bool m_disableAfterBatch = true;

    private bool m_isCollecting;
    private bool m_armed = true;

    private void Update()
    {
        if (m_handScore == null || m_predictor == null || m_collector == null)
        {
            return;
        }

        var score = m_handScore.Score;
        if (m_requireScoreDropToRetrigger && score < m_scoreThreshold)
        {
            m_armed = true;
        }

        if (m_isCollecting)
        {
            return;
        }

        if (score >= m_scoreThreshold && (!m_requireScoreDropToRetrigger || m_armed))
        {
            m_armed = false;
            StartCoroutine(CollectBatch());
        }
    }

    private IEnumerator CollectBatch()
    {
        m_isCollecting = true;
        m_collector.Begin(m_predictionsPerBatch);

        var delaySeconds = Mathf.Max(0f, m_delayMs) * 0.001f;
        for (var i = 0; i < m_predictionsPerBatch; i++)
        {
            yield return CaptureOnce();

            if (i < m_predictionsPerBatch - 1 && delaySeconds > 0f)
            {
                yield return new WaitForSeconds(delaySeconds);
            }
        }

        m_collector.Complete();
        m_isCollecting = false;

        if (m_disableAfterBatch)
        {
            enabled = false;
        }
    }

    private IEnumerator CaptureOnce()
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
        m_predictor.CaptureAndPredict();

        var timeoutSeconds = Mathf.Max(0f, m_predictionTimeoutMs) * 0.001f;
        var startTime = Time.realtimeSinceStartup;
        while (m_predictor.IsCapturing && (timeoutSeconds <= 0f || Time.realtimeSinceStartup - startTime < timeoutSeconds))
        {
            yield return null;
        }

        if (m_predictor.ResultVersion != startVersion && m_predictor.HasResult)
        {
            m_collector.AddSample(m_predictor.LastMean, m_predictor.LastLogVariance, m_predictor.LastInferenceMs);
        }
    }
}
