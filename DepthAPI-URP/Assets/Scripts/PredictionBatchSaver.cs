using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

public sealed class PredictionBatchSaver : MonoBehaviour
{
    [Header("Input")]
    [SerializeField] private PredictionBatchCollector m_collector;
    [SerializeField] private bool m_autoFindCollector = true;

    [Header("Output")]
    [SerializeField] private string m_folder = "PredictionBatch";
    [SerializeField] private string m_batchPrefix = "batch_";
    [SerializeField] private string m_fileName = "batch.csv";
    [SerializeField] private bool m_includeHeader = true;
    [SerializeField] private bool m_saveImages = true;
    [SerializeField] private string m_imagePrefix = "Prediction";

    public void SaveLatestBatch()
    {
        if (!TryResolveCollector())
        {
            return;
        }

        if (!m_collector.TryGetLastBatch(out var batch))
        {
            return;
        }

        SaveBatch(batch);
    }

    private void SaveBatch(PredictionBatchCollector.PredictionBatch batch)
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

        var imagePaths = m_saveImages ? SaveImages(batch, root) : null;

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
            var imagePath = imagePaths != null && i < imagePaths.Length ? imagePaths[i] : sample.ImagePath;
            sb.Append(EscapeCsv(imagePath));
            sb.AppendLine();
        }

        File.WriteAllText(path, sb.ToString());
    }

    private bool TryResolveCollector()
    {
        if (m_collector != null)
        {
            return true;
        }

        if (m_autoFindCollector)
        {
            m_collector = FindAnyObjectByType<PredictionBatchCollector>();
        }

        return m_collector != null;
    }

    private string[] SaveImages(PredictionBatchCollector.PredictionBatch batch, string root)
    {
        var paths = new string[batch.Count];
        var prefix = string.IsNullOrWhiteSpace(m_imagePrefix) ? "Prediction" : m_imagePrefix;

        for (var i = 0; i < batch.Count; i++)
        {
            var sample = batch.Samples[i];
            var png = (batch.ImagePngs != null && i < batch.ImagePngs.Count) ? batch.ImagePngs[i] : null;

            if (png != null && png.Length > 0)
            {
                var fileName = $"{prefix}_{i:000}_{sample.Hand}.png";
                var fullPath = Path.Combine(root, fileName);
                try
                {
                    File.WriteAllBytes(fullPath, png);
                    paths[i] = fullPath;
                    continue;
                }
                catch
                {
                    // fall through to try copying an existing file if available
                }
            }

            if (!string.IsNullOrWhiteSpace(sample.ImagePath) && File.Exists(sample.ImagePath))
            {
                var existingName = Path.GetFileName(sample.ImagePath);
                var targetName = string.IsNullOrWhiteSpace(existingName)
                    ? $"{prefix}_{i:000}_{sample.Hand}.png"
                    : existingName;
                var targetPath = Path.Combine(root, targetName);
                try
                {
                    File.Copy(sample.ImagePath, targetPath, true);
                    paths[i] = targetPath;
                }
                catch
                {
                    paths[i] = sample.ImagePath;
                }
            }
        }

        return paths;
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
