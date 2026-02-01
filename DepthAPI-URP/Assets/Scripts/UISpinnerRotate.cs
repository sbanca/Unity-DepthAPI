using UnityEngine;

public class UISpinnerRotate : MonoBehaviour
{
    [Tooltip("Rotation speed in degrees per second.")]
    public float degreesPerSecond = 180f;

    [Tooltip("Use unscaled time so it keeps spinning when timeScale is 0.")]
    public bool useUnscaledTime = true;

    private RectTransform m_rectTransform;

    private void Awake()
    {
        m_rectTransform = transform as RectTransform;
    }

    private void Update()
    {
        if (m_rectTransform == null)
        {
            return;
        }

        var dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        m_rectTransform.Rotate(0f, 0f, -degreesPerSecond * dt); // negative = clockwise
    }
}
