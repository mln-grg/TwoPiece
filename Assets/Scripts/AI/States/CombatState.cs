using UnityEngine;

/// <summary>
/// The ship actively engages its current target.
///
/// Distance bands:
///   dist > EngageDistance (60)  → Approach  : close the gap at speed
///   dist in [MinFireDist, EngageDistance] → Maneuver zone
///     angle correct              → Broadside : hold position and fire
///     angle wrong, target ahead  → Reposition at half sail
///     angle wrong, target behind → Reposition at no sail (stop while turning)
///
/// Transition rules:
///   → PatrolState  when the target is null, destroyed, or beyond pursuit range
/// </summary>
public class CombatState : AIState
{
    public override string Name => "Combat";

    // ── Tuning ─────────────────────────────────────────────────────────────────

    const float MaxPursuitDistance      = 200f;

    /// <summary>
    /// Close in whenever beyond this distance.
    /// Must be LESS than MaxFireDistance so we don't try to establish a
    /// broadside angle while still too far away to shoot.
    /// </summary>
    const float EngageDistance          = 50f;

    const float DesiredCombatDistance   = 32f;
    const float DistanceTolerance       = 6f;
    const float MinFireDistance         = 12f;
    const float MaxFireDistance         = 50f;   // Matches EngageDistance

    /// <summary>
    /// Target angle (from ship forward) where we want the enemy to sit off our side.
    /// 80° = slightly forward of pure perpendicular.
    /// </summary>
    const float PreferredBroadsideAngle = 80f;

    /// <summary>
    /// How many degrees of slop we allow before entering Reposition.
    /// Wider = easier to stay in Broadside mode; narrower = more precise.
    /// </summary>
    const float BroadsideTolerance      = 20f;

    /// <summary>
    /// Half-angle of the broadside cannon arc.
    /// Firing only triggers when the target is within this many degrees of 90° off our side.
    /// </summary>
    const float FireArcHalfAngle        = 28f;

    /// <summary>
    /// Seconds between volleys. Deliberately longer than CannonsController.minFireInterval
    /// so the controller's spam-guard is never hit.
    /// </summary>
    const float FireCooldown            = 4f;

    const float SteeringFullRudder      = 25f;
    const float LeadAmount              = 1.2f;

    // ── Runtime ────────────────────────────────────────────────────────────────

    enum Mode { Approach, Broadside, Reposition }

    Mode  _mode;
    float _fireTimer;

    /// <summary>
    /// -1 = engage from the left side, +1 = from the right side, 0 = undecided.
    /// Settled on the first tick via the initial target angle and only switched
    /// when the target clearly crosses to the opposite half (45° hysteresis).
    /// </summary>
    int _engageSide;

    // ─── Lifecycle ─────────────────────────────────────────────────────────────

    public override void OnEnter(AIBrain brain)
    {
        brain.Log($"Engaging target: {brain.CurrentTarget?.name ?? "Unknown"}");

        // Debug cannon readiness up front — reveals empty lists / missing origins
        brain.Log($"[Cannon Check] Left cannons: {brain.Cannons.leftCannons?.Count ?? -1}  " +
                  $"Right cannons: {brain.Cannons.rightCannons?.Count ?? -1}  " +
                  $"LeftOrigin: {(brain.Cannons.leftCannonOrigin != null ? "OK" : "NULL")}  " +
                  $"RightOrigin: {(brain.Cannons.rightCannonOrigin != null ? "OK" : "NULL")}");

        _engageSide = 0;
        _fireTimer  = 1.5f;
        _mode       = Mode.Approach;
    }

    public override void OnExit(AIBrain brain)
    {
        brain.Ship.steeringInput = 0f;
        brain.Log("Disengaging.");
    }

    // ─── Tick ──────────────────────────────────────────────────────────────────

    public override AIState Tick(AIBrain brain, float delta)
    {
        // ── 1. Target validation ───────────────────────────────────────────────
        if (!IsTargetValid(brain))
        {
            brain.Log("Target lost — returning to patrol.");
            brain.CurrentTarget = null;
            return new PatrolState();
        }

        _fireTimer -= delta;

        // ── 2. Shared geometry ─────────────────────────────────────────────────
        // signedAngle > 0 → target is to the RIGHT
        // signedAngle < 0 → target is to the LEFT
        Vector3 toTarget = brain.CurrentTarget.position - brain.transform.position;
        toTarget.y = 0f;

        float distance    = toTarget.magnitude;
        float signedAngle = Vector3.SignedAngle(brain.transform.forward, toTarget, Vector3.up);

        // ── 3. Decide which side to engage from ────────────────────────────────
        UpdateEngageSide(signedAngle);

        // ── 4. Mode selection ──────────────────────────────────────────────────
        UpdateMode(brain, distance, signedAngle);

        // ── 5. Movement ────────────────────────────────────────────────────────
        ApplyMovement(brain, distance, signedAngle);

        // ── 6. Firing ──────────────────────────────────────────────────────────
        TryFire(brain, toTarget, distance, signedAngle);

        return null;
    }

