using UnityEngine;

public enum ShipCollisionType
{
    /// <summary>
    /// Receives cannonball hits, bumps other ship hulls, and runs aground on landscape.
    /// Placed on the ship's main body.
    /// </summary>
    Hull,

    /// <summary>
    /// Receives cannonball hits only — ignores other hulls and landscape.
    /// Placed on mast / sail geometry.
    /// </summary>
    Sail
}

/// <summary>
/// Owns the BoxCollider for one collidable part of a ship and assigns the correct
/// physics layer so the collision matrix handles all filtering automatically.
///
/// Attach two of these to a ship prefab:
///   • one sized to the hull  → type Hull
///   • one sized to the sails → type Sail
///
/// HealthComponent requires this component — no other script dependencies exist here.
/// Run  Tools > TwoPiece > Setup Physics Layers  once to create the layers and matrix.
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class ShipCollision : MonoBehaviour
{
    [Tooltip("Hull – collides with cannonballs, other hulls, and landscape.\n" +
             "Sail – collides with cannonballs only.")]
    public ShipCollisionType collisionType = ShipCollisionType.Hull;

    // -----------------------------------------------------------------------
    void Awake()   => ApplyLayer();

    // Updates the layer immediately when the value is changed in the Inspector.
    void OnValidate() => ApplyLayer();

    // -----------------------------------------------------------------------
    void ApplyLayer()
    {
        string layerName = collisionType == ShipCollisionType.Hull ? "ShipHull" : "ShipSail";
        int layer = LayerMask.NameToLayer(layerName);

        if (layer >= 0)
        {
            gameObject.layer = layer;
        }
        else
        {
            Debug.LogWarning(
                $"[ShipCollision] Layer '{layerName}' not found. " +
                "Run  Tools > TwoPiece > Setup Physics Layers  to create it.", this);
        }
    }
}
