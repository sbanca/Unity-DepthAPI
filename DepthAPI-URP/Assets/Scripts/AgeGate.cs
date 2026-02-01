using TMPro;
using UnityEngine;
using UnityEngine.Events;

public sealed class AgeGate : MonoBehaviour
{
    public enum Policy
    {
        AdultOnly = 1,
        ChildOnly = 2
    }

    [Header("Policy")]
    [SerializeField] private Policy m_policy = Policy.AdultOnly;
    [SerializeField, Range(0f, 1f)] private float m_tau = 0.5f;
    [SerializeField, Min(0f)] private float m_ageThreshold = 18f;

    [Header("Events")]
    [SerializeField] private UnityEvent m_onAdmitted;
    [SerializeField] private UnityEvent m_onRejected;
    [SerializeField] private UnityEvent m_onReset;

    [Header("Output")]
    [SerializeField] private TMP_Text m_statusTmpText;

    public float LastPAdult { get; private set; }
    public bool LastAdmitted { get; private set; }
    public bool HasResult { get; private set; }

    /// <summary>
    /// Set policy directly via enum.
    /// </summary>
    public void SetPolicy(Policy policy)
    {
        m_policy = policy;
        UpdateStatusText();
    }

    /// <summary>
    /// Set policy using an index (0 = AdultOnly, 1 = ChildOnly). Out-of-range values are clamped.
    /// </summary>
    public void SetPolicyIndex(int index)
    {
        var clamped = Mathf.Clamp(index, 0, 1);
        m_policy = clamped == 0 ? Policy.AdultOnly : Policy.ChildOnly;
        UpdateStatusText();
    }

    /// <summary>Convenience: set to AdultOnly policy.</summary>
    public void SetAdultPolicy()
    {
        m_policy = Policy.AdultOnly;
        UpdateStatusText();
    }

    /// <summary>Convenience: set to ChildOnly policy.</summary>
    public void SetChildPolicy()
    {
        m_policy = Policy.ChildOnly;
        UpdateStatusText();
    }

    public void SetTau(float value)
    {
        m_tau = Mathf.Clamp01(value);
        UpdateStatusText();
    }

    public void EvaluateBatchMeans(float mean, float logVariance)
    {
        var admitted = Evaluate(mean, logVariance);
        if (admitted)
        {
            m_onAdmitted?.Invoke();
        }
        else
        {
            m_onRejected?.Invoke();
        }
    }

    public bool TryEvaluate(float mean, float logVariance, out bool admitted)
    {
        admitted = Evaluate(mean, logVariance);
        return true;
    }

    private bool Evaluate(float mean, float logVariance)
    {
        var pAdult = ComputePAdult(mean, logVariance);
        LastPAdult = pAdult;
        LastAdmitted = ShouldAdmit(pAdult);
        HasResult = true;
        UpdateStatusText();
        return LastAdmitted;
    }

    public float ComputePAdult(float mean, float logVariance)
    {
        var clampedLogVar = Mathf.Clamp(logVariance, -10f, 10f);
        var std = Mathf.Exp(0.5f * clampedLogVar);
        if (std <= 0f)
        {
            return 0f;
        }

        var z = (m_ageThreshold - mean) / std;
        var cdf = 0.5f * (1f + Erf(z / Mathf.Sqrt(2f)));
        return Mathf.Clamp01(1f - cdf);
    }

    private bool ShouldAdmit(float pAdult)
    {
        if (m_policy == Policy.ChildOnly)
        {
            return pAdult < m_tau;
        }

        return pAdult >= m_tau;
    }

    private static float Erf(float x)
    {
        var sign = Mathf.Sign(x);
        x = Mathf.Abs(x);
        var t = 1f / (1f + 0.3275911f * x);
        var y = 1f - (((((1.061405429f * t - 1.453152027f) * t) + 1.421413741f) * t - 0.284496736f) * t + 0.254829592f) * t * Mathf.Exp(-x * x);
        return sign * y;
    }

    private void OnEnable()
    {
        UpdateStatusText();
    }

    private void OnValidate()
    {
        UpdateStatusText();
    }

    private void UpdateStatusText()
    {
        if (m_statusTmpText == null)
        {
            return;
        }

        var pAdultText = HasResult ? $"{LastPAdult:0.###}" : "N/A";
        var policyText = m_policy == Policy.ChildOnly ? "Children only" : "Adults only";
        m_statusTmpText.text =
            $"Policy: {policyText}\n" +
            $"Adult probability: {pAdultText}\n" +
            $"Decision threshold (tau): {m_tau:0.###}\n" +
            $"Adult age threshold: {m_ageThreshold:0.###}";
    }

    /// <summary>
    /// Clears the latest decision/probability and updates UI, then fires the reset event.
    /// </summary>
    public void ResetGate()
    {
        LastPAdult = 0f;
        LastAdmitted = false;
        HasResult = false;
        UpdateStatusText();
        m_onReset?.Invoke();
    }
}
