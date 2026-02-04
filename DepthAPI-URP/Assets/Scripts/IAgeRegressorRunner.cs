// Copyright (c) Meta Platforms, Inc. and affiliates.

using System.Collections;
using UnityEngine;

public interface IAgeRegressorRunner
{
    Vector2Int InputSize { get; }
    bool IsReady { get; }
    float LastInferenceMs { get; }

    bool TryPredict(Texture input, out float mean, out float logVariance);
    bool TryPredict(Texture input, out float mean, out float logVariance, out float inferenceMs);
    IEnumerator TryPredictAsync(Texture input, System.Action<bool, float, float, float> onCompleted);
}
