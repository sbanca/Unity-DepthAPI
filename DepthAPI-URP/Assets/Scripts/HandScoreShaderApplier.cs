using UnityEngine;

public class HandScoreShaderApplier : MonoBehaviour
{
    [Header("Input")]
    [SerializeField] private HandScore m_scoreSource;

    [Header("Output")]
    [SerializeField] private Renderer m_targetRenderer;
    [SerializeField] private Material m_targetMaterial;
    [SerializeField] private bool m_usePropertyBlock = true;
    [SerializeField] private string m_scoreProperty = "_Score";

    private MaterialPropertyBlock m_block;
    private int m_scoreId;

    private void Awake()
    {
        CacheScoreId();
        if (m_usePropertyBlock && m_block == null)
        {
            m_block = new MaterialPropertyBlock();
        }
    }

    private void OnValidate()
    {
        CacheScoreId();
    }

    private void Update()
    {
        var score = m_scoreSource != null ? m_scoreSource.Score : 0f;
        ApplyScore(score);
    }

    private void CacheScoreId()
    {
        if (string.IsNullOrEmpty(m_scoreProperty))
        {
            m_scoreProperty = "_Score";
        }

        m_scoreId = Shader.PropertyToID(m_scoreProperty);
    }

    private void ApplyScore(float score)
    {
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
                m_targetRenderer.SetPropertyBlock(m_block);
            }
            else if (m_targetRenderer.sharedMaterial != null)
            {
                m_targetRenderer.sharedMaterial.SetFloat(m_scoreId, score);
            }

            return;
        }

        if (m_targetMaterial != null)
        {
            m_targetMaterial.SetFloat(m_scoreId, score);
            return;
        }

        Shader.SetGlobalFloat(m_scoreId, score);
    }
}
