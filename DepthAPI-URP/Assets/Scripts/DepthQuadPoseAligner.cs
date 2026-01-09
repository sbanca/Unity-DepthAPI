// Copyright (c) Meta Platforms, Inc. and affiliates.

using PassthroughCameraSamples;
using UnityEngine;

public class DepthQuadPoseAligner : MonoBehaviour
{
    [SerializeField] private Transform depthSamplingQuad;
    [SerializeField] private Transform depthPreviewQuad;
    [SerializeField] private RectTransform canvasRect;
    [SerializeField, Min(0f)] private float quadDistance = 1f;
    [SerializeField] private float zOffset = 0.001f;
    [SerializeField] private bool useHandCaptureGlobalsEyeIndex = true;
    [SerializeField] private PassthroughCameraEye eye = PassthroughCameraEye.Left;
    [SerializeField] private bool scaleQuadsToCamera = true;

    private void Update()
    {
        var cameraEye = ResolveEye();
        var cameraPose = PassthroughCameraUtils.GetCameraPoseInWorld(cameraEye);
        var basePosition = cameraPose.position + cameraPose.rotation * Vector3.forward * quadDistance;
        var rotation = cameraPose.rotation;
        var quadSize = scaleQuadsToCamera ? GetQuadSize(cameraEye) : Vector2.zero;

        UpdateDepthQuadTransform(depthSamplingQuad, basePosition, rotation, quadSize);
        UpdateDepthQuadTransform(depthPreviewQuad, basePosition, rotation, quadSize);
    }

    private void UpdateDepthQuadTransform(Transform quad, Vector3 basePosition, Quaternion rotation, Vector2 quadSize)
    {
        if (!quad) return;

        quad.SetPositionAndRotation(basePosition + rotation * Vector3.forward * -zOffset, rotation);
        if (scaleQuadsToCamera && quadSize.x > 0f && quadSize.y > 0f)
        {
            quad.localScale = new Vector3(quadSize.x, quadSize.y, 1f);
        }
    }

    private PassthroughCameraEye ResolveEye()
    {
        if (!useHandCaptureGlobalsEyeIndex)
        {
            return eye;
        }

        return HandCaptureGlobals.EyeIndex == 0 ? PassthroughCameraEye.Left : PassthroughCameraEye.Right;
    }

    private Vector2 GetQuadSize(PassthroughCameraEye cameraEye)
    {
        if (TryGetCanvasWorldSize(out var canvasSize))
        {
            return canvasSize;
        }

        if (quadDistance <= 0f)
        {
            return Vector2.zero;
        }

        var intrinsics = PassthroughCameraUtils.GetCameraIntrinsics(cameraEye);
        if (intrinsics.FocalLength.x <= 0f || intrinsics.FocalLength.y <= 0f ||
            intrinsics.Resolution.x <= 0 || intrinsics.Resolution.y <= 0)
        {
            return Vector2.zero;
        }

        var horizontalFov = 2f * Mathf.Atan(intrinsics.Resolution.x / (2f * intrinsics.FocalLength.x));
        var verticalFov = 2f * Mathf.Atan(intrinsics.Resolution.y / (2f * intrinsics.FocalLength.y));
        var width = 2f * quadDistance * Mathf.Tan(horizontalFov * 0.5f);
        var height = 2f * quadDistance * Mathf.Tan(verticalFov * 0.5f);
        return new Vector2(width, height);
    }

    private bool TryGetCanvasWorldSize(out Vector2 size)
    {
        size = Vector2.zero;
        if (!canvasRect)
        {
            return false;
        }

        var corners = new Vector3[4];
        canvasRect.GetWorldCorners(corners); // 0=BL,1=TL,2=TR,3=BR

        var worldWidth = Vector3.Distance(corners[3], corners[0]);
        var worldHeight = Vector3.Distance(corners[1], corners[0]);
        if (worldWidth <= 0f || worldHeight <= 0f)
        {
            return false;
        }

        size = new Vector2(worldWidth, worldHeight);
        return true;
    }
}
