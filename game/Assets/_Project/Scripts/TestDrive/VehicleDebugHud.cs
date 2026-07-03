using Shitboxer.Vehicle;
using UnityEngine;

namespace Shitboxer.TestDrive
{
    /// <summary>
    /// Throwaway IMGUI readout for tuning: speed, gear, revs, per-wheel slip and load.
    /// Dies when a real HUD arrives in Phase 2.
    /// </summary>
    public class VehicleDebugHud : MonoBehaviour
    {
        [SerializeField] private CarSwitcher switcher;

        public void Configure(CarSwitcher s) => switcher = s;

        private void OnGUI()
        {
            var car = switcher ? switcher.ActiveCar : null;
            if (car == null || car.Sim == null) return;
            var sim = car.Sim;

            GUILayout.BeginArea(new Rect(12, 12, 340, 260), GUI.skin.box);
            GUILayout.Label($"{car.name}   [{car.SpecAsset.name}]");
            GUILayout.Label($"{car.SpeedKmh,5:0} km/h    gear {(sim.InReverse ? "R" : sim.Gear.ToString())}    {sim.EngineRpm,5:0} rpm");
            GUILayout.Label($"steer {sim.SteerAngleDeg,5:0.0}°");
            GUILayout.Space(4);
            string[] names = { "FL", "FR", "RL", "RR" };
            for (int i = 0; i < VehicleSim.WheelCount; i++)
            {
                string ground = sim.Grounded[i] ? "▮" : "▯";
                GUILayout.Label(
                    $"{names[i]} {ground}  load {sim.SuspensionForce[i],6:0} N   " +
                    $"slipA {sim.SlipAngleDeg[i],6:0.0}°   slipR {sim.SlipRatio[i],5:0.00}");
            }
            GUILayout.Label("Tab: swap car   R: flip upright   Space: handbrake");
            GUILayout.EndArea();
        }
    }
}
