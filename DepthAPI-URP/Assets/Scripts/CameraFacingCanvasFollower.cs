using UnityEngine;

[RequireComponent(typeof(RectTransform))]
public sealed class CameraFacingCanvasFollower : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private Camera m_camera;
    [SerializeField] private RectTransform m_canvas;

    [Header("Offset")]
    [SerializeField, Min(0f)] private float m_forwardDistance = 1f;
    [SerializeField] private Vector3 m_localOffset = Vector3.zero;

    [Header("Motion")]
    [SerializeField, Min(0f)] private float m_positionSpeed = 2f;
    [SerializeField, Min(0f)] private float m_rotationSpeedDegrees = 180f;

    private void Awake()
    {
        if (m_canvas == null)
        {
            m_canvas = GetComponent<RectTransform>();
        }
    }

    private void LateUpdate()
    {
        var cam = m_camera != null ? m_camera : Camera.main;
        if (cam == null || m_canvas == null)
        {
            return;
        }

        var camTransform = cam.transform;
        var targetPosition =
            camTransform.position +
            camTransform.forward * m_forwardDistance +
            camTransform.TransformVector(m_localOffset);

        var toCamera = camTransform.position - targetPosition;
        if (toCamera.sqrMagnitude < 1e-6f)
        {
            toCamera = camTransform.forward;
        }

        var targetRotation = Quaternion.LookRotation(toCamera, camTransform.up);

        m_canvas.position = MoveTowards(m_canvas.position, targetPosition, m_positionSpeed);
        m_canvas.rotation = RotateTowards(m_canvas.rotation, targetRotation, m_rotationSpeedDegrees);
    }

    private static Vector3 MoveTowards(Vector3 current, Vector3 target, float speed)
    {
        if (speed <= 0f)
        {
            return target;
        }

        return Vector3.MoveTowards(current, target, speed * Time.deltaTime);
    }

    private static Quaternion RotateTowards(Quaternion current, Quaternion target, float degreesPerSecond)
    {
        if (degreesPerSecond <= 0f)
        {
            return target;
        }

        return Quaternion.RotateTowards(current, target, degreesPerSecond * Time.deltaTime);
    }
}
