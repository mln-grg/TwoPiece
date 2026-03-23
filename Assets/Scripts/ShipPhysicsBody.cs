using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Gives the ship physical presence against other hulls and landscape.
///
/// Every LateUpdate:
///   • Resolves penetration (pushes this ship out of overlapping colliders).
///   • Calls ApplyWallSlide so the ship cannot drive forward into the obstacle
///     but can slide along its surface.
///   • On FIRST contact with a new collider, triggers a camera shake.
///     Subsequent frames while in contact skip the shake — no repeated impulse.
///
/// Setup: add to the ship root alongside ShipController.
///        Wire shipCamera only on the player ship; leave null on enemies.
/// </summary>
[DefaultExecutionOrder(100)]
[RequireComponent(typeof(Rigidbody))]
public class ShipPhysicsBody : MonoBehaviour
{
    [Header("Camera Shake")]
    [Tooltip("The ShipCamera to shake on impact. Assign on the player ship only.")]
    public ShipCamera shipCamera;

    [Tooltip("Base shake force. Actual force = this × normalised collision speed.")]
    public float shakeForce = 1f;

    // -----------------------------------------------------------------------
    private Rigidbody      _rb;
    private ShipController _controller;
    private BoxCollider    _hullCollider;
    private int            _collisionMask;

    private const int         MaxOverlaps = 8;
    private readonly Collider[] _overlaps  = new Collider[MaxOverlaps];

    // Tracks which colliders were overlapping last frame so we fire the
    // camera shake only on the first frame of contact.
    private readonly HashSet<Collider> _activeOverlaps = new HashSet<Collider>();
    private readonly HashSet<Collider> _nextOverlaps   = new HashSet<Collider>();

    // -----------------------------------------------------------------------
    void Awake()
    {
        _rb               = GetComponent<Rigidbody>();
        _rb.isKinematic   = true;
        _rb.useGravity    = false;
        _rb.interpolation = RigidbodyInterpolation.None;

        _controller = GetComponent<ShipController>();

        foreach (ShipCollision sc in GetComponentsInChildren<ShipCollision>())
        {
            if (sc.collisionType == ShipCollisionType.Hull)
            {
                _hullCollider = sc.GetComponent<BoxCollider>();
                break;
            }
        }

        if (_hullCollider == null)
            Debug.LogWarning("[ShipPhysicsBody] No Hull-type ShipCollision found in children.", this);

        int hullLayer      = LayerMask.NameToLayer("ShipHull");
        int landscapeLayer = LayerMask.NameToLayer("Landscape");

        if (hullLayer      < 0) Debug.LogWarning("[ShipPhysicsBody] Layer 'ShipHull' missing.",  this);
        if (landscapeLayer < 0) Debug.LogWarning("[ShipPhysicsBody] Layer 'Landscape' missing.", this);

        if (hullLayer      >= 0) _collisionMask |= 1 << hullLayer;
        if (landscapeLayer >= 0) _collisionMask |= 1 << landscapeLayer;
    }

    // -----------------------------------------------------------------------
    void LateUpdate()
    {
        if (_hullCollider == null || _collisionMask == 0) return;

        Physics.SyncTransforms();

        Bounds b     = _hullCollider.bounds;
        int    count = Physics.OverlapBoxNonAlloc(
            b.center, b.extents, _overlaps,
            transform.rotation, _collisionMask,
            QueryTriggerInteraction.Ignore);

        _nextOverlaps.Clear();

        for (int i = 0; i < count; i++)
        {
            Collider other = _overlaps[i];
            if (other == null || other == _hullCollider) continue;
            if (other.isTrigger)                         continue;
            if (other.transform.IsChildOf(transform))    continue;
            if (other.attachedRigidbody == _rb)          continue;

            if (!Physics.ComputePenetration(
                    _hullCollider, _hullCollider.transform.position, _hullCollider.transform.rotation,
                    other,         other.transform.position,         other.transform.rotation,
                    out Vector3 pushDir, out float pushDist))
                continue;

            // ── Push this ship out of the overlap ───────────────────────────
            transform.position += pushDir * pushDist;

            // ── Block forward motion into the wall (allow sliding along it) ─
            _controller?.ApplyWallSlide(pushDir);

            // ── Camera shake on first contact only ──────────────────────────
            if (!_activeOverlaps.Contains(other) && shipCamera != null)
            {
                float speed         = _controller != null ? _controller.CurrentSpeed : 0f;
                float normalisedHit = Mathf.Clamp01(speed / (_controller != null ? _controller.MaxSpeed : 1f));
                shipCamera.Shake(shakeForce * normalisedHit);
            }

            _nextOverlaps.Add(other);
        }

        // Swap sets for next frame
        _activeOverlaps.Clear();
        foreach (Collider c in _nextOverlaps)
            _activeOverlaps.Add(c);
    }
}
