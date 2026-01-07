// Copyright (c) Meta Platforms, Inc. and affiliates.

using System.Collections;
using PassthroughCameraSamples;
using PassthroughCameraSamples.CameraToWorld;
using UnityEngine;

public class CameraCanvasPoseAligner : MonoBehaviour
{
    [SerializeField] private WebCamTextureManager m_webCamTextureManager;
    [SerializeField] private CameraToWorldCameraCanvas m_cameraCanvas;
    [SerializeField] private float m_canvasDistance = 1f;
    [SerializeField] private bool m_alignPose = true;
    [SerializeField] private bool m_applyScaleOnStart = true;

    private PassthroughCameraEye CameraEye => m_webCamTextureManager.Eye;
    private Vector2Int CameraResolution => m_webCamTextureManager.RequestedResolution;

    private IEnumerator Start()
    {
        while (!ResolveDependencies())
        {
            yield return null;
        }

        while (PassthroughCameraPermissions.HasCameraPermission != true)
        {
            yield return null;
        }

        if (m_applyScaleOnStart)
        {
            ApplyScale();
        }
    }

    private void LateUpdate()
    {
        if (!m_alignPose || m_webCamTextureManager == null || m_cameraCanvas == null)
        {
            return;
        }

        if (m_webCamTextureManager.WebCamTexture == null || !m_webCamTextureManager.WebCamTexture.isPlaying)
        {
            return;
        }

        var cameraPose = PassthroughCameraUtils.GetCameraPoseInWorld(m_webCamTextureManager.Eye);
        m_cameraCanvas.transform.position = cameraPose.position + cameraPose.rotation * Vector3.forward * m_canvasDistance;
        m_cameraCanvas.transform.rotation = cameraPose.rotation;
    }

    public void ApplyScale()
    {
        if (m_webCamTextureManager == null || m_cameraCanvas == null)
        {
            return;
        }

        var cameraCanvasRectTransform = m_cameraCanvas.GetComponentInChildren<RectTransform>();
        if (cameraCanvasRectTransform == null || cameraCanvasRectTransform.sizeDelta.x <= 0f)
        {
            return;
        }

        // ScreenPointToRayInCamera expects coordinates in the max camera resolution (intrinsics space),
        // not the requested output size, otherwise the computed FOV shrinks at lower resolutions.
        var intrinsicsResolution = PassthroughCameraUtils.GetCameraIntrinsics(CameraEye).Resolution;
        if (intrinsicsResolution.x <= 0 || intrinsicsResolution.y <= 0)
        {
            intrinsicsResolution = CameraResolution;
        }

        var leftSidePointInCamera = PassthroughCameraUtils.ScreenPointToRayInCamera(CameraEye, new Vector2Int(0, intrinsicsResolution.y / 2));
        var rightSidePointInCamera = PassthroughCameraUtils.ScreenPointToRayInCamera(CameraEye, new Vector2Int(intrinsicsResolution.x, intrinsicsResolution.y / 2));
        var horizontalFoVDegrees = Vector3.Angle(leftSidePointInCamera.direction, rightSidePointInCamera.direction);
        var horizontalFoVRadians = horizontalFoVDegrees * Mathf.Deg2Rad;
        var newCanvasWidthInMeters = 2 * m_canvasDistance * Mathf.Tan(horizontalFoVRadians / 2);
        var localScale = (float)(newCanvasWidthInMeters / cameraCanvasRectTransform.sizeDelta.x);
        cameraCanvasRectTransform.localScale = new Vector3(localScale, localScale, localScale);
    }

    private bool ResolveDependencies()
    {
        if (m_webCamTextureManager == null)
        {
            m_webCamTextureManager = FindAnyObjectByType<WebCamTextureManager>();
        }

        if (m_cameraCanvas == null)
        {
            m_cameraCanvas = FindAnyObjectByType<CameraToWorldCameraCanvas>();
        }

        return m_webCamTextureManager != null && m_cameraCanvas != null;
    }
}
