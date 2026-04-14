using UnityEngine;

/// <summary>
/// Legacy input for CarController.
///
/// Keyboard:  W = forward   S = brake/reverse   A/D = steer
///            Space = handbrake
///
/// Gamepad:   Right Trigger = forward   Left Stick X = steer
///            B/Circle (JoystickButton1) = handbrake
///
/// Set rightTriggerButton in the Inspector to match your controller's button index for RT.
/// Common values: 7 (PS), 5 (Xbox RB). Check your controller if unsure.
/// </summary>
[RequireComponent(typeof(CarController))]
public class CarInput : MonoBehaviour
{
    [Tooltip("Joystick button index for the right trigger on your controller. " +
             "Common values: 7 (PS), 5 (Xbox RB) — check yours if unsure.")]
    public int rightTriggerButton = 7;

    CarController car;

    void Awake()
    {
        car = GetComponent<CarController>();
    }

    void Update()
    {
        // ── Throttle ─────────────────────────────────────────────────────
        float keyForward = Input.GetKey(KeyCode.W) ? 1f : 0f;
        float rtForward  = Input.GetKey((KeyCode)(KeyCode.Joystick1Button0 + rightTriggerButton)) ? 1f : 0f;
        float forward    = Mathf.Max(keyForward, rtForward);

        float backward = Input.GetKey(KeyCode.S) ? 1f : 0f;

        car.ThrottleInput = forward - backward;

        // ── Steer ─────────────────────────────────────────────────────────
        car.SteerInput = Input.GetAxis("Horizontal");

        // ── Handbrake ─────────────────────────────────────────────────────
        // Space (keyboard) or JoystickButton1 (B/Circle on most controllers)
        car.HandbrakeHeld = Input.GetKey(KeyCode.Space)
                         || Input.GetKey(KeyCode.JoystickButton1);
    }
}