    // ─── Engage-side selection ─────────────────────────────────────────────────

    /// <summary>
    /// Commits to the side the target is already on, then holds it with a
    /// 45° dead zone to avoid oscillating when the target is near our bow/stern.
    /// </summary>
    void UpdateEngageSide(float signedAngle)
    {
        if (_engageSide == 0)
        {
            if      (signedAngle < -10f) _engageSide = -1;
            else if (signedAngle >  10f) _engageSide =  1;
            // Inside ±10° leave undecided one more tick
        }
        else
        {
            if (_engageSide == -1 && signedAngle >  45f) _engageSide =  1;
            if (_engageSide ==  1 && signedAngle < -45f) _engageSide = -1;
        }
    }

    /// <summary>The signed angle we want the target to sit at.</summary>
    float DesiredAngle => _engageSide >= 0 ? PreferredBroadsideAngle : -PreferredBroadsideAngle;

    // ─── Mode selection ────────────────────────────────────────────────────────

    void UpdateMode(AIBrain brain, float distance, float signedAngle)
    {
        Mode previous = _mode;

        if (distance > EngageDistance)
        {
            // Too far — just close in, don't bother with angle yet
            _mode = Mode.Approach;
        }
        else if (distance < MinFireDistance)
        {
            // Too close — reposition back out
            _mode = Mode.Reposition;
        }
        else if (Mathf.Abs(signedAngle - DesiredAngle) < BroadsideTolerance)
        {
            // In range and angle is correct — fire!
            _mode = Mode.Broadside;
        }
        else
        {
            // In range but need to turn to expose the broadside
            _mode = Mode.Reposition;
        }

        if (_mode != previous)
            brain.Log($"Combat mode → {_mode}  " +
                      $"(dist:{distance:F1}  angle:{signedAngle:F1}°  " +
                      $"desired:{DesiredAngle:F0}°  side:{(_engageSide >= 0 ? "right" : "left")})");
    }

    // ─── Movement ──────────────────────────────────────────────────────────────

    void ApplyMovement(AIBrain brain, float distance, float signedAngle)
    {
        switch (_mode)
        {
            case Mode.Approach:
                // If target is mostly behind us (>120°) use half sail so the
                // turning radius stays tight — full sail here just drives us away.
                bool targetBehind = Mathf.Abs(signedAngle) > 120f;
                SetSail(brain, targetBehind ? GearState.Gear2 : GearState.Gear3);

                // Steer directly toward target
                brain.Ship.steeringInput = Mathf.Clamp(
                    signedAngle / SteeringFullRudder, -1f, 1f);
                break;

            case Mode.Broadside:
                // Distance management: close in, hold, or drift back
                if      (distance > DesiredCombatDistance + DistanceTolerance) SetSail(brain, GearState.Gear3);
                else if (distance < DesiredCombatDistance - DistanceTolerance) SetSail(brain, GearState.Idle);
                else                                                            SetSail(brain, GearState.Gear2);

                // Angle maintenance: nudge steering to keep target at DesiredAngle.
                // DeltaAngle handles the ±180° wrap so we always take the shortest arc.
                // Convention: DeltaAngle(desired→actual) > 0 → actual is clockwise of desired
                //             → steer clockwise (right, +1) to correct.
                float angleDiff = Mathf.DeltaAngle(DesiredAngle, signedAngle);
                brain.Ship.steeringInput = Mathf.Clamp(
                    angleDiff / SteeringFullRudder, -1f, 1f);
                break;

            case Mode.Reposition:
                // Always keep half sail so the ship retains steerage way.
                // Idle when target is behind caused the ship to stop dead and
                // spin in place because it had no forward momentum to steer with.
                SetSail(brain, GearState.Gear2);

                // DeltaAngle(actual→desired) gives the shortest signed rotation needed.
                //   > 0 → rotate counterclockwise (left)  → steeringInput = -1
                //   < 0 → rotate clockwise (right)        → steeringInput = +1
                // The previous formula used (desired - actual) with the wrong sign and
                // no wrap handling, which is what caused the ship to spiral to ±180°.
                float shortestArc = Mathf.DeltaAngle(signedAngle, DesiredAngle);
                brain.Ship.steeringInput = shortestArc > 0f ? -1f : 1f;
                break;
        }
    }

