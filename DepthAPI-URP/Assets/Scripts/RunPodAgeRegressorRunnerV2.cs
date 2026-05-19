// Copyright (c) Meta Platforms, Inc. and affiliates.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;
using Stopwatch = System.Diagnostics.Stopwatch;

public sealed class RunPodAgeRegressorRunnerV2 : MonoBehaviour, IAgeRegressorRunner
{
    private const string k_FirebaseUrl = "https://webage-3176e.web.app/api/runpod";

    [Header("RunPod")]
    [SerializeField, Min(0f)] private float m_timeoutMs = 15000f;

    [Header("Input")]
    [SerializeField, Min(1)] private int m_inputWidth = 384;
    [SerializeField, Min(1)] private int m_inputHeight = 384;
    [SerializeField] private bool m_sendAsDataUrl = false;
    [SerializeField] private bool m_encodeAsJpg;
    [SerializeField, Range(1, 100)] private int m_jpgQuality = 90;

    [Header("RunPod V2")]
    [SerializeField] private string m_selectedModelTag;
    [SerializeField] private bool m_useHandLandmarks = true;
    [SerializeField] private bool m_useHandMasking = true;
    [SerializeField] private bool m_autoRefreshModelsOnEnable = true;
    [SerializeField] private bool m_autoSelectFirstModelWhenEmpty = true;

    [Header("Output")]
    [SerializeField] private bool m_outputIsStd = true;

    [NonSerialized] private string[] m_cachedModelTags = Array.Empty<string>();

    public Vector2Int InputSize => new Vector2Int(m_inputWidth, m_inputHeight);
    public bool IsReady => true;
    public float LastInferenceMs { get; private set; } = -1f;

    public string SelectedModelTag
    {
        get => m_selectedModelTag;
        set => m_selectedModelTag = value?.Trim() ?? string.Empty;
    }

    public bool UseHandMasking
    {
        get => m_useHandMasking;
        set => m_useHandMasking = value;
    }

    public bool UseHandLandmarks
    {
        get => m_useHandLandmarks;
        set => m_useHandLandmarks = value;
    }

    public IReadOnlyList<string> CachedModelTags => m_cachedModelTags;

