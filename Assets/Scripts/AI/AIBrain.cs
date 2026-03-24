using System.Collections.Generic;
using UnityEngine;

public enum AIArchetype
{
    /// <summary>
    /// Does not engage the player on sight. Will retaliate if attacked,
    /// and will enter combat if a known hostile enters detection range.
    /// </summary>
    Neutral,

    /// <summary>
    /// Treats the player as a hostile target on sight.
    /// </summary>
    Enemy
}

/// <summary>
/// The AI state machine and shared blackboard.
///
/// This MonoBehaviour owns all data that states need to read or write.
/// States themselves are plain C# objects (no MonoBehaviour) that reference
/// this brain to drive the ship.
///
/// Transitions are initiated in two ways:
///   1. A state returns a new AIState from its Tick method.
///   2. An external event (e.g. taking damage) calls TransitionTo directly.
/// </summary>
[RequireComponent(typeof(ShipController))]
[RequireComponent(typeof(CannonsController))]
public class AIBrain : MonoBehaviour
{
    // ─── Inspector Configuration ───────────────────────────────────────────────

    [Header("Archetype")]
    [Tooltip("Neutral: only fights back when attacked or when a known hostile enters range.\n" +
             "Enemy: treats the Player on sight as a hostile target.")]
    public AIArchetype archetype = AIArchetype.Neutral;

    [Header("Detection")]
    [Tooltip("Radius within which this ship notices potential enemies.")]
    public float detectionRange = 30f;

    [Tooltip("Layer mask for enemy detection sphere queries. Set this to include only ship layers.")]
    public LayerMask detectionMask = ~0;

    [Header("Patrol")]
    [Tooltip("Ships wander randomly within this radius, anchored to their spawn position.")]
    public float patrolRadius = 60f;

    [Tooltip("How close the ship must get to a waypoint before selecting the next one.")]
    public float waypointArrivalDistance = 5f;

    [Tooltip("If enabled the ship will head to Initial Destination first before patrolling randomly.")]
    public bool useInitialDestination = false;

    [Tooltip("First waypoint the ship heads to when it enters patrol. Only used once.")]
    public Vector3 initialDestination;

    [Header("Debug")]
    [Tooltip("Enable/disable log messages from this brain and its states.")]
    public bool debugLog = true;

    [Tooltip("Draw gizmos for patrol radius, detection range, target lines, etc.")]
    public bool drawGizmos = true;

    // ─── Blackboard — shared data for states ───────────────────────────────────
    // States read and write these fields to communicate without coupling to each other.

    /// <summary>Cached ShipController on this GameObject.</summary>
    [HideInInspector] public ShipController Ship;

    /// <summary>Cached CannonsController on this GameObject.</summary>
    [HideInInspector] public CannonsController Cannons;

    /// <summary>Active combat target. Null when not in combat.</summary>
    [HideInInspector] public Transform CurrentTarget;

    /// <summary>World position where this ship spawned. Used as patrol anchor.</summary>
    [HideInInspector] public Vector3 SpawnPosition;

    /// <summary>
    /// True after the initial destination waypoint has been consumed.
    /// Set by PatrolState so re-entering patrol skips the initial point.
    /// </summary>
    [HideInInspector] public bool InitialDestinationConsumed;

    /// <summary>
    /// All GameObjects that have been flagged as hostile to this ship —
    /// either by attacking it or by being an Enemy-archetype target.
    /// </summary>
    [HideInInspector] public readonly HashSet<GameObject> KnownEnemies = new();

    // ─── Inspector readout (runtime, read-only intent) ─────────────────────────

    [Header("Runtime — Read Only")]
    [SerializeField] string _stateName   = "None";
    [SerializeField] string _targetName  = "None";
    [SerializeField] int    _enemyCount;

    // ─── Private ───────────────────────────────────────────────────────────────

    AIState _currentState;

    // ─── Unity Lifecycle ───────────────────────────────────────────────────────

    void Awake()
    {
        Ship         = GetComponent<ShipController>();
        Cannons      = GetComponent<CannonsController>();
        SpawnPosition = transform.position;
    }

    void Start()
    {
        SubscribeToDamageEvents();
        TransitionTo(new PatrolState());
    }

    void Update()
    {
        if (_currentState == null) return;

        // Ship has been destroyed — shut everything down immediately.
        if (Ship.IsDestroyed)
        {
            _currentState.OnExit(this);
            _currentState  = null;
            CurrentTarget  = null;
            Ship.steeringInput = 0f;

            // Kill any broadside volley that is already mid-coroutine.
            Cannons.StopAllCoroutines();

            enabled = false;   // stop this Update from running again
            return;
        }

        UpdateInspectorReadout();

        AIState next = _currentState.Tick(this, Time.deltaTime);
        if (next != null)
            TransitionTo(next);
    }

