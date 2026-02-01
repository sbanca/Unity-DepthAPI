using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class HandDorsalFacingCamera : MonoBehaviour
{
    [Header("Hands")]
    [SerializeField] private OVRSkeleton m_leftHand;
    [SerializeField] private OVRSkeleton m_rightHand;

    [Header("Camera")]
    [SerializeField] private Camera m_camera;

    [Header("Orientation")]
    [SerializeField] private bool m_flipLeftNormal;
    [SerializeField] private bool m_flipRightNormal;

    [Header("Facing")]
    [SerializeField, Range(-1f, 1f)] private float m_facingDotThreshold = 0.98f;

    [Header("Output")]
    [SerializeField] private Text m_statusText;
    [SerializeField] private string m_leftLabel = "Left";
    [SerializeField] private string m_rightLabel = "Right";

    public bool LeftDorsalFacing { get; private set; }
    public bool RightDorsalFacing { get; private set; }
    public float LeftPerpendicularity { get; private set; }
    public float RightPerpendicularity { get; private set; }
    public float LeftFacingDot { get; private set; }
    public float RightFacingDot { get; private set; }
    public bool LeftHasData { get; private set; }
    public bool RightHasData { get; private set; }
    public float FacingDotThreshold => m_facingDotThreshold;

    public void SetFacingDotThreshold(float value)
    {
        m_facingDotThreshold = Mathf.Clamp(value, -1f, 1f);
    }

    private string m_lastStatus;

    private void Update()
    {
        var cam = m_camera != null ? m_camera : Camera.main;
        if (cam == null)
        {
            LeftDorsalFacing = false;
            RightDorsalFacing = false;
            LeftPerpendicularity = 0f;
            RightPerpendicularity = 0f;
            LeftFacingDot = 0f;
            RightFacingDot = 0f;
            LeftHasData = false;
            RightHasData = false;
            UpdateText("Camera: Missing");
            return;
        }

        LeftDorsalFacing = TryEvaluate(m_leftHand, cam, m_flipLeftNormal, out var leftPerp, out var leftFacingDot, out var leftHasData);
        RightDorsalFacing = TryEvaluate(m_rightHand, cam, m_flipRightNormal, out var rightPerp, out var rightFacingDot, out var rightHasData);
        LeftPerpendicularity = leftPerp;
        RightPerpendicularity = rightPerp;
        LeftFacingDot = leftFacingDot;
        RightFacingDot = rightFacingDot;
        LeftHasData = leftHasData;
        RightHasData = rightHasData;

        if (m_statusText == null)
        {
            return;
        }

        var leftStatus = FormatStatus(m_leftHand, LeftDorsalFacing, leftPerp);
        var rightStatus = FormatStatus(m_rightHand, RightDorsalFacing, rightPerp);
        UpdateText($"{m_leftLabel}: {leftStatus}\n{m_rightLabel}: {rightStatus}");
    }

    private bool TryEvaluate(OVRSkeleton skeleton, Camera cam, bool flipNormal, out float perpendicularity, out float facingDot, out bool hasData)
    {
        perpendicularity = 0f;
        facingDot = 0f;
        hasData = false;
        if (skeleton == null || !skeleton.IsDataValid)
        {
            return false;
        }

        if (!TryGetPalmPlanePoints(skeleton, out var palm, out var index, out var pinky))
        {
            return false;
        }

        var normal = Vector3.Cross(index - palm, pinky - palm);
        if (normal.sqrMagnitude < 1e-6f)
        {
            return false;
        }

        normal = normal.normalized;
        if (flipNormal)
        {
            normal = -normal;
        }

        var toCamera = (cam.transform.position - palm).normalized;
        facingDot = Vector3.Dot(normal, toCamera);
        perpendicularity = Mathf.Abs(Vector3.Dot(normal, cam.transform.forward));
        hasData = true;
        return facingDot >= m_facingDotThreshold;
    }

    private bool TryGetPalmPlanePoints(OVRSkeleton skeleton, out Vector3 palm, out Vector3 index, out Vector3 pinky)
    {
        palm = Vector3.zero;
        index = Vector3.zero;
        pinky = Vector3.zero;

        var bones = skeleton.Bones;
        if (bones == null || bones.Count == 0)
        {
            return false;
        }

        if (TryGetBoneTransform(bones, OVRSkeleton.BoneId.XRHand_Palm, out var palmTransform) ||
            TryGetBoneTransform(bones, OVRSkeleton.BoneId.XRHand_Wrist, out palmTransform))
        {
            if (TryGetBoneTransform(bones, OVRSkeleton.BoneId.XRHand_IndexMetacarpal, out var indexTransform) &&
                TryGetBoneTransform(bones, OVRSkeleton.BoneId.XRHand_LittleMetacarpal, out var pinkyTransform))
            {
                palm = palmTransform.position;
                index = indexTransform.position;
                pinky = pinkyTransform.position;
                return true;
            }
        }

        if (TryGetBoneTransform(bones, OVRSkeleton.BoneId.Hand_WristRoot, out palmTransform) &&
            TryGetBoneTransform(bones, OVRSkeleton.BoneId.Hand_Index1, out var indexLegacy) &&
            (TryGetBoneTransform(bones, OVRSkeleton.BoneId.Hand_Pinky0, out var pinkyLegacy) ||
             TryGetBoneTransform(bones, OVRSkeleton.BoneId.Hand_Pinky1, out pinkyLegacy)))
        {
            palm = palmTransform.position;
            index = indexLegacy.position;
            pinky = pinkyLegacy.position;
            return true;
        }

        return false;
    }

    private bool TryGetBoneTransform(IList<OVRBone> bones, OVRSkeleton.BoneId id, out Transform boneTransform)
    {
        for (var i = 0; i < bones.Count; i++)
        {
            var bone = bones[i];
            if (bone.Id == id && bone.Transform != null)
            {
                boneTransform = bone.Transform;
                return true;
            }
        }

        boneTransform = null;
        return false;
    }

    private string FormatStatus(OVRSkeleton skeleton, bool facing, float perpendicularity)
    {
        if (skeleton == null)
        {
            return "Missing";
        }

        if (!skeleton.IsDataValid)
        {
            return "No Data";
        }

        return $"{(facing ? "Facing" : "Not Facing")} ({perpendicularity:0.00})";
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
