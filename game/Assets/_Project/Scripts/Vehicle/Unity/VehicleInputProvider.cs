using UnityEngine;
using UnityEngine.InputSystem;

namespace Shitboxer.Vehicle
{
    /// <summary>
    /// Polls keyboard/gamepad and writes VehicleInput into the controller. Deliberately
    /// direct-device polling for Phase 1 — swap for an .inputactions asset when menus exist.
    /// Keys: WASD/arrows drive, Space handbrake, R flips the car back onto its wheels.
    ///
    /// Analog inputs are cleaned up before they reach the sim: sticks/triggers get a deadzone
    /// with a smooth remap (no jump at the edge), steering runs through a gentle response curve
    /// so small movements are finer near centre while full lock is still reachable, and the
    /// triggers can be shaped independently. Keyboard drive/brake stay crisp (full press = full
    /// value, no shaping); only the keyboard steer ramp is preserved. All defaults reproduce the
    /// prior feel closely, so tuning is opt-in via the Inspector.
    /// </summary>
    [RequireComponent(typeof(VehicleController))]
    public class VehicleInputProvider : MonoBehaviour
    {
        [Header("Steering")]
        [Tooltip("Seconds for keyboard steer to travel to full lock (analog sticks bypass this).")]
        [SerializeField] private float keyboardSteerRampS = 0.12f;

        [Tooltip("Left-stick X below this magnitude is treated as noise (dead). Above it the value " +
                 "is smoothly remapped to full travel so there is no jump at the deadzone edge.")]
        [Range(0f, 0.5f)]
        [SerializeField] private float stickDeadzone = 0.08f;

        [Tooltip("Steering response exponent applied to both stick and (ramped) keyboard steer. " +
                 "1 = linear (prior feel, default). ~1.3-1.6 makes small inputs finer near centre " +
                 "while full lock still reaches 1.")]
        [Range(1f, 3f)]
        [SerializeField] private float steerExponent = 1f;

        [Header("Triggers")]
        [Tooltip("Trigger pull below this is treated as noise (dead), then smoothly remapped above it.")]
        [Range(0f, 0.5f)]
        [SerializeField] private float triggerDeadzone = 0.02f;

        [Tooltip("Throttle-trigger response exponent. 1 = linear (prior feel). >1 gives finer " +
                 "control just off idle. Keyboard throttle is unaffected and stays crisp.")]
        [Range(1f, 3f)]
        [SerializeField] private float throttleExponent = 1f;

        [Tooltip("Brake-trigger response exponent. 1 = linear (prior feel). >1 gives finer " +
                 "control just off idle. Keyboard brake is unaffected and stays crisp.")]
        [Range(1f, 3f)]
        [SerializeField] private float brakeExponent = 1f;

        private VehicleController _controller;
        private float _steer;

        public bool InputEnabled = true;

        private void Awake() => _controller = GetComponent<VehicleController>();

        private void Update()
        {
            if (!InputEnabled)
            {
                _controller.Input = default;
                return;
            }

            var kb = Keyboard.current;
            var pad = Gamepad.current;

            float steerTarget = 0f, throttle = 0f, brake = 0f, handbrake = 0f;

            if (kb != null)
            {
                // Keyboard drive/brake are intentionally crisp: full press = full value, no shaping.
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) steerTarget -= 1f;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) steerTarget += 1f;
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) throttle = 1f;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) brake = 1f;
                if (kb.spaceKey.isPressed) handbrake = 1f;
                if (kb.rKey.wasPressedThisFrame) ResetCar();
            }

            bool analogSteer = false;
            if (pad != null)
            {
                float stick = ApplyDeadzoneSigned(pad.leftStick.x.ReadValue(), stickDeadzone);
                if (stick != 0f) { steerTarget = stick; analogSteer = true; }

                float padThrottle = Shape01(ApplyDeadzone01(pad.rightTrigger.ReadValue(), triggerDeadzone), throttleExponent);
                float padBrake = Shape01(ApplyDeadzone01(pad.leftTrigger.ReadValue(), triggerDeadzone), brakeExponent);
                throttle = Mathf.Max(throttle, padThrottle);
                brake = Mathf.Max(brake, padBrake);

                if (pad.buttonEast.isPressed) handbrake = 1f;
                if (pad.buttonNorth.wasPressedThisFrame) ResetCar();
            }

            // _steer is kept in linear space so the keyboard ramp is uniform; the response curve
            // is applied to the output only, covering both analog and (ramped) keyboard steer.
            if (analogSteer)
                _steer = steerTarget;
            else
                _steer = Mathf.MoveTowards(_steer, steerTarget,
                    Time.deltaTime / Mathf.Max(0.01f, keyboardSteerRampS));

            _controller.Input = new VehicleInput
            {
                Steer = Mathf.Clamp(ShapeSigned(_steer, steerExponent), -1f, 1f),
                Throttle = Mathf.Clamp01(throttle),
                Brake = Mathf.Clamp01(brake),
                Handbrake = Mathf.Clamp01(handbrake),
            };
        }

        /// <summary>
        /// Axial deadzone for a signed axis (-1..1). Zeroes anything at/below the deadzone and
        /// smoothly remaps the remainder back to full travel, so crossing the edge has no jump.
        /// </summary>
        private static float ApplyDeadzoneSigned(float value, float deadzone)
        {
            deadzone = Mathf.Clamp(deadzone, 0f, 0.99f);
            float mag = Mathf.Abs(value);
            if (mag <= deadzone) return 0f;
            float remapped = (mag - deadzone) / (1f - deadzone);
            return Mathf.Sign(value) * Mathf.Clamp01(remapped);
        }

        /// <summary>Deadzone + smooth remap for a unipolar 0..1 axis (triggers).</summary>
        private static float ApplyDeadzone01(float value, float deadzone)
        {
            deadzone = Mathf.Clamp(deadzone, 0f, 0.99f);
            if (value <= deadzone) return 0f;
            return Mathf.Clamp01((value - deadzone) / (1f - deadzone));
        }

        /// <summary>
        /// Signed response curve: raises magnitude to <paramref name="exponent"/>, keeping the sign
        /// and the full-lock end pinned at 1 while making small inputs finer near centre.
        /// </summary>
        private static float ShapeSigned(float value, float exponent)
        {
            if (exponent <= 0f || Mathf.Approximately(exponent, 1f)) return value;
            return Mathf.Sign(value) * Mathf.Pow(Mathf.Clamp01(Mathf.Abs(value)), exponent);
        }

        /// <summary>Unipolar (0..1) response curve for triggers.</summary>
        private static float Shape01(float value, float exponent)
        {
            if (exponent <= 0f || Mathf.Approximately(exponent, 1f)) return value;
            return Mathf.Pow(Mathf.Clamp01(value), exponent);
        }

        private void ResetCar()
        {
            var body = _controller.Body;
            Vector3 flatFwd = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            if (flatFwd.sqrMagnitude < 0.01f) flatFwd = Vector3.forward;
            body.position = body.position + Vector3.up * 1.5f;
            body.rotation = Quaternion.LookRotation(flatFwd, Vector3.up);
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
    }
}
