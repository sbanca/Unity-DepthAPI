using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PassthroughCameraSamples
{
    [ExecuteAlways]
    public class PassthroughCameraResolutionApplier : MonoBehaviour
    {
        [SerializeField] private WebCamTextureManager m_webCamTextureManager;
        private static readonly Vector2Int AutoPreviewResolution = new Vector2Int(1280, 960);
        [SerializeField] private List<ResolutionTarget> m_targets = new List<ResolutionTarget>();

        private Vector2Int m_lastAppliedResolution;

        private void OnEnable()
        {
            ApplyIfNeeded(true);
        }

        private void OnValidate()
        {
            ApplyIfNeeded(true);
        }

        private void Update()
        {
            ApplyIfNeeded(false);
        }

        private void ApplyIfNeeded(bool force)
        {
            if (!Application.isEditor || Application.isPlaying)
            {
                return;
            }

            var resolution = ResolveResolution();
            if (resolution.x <= 0 || resolution.y <= 0)
            {
                return;
            }

            if (!force && resolution == m_lastAppliedResolution)
            {
                return;
            }

            ApplyResolution(resolution);
            m_lastAppliedResolution = resolution;
        }

        private Vector2Int ResolveResolution()
        {
            if (m_webCamTextureManager == null)
            {
                m_webCamTextureManager = FindAnyObjectByType<WebCamTextureManager>();
            }

            if (m_webCamTextureManager == null)
            {
                return Vector2Int.zero;
            }

            var requested = m_webCamTextureManager.RequestedResolution;
            return requested == Vector2Int.zero ? AutoPreviewResolution : requested;
        }

        private void ApplyResolution(Vector2Int resolution)
        {
            foreach (var target in m_targets)
            {
                ApplyTarget(target, resolution);
            }
        }

        private void ApplyTarget(ResolutionTarget target, Vector2Int resolution)
        {
            if (target == null || target.Target == null)
            {
                return;
            }

            switch (target.Type)
            {
                case ResolutionTargetType.RectTransform:
                {
                    var rect = target.Target as RectTransform;
                    if (rect == null) return;
                    SetRectSize(rect, resolution);
                    break;
                }
                case ResolutionTargetType.RawImage:
                {
                    var image = target.Target as RawImage;
                    if (image == null) return;
                    SetRectSize(image.rectTransform, resolution);
                    break;
                }
                case ResolutionTargetType.RenderTexture:
                {
                    var rt = target.Target as RenderTexture;
                    if (rt == null) return;
                    SetRenderTextureSize(rt, resolution);
                    break;
                }
            }
        }

        private static void SetRectSize(RectTransform rect, Vector2Int resolution)
        {
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, resolution.x);
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, resolution.y);
        }

        private static void SetRenderTextureSize(RenderTexture rt, Vector2Int resolution)
        {
            if (rt.width == resolution.x && rt.height == resolution.y)
            {
                return;
            }

            if (rt.IsCreated())
            {
                rt.Release();
            }

            rt.width = resolution.x;
            rt.height = resolution.y;
            rt.Create();

#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(rt);
#endif
        }
    }

    [Serializable]
    public class ResolutionTarget
    {
        public ResolutionTargetType Type;
        public UnityEngine.Object Target;
    }

    public enum ResolutionTargetType
    {
        RectTransform,
        RawImage,
        RenderTexture
    }
}
