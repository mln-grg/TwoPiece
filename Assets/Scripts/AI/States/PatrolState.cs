using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The ship wanders between random waypoints within its patrol radius.
///
/// Transition rules:
///   → CombatState  if an enemy enters detection range, or if the brain
///                  receives damage (handled externally by AIBrain).
///
/// Patrol logic:
///   • On first entry the ship can optionally head to an editor-specified
///     initial destination (set <see cref="AIBrain.useInitialDestination"/>).
///   • After that it picks random waypoints inside the patrol radius anchored
///     to the ship's spawn position.
///   • The ship maintains HalfSail speed while patrolling.
/// </summary>
public class PatrolState : AIState
{
    public override string Name => "Patrol";

    // Proportional steering: the ship applies full rudder when the heading
    // error exceeds this many degrees, and eases off as it gets closer.
    const float SteeringFullRudderAngle = 35f;

    Vector3 _destination;
    bool    _hasDestination;

    // ─── Lifecycle ─────────────────────────────────────────────────────────────

    public override void OnEnter(AIBrain brain)
    {
        _hasDestination = false;
        AssignNextDestination(brain);
    }

    public override void OnExit(AIBrain brain)
    {
        // Clear steering so the ship doesn't carry over stale input
        brain.Ship.steeringInput = 0f;
    }

    // ─── Tick ──────────────────────────────────────────────────────────────────

    public override AIState Tick(AIBrain brain, float delta)
    {
        // ── 1. Enemy detection ─────────────────────────────────────────────────
        AIState combatTransition = CheckForEnemies(brain);
        if (combatTransition != null) return combatTransition;

        // ── 2. Ensure we have a destination ───────────────────────────────────
        if (!_hasDestination)
        {
            AssignNextDestination(brain);
            return null;
        }

        // ── 3. Arrival check ──────────────────────────────────────────────────
        Vector3 flatPos  = Flatten(brain.transform.position);
        Vector3 flatDest = Flatten(_destination);

        if (Vector3.Distance(flatPos, flatDest) <= brain.waypointArrivalDistance)
        {
            brain.Log($"Waypoint reached. Selecting next.");
            AssignNextDestination(brain);
            return null;
        }

        // ── 4. Navigate ────────────────────────────────────────────────────────
        SteerToward(brain, flatPos, flatDest);
        MaintainPatrolSpeed(brain);

        return null;
    }

    // ─── Enemy detection ───────────────────────────────────────────────────────

    AIState CheckForEnemies(AIBrain brain)
    {
        Collider[] hits = Physics.OverlapSphere(
            brain.transform.position, brain.detectionRange, brain.detectionMask);

        // De-duplicate by root so multi-collider ships are only evaluated once
        HashSet<GameObject> seen = new();

        foreach (Collider col in hits)
        {
            GameObject root = col.transform.root.gameObject;

            if (root == brain.gameObject) continue;
            if (!seen.Add(root))         continue;

            if (brain.IsEnemy(root))
            {
                brain.CurrentTarget = root.transform;
                brain.Log($"Enemy spotted during patrol: {root.name}");
                return new CombatState();
            }
        }

        return null;
    }

    // ─── Waypoint assignment ───────────────────────────────────────────────────

    void AssignNextDestination(AIBrain brain)
    {
        if (brain.useInitialDestination && !brain.InitialDestinationConsumed)
        {
            // Use the designer-specified first waypoint exactly once
            _destination = brain.initialDestination;
            brain.InitialDestinationConsumed = true;
            brain.Log($"Heading to initial destination {_destination}");
        }
        else
        {
            // Pick a random point inside the patrol radius centred on spawn
            Vector2 disc = Random.insideUnitCircle * brain.patrolRadius;
            _destination = brain.SpawnPosition + new Vector3(disc.x, 0f, disc.y);
            brain.Log($"New patrol waypoint: {_destination}");
        }

        _hasDestination = true;
    }

    // ─── Navigation helpers ────────────────────────────────────────────────────

    void SteerToward(AIBrain brain, Vector3 flatPos, Vector3 flatDest)
    {
        Vector3 dirToTarget = (flatDest - flatPos).normalized;
        Vector3 flatForward = Flatten(brain.transform.forward).normalized;

        float signedAngle = Vector3.SignedAngle(flatForward, dirToTarget, Vector3.up);

        // Proportional steering — ±1 at full rudder angle, tapers toward 0 on approach
        brain.Ship.steeringInput = Mathf.Clamp(signedAngle / SteeringFullRudderAngle, -1f, 1f);
    }

    /// <summary>
    /// Steps sailDelta toward HalfSail. sailDelta is consumed by ShipController
    /// each frame (one step per call), so we keep nudging until we're there.
    /// </summary>
    void MaintainPatrolSpeed(AIBrain brain)
    {
        SailState current = brain.Ship.currentSail;
        SailState target  = SailState.HalfSail;

        if (current == target) return;

        brain.Ship.sailDelta = current < target ? 1 : -1;
    }

    // ─── Utilities ─────────────────────────────────────────────────────────────

    static Vector3 Flatten(Vector3 v) => new Vector3(v.x, 0f, v.z);
}
