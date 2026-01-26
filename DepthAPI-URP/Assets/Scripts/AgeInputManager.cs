using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

public sealed class AgeInputManager : MonoBehaviour
{
    [System.Serializable]
    public sealed class AgeChangedEvent : UnityEvent { }

    [Header("Output")]
    [SerializeField] private TMP_Text m_ageText;
    [SerializeField] private AgeChangedEvent m_onAgeChanged;

    private int m_decade;
    private int m_year;

    public int age { get; private set; }

    public void SetDecade(int decade)
    {
        m_decade = decade;
        UpdateAge();
    }

    public void SetYear(int year)
    {
        m_year = year;
        UpdateAge();
    }

    private void OnEnable()
    {
        UpdateAge();
    }

    private void OnValidate()
    {
        UpdateAge();
    }

    private void UpdateAge()
    {
        var decadeValue = m_decade;
        if (decadeValue >= 0 && decadeValue <= 9)
        {
            decadeValue *= 10;
        }

        age = Mathf.Max(0, decadeValue + m_year);
        if (m_ageText != null)
        {
            m_ageText.text = age.ToString(CultureInfo.InvariantCulture);
        }

        m_onAgeChanged?.Invoke();
    }
}
