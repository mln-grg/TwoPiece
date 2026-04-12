using UnityEngine;

/// <summary>
/// Legacy input for HoverCarController.
///
/// Keyboard:      W = forward   S = brake/reverse   A/D = steer
///                Left Shift = drift   Space = jump
///
/// Gamepad:       Right Trigger = forward   Left Stick X = steer
///                RB = drift   A/Cross = jump
///
/// Set rightTriggerButton in the Inspector to match your controller's
/// button index for RT. To find it: play the scene and press RT while
/// watching the joystick button number in a debug log, or check your
/// controller's Unity button map online.
/// </summary>
[RequireComponent(typeof(HoverCarController))]
public class HoverCarInput : MonoBehaviour
{
    [Tooltip("Joystick button index for the right trigger on your controller. " +
             "Common values: 7 (PS), 5 (Xbox RB) — check yours if unsure.")]
    public int rightTriggerButton = 7;

    HoverCarController car;

    void Awake()
    {
        car = GetComponent<HoverCarController>();
    }

    void Update()
    {
        // ── Throttle ─────────────────────────────────────────────────────
        // Forward: W key OR right trigger button
        float keyForward = Input.GetKey(KeyCode.W) ? 1f : 0f;
        float rtForward  = Input.GetKey((KeyCode)(KeyCode.Joystick1Button0 + rightTriggerButton)) ? 1f : 0f;
        float forward    = Mathf.Max(keyForward, rtForward);

        // Reverse/brake: S key
        float backward = Input.GetKey(KeyCode.S) ? 1f : 0f;

        car.ThrottleInput = forward - backward;

        // ── Steer ─────────────────────────────────────────────────────────
        car.SteerInput = Input.GetAxis("Horizontal");

        // ── Drift ─────────────────────────────────────────────────────────
        car.DriftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.JoystickButton4);

        // ── Jump ──────────────────────────────────────────────────────────
        if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.JoystickButton0))
            car.JumpPressed = true;
    }
}