    private void OnEnable()
    {
        if (m_autoRefreshModelsOnEnable && IsReady)
        {
            StartCoroutine(RefreshModelsOnEnableCoroutine());
        }
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

        if (!TryBuildInferenceRequest(input, out var payload, out var url))
        {
            return false;
        }

        using var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
        var bodyBytes = Encoding.UTF8.GetBytes(payload);
        request.uploadHandler = new UploadHandlerRaw(bodyBytes);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.timeout = TimeoutSeconds();

        var stopwatch = Stopwatch.StartNew();
        var op = request.SendWebRequest();
        while (!op.isDone)
        {
            Thread.Sleep(1);
        }
        stopwatch.Stop();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"{nameof(RunPodAgeRegressorRunnerV2)}.{nameof(TryPredict)}: request failed ({request.responseCode}): {request.error}");
            return false;
        }

        var responseText = request.downloadHandler.text;
        if (string.IsNullOrWhiteSpace(responseText))
        {
            Debug.LogError($"{nameof(RunPodAgeRegressorRunnerV2)}.{nameof(TryPredict)}: empty response.");
            return false;
        }

        if (!TryParseResponse(responseText, out mean, out logVariance))
        {
            Debug.LogError($"{nameof(RunPodAgeRegressorRunnerV2)}.{nameof(TryPredict)}: failed to parse response.");
            return false;
        }

        inferenceMs = TryExtractFloat(responseText, "executionTime", out var executionTimeMs)
            ? executionTimeMs
            : (float)stopwatch.Elapsed.TotalMilliseconds;
        LastInferenceMs = inferenceMs;

        return true;
    }

    public IEnumerator TryPredictAsync(Texture input, Action<bool, float, float, float> onCompleted)
    {
        var success = false;
        var mean = 0f;
        var logVariance = 0f;
        var inferenceMs = -1f;
        LastInferenceMs = -1f;

        if (!TryBuildInferenceRequest(input, out var payload, out var url))
        {
            onCompleted?.Invoke(false, mean, logVariance, inferenceMs);
            yield break;
        }

        using var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
        var bodyBytes = Encoding.UTF8.GetBytes(payload);
        request.uploadHandler = new UploadHandlerRaw(bodyBytes);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.timeout = TimeoutSeconds();

        var stopwatch = Stopwatch.StartNew();
        yield return request.SendWebRequest();
        stopwatch.Stop();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"{nameof(RunPodAgeRegressorRunnerV2)}.{nameof(TryPredictAsync)}: request failed ({request.responseCode}): {request.error}");
            onCompleted?.Invoke(false, mean, logVariance, inferenceMs);
            yield break;
        }

        var responseText = request.downloadHandler.text;
        if (string.IsNullOrWhiteSpace(responseText))
        {
            Debug.LogError($"{nameof(RunPodAgeRegressorRunnerV2)}.{nameof(TryPredictAsync)}: empty response.");
            onCompleted?.Invoke(false, mean, logVariance, inferenceMs);
            yield break;
        }

        if (!TryParseResponse(responseText, out mean, out logVariance))
        {
            Debug.LogError($"{nameof(RunPodAgeRegressorRunnerV2)}.{nameof(TryPredictAsync)}: failed to parse response.");
            onCompleted?.Invoke(false, mean, logVariance, inferenceMs);
            yield break;
        }

        inferenceMs = TryExtractFloat(responseText, "executionTime", out var executionTimeMs)
            ? executionTimeMs
            : (float)stopwatch.Elapsed.TotalMilliseconds;
        LastInferenceMs = inferenceMs;

        success = true;
        onCompleted?.Invoke(success, mean, logVariance, inferenceMs);
    }

    public bool TryRefreshModels(out string[] modelTags, out float requestMs)
    {
        modelTags = Array.Empty<string>();
        requestMs = -1f;

        const string payload = "{\"input\":{\"action\":\"list_models\"}}";

        using var request = new UnityWebRequest(k_FirebaseUrl, UnityWebRequest.kHttpVerbPOST);
        var bodyBytes = Encoding.UTF8.GetBytes(payload);
        request.uploadHandler = new UploadHandlerRaw(bodyBytes);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.timeout = TimeoutSeconds();

        var stopwatch = Stopwatch.StartNew();
        var op = request.SendWebRequest();
        while (!op.isDone)
        {
            Thread.Sleep(1);
        }
        stopwatch.Stop();

        requestMs = (float)stopwatch.Elapsed.TotalMilliseconds;

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"{nameof(RunPodAgeRegressorRunnerV2)}.{nameof(TryRefreshModels)}: request failed ({request.responseCode}): {request.error}");
            return false;
        }

        var responseText = request.downloadHandler.text;
        if (string.IsNullOrWhiteSpace(responseText))
        {
            Debug.LogError($"{nameof(RunPodAgeRegressorRunnerV2)}.{nameof(TryRefreshModels)}: empty response.");
            return false;
        }

        if (!TryParseModelInfo(responseText, out modelTags, out var defaultTag))
        {
            Debug.LogWarning($"{nameof(RunPodAgeRegressorRunnerV2)}.{nameof(TryRefreshModels)}: no model tags found in response.");
            modelTags = Array.Empty<string>();
        }

        m_cachedModelTags = modelTags;
        if (string.IsNullOrWhiteSpace(m_selectedModelTag))
        {
            if (!string.IsNullOrWhiteSpace(defaultTag))
            {
                m_selectedModelTag = defaultTag;
            }
            else if (m_autoSelectFirstModelWhenEmpty && m_cachedModelTags.Length > 0)
            {
                m_selectedModelTag = m_cachedModelTags[0];
            }
        }

        return true;
    }

    public IEnumerator TryRefreshModelsAsync(Action<bool, string[], float> onCompleted)
    {
        var success = false;
        var requestMs = -1f;
        var tags = Array.Empty<string>();

        const string payload = "{\"input\":{\"action\":\"list_models\"}}";

        using var request = new UnityWebRequest(k_FirebaseUrl, UnityWebRequest.kHttpVerbPOST);
        var bodyBytes = Encoding.UTF8.GetBytes(payload);
        request.uploadHandler = new UploadHandlerRaw(bodyBytes);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.timeout = TimeoutSeconds();

        var stopwatch = Stopwatch.StartNew();
        yield return request.SendWebRequest();
        stopwatch.Stop();

        requestMs = (float)stopwatch.Elapsed.TotalMilliseconds;

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"{nameof(RunPodAgeRegressorRunnerV2)}.{nameof(TryRefreshModelsAsync)}: request failed ({request.responseCode}): {request.error}");
            onCompleted?.Invoke(false, tags, requestMs);
            yield break;
        }

        var responseText = request.downloadHandler.text;
        if (string.IsNullOrWhiteSpace(responseText))
        {
            Debug.LogError($"{nameof(RunPodAgeRegressorRunnerV2)}.{nameof(TryRefreshModelsAsync)}: empty response.");
            onCompleted?.Invoke(false, tags, requestMs);
            yield break;
        }

        if (!TryParseModelInfo(responseText, out tags, out var defaultTag))
        {
            Debug.LogWarning($"{nameof(RunPodAgeRegressorRunnerV2)}.{nameof(TryRefreshModelsAsync)}: no model tags found in response.");
            tags = Array.Empty<string>();
        }

        m_cachedModelTags = tags;
        if (string.IsNullOrWhiteSpace(m_selectedModelTag))
        {
            if (!string.IsNullOrWhiteSpace(defaultTag))
            {
                m_selectedModelTag = defaultTag;
            }
            else if (m_autoSelectFirstModelWhenEmpty && m_cachedModelTags.Length > 0)
            {
                m_selectedModelTag = m_cachedModelTags[0];
            }
        }

        success = true;
        onCompleted?.Invoke(success, tags, requestMs);
    }

    [ContextMenu("RunPod V2/Refresh Models")]
    private void RefreshModelsContextMenu()
    {
        if (TryRefreshModels(out var tags, out var requestMs))
        {
            Debug.Log($"{nameof(RunPodAgeRegressorRunnerV2)}: refreshed {tags.Length} model(s) in {requestMs:0.##} ms.");
        }
    }

    private IEnumerator RefreshModelsOnEnableCoroutine()
    {
        yield return TryRefreshModelsAsync((success, tags, _) =>
        {
            if (!success)
            {
                return;
            }

            Debug.Log($"{nameof(RunPodAgeRegressorRunnerV2)}: loaded {tags.Length} model(s).");
        });
    }

    private bool TryBuildInferenceRequest(Texture input, out string payload, out string url)
    {
        payload = null;
        url = null;

        if (input == null)
        {
            Debug.LogError($"{nameof(RunPodAgeRegressorRunnerV2)}.{nameof(TryPredict)}: input texture is null.");
            return false;
        }

        if (!TryEncodeTexture(input, out var imageBytes, out var mimeType))
        {
            Debug.LogError($"{nameof(RunPodAgeRegressorRunnerV2)}.{nameof(TryPredict)}: failed to encode input texture.");
            return false;
        }

        var imageBase64 = Convert.ToBase64String(imageBytes);
        if (m_sendAsDataUrl)
        {
            imageBase64 = $"data:{mimeType};base64,{imageBase64}";
        }

        var requestPayload = new RunPodInferenceRequest
        {
            input = new RunPodInferenceInput
            {
                model_tag = string.IsNullOrWhiteSpace(m_selectedModelTag) ? null : m_selectedModelTag.Trim(),
                use_hand_landmarks = m_useHandLandmarks,
                use_hand_masking = m_useHandMasking,
                image_base64 = imageBase64
            }
        };

        payload = JsonUtility.ToJson(requestPayload);
        url = k_FirebaseUrl;
        return true;
    }

    private int TimeoutSeconds()
    {
        if (m_timeoutMs <= 0f)
        {
            return 0;
        }

        return Mathf.Max(1, Mathf.CeilToInt(m_timeoutMs / 1000f));
    }

    private bool TryEncodeTexture(Texture input, out byte[] imageBytes, out string mimeType)
    {
        imageBytes = null;
        mimeType = "image/png";
        if (input == null)
        {
            return false;
        }

        var width = input.width;
        var height = input.height;
        if (width <= 0 || height <= 0)
        {
            return false;
        }

        var rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
        try
        {
            Graphics.Blit(input, rt);
            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            try
            {
                tex.ReadPixels(new Rect(0f, 0f, width, height), 0, 0, false);
                tex.Apply(false, false);

                if (m_encodeAsJpg)
                {
                    mimeType = "image/jpeg";
                    imageBytes = tex.EncodeToJPG(Mathf.Clamp(m_jpgQuality, 1, 100));
                }
                else
                {
                    mimeType = "image/png";
                    imageBytes = tex.EncodeToPNG();
                }
            }
            finally
            {
                Destroy(tex);
                RenderTexture.active = previous;
            }
        }
        finally
        {
            RenderTexture.ReleaseTemporary(rt);
        }

        return imageBytes != null && imageBytes.Length > 0;
    }

    private bool TryParseResponse(string json, out float mean, out float logVariance)
    {
        mean = 0f;
        logVariance = 0f;

        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        var hasMean = TryExtractFloat(json, "age", out mean);
        if (!hasMean)
        {
            hasMean = TryExtractFloat(json, "mean", out mean);
        }

        var hasLogVariance = TryExtractFloat(json, "logVariance", out logVariance)
            || TryExtractFloat(json, "log_variance", out logVariance);

        var hasStd = TryExtractFloat(json, "std", out var std);
        var hasVariance = TryExtractFloat(json, "variance", out var variance);

        if (!hasMean)
        {
            return false;
        }

        if (hasLogVariance)
        {
            return true;
        }

        if (m_outputIsStd && hasStd && std > 0f)
        {
            logVariance = 2f * Mathf.Log(std);
            return true;
        }

        if (hasVariance && variance > 0f)
        {
            logVariance = Mathf.Log(variance);
            return true;
        }

        if (hasStd && std > 0f)
        {
            logVariance = 2f * Mathf.Log(std);
            return true;
        }

        return false;
    }

    private static bool TryParseModelInfo(string json, out string[] tags, out string defaultTag)
    {
        defaultTag = null;
        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<string>();

        TryExtractString(json, "default_tag", out defaultTag);
        AddMatches(json, "tag", unique, ordered);
        AddMatches(json, "model_tag", unique, ordered);
        AddArrayValues(json, "model_tags", unique, ordered);
        AddArrayValues(json, "available_models", unique, ordered);

        if (!string.IsNullOrWhiteSpace(defaultTag) && unique.Add(defaultTag))
        {
            ordered.Insert(0, defaultTag);
        }

        tags = ordered.ToArray();
        return tags.Length > 0;
    }

    private static void AddMatches(string json, string key, ISet<string> unique, ICollection<string> ordered)
    {
        if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        var pattern = $"\\\"{Regex.Escape(key)}\\\"\\s*:\\s*\\\"([^\\\"]+)\\\"";
        var matches = Regex.Matches(json, pattern);
        for (var i = 0; i < matches.Count; i++)
        {
            var value = matches[i].Groups[1].Value?.Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (unique.Add(value))
            {
                ordered.Add(value);
            }
        }
    }

    private static void AddArrayValues(string json, string key, ISet<string> unique, ICollection<string> ordered)
    {
        if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        var pattern = $"\\\"{Regex.Escape(key)}\\\"\\s*:\\s*\\[(.*?)\\]";
        var match = Regex.Match(json, pattern, RegexOptions.Singleline);
        if (!match.Success)
        {
            return;
        }

        var itemMatches = Regex.Matches(match.Groups[1].Value, "\\\"([^\\\"]+)\\\"");
        for (var i = 0; i < itemMatches.Count; i++)
        {
            var value = itemMatches[i].Groups[1].Value?.Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (unique.Add(value))
            {
                ordered.Add(value);
            }
        }
    }

    private static bool TryExtractFloat(string json, string key, out float value)
    {
        value = 0f;
        if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        var pattern = $"\\\"{Regex.Escape(key)}\\\"\\s*:\\s*(-?\\d+(?:\\.\\d+)?(?:[eE][+-]?\\d+)?)";
        var match = Regex.Match(json, pattern);
        if (!match.Success)
        {
            return false;
        }

        return float.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryExtractString(string json, string key, out string value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        var pattern = $"\\\"{Regex.Escape(key)}\\\"\\s*:\\s*\\\"([^\\\"]+)\\\"";
        var match = Regex.Match(json, pattern);
        if (!match.Success)
        {
            return false;
        }

        value = match.Groups[1].Value?.Trim();
        return !string.IsNullOrWhiteSpace(value);
    }

    [Serializable]
    private sealed class RunPodInferenceRequest
    {
        public RunPodInferenceInput input;
    }

    [Serializable]
    private sealed class RunPodInferenceInput
    {
        public string model_tag;
        public bool use_hand_landmarks;
        public bool use_hand_masking;
        public string image_base64;
    }
}
