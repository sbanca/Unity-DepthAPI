// Copyright (c) Meta Platforms, Inc. and affiliates.

using UnityEngine;

public interface IAgeRegressorRunner
{
    Vector2Int InputSize { get; }
    bool IsReady { get; }
    float LastInferenceMs { get; }

    bool TryPredict(Texture input, out float mean, out float logVariance);
    bool TryPredict(Texture input, out float mean, out float logVariance, out float inferenceMs);
}
