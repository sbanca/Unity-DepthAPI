using UnityEngine;
using UnityEngine.UI;

public class HandJointsInDepthFrustum : MonoBehaviour
{
    [Header("Frustum")]
    [SerializeField] private DepthFrustumVisualizer m_frustum;
    [SerializeField, Min(0f)] private float m_epsilon = 0.001f;

    [Header("Hands")]
    [SerializeField] private OVRSkeleton m_leftHand;
    [SerializeField] private OVRSkeleton m_rightHand;
    [SerializeField] private bool m_requireDataValid = true;
    [SerializeField] private bool m_requireHighConfidence;

    [Header("Output")]
    [SerializeField] private Text m_statusText;
    [SerializeField] private string m_leftLabel = "Left";
    [SerializeField] private string m_rightLabel = "Right";

    public bool LeftHandAllInside { get; private set; }
    public bool RightHandAllInside { get; private set; }

    private string m_lastStatus;

    private void Update()
    {
        if (m_frustum == null)
        {
            LeftHandAllInside = false;
            RightHandAllInside = false;
            UpdateText("Frustum: Missing");
            return;
        }

        if (!m_frustum.TryGetFrustumParameters(out var minZ, out var maxZ, out var hFovRad, out var vFovRad))
        {
            LeftHandAllInside = false;
            RightHandAllInside = false;
            UpdateText("Frustum: Not Ready");
            return;
        }

        var frustumTransform = m_frustum.transform;
        var tanHalfHFov = Mathf.Tan(hFovRad * 0.5f);
        var tanHalfVFov = Mathf.Tan(vFovRad * 0.5f);

        LeftHandAllInside = AreAllBonesInside(m_leftHand, frustumTransform, minZ, maxZ, tanHalfHFov, tanHalfVFov);
        RightHandAllInside = AreAllBonesInside(m_rightHand, frustumTransform, minZ, maxZ, tanHalfHFov, tanHalfVFov);

        if (m_statusText == null)
        {
            return;
        }

        var leftStatus = FormatHandStatus(m_leftHand, LeftHandAllInside);
        var rightStatus = FormatHandStatus(m_rightHand, RightHandAllInside);
        UpdateText($"{m_leftLabel}: {leftStatus}\n{m_rightLabel}: {rightStatus}");
    }

    private bool AreAllBonesInside(OVRSkeleton skeleton, Transform frustumTransform, float minZ, float maxZ, float tanHalfHFov, float tanHalfVFov)
    {
        if (skeleton == null)
        {
            return false;
        }

        if (m_requireDataValid && !skeleton.IsDataValid)
        {
            return false;
        }

        if (m_requireHighConfidence && !skeleton.IsDataHighConfidence)
        {
            return false;
        }

        var bones = skeleton.Bones;
        if (bones == null || bones.Count == 0)
        {
            return false;
        }

        for (var i = 0; i < bones.Count; i++)
        {
            var boneTransform = bones[i].Transform;
            if (boneTransform == null)
            {
                return false;
            }

            var localPoint = frustumTransform.InverseTransformPoint(boneTransform.position);
            if (!IsPointInsideFrustum(localPoint, minZ, maxZ, tanHalfHFov, tanHalfVFov))
            {
                return false;
            }
        }

        return true;
    }

    private bool IsPointInsideFrustum(Vector3 localPoint, float minZ, float maxZ, float tanHalfHFov, float tanHalfVFov)
    {
        if (localPoint.z < minZ - m_epsilon || localPoint.z > maxZ + m_epsilon)
        {
            return false;
        }

        var maxX = localPoint.z * tanHalfHFov;
        var maxY = localPoint.z * tanHalfVFov;
        if (Mathf.Abs(localPoint.x) > maxX + m_epsilon)
        {
            return false;
        }

        if (Mathf.Abs(localPoint.y) > maxY + m_epsilon)
        {
            return false;
        }

        return true;
    }

    private string FormatHandStatus(OVRSkeleton skeleton, bool allInside)
    {
        if (skeleton == null)
        {
            return "Missing";
        }

        if (m_requireDataValid && !skeleton.IsDataValid)
        {
            return "No Data";
        }

        if (m_requireHighConfidence && !skeleton.IsDataHighConfidence)
        {
            return "Low Conf";
        }

        if (skeleton.Bones == null || skeleton.Bones.Count == 0)
        {
            return "No Bones";
        }

        return allInside ? "Inside" : "Outside";
    }

    private void UpdateText(string value)
    {
        if (m_statusText == null)
        {
            return;
        }

        if (m_lastStatus == value)
        {
            return;
        }

        m_lastStatus = value;
        m_statusText.text = value;
    }
}
