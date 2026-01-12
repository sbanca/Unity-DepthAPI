using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class HandFlatnessEvaluator : MonoBehaviour
{
    [Header("Hands")]
    [SerializeField] private OVRSkeleton m_leftHand;
    [SerializeField] private OVRSkeleton m_rightHand;

    [Header("Flatness")]
    [SerializeField, Min(0f)] private float m_flatnessThreshold = 0.01f;

    [Header("Output")]
    [SerializeField] private Text m_statusText;
    [SerializeField] private string m_leftLabel = "Left";
    [SerializeField] private string m_rightLabel = "Right";

    public bool LeftHandFlat { get; private set; }
    public bool RightHandFlat { get; private set; }
    public bool LeftHasData { get; private set; }
    public bool RightHasData { get; private set; }
    public float LeftRms { get; private set; }
    public float RightRms { get; private set; }
    public float FlatnessThreshold => m_flatnessThreshold;

    private readonly float[] m_covariance = new float[9];
    private readonly float[] m_eigenVectors = new float[9];
    private string m_lastStatus;

    private const float JacobiEpsilon = 1e-6f;
    private const int JacobiIterations = 10;

    private void Update()
    {
        LeftHandFlat = TryEvaluateHand(m_leftHand, out var leftHasData, out var leftRms);
        RightHandFlat = TryEvaluateHand(m_rightHand, out var rightHasData, out var rightRms);
        LeftHasData = leftHasData;
        RightHasData = rightHasData;
        LeftRms = leftRms;
        RightRms = rightRms;

        if (m_statusText == null)
        {
            return;
        }

        var leftStatus = FormatStatus(m_leftHand, leftHasData, LeftHandFlat);
        var rightStatus = FormatStatus(m_rightHand, rightHasData, RightHandFlat);
        UpdateText($"{m_leftLabel}: {leftStatus}\n{m_rightLabel}: {rightStatus}");
    }

    private bool TryEvaluateHand(OVRSkeleton skeleton, out bool hasData, out float rms)
    {
        hasData = false;
        rms = float.PositiveInfinity;
        if (skeleton == null || !skeleton.IsDataValid)
        {
            return false;
        }

        var bones = skeleton.Bones;
        if (bones == null || bones.Count < 3)
        {
            return false;
        }

        if (!TryComputeBestFitPlane(bones, out var centroid, out var normal))
        {
            return false;
        }

        rms = ComputeRmsDistance(bones, centroid, normal);
        hasData = true;
        return rms <= m_flatnessThreshold;
    }

    private bool TryComputeBestFitPlane(IList<OVRBone> bones, out Vector3 centroid, out Vector3 normal)
    {
        centroid = Vector3.zero;
        normal = Vector3.zero;

        var count = 0;
        for (var i = 0; i < bones.Count; i++)
        {
            var boneTransform = bones[i].Transform;
            if (boneTransform == null)
            {
                continue;
            }

            centroid += boneTransform.position;
            count++;
        }

        if (count < 3)
        {
            return false;
        }

        centroid /= count;

        var xx = 0f;
        var xy = 0f;
        var xz = 0f;
        var yy = 0f;
        var yz = 0f;
        var zz = 0f;

        for (var i = 0; i < bones.Count; i++)
        {
            var boneTransform = bones[i].Transform;
            if (boneTransform == null)
            {
                continue;
            }

            var r = boneTransform.position - centroid;
            xx += r.x * r.x;
            xy += r.x * r.y;
            xz += r.x * r.z;
            yy += r.y * r.y;
            yz += r.y * r.z;
            zz += r.z * r.z;
        }

        var invCount = 1f / count;
        xx *= invCount;
        xy *= invCount;
        xz *= invCount;
        yy *= invCount;
        yz *= invCount;
        zz *= invCount;

        normal = GetSmallestEigenvector(xx, xy, xz, yy, yz, zz);
        return normal.sqrMagnitude > 0f;
    }

    private float ComputeRmsDistance(IList<OVRBone> bones, Vector3 centroid, Vector3 normal)
    {
        var sumSq = 0f;
        var count = 0;

        for (var i = 0; i < bones.Count; i++)
        {
            var boneTransform = bones[i].Transform;
            if (boneTransform == null)
            {
                continue;
            }

            var d = Vector3.Dot(normal, boneTransform.position - centroid);
            sumSq += d * d;
            count++;
        }

        if (count == 0)
        {
            return float.PositiveInfinity;
        }

        return Mathf.Sqrt(sumSq / count);
    }

    private Vector3 GetSmallestEigenvector(float xx, float xy, float xz, float yy, float yz, float zz)
    {
        var a = m_covariance;
        var v = m_eigenVectors;

        a[0] = xx;
        a[1] = xy;
        a[2] = xz;
        a[3] = xy;
        a[4] = yy;
        a[5] = yz;
        a[6] = xz;
        a[7] = yz;
        a[8] = zz;

        v[0] = 1f;
        v[1] = 0f;
        v[2] = 0f;
        v[3] = 0f;
        v[4] = 1f;
        v[5] = 0f;
        v[6] = 0f;
        v[7] = 0f;
        v[8] = 1f;

        for (var iter = 0; iter < JacobiIterations; iter++)
        {
            var p = 0;
            var q = 1;
            var max = Mathf.Abs(a[1]);

            var a02 = Mathf.Abs(a[2]);
            if (a02 > max)
            {
                max = a02;
                p = 0;
                q = 2;
            }

            var a12 = Mathf.Abs(a[5]);
            if (a12 > max)
            {
                max = a12;
                p = 1;
                q = 2;
            }

            if (max < JacobiEpsilon)
            {
                break;
            }

            var r = 3 - p - q;
            var pp = p * 3 + p;
            var qq = q * 3 + q;
            var pq = p * 3 + q;
            var qp = q * 3 + p;
            var rp = r * 3 + p;
            var pr = p * 3 + r;
            var rq = r * 3 + q;
            var qr = q * 3 + r;

            var app = a[pp];
            var aqq = a[qq];
            var apq = a[pq];

            var tau = (aqq - app) / (2f * apq);
            var signTau = tau >= 0f ? 1f : -1f;
            var t = signTau / (Mathf.Abs(tau) + Mathf.Sqrt(1f + tau * tau));
            var c = 1f / Mathf.Sqrt(1f + t * t);
            var s = t * c;

            a[pp] = app - t * apq;
            a[qq] = aqq + t * apq;
            a[pq] = 0f;
            a[qp] = 0f;

            var arp = a[rp];
            var arq = a[rq];
            var arpNew = c * arp - s * arq;
            var arqNew = c * arq + s * arp;
            a[rp] = arpNew;
            a[pr] = arpNew;
            a[rq] = arqNew;
            a[qr] = arqNew;

            for (var i = 0; i < 3; i++)
            {
                var ip = i * 3 + p;
                var iq = i * 3 + q;
                var vip = v[ip];
                var viq = v[iq];
                v[ip] = c * vip - s * viq;
                v[iq] = c * viq + s * vip;
            }
        }

        var e0 = a[0];
        var e1 = a[4];
        var e2 = a[8];
        var minIndex = 0;
        var minValue = e0;
        if (e1 < minValue)
        {
            minValue = e1;
            minIndex = 1;
        }

        if (e2 < minValue)
        {
            minIndex = 2;
        }

        var normal = new Vector3(v[minIndex], v[3 + minIndex], v[6 + minIndex]);
        return normal.sqrMagnitude > 0f ? normal.normalized : Vector3.zero;
    }

    private string FormatStatus(OVRSkeleton skeleton, bool hasData, bool isFlat)
    {
        if (skeleton == null)
        {
            return "Missing";
        }

        if (!skeleton.IsDataValid)
        {
            return "No Data";
        }

        if (!hasData)
        {
            return "No Bones";
        }

        return isFlat ? "Flat" : "Not Flat";
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
