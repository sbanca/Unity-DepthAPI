using UnityEngine;

[RequireComponent(typeof(MeshFilter))]
public class PlaneShaderGlobals : MonoBehaviour
{
    public static readonly int PlaneCenterId = Shader.PropertyToID("_PlaneCenterWS");
    public static readonly int PlaneRightHalfId = Shader.PropertyToID("_PlaneRightHalfWS");
    public static readonly int PlaneUpHalfId = Shader.PropertyToID("_PlaneUpHalfWS");

    [SerializeField] private MeshFilter m_meshFilter;
    [SerializeField] private bool m_updateEveryFrame = true;

    private void Awake()
    {
        if (m_meshFilter == null)
        {
            m_meshFilter = GetComponent<MeshFilter>();
        }
    }

    private void OnEnable()
    {
        UpdateGlobals();
    }

    private void LateUpdate()
    {
        if (m_updateEveryFrame)
        {
            UpdateGlobals();
        }
    }

    public void UpdateGlobals()
    {
        if (m_meshFilter == null || m_meshFilter.sharedMesh == null)
        {
            return;
        }

        var bounds = m_meshFilter.sharedMesh.bounds;
        var centerWS = transform.TransformPoint(bounds.center);
        var rightHalfWS = transform.TransformVector(Vector3.right * bounds.extents.x);
        var upHalfWS = transform.TransformVector(Vector3.up * bounds.extents.y);

        Shader.SetGlobalVector(PlaneCenterId, centerWS);
        Shader.SetGlobalVector(PlaneRightHalfId, rightHalfWS);
        Shader.SetGlobalVector(PlaneUpHalfId, upHalfWS);
    }
}
