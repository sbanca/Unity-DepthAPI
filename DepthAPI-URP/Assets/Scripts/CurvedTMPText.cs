using TMPro;
using UnityEngine;

[RequireComponent(typeof(TMP_Text))]
public class CurvedTMPText : MonoBehaviour
{
    [Header("Curve")]
    [SerializeField, Min(0.001f)] private float m_radius = 0.25f;
    [SerializeField, Range(-360f, 360f)] private float m_arcDegrees = 180f;
    [SerializeField, Range(-180f, 180f)] private float m_angleOffset = 0f;
    [SerializeField] private bool m_reverseDirection;

    [Header("Update")]
    [SerializeField] private bool m_updateEveryFrame = true;

    private TMP_Text m_text;

    private void Awake()
    {
        m_text = GetComponent<TMP_Text>();
        Apply();
    }

    private void OnEnable()
    {
        Apply();
    }

    private void OnValidate()
    {
        if (m_text == null)
        {
            m_text = GetComponent<TMP_Text>();
        }

        Apply();
    }

    private void Update()
    {
        if (m_updateEveryFrame)
        {
            Apply();
        }
    }

    [ContextMenu("Apply")]
    public void Apply()
    {
        if (m_text == null)
        {
            return;
        }

        m_text.ForceMeshUpdate();
        var textInfo = m_text.textInfo;
        if (textInfo.characterCount == 0)
        {
            return;
        }

        var bounds = m_text.textBounds;
        var width = bounds.size.x;
        if (width <= 1e-5f)
        {
            return;
        }

        var totalAngleRad = Mathf.Deg2Rad * m_arcDegrees;
        var offsetRad = Mathf.Deg2Rad * m_angleOffset;
        var startAngle = -0.5f * totalAngleRad + offsetRad;

        for (var i = 0; i < textInfo.characterCount; i++)
        {
            var charInfo = textInfo.characterInfo[i];
            if (!charInfo.isVisible)
            {
                continue;
            }

            var matIndex = charInfo.materialReferenceIndex;
            var vertIndex = charInfo.vertexIndex;
            var vertices = textInfo.meshInfo[matIndex].vertices;

            var charMid = (vertices[vertIndex + 0] + vertices[vertIndex + 2]) * 0.5f;
            var t = (charMid.x - bounds.min.x) / width;
            var angle = startAngle + t * totalAngleRad;
            if (m_reverseDirection)
            {
                angle = -angle;
            }

            var sin = Mathf.Sin(angle);
            var cos = Mathf.Cos(angle);
            var center = new Vector3(sin * m_radius, cos * m_radius, charMid.z);
            var rot = -angle;

            for (var v = 0; v < 4; v++)
            {
                var pos = vertices[vertIndex + v];
                pos -= charMid;

                var rx = pos.x * Mathf.Cos(rot) - pos.y * Mathf.Sin(rot);
                var ry = pos.x * Mathf.Sin(rot) + pos.y * Mathf.Cos(rot);
                pos = new Vector3(rx, ry, pos.z);

                vertices[vertIndex + v] = pos + center;
            }
        }

        for (var m = 0; m < textInfo.meshInfo.Length; m++)
        {
            var meshInfo = textInfo.meshInfo[m];
            meshInfo.mesh.vertices = meshInfo.vertices;
            m_text.UpdateGeometry(meshInfo.mesh, m);
        }
    }
}
