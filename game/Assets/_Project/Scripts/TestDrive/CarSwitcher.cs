using Shitboxer.Cameras;
using Shitboxer.Vehicle;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Shitboxer.TestDrive
{
    /// <summary>
    /// Phase-1 A/B rig: Tab (or gamepad Select) hops the driver seat between the parked
    /// cars so the Grip car vs Power car axis can be felt back-to-back.
    /// </summary>
    public class CarSwitcher : MonoBehaviour
    {
        [SerializeField] private VehicleController[] cars;
        [SerializeField] private ChaseCamera chaseCamera;
        [SerializeField] private int activeIndex;

        public VehicleController ActiveCar =>
            cars != null && cars.Length > 0 ? cars[activeIndex] : null;

        public void Configure(VehicleController[] newCars, ChaseCamera cam)
        {
            cars = newCars;
            chaseCamera = cam;
        }

        private void Start() => Activate(activeIndex);

        private void Update()
        {
            bool switchPressed =
                (Keyboard.current?.tabKey.wasPressedThisFrame ?? false) ||
                (Gamepad.current?.selectButton.wasPressedThisFrame ?? false);

            if (switchPressed && cars != null && cars.Length > 1)
                Activate((activeIndex + 1) % cars.Length);
        }

        private void Activate(int index)
        {
            activeIndex = index;
            for (int i = 0; i < cars.Length; i++)
            {
                var provider = cars[i].GetComponent<VehicleInputProvider>();
                if (provider) provider.InputEnabled = i == index;
                if (i != index) cars[i].Input = default;
            }
            if (chaseCamera && ActiveCar)
                chaseCamera.SetTarget(ActiveCar.transform);
        }
    }
}
