// Copyright (c) Meta Platforms, Inc. and affiliates.

using System.Collections;
using Meta.XR.Samples;
using PassthroughCameraSamples;
using UnityEngine;
using UnityEngine.UI;

namespace PassthroughCameraSamples.CameraToWorld
{
    [MetaCodeSample("PassthroughCameraApiSamples-CameraToWorld")]
    public class CameraToWorldCameraCanvas : MonoBehaviour
    {
        [SerializeField] private WebCamTextureManager m_webCamTextureManager;
        [SerializeField] private Text m_debugText;
        [SerializeField] private RawImage m_image;
        private Texture2D m_cameraSnapshot;
        private Coroutine m_streamCoroutine;
        private bool m_isCapturing;

        public void MakeCameraSnapshot()
        {
            if (m_isCapturing)
            {
                return;
            }
            StartCoroutine(CaptureSnapshotCoroutine());
        }

        public void ResumeStreamingFromCamera()
        {
            if (m_streamCoroutine != null)
            {
                StopCoroutine(m_streamCoroutine);
            }
            m_streamCoroutine = StartCoroutine(ResumeStreamingFromCameraCor());
        }

        private IEnumerator ResumeStreamingFromCameraCor()
        {
            if (!TryResolveWebCamManager())
            {
                yield break;
            }

            var webCamTexture = m_webCamTextureManager.WebCamTexture;
            while (webCamTexture == null || webCamTexture.width <= 0 || webCamTexture.height <= 0)
            {
                webCamTexture = m_webCamTextureManager.WebCamTexture;
                yield return null;
            }

            if (!webCamTexture.isPlaying)
            {
                webCamTexture.Play();
            }

            const int maxWaitFrames = 5;
            int waitedFrames = 0;
            do
            {
                yield return new WaitForEndOfFrame();
                waitedFrames++;
            } while (!webCamTexture.didUpdateThisFrame && waitedFrames < maxWaitFrames);

            m_image.texture = webCamTexture;
            m_streamCoroutine = null;
        }

        private IEnumerator Start()
        {
            if (m_debugText != null)
            {
                m_debugText.text = "Waiting for camera...";
            }

            if (!TryResolveWebCamManager())
            {
                yield break;
            }

            var webCamTexture = m_webCamTextureManager.WebCamTexture;
            while (webCamTexture == null || webCamTexture.width <= 0 || webCamTexture.height <= 0)
            {
                webCamTexture = m_webCamTextureManager.WebCamTexture;
                yield return null;
            }

            if (m_debugText != null)
            {
                m_debugText.text = "Camera ready.";
            }
            ResumeStreamingFromCamera();
        }

        private IEnumerator CaptureSnapshotCoroutine()
        {
            m_isCapturing = true;

            if (!TryResolveWebCamManager())
            {
                m_isCapturing = false;
                yield break;
            }

            var webCamTexture = m_webCamTextureManager.WebCamTexture;
            while (webCamTexture == null || webCamTexture.width <= 0 || webCamTexture.height <= 0)
            {
                webCamTexture = m_webCamTextureManager.WebCamTexture;
                yield return null;
            }

            if (!webCamTexture.isPlaying)
            {
                webCamTexture.Play();
            }

            const int maxWaitFrames = 5;
            int waitedFrames = 0;
            do
            {
                yield return new WaitForEndOfFrame();
                waitedFrames++;
            } while (!webCamTexture.didUpdateThisFrame && waitedFrames < maxWaitFrames);

            var outputSize = m_webCamTextureManager.RequestedResolution;
            if (outputSize == Vector2Int.zero)
            {
                outputSize = new Vector2Int(webCamTexture.width, webCamTexture.height);
            }

            if (outputSize.x <= 0 || outputSize.y <= 0)
            {
                Debug.LogWarning("CameraToWorldCameraCanvas: WebCamTexture not ready for snapshot.");
                m_isCapturing = false;
                yield break;
            }

            var rt = RenderTexture.GetTemporary(outputSize.x, outputSize.y, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
            var prev = RenderTexture.active;
            Graphics.Blit(webCamTexture, rt);
            RenderTexture.active = rt;

            if (m_cameraSnapshot == null || m_cameraSnapshot.width != outputSize.x || m_cameraSnapshot.height != outputSize.y)
            {
                if (m_cameraSnapshot != null)
                {
                    Destroy(m_cameraSnapshot);
                }
                m_cameraSnapshot = new Texture2D(outputSize.x, outputSize.y, TextureFormat.RGBA32, false);
            }

            m_cameraSnapshot.ReadPixels(new Rect(0, 0, outputSize.x, outputSize.y), 0, 0, false);
            m_cameraSnapshot.Apply(false, false);

            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);

            if (m_streamCoroutine != null)
            {
                StopCoroutine(m_streamCoroutine);
                m_streamCoroutine = null;
            }
            m_image.texture = m_cameraSnapshot;
            m_isCapturing = false;
        }

        private bool TryResolveWebCamManager()
        {
            if (m_webCamTextureManager == null)
            {
                m_webCamTextureManager = FindAnyObjectByType<WebCamTextureManager>();
            }

            if (m_webCamTextureManager == null)
            {
                Debug.LogWarning("CameraToWorldCameraCanvas: WebCamTextureManager not found.");
                if (m_debugText != null)
                {
                    m_debugText.text = "WebCamTextureManager not found.";
                }
                return false;
            }

            return true;
        }
    }
}
