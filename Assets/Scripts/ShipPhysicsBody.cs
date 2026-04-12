using UnityEngine;

/// <summary>
/// Triggers a camera shake on the first frame of a physics collision.
/// The old kinematic penetration-resolution logic has been removed — the
/// Rigidbody's built-in physics engine now handles all collision response.
/// </summary>
[DefaultExecutionOrder(100)]
[RequireComponent(typeof(Rigidbody))]
public class ShipPhysicsBody : MonoBehaviour
{
    [Header("Camera Shake")]
    [Tooltip("The ShipCamera to shake on impact. Assign on the player ship only.")]
    public ShipCamera shipCamera;

    [Tooltip("Base shake intensity. Actual intensity = this × normalised collision speed.")]
    public float shakeForce = 1f;

    ShipController _controller;

    void Awake()
    {
        _controller = GetComponent<ShipController>();
        // NOTE: do NOT configure the Rigidbody here — ShipController owns those settings.
    }

    void OnCollisionEnter(Collision collision)
    {
        if (shipCamera == null) return;

        float speed      = _controller != null ? _controller.CurrentSpeed  : 0f;
        float maxSpeed   = _controller != null ? _controller.MaxSpeed      : 1f;
        float normalised = Mathf.Clamp01(speed / Mathf.Max(maxSpeed, 0.01f));

        shipCamera.Shake(shakeForce * normalised);
    }
}
