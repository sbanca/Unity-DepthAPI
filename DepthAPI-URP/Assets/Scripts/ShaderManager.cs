using UnityEngine;

public class ShaderManager : MonoBehaviour
{
    [Header("Input")]
    [SerializeField] private HandScore m_scoreSource;

    [Header("Hand Selection")]
    [SerializeField] private bool m_useHandFromScore = true;
    [SerializeField] private HandScore.HandSelection m_hand = HandScore.HandSelection.Left;

    [Header("Badge")]
    [SerializeField] private Texture m_leftBadgeTexture;
    [SerializeField] private Texture m_rightBadgeTexture;
    [SerializeField] private float m_leftBadgeRotation;
    [SerializeField] private float m_rightBadgeRotation;
    [SerializeField] private string m_badgeTextureProperty = "_BadgeTex";
    [SerializeField] private string m_badgeRotationProperty = "_BadgeRotation";

    [Header("Output")]
    [SerializeField] private Renderer m_targetRenderer;
    [SerializeField] private Material m_targetMaterial;
    [SerializeField] private bool m_usePropertyBlock = true;
    [SerializeField] private string m_scoreProperty = "_Score";

    private MaterialPropertyBlock m_block;
    private int m_scoreId;
    private int m_badgeTexId;
    private int m_badgeRotationId;

    private void Awake()
    {
        CachePropertyIds();
        if (m_usePropertyBlock && m_block == null)
        {
            m_block = new MaterialPropertyBlock();
        }
    }

    private void OnValidate()
    {
        CachePropertyIds();
    }

    private void Update()
    {
        var score = m_scoreSource != null ? m_scoreSource.Score : 0f;
        var hand = ResolveHandSelection();
        ApplyProperties(score, hand);
    }

    private HandScore.HandSelection ResolveHandSelection()
    {
        if (m_useHandFromScore && m_scoreSource != null)
        {
            return m_scoreSource.SelectedHand;
        }

        return m_hand;
    }

    private void CachePropertyIds()
    {
        if (string.IsNullOrEmpty(m_scoreProperty))
        {
            m_scoreProperty = "_Score";
        }

        if (string.IsNullOrEmpty(m_badgeTextureProperty))
        {
            m_badgeTextureProperty = "_BadgeTex";
        }

        if (string.IsNullOrEmpty(m_badgeRotationProperty))
        {
            m_badgeRotationProperty = "_BadgeRotation";
        }

        m_scoreId = Shader.PropertyToID(m_scoreProperty);
        m_badgeTexId = Shader.PropertyToID(m_badgeTextureProperty);
        m_badgeRotationId = Shader.PropertyToID(m_badgeRotationProperty);
    }

    private void ApplyProperties(float score, HandScore.HandSelection hand)
    {
        var badgeTexture = hand == HandScore.HandSelection.Left ? m_leftBadgeTexture : m_rightBadgeTexture;
        var badgeRotation = hand == HandScore.HandSelection.Left ? m_leftBadgeRotation : m_rightBadgeRotation;

        if (m_targetRenderer != null)
        {
            if (m_usePropertyBlock)
            {
                if (m_block == null)
                {
                    m_block = new MaterialPropertyBlock();
                }

                m_targetRenderer.GetPropertyBlock(m_block);
                m_block.SetFloat(m_scoreId, score);
                m_block.SetFloat(m_badgeRotationId, badgeRotation);
                m_block.SetTexture(m_badgeTexId, badgeTexture);
                m_targetRenderer.SetPropertyBlock(m_block);
            }
            else if (m_targetRenderer.sharedMaterial != null)
            {
                var material = m_targetRenderer.sharedMaterial;
                material.SetFloat(m_scoreId, score);
                material.SetFloat(m_badgeRotationId, badgeRotation);
                material.SetTexture(m_badgeTexId, badgeTexture);
            }

            return;
        }

        if (m_targetMaterial != null)
        {
            m_targetMaterial.SetFloat(m_scoreId, score);
            m_targetMaterial.SetFloat(m_badgeRotationId, badgeRotation);
            m_targetMaterial.SetTexture(m_badgeTexId, badgeTexture);
            return;
        }

        Shader.SetGlobalFloat(m_scoreId, score);
        Shader.SetGlobalFloat(m_badgeRotationId, badgeRotation);
        Shader.SetGlobalTexture(m_badgeTexId, badgeTexture);
    }
}