    void SetSail(AIBrain brain, GearState target)
    {
        GearState current = brain.Ship.currentGear;
        if (current == target) return;
        brain.Ship.sailDelta = current < target ? 1 : -1;
    }

    // ─── Firing ────────────────────────────────────────────────────────────────

    void TryFire(AIBrain brain, Vector3 toTarget, float distance, float signedAngle)
    {
        if (_fireTimer > 0f) return;

        // ── Distance gate ──────────────────────────────────────────────────────
        if (distance < MinFireDistance || distance > MaxFireDistance)
        {
            brain.Log($"[Fire blocked] distance {distance:F1} outside [{MinFireDistance}, {MaxFireDistance}]");
            _fireTimer = 0.5f; // Throttle the log spam
            return;
        }

        // ── Arc gate ───────────────────────────────────────────────────────────
        // Target must be within FireArcHalfAngle degrees of a 90° perpendicular.
        // signedAngle -60 to -120 → left broadside  |  +60 to +120 → right broadside
        float absAngle  = Mathf.Abs(signedAngle);
        bool  inArc     = absAngle > (90f - FireArcHalfAngle) && absAngle < (90f + FireArcHalfAngle);

        if (!inArc)
        {
            brain.Log($"[Fire blocked] angle {signedAngle:F1}° outside broadside arc " +
                      $"(need ±{90f - FireArcHalfAngle:F0}° to ±{90f + FireArcHalfAngle:F0}°)");
            _fireTimer = 0.5f;
            return;
        }

        // ── Cannon readiness check ─────────────────────────────────────────────
        bool firingLeft = signedAngle < 0f;

        if (firingLeft)
        {
            int  count  = brain.Cannons.leftCannons?.Count ?? 0;
            bool origin = brain.Cannons.leftCannonOrigin != null;
            if (count == 0 || !origin)
            {
                brain.Log($"[Fire blocked] Left broadside not ready — " +
                          $"cannons:{count}  origin:{(origin ? "OK" : "NULL")}");
                _fireTimer = 1f;
                return;
            }
        }
        else
        {
            int  count  = brain.Cannons.rightCannons?.Count ?? 0;
            bool origin = brain.Cannons.rightCannonOrigin != null;
            if (count == 0 || !origin)
            {
                brain.Log($"[Fire blocked] Right broadside not ready — " +
                          $"cannons:{count}  origin:{(origin ? "OK" : "NULL")}");
                _fireTimer = 1f;
                return;
            }
        }

        // ── Lead targeting ─────────────────────────────────────────────────────
        Vector3 aimPoint = brain.CurrentTarget.position;
        Rigidbody targetRb = brain.CurrentTarget.GetComponent<Rigidbody>();
        if (targetRb != null)
        {
            float timeToTarget = distance / brain.Cannons.muzzleVelocity;
            aimPoint += targetRb.linearVelocity * timeToTarget * LeadAmount;
        }

        // ── Fire ───────────────────────────────────────────────────────────────
        if (firingLeft)
        {
            brain.Cannons.FireLeftBroadsideAtPoint(aimPoint);
            brain.Log($"Fired LEFT broadside  (angle:{signedAngle:F1}°  dist:{distance:F1}  aim:{aimPoint})");
        }
        else
        {
            brain.Cannons.FireRightBroadsideAtPoint(aimPoint);
            brain.Log($"Fired RIGHT broadside  (angle:{signedAngle:F1}°  dist:{distance:F1}  aim:{aimPoint})");
        }

        _fireTimer = FireCooldown + Random.Range(-0.5f, 0.5f);
    }

    // ─── Validation ────────────────────────────────────────────────────────────

    bool IsTargetValid(AIBrain brain)
    {
        if (brain.CurrentTarget == null)                          return false;
        if (!brain.CurrentTarget.gameObject.activeInHierarchy)   return false;

        float dist = Vector3.Distance(brain.transform.position, brain.CurrentTarget.position);
        if (dist > MaxPursuitDistance)
        {
            brain.Log($"Target out of pursuit range ({dist:F0} > {MaxPursuitDistance}).");
            return false;
        }

        return true;
    }
}
