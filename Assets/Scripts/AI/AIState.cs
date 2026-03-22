/// <summary>
/// Non-MonoBehaviour base class for all AI states.
///
/// Each state owns its own data and logic. States communicate transitions back
/// to the machine by returning a new AIState from Tick — returning null means
/// "stay in this state". OnEnter / OnExit are guaranteed to be called exactly
/// once per transition.
/// </summary>
public abstract class AIState
{
    /// <summary>
    /// Display name used in debug logs and the inspector readout.
    /// Defaults to the class name; override to customise.
    /// </summary>
    public virtual string Name => GetType().Name;

    /// <summary>
    /// Called once immediately after this state becomes the active state.
    /// Use for setup — e.g. resetting timers, choosing an initial target.
    /// </summary>
    public virtual void OnEnter(AIBrain brain) { }

    /// <summary>
    /// Called once just before this state is replaced by another.
    /// Use for cleanup — e.g. zeroing control inputs, unsubscribing events.
    /// </summary>
    public virtual void OnExit(AIBrain brain) { }

    /// <summary>
    /// Called every frame while this state is active.
    /// <para>Return a new <see cref="AIState"/> to transition to it.</para>
    /// <para>Return <c>null</c> to remain in the current state.</para>
    /// </summary>
    /// <param name="brain">The owning brain — use this as the blackboard.</param>
    /// <param name="delta">Time since last frame (seconds).</param>
    public abstract AIState Tick(AIBrain brain, float delta);
}
