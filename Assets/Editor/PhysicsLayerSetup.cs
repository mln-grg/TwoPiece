// Assets/Editor/PhysicsLayerSetup.cs
// Run once via:  Tools > TwoPiece > Setup Physics Layers
//
// Layers
// ──────
//   ShipHull   – main ship body     (Hull  ShipCollision)
//   ShipSail   – mast / sail area   (Sail  ShipCollision)
//   Cannonball – projectiles
//   Landscape  – islands / terrain
//
// Collision matrix
// ────────────────
//   Cannonball ↔ ShipHull    ON   cannonball hits hull → damage
//   Cannonball ↔ ShipSail    ON   cannonball hits sail → damage
//   Cannonball ↔ everything  OFF  projectiles ignore all other geometry
//   ShipHull   ↔ ShipHull    ON   ships bump each other
//   ShipHull   ↔ Landscape   ON   ships run aground
//   ShipHull   ↔ everything  OFF  hull ignores sails, scene debris, etc.
//   ShipSail   ↔ everything  OFF  sails are invisible to physics except cannonballs

using UnityEngine;
using UnityEditor;

public static class PhysicsLayerSetup
{
    const string LayerShipHull   = "ShipHull";
    const string LayerShipSail   = "ShipSail";
    const string LayerCannonball = "Cannonball";
    const string LayerLandscape  = "Landscape";

    [MenuItem("Tools/TwoPiece/Setup Physics Layers")]
    public static void Run()
    {
        // ── 1. Ensure all layers exist ──────────────────────────────────────
        EnsureLayer(LayerShipHull);
        EnsureLayer(LayerShipSail);
        EnsureLayer(LayerCannonball);
        EnsureLayer(LayerLandscape);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        int shipHull   = LayerMask.NameToLayer(LayerShipHull);
        int shipSail   = LayerMask.NameToLayer(LayerShipSail);
        int cannonball = LayerMask.NameToLayer(LayerCannonball);
        int landscape  = LayerMask.NameToLayer(LayerLandscape);

        bool ok = true;
        if (shipHull   < 0) { Debug.LogError($"[PhysicsLayerSetup] Layer '{LayerShipHull}'   missing."); ok = false; }
        if (shipSail   < 0) { Debug.LogError($"[PhysicsLayerSetup] Layer '{LayerShipSail}'   missing."); ok = false; }
        if (cannonball < 0) { Debug.LogError($"[PhysicsLayerSetup] Layer '{LayerCannonball}' missing."); ok = false; }
        if (landscape  < 0) { Debug.LogError($"[PhysicsLayerSetup] Layer '{LayerLandscape}'  missing."); ok = false; }
        if (!ok) return;

        // ── 2. Configure the collision matrix ──────────────────────────────

        // Cannonball: only hits ShipHull and ShipSail — ignore everything else
        for (int i = 0; i < 32; i++)
            Physics.IgnoreLayerCollision(cannonball, i, true);

        Physics.IgnoreLayerCollision(cannonball, shipHull, false);
        Physics.IgnoreLayerCollision(cannonball, shipSail, false);

        // ShipSail: only hit by cannonballs — ignore everything else
        for (int i = 0; i < 32; i++)
            Physics.IgnoreLayerCollision(shipSail, i, true);

        Physics.IgnoreLayerCollision(shipSail, cannonball, false);  // mirrors the above

        // ShipHull: hits other hulls and landscape — ignore everything else
        for (int i = 0; i < 32; i++)
            Physics.IgnoreLayerCollision(shipHull, i, true);

        Physics.IgnoreLayerCollision(shipHull, cannonball,  false);
        Physics.IgnoreLayerCollision(shipHull, shipHull,    false);
        Physics.IgnoreLayerCollision(shipHull, landscape,   false);

        // ── 3. Persist ──────────────────────────────────────────────────────
        var physicsAssets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/Physics.asset");
        if (physicsAssets.Length > 0)
            EditorUtility.SetDirty(physicsAssets[0]);

        AssetDatabase.SaveAssets();

        Debug.Log(
            "[PhysicsLayerSetup] Done.\n\n" +
            $"  ShipHull   → layer {shipHull}\n"   +
            $"  ShipSail   → layer {shipSail}\n"   +
            $"  Cannonball → layer {cannonball}\n" +
            $"  Landscape  → layer {landscape}\n\n" +
            "Remaining steps:\n" +
            "  • Add two ShipCollision + HealthComponent pairs to each ship prefab:\n" +
            "      – one BoxCollider sized to the hull,  type = Hull\n" +
            "      – one BoxCollider sized to the sails, type = Sail\n" +
            "  • Set island / terrain GameObjects to the 'Landscape' layer.\n" +
            "  • ShipCollision assigns its layer automatically; " +
                "Cannonball assigns its layer automatically.");
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    static bool EnsureLayer(string name)
    {
        if (LayerMask.NameToLayer(name) >= 0)
        {
            Debug.Log($"[PhysicsLayerSetup] Layer '{name}' already exists — skipping.");
            return false;
        }

        var tagManager = new SerializedObject(
            AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);

        SerializedProperty layers = tagManager.FindProperty("layers");

        for (int i = 8; i < 32; i++)
        {
            SerializedProperty slot = layers.GetArrayElementAtIndex(i);
            if (string.IsNullOrEmpty(slot.stringValue))
            {
                slot.stringValue = name;
                tagManager.ApplyModifiedProperties();
                Debug.Log($"[PhysicsLayerSetup] Created layer '{name}' at index {i}.");
                return true;
            }
        }

        Debug.LogError(
            $"[PhysicsLayerSetup] No free slot for '{name}'. " +
            "Delete an unused layer in Project Settings > Tags & Layers.");
        return false;
    }

    [MenuItem("Tools/TwoPiece/Setup Physics Layers", validate = true)]
    static bool Validate() => !Application.isPlaying;
}
