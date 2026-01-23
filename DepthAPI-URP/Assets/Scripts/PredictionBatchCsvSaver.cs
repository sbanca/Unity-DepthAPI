using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

public sealed class PredictionBatchCsvSaver : MonoBehaviour
{
    [Header("Input")]
    [SerializeField] private PredictionBatchCollector m_collector;
    [SerializeField] private bool m_autoFindCollector = true;

    [Header("Output")]
    [SerializeField] private string m_folder = "PredictionBatch";
    [SerializeField] private string m_batchPrefix = "batch_";
    [SerializeField] private string m_fileName = "batch.csv";
    [SerializeField] private bool m_includeHeader = true;

    private void OnEnable()
    {
        if (m_collector == null && m_autoFindCollector)
        {
            m_collector = FindAnyObjectByType<PredictionBatchCollector>();
        }

        if (m_collector != null)
        {
            m_collector.BatchReady += HandleBatchReady;
        }
    }

    private void OnDisable()
    {
        if (m_collector != null)
        {
            m_collector.BatchReady -= HandleBatchReady;
        }
    }

    private void HandleBatchReady(PredictionBatchCollector.PredictionBatch batch)
    {
        if (batch.Samples == null || batch.Count == 0)
        {
            return;
        }

        var folder = string.IsNullOrWhiteSpace(m_folder) ? "PredictionBatch" : m_folder;
        var prefix = string.IsNullOrWhiteSpace(m_batchPrefix) ? "batch_" : m_batchPrefix;
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
        var root = Path.Combine(Application.persistentDataPath, folder, $"{prefix}{timestamp}");
        Directory.CreateDirectory(root);

        var fileName = string.IsNullOrWhiteSpace(m_fileName) ? "batch.csv" : m_fileName;
        var path = Path.Combine(root, fileName);

        var sb = new StringBuilder(256 + batch.Count * 128);
        if (m_includeHeader)
        {
            sb.AppendLine("index,hand,mean,logVariance,inferenceMs,brightness,timestamp,imagePath");
        }

        var inv = CultureInfo.InvariantCulture;
        for (var i = 0; i < batch.Count; i++)
        {
            var sample = batch.Samples[i];
            sb.Append(i.ToString(inv)).Append(',');
            sb.Append(sample.Hand).Append(',');
            sb.Append(sample.Mean.ToString("G9", inv)).Append(',');
            sb.Append(sample.LogVariance.ToString("G9", inv)).Append(',');
            sb.Append(sample.InferenceMs.ToString("G9", inv)).Append(',');
            sb.Append(sample.Brightness.ToString("G9", inv)).Append(',');
            sb.Append(sample.Timestamp.ToString("G9", inv)).Append(',');
            sb.Append(EscapeCsv(sample.ImagePath));
            sb.AppendLine();
        }

        File.WriteAllText(path, sb.ToString());
    }

    private static string EscapeCsv(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var needsQuotes = value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0;
        if (!needsQuotes)
        {
            return value;
        }

        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
