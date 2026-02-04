// Copyright (c) Meta Platforms, Inc. and affiliates.

using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;
using Stopwatch = System.Diagnostics.Stopwatch;

public sealed class RunPodAgeRegressorRunner : MonoBehaviour, IAgeRegressorRunner
{
    [Header("RunPod")]
    [SerializeField] private string m_endpointId;
    [SerializeField] private string m_apiKey;
    [SerializeField] private string m_apiKeyEnvVar = "RUNPOD_API_KEY";
    [SerializeField] private string m_baseUrl = "https://api.runpod.ai/v2";
    [SerializeField, Min(0f)] private float m_timeoutMs = 15000f;

    [Header("Input")]
    [SerializeField, Min(1)] private int m_inputWidth = 384;
    [SerializeField, Min(1)] private int m_inputHeight = 384;
    [SerializeField] private bool m_sendAsDataUrl = true;
    [SerializeField] private bool m_encodeAsJpg;
    [SerializeField, Range(1, 100)] private int m_jpgQuality = 90;

    [Header("Output")]
    [SerializeField] private bool m_outputIsStd = true;

    public Vector2Int InputSize => new Vector2Int(m_inputWidth, m_inputHeight);
    public bool IsReady => !string.IsNullOrWhiteSpace(m_endpointId) && !string.IsNullOrWhiteSpace(GetApiKey());
    public float LastInferenceMs { get; private set; } = -1f;

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

        if (input == null)
        {
            Debug.LogError($"{nameof(RunPodAgeRegressorRunner)}.{nameof(TryPredict)}: input texture is null.");
            return false;
        }

        if (!IsReady)
        {
            Debug.LogError($"{nameof(RunPodAgeRegressorRunner)}.{nameof(TryPredict)}: missing endpoint ID or API key.");
            return false;
        }

        if (!TryEncodeTexture(input, out var imageBytes, out var mimeType))
        {
            Debug.LogError($"{nameof(RunPodAgeRegressorRunner)}.{nameof(TryPredict)}: failed to encode input texture.");
            return false;
        }

        var apiKey = GetApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            Debug.LogError($"{nameof(RunPodAgeRegressorRunner)}.{nameof(TryPredict)}: API key is empty.");
            return false;
        }

        var imageBase64 = Convert.ToBase64String(imageBytes);
        if (m_sendAsDataUrl)
        {
            imageBase64 = $"data:{mimeType};base64,{imageBase64}";
        }

        var requestPayload = JsonUtility.ToJson(new RunPodRequest
        {
            input = new RunPodInput
            {
                image_base64 = imageBase64
            }
        });

        var url = BuildUrl();
        if (string.IsNullOrWhiteSpace(url))
        {
            Debug.LogError($"{nameof(RunPodAgeRegressorRunner)}.{nameof(TryPredict)}: invalid URL.");
            return false;
        }

        using var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
        var bodyBytes = Encoding.UTF8.GetBytes(requestPayload);
        request.uploadHandler = new UploadHandlerRaw(bodyBytes);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("Authorization", $"Bearer {apiKey}");
        request.timeout = TimeoutSeconds();

        var stopwatch = Stopwatch.StartNew();
        var op = request.SendWebRequest();
        while (!op.isDone)
        {
            Thread.Sleep(1);
        }
        stopwatch.Stop();

        inferenceMs = (float)stopwatch.Elapsed.TotalMilliseconds;
        LastInferenceMs = inferenceMs;

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"{nameof(RunPodAgeRegressorRunner)}.{nameof(TryPredict)}: request failed ({request.responseCode}): {request.error}");
            return false;
        }

        var responseText = request.downloadHandler.text;
        if (string.IsNullOrWhiteSpace(responseText))
        {
            Debug.LogError($"{nameof(RunPodAgeRegressorRunner)}.{nameof(TryPredict)}: empty response.");
            return false;
        }

        if (!TryParseResponse(responseText, out mean, out logVariance))
        {
            Debug.LogError($"{nameof(RunPodAgeRegressorRunner)}.{nameof(TryPredict)}: failed to parse response.");
            return false;
        }

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

    private string BuildUrl()
    {
        if (string.IsNullOrWhiteSpace(m_endpointId))
        {
            return null;
        }

        var baseUrl = string.IsNullOrWhiteSpace(m_baseUrl) ? "https://api.runpod.ai/v2" : m_baseUrl.Trim().TrimEnd('/');
        var endpoint = m_endpointId.Trim().Trim('/');
        return $"{baseUrl}/{endpoint}/runsync";
    }

    private string GetApiKey()
    {
        if (!string.IsNullOrWhiteSpace(m_apiKey))
        {
            return m_apiKey.Trim();
        }

        if (!string.IsNullOrWhiteSpace(m_apiKeyEnvVar))
        {
            return Environment.GetEnvironmentVariable(m_apiKeyEnvVar)?.Trim();
        }

        return string.Empty;
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

    [Serializable]
    private sealed class RunPodRequest
    {
        public RunPodInput input;
    }

    [Serializable]
    private sealed class RunPodInput
    {
        public string image_base64;
    }
}
