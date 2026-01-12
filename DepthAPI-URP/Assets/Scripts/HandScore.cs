using UnityEngine;
using UnityEngine.UI;

public class HandScore : MonoBehaviour
{
    public enum HandSelection
    {
        Left,
        Right
    }

    [Header("Inputs")]
    [SerializeField] private HandJointsInDepthFrustum m_frustumCheck;
    [SerializeField] private HandFlatnessEvaluator m_flatness;
    [SerializeField] private HandDorsalFacingCamera m_dorsalFacing;

    [Header("Hand")]
    [SerializeField] private HandSelection m_hand = HandSelection.Left;

    [Header("Scoring")]
    [SerializeField, Min(0f)] private float m_flatnessFalloff = 0.01f;
    [SerializeField, Min(0f)] private float m_facingFalloff = 0.02f;

    [Header("Output")]
    [SerializeField] private Text m_scoreText;

    public float Score { get; private set; }
    public HandSelection SelectedHand => m_hand;

    private void Update()
    {
        Score = ComputeScore();
        UpdateScoreText();
    }

    private float ComputeScore()
    {
        if (m_frustumCheck == null || m_flatness == null || m_dorsalFacing == null)
        {
            return 0f;
        }

        if (!IsHandInsideFrustum())
        {
            return 0f;
        }

        var flatScore = ComputeFlatnessScore();
        if (flatScore <= 0f)
        {
            return 0f;
        }

        var facingScore = ComputeFacingScore();
        return Mathf.Clamp01(flatScore * facingScore);
    }

    private void UpdateScoreText()
    {
        if (m_scoreText == null)
        {
            return;
        }

        m_scoreText.text = $"Score: {Score:0.###}";
    }

    private bool IsHandInsideFrustum()
    {
        return m_hand == HandSelection.Left ? m_frustumCheck.LeftHandAllInside : m_frustumCheck.RightHandAllInside;
    }

    private float ComputeFlatnessScore()
    {
        var hasData = m_hand == HandSelection.Left ? m_flatness.LeftHasData : m_flatness.RightHasData;
        if (!hasData)
        {
            return 0f;
        }

        var rms = m_hand == HandSelection.Left ? m_flatness.LeftRms : m_flatness.RightRms;
        var threshold = m_flatness.FlatnessThreshold;
        if (threshold <= 0f)
        {
            return 0f;
        }

        var falloff = Mathf.Max(m_flatnessFalloff, 1e-6f);
        var score = 1f - Mathf.InverseLerp(threshold, threshold + falloff, rms);
        return Mathf.Clamp01(score);
    }

    private float ComputeFacingScore()
    {
        var hasData = m_hand == HandSelection.Left ? m_dorsalFacing.LeftHasData : m_dorsalFacing.RightHasData;
        if (!hasData)
        {
            return 0f;
        }

        var facingDot = m_hand == HandSelection.Left ? m_dorsalFacing.LeftFacingDot : m_dorsalFacing.RightFacingDot;
        var threshold = m_dorsalFacing.FacingDotThreshold;
        if (threshold <= 0f)
        {
            return Mathf.Clamp01(facingDot);
        }

        if (threshold >= 1f)
        {
            return 0f;
        }

        var falloff = Mathf.Max(m_facingFalloff, 1e-6f);
        var min = Mathf.Max(0f, threshold - falloff);
        var score = Mathf.InverseLerp(min, threshold, facingDot);
        return Mathf.Clamp01(score);
    }

}
