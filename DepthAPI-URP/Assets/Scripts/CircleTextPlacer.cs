using UnityEngine;

public class CircleTextPlacer : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private Transform m_textTransform;

    [Header("Circle")]
    [SerializeField, Min(0f)] private float m_radius = 0.1f;
    [SerializeField] private float m_angleDegrees = 0f;

    [Header("Rotation")]
    [SerializeField] private bool m_tangentToCircle = true;
    [SerializeField] private float m_rotationOffset = 0f;

    private void OnEnable()
    {
        Apply();
    }

    private void OnValidate()
    {
        Apply();
    }

    [ContextMenu("Apply")]
    public void Apply()
    {
        if (m_textTransform == null)
        {
            return;
        }

        var rad = m_angleDegrees * Mathf.Deg2Rad;
        var localPos = new Vector3(Mathf.Cos(rad), Mathf.Sin(rad), 0f) * m_radius;
        m_textTransform.localPosition = localPos;

        var zRot = m_angleDegrees + (m_tangentToCircle ? 90f : 0f) + m_rotationOffset;
        m_textTransform.localRotation = Quaternion.Euler(0f, 0f, zRot);
    }
}
