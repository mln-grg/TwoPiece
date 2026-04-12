using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    [Header("Target")]
    public Transform target;

    [Header("Follow Settings")]
    public Vector3 followOffset = new Vector3(0, 5, -10);
    public float followSmoothTime = 0.1f;

    [Header("Look Settings")]
    public Vector3 lookOffset = new Vector3(0, 2, 5);
    public float lookSmoothTime = 0.1f;

    private Vector3 currentVelocity;
    private Vector3 lookVelocity;

    private void FixedUpdate()
    {
        if (target == null) return;

        // Calculate desired position
        Vector3 desiredPosition = target.TransformPoint(followOffset);
        transform.position = Vector3.SmoothDamp(transform.position, desiredPosition, ref currentVelocity, followSmoothTime);

        // Calculate desired look-at point
        Vector3 desiredLookPoint = target.TransformPoint(lookOffset);
        Vector3 lookDirection = desiredLookPoint - transform.position;
        Quaternion desiredRotation = Quaternion.LookRotation(lookDirection);

        transform.rotation = Quaternion.Slerp(transform.rotation, desiredRotation, lookSmoothTime);
    }
}