    void OnDestroy()
    {
        UnsubscribeFromDamageEvents();
    }

    // ─── State Machine ─────────────────────────────────────────────────────────

    /// <summary>
    /// Transitions the brain to a new state. Calls OnExit on the current state
    /// and OnEnter on the incoming state. Safe to call from within a state's Tick.
    /// </summary>
    public void TransitionTo(AIState next)
    {
        if (next == null)
        {
            Log("TransitionTo called with null — ignoring.");
            return;
        }

        if (_currentState != null)
        {
            Log($"  ← Exit  [{_currentState.Name}]");
            _currentState.OnExit(this);
        }

        _currentState = next;
        _stateName    = next.Name;

        Log($"  → Enter [{_currentState.Name}]");
        _currentState.OnEnter(this);
    }

    // ─── Hostility ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns true if <paramref name="obj"/> should be treated as a combat target.
    /// <list type="bullet">
    ///   <item>Always true for objects in <see cref="KnownEnemies"/>.</item>
    ///   <item>True for Player-tagged objects when archetype is Enemy.</item>
    /// </list>
    /// </summary>
    public bool IsEnemy(GameObject obj)
    {
        if (obj == null || obj == gameObject) return false;
        if (KnownEnemies.Contains(obj)) return true;
        if (archetype == AIArchetype.Enemy && obj.CompareTag("Player")) return true;
        return false;
    }

    /// <summary>
    /// Marks <paramref name="attacker"/> as a known enemy.
    /// If this is a new threat and no current target exists, immediately
    /// transitions to <see cref="CombatState"/>.
    /// </summary>
    public void RegisterAttacker(GameObject attacker)
    {
        if (attacker == null || attacker == gameObject) return;

        bool isNew = KnownEnemies.Add(attacker);
        if (!isNew) return; // Already tracking this enemy

        Log($"New attacker registered: {attacker.name}");

        if (CurrentTarget == null)
        {
            CurrentTarget = attacker.transform;
            TransitionTo(new CombatState());
        }
    }

    // ─── Damage Subscriptions ──────────────────────────────────────────────────
    // Scans ALL HealthComponents in children so aggro fires regardless of whether
    // ShipController.hullHealth / sailHealth are wired in the Inspector.

    void SubscribeToDamageEvents()
    {
        foreach (HealthComponent hc in GetComponentsInChildren<HealthComponent>())
            hc.OnDamaged += OnAnyPartDamaged;
    }

    void UnsubscribeFromDamageEvents()
    {
        foreach (HealthComponent hc in GetComponentsInChildren<HealthComponent>())
            hc.OnDamaged -= OnAnyPartDamaged;
    }

    // Any hit on any part of this ship (hull or sail) triggers retaliation.
    void OnAnyPartDamaged(DamageInfo info) => RegisterAttacker(info.source);

    // ─── Utilities ─────────────────────────────────────────────────────────────

    /// <summary>Logs a message prefixed with this brain's name if debug logging is on.</summary>
    public void Log(string msg)
    {
        if (debugLog) Debug.Log($"[AIBrain:{name}] {msg}");
    }

    void UpdateInspectorReadout()
    {
        _targetName = CurrentTarget != null ? CurrentTarget.name : "None";
        _enemyCount = KnownEnemies.Count;
    }

    // ─── Gizmos ────────────────────────────────────────────────────────────────

    void OnDrawGizmosSelected()
    {
        if (!drawGizmos) return;

        Vector3 anchor = Application.isPlaying ? SpawnPosition : transform.position;

        // Patrol radius — blue
        Gizmos.color = new Color(0.2f, 0.6f, 1f, 0.07f);
        Gizmos.DrawSphere(anchor, patrolRadius);
        Gizmos.color = new Color(0.2f, 0.6f, 1f, 0.6f);
        Gizmos.DrawWireSphere(anchor, patrolRadius);

        // Detection range — orange
        Gizmos.color = new Color(1f, 0.5f, 0f, 0.07f);
        Gizmos.DrawSphere(transform.position, detectionRange);
        Gizmos.color = new Color(1f, 0.5f, 0f, 0.7f);
        Gizmos.DrawWireSphere(transform.position, detectionRange);

        // Initial destination — cyan marker
        if (useInitialDestination && !InitialDestinationConsumed)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawSphere(initialDestination, 1.5f);
            Gizmos.DrawLine(transform.position, initialDestination);
        }

        // Active target — red line
        if (CurrentTarget != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawLine(transform.position, CurrentTarget.position);
        }
    }
}
