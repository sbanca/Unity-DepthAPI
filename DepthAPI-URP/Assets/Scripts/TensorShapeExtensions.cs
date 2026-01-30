// Copyright (c) Meta Platforms, Inc. and affiliates.

using Unity.InferenceEngine;

public static class TensorShapeExtensions
{
    public static int Get(this TensorShape shape, int axis)
    {
        var rank = shape.rank;
        if (rank <= 0)
        {
            return 0;
        }

        if (axis < 0)
        {
            axis = rank + axis;
        }

        if (axis < 0 || axis >= rank)
        {
            return 0;
        }

        return shape[axis];
    }
}
