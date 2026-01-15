using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class PredictionBatchThumbnailApplier : MonoBehaviour
{
    [Serializable]
    private sealed class TileBinding
    {
        public Transform Root;
        public Image Thumbnail;
        public TMP_Text Label;
        public TMP_Text SecondaryLabel;

        [NonSerialized] public Texture2D RuntimeTexture;
        [NonSerialized] public Sprite RuntimeSprite;
    }

    [Header("Input")]
    [SerializeField] private PredictionBatchCollector m_collector;
    [SerializeField] private Transform m_tilesRoot;

    [Header("Auto-Register")]
    [SerializeField] private bool m_autoRegisterOnEnable = true;
    [SerializeField] private string m_tileNamePrefix = "TextTileButton_IconAndLabel_Toggle";
    [SerializeField] private string m_backgroundName = "Background";
    [SerializeField] private string m_elementsName = "Elements";
    [SerializeField] private string m_labelName = "Label";
    [SerializeField] private string m_labelSecondaryName = "Label (1)";

    [Header("Display")]
    [SerializeField] private bool m_preserveAspect = true;
    [SerializeField] private bool m_clearUnusedTiles;
    [SerializeField] private bool m_clearImageWhenMissing;
    [SerializeField] private bool m_applyLastBatchOnEnable = true;
    [SerializeField] private string m_primaryLabelFormat = "Mean: {0:0.###}";
    [SerializeField] private string m_secondaryLabelFormat = "LogVar: {0:0.###}";
    [SerializeField] private bool m_useStdDevForSecondary;

    [SerializeField] private List<TileBinding> m_tiles = new List<TileBinding>();

    private void OnEnable()
    {
        if (m_autoRegisterOnEnable)
        {
            AutoRegister();
        }

        if (m_collector != null)
        {
            m_collector.BatchReady += HandleBatchReady;
        }

        if (m_applyLastBatchOnEnable && m_collector != null && m_collector.TryGetLastBatch(out var lastBatch))
        {
            ApplyBatch(lastBatch);
        }
    }

    private void OnDisable()
    {
        if (m_collector != null)
        {
            m_collector.BatchReady -= HandleBatchReady;
        }
    }

    private void OnDestroy()
    {
        ClearRuntimeAssets();
    }

    public void AutoRegister()
    {
        var root = m_tilesRoot != null ? m_tilesRoot : transform;
        m_tiles.Clear();

        for (var i = 0; i < root.childCount; i++)
        {
            var child = root.GetChild(i);
            if (child.name.StartsWith(m_tileNamePrefix, StringComparison.Ordinal))
            {
                m_tiles.Add(CreateBinding(child));
            }
        }
    }

    private void HandleBatchReady(PredictionBatchCollector.PredictionBatch batch)
    {
        ApplyBatch(batch);
    }

    public void ApplyBatch(PredictionBatchCollector.PredictionBatch batch)
    {
        if (batch.Samples == null || batch.Count == 0)
        {
            return;
        }

        if (m_tiles.Count == 0 && m_autoRegisterOnEnable)
        {
            AutoRegister();
        }

        var count = Mathf.Min(m_tiles.Count, batch.Count);
        for (var i = 0; i < count; i++)
        {
            var binding = m_tiles[i];
            if (binding == null)
            {
                continue;
            }

            var sample = batch.Samples[i];
            var png = (batch.ImagePngs != null && i < batch.ImagePngs.Count) ? batch.ImagePngs[i] : null;
            ApplyImage(binding, png);
            ApplyLabels(binding, sample);
        }

        if (m_clearUnusedTiles && m_tiles.Count > count)
        {
            for (var i = count; i < m_tiles.Count; i++)
            {
                ClearTile(m_tiles[i]);
            }
        }
    }

    private void ApplyImage(TileBinding binding, byte[] png)
    {
        if (binding == null || binding.Thumbnail == null)
        {
            return;
        }

        if (png == null || png.Length == 0)
        {
            if (m_clearImageWhenMissing)
            {
                ClearThumbnail(binding);
            }
            return;
        }

        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!tex.LoadImage(png))
        {
            Destroy(tex);
            return;
        }

        var sprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f));
        DestroyRuntimeAssets(binding);
        binding.RuntimeTexture = tex;
        binding.RuntimeSprite = sprite;
        binding.Thumbnail.sprite = sprite;
        binding.Thumbnail.preserveAspect = m_preserveAspect;
    }

    private void ApplyLabels(TileBinding binding, PredictionBatchCollector.PredictionSample sample)
    {
        if (binding == null)
        {
            return;
        }

        if (binding.Label != null)
        {
            binding.Label.text = string.Format(m_primaryLabelFormat, sample.Mean);
        }

        if (binding.SecondaryLabel != null)
        {
            var secondaryValue = m_useStdDevForSecondary
                ? Mathf.Sqrt(Mathf.Exp(sample.LogVariance))
                : sample.LogVariance;
            binding.SecondaryLabel.text = string.Format(m_secondaryLabelFormat, secondaryValue);
        }
    }

    private void ClearTile(TileBinding binding)
    {
        if (binding == null)
        {
            return;
        }

        if (binding.Label != null)
        {
            binding.Label.text = string.Empty;
        }

        if (binding.SecondaryLabel != null)
        {
            binding.SecondaryLabel.text = string.Empty;
        }

        ClearThumbnail(binding);
    }

    private void ClearThumbnail(TileBinding binding)
    {
        if (binding == null || binding.Thumbnail == null)
        {
            return;
        }

        DestroyRuntimeAssets(binding);
        binding.Thumbnail.sprite = null;
    }

    private void ClearRuntimeAssets()
    {
        for (var i = 0; i < m_tiles.Count; i++)
        {
            DestroyRuntimeAssets(m_tiles[i]);
        }
    }

    private void DestroyRuntimeAssets(TileBinding binding)
    {
        if (binding == null)
        {
            return;
        }

        if (binding.RuntimeSprite != null)
        {
            Destroy(binding.RuntimeSprite);
            binding.RuntimeSprite = null;
        }

        if (binding.RuntimeTexture != null)
        {
            Destroy(binding.RuntimeTexture);
            binding.RuntimeTexture = null;
        }
    }

    private TileBinding CreateBinding(Transform root)
    {
        var binding = new TileBinding
        {
            Root = root
        };

        var background = FindChildByName(root, m_backgroundName);
        if (background != null)
        {
            binding.Thumbnail = background.GetComponent<Image>();
        }

        var elements = background != null ? FindChildByName(background, m_elementsName) : FindChildByName(root, m_elementsName);
        var labelRoot = elements != null ? elements : root;
        var labelTransform = FindChildByName(labelRoot, m_labelName);
        var secondaryTransform = FindChildByName(labelRoot, m_labelSecondaryName);
        if (labelTransform != null)
        {
            binding.Label = labelTransform.GetComponent<TMP_Text>();
        }

        if (secondaryTransform != null)
        {
            binding.SecondaryLabel = secondaryTransform.GetComponent<TMP_Text>();
        }

        return binding;
    }

    private static Transform FindChildByName(Transform root, string name)
    {
        if (root == null || string.IsNullOrEmpty(name))
        {
            return null;
        }

        if (root.name == name)
        {
            return root;
        }

        for (var i = 0; i < root.childCount; i++)
        {
            var child = root.GetChild(i);
            var result = FindChildByName(child, name);
            if (result != null)
            {
                return result;
            }
        }

        return null;
    }
}
