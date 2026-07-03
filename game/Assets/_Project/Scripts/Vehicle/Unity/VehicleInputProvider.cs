using UnityEngine;
using UnityEngine.InputSystem;

namespace Shitboxer.Vehicle
{
    /// <summary>
    /// Polls keyboard/gamepad and writes VehicleInput into the controller. Deliberately
    /// direct-device polling for Phase 1 — swap for an .inputactions asset when menus exist.
    /// Keys: WASD/arrows drive, Space handbrake, R flips the car back onto its wheels.
    /// </summary>
    [RequireComponent(typeof(VehicleController))]
    public class VehicleInputProvider : MonoBehaviour
    {
        [Tooltip("Seconds for keyboard steer to travel to full lock (analog sticks bypass this).")]
        [SerializeField] private float keyboardSteerRampS = 0.12f;

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
                float stick = pad.leftStick.x.ReadValue();
                if (Mathf.Abs(stick) > 0.08f) { steerTarget = stick; analogSteer = true; }
                throttle = Mathf.Max(throttle, pad.rightTrigger.ReadValue());
                brake = Mathf.Max(brake, pad.leftTrigger.ReadValue());
                if (pad.buttonEast.isPressed) handbrake = 1f;
                if (pad.buttonNorth.wasPressedThisFrame) ResetCar();
            }

            if (analogSteer)
                _steer = steerTarget;
            else
                _steer = Mathf.MoveTowards(_steer, steerTarget,
                    Time.deltaTime / Mathf.Max(0.01f, keyboardSteerRampS));

            _controller.Input = new VehicleInput
            {
                Steer = Mathf.Clamp(_steer, -1f, 1f),
                Throttle = throttle,
                Brake = brake,
                Handbrake = handbrake,
            };
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
