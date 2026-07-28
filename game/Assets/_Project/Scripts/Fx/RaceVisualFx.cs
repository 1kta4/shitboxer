using Shitboxer.Race;
using Shitboxer.Vehicle;
using UnityEngine;

namespace Shitboxer.Fx
{
    /// <summary>
    /// The race's visual juice, built entirely in code the same way the audio is synthesized — three
    /// ParticleSystems configured at Awake, no prefabs, no materials on disk: impact SPARKS burst at
    /// the contact point (count scales with severity), a boost FLARE streams backwards while the
    /// sim's BoostMult is live, and tyre SMOKE rates up with slip angle so a drift is readable at a
    /// glance. Read-only over the sim like everything in Fx; particles run on scaled time, so the
    /// garage/ESC pause (timeScale 0) freezes them for free.
    /// </summary>
    public sealed class RaceVisualFx : MonoBehaviour
    {
        /// <summary>Slip angle where smoke starts — comfortably above cornering slip (peak grip sits
        /// around 6-8 deg) so clean fast laps stay clean, and only a real drift smokes.</summary>
        public const float SmokeStartSlipDeg = 10f;
        /// <summary>Slip angle of a full-noise drift — matches SectorObserver's 30 deg spin line.</summary>
        public const float SmokeFullSlipDeg = 30f;
        /// <summary>No smoke below this speed: parking-lot shuffling isn't a burnout.</summary>
        public const float SmokeMinSpeedKmh = 25f;

        private VehicleController _player;
        private VehicleCombat _combat;
        private ParticleSystem _sparks, _flare, _smoke;

        /// <summary>Sparks per hit: a handful for a tap, a shower for a slam (quadratic like the
        /// camera's trauma curve, so the two juice layers agree about what "a big one" is).</summary>
        public static int SparkCountFor(float severity01)
        {
            float s = Mathf.Clamp01(severity01);
            return 4 + Mathf.RoundToInt(36f * s * s);
        }

        /// <summary>Smoke particles per second for the worst wheel's slip at a given speed.</summary>
        public static float SmokeRateFor(float maxSlipDeg, float speedKmh)
        {
            if (speedKmh < SmokeMinSpeedKmh) return 0f;
            float t = Mathf.InverseLerp(SmokeStartSlipDeg, SmokeFullSlipDeg, Mathf.Abs(maxSlipDeg));
            return 60f * t;
        }

        public void Bind(VehicleController player)
        {
            _player = player;
            _combat = player ? player.GetComponent<VehicleCombat>() : null;
            if (_combat != null) _combat.OnImpact += OnImpact;

            // The flare and the smoke ride the car; sparks live on the rig and teleport to each hit.
            if (player != null)
            {
                _flare.transform.SetParent(player.transform, false);
                _flare.transform.localPosition = new Vector3(0f, 0.5f, -2f);
                _flare.transform.localRotation = Quaternion.Euler(0f, 180f, 0f); // stream backwards
                _smoke.transform.SetParent(player.transform, false);
                _smoke.transform.localPosition = new Vector3(0f, 0.25f, -1.2f); // rear axle, where the drive is
            }
        }

        private void Awake()
        {
            _sparks = NewSystem("Fx_Sparks",
                new Color(1f, 0.75f, 0.25f), new Color(1f, 0.95f, 0.7f),
                sizeMin: 0.05f, sizeMax: 0.1f, lifeMin: 0.3f, lifeMax: 0.55f,
                speedMin: 4f, speedMax: 9f, gravity: 1f);

            _flare = NewSystem("Fx_BoostFlare",
                new Color(0.35f, 0.8f, 1f), new Color(0.9f, 0.98f, 1f),
                sizeMin: 0.2f, sizeMax: 0.35f, lifeMin: 0.15f, lifeMax: 0.3f,
                speedMin: 8f, speedMax: 12f, gravity: 0f);

            _smoke = NewSystem("Fx_TyreSmoke",
                new Color(0.75f, 0.75f, 0.78f, 0.35f), new Color(0.9f, 0.9f, 0.92f, 0.25f),
                sizeMin: 0.5f, sizeMax: 0.9f, lifeMin: 0.6f, lifeMax: 1f,
                speedMin: 0.8f, speedMax: 1.6f, gravity: -0.03f); // smoke drifts up, barely
        }

        private ParticleSystem NewSystem(string name, Color colA, Color colB,
            float sizeMin, float sizeMax, float lifeMin, float lifeMax,
            float speedMin, float speedMax, float gravity)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startColor = new ParticleSystem.MinMaxGradient(colA, colB);
            main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speedMin, speedMax);
            main.gravityModifier = gravity;
            main.maxParticles = 512;

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f; // bursts via Emit(), streams via the rate set per frame

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 20f;
            shape.radius = 0.15f;

            // Fade every particle out instead of popping it — the single cheapest read-improvement.
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.7f, 0.5f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(g);

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = ParticleMaterial();
            ps.Play();
            return ps;
        }

        // One shared unlit material for all three systems. Shader.Find is editor/play-mode reliable;
        // if a player build ever renders these magenta, add the found shader to Always Included.
        private static Material _particleMat;
        private static Material ParticleMaterial()
        {
            if (_particleMat != null) return _particleMat;
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            _particleMat = new Material(shader);
            return _particleMat;
        }

        private void OnDestroy()
        {
            if (_combat != null) _combat.OnImpact -= OnImpact;
        }

        private void OnImpact(VehicleCombat.ImpactEvent e)
        {
            _sparks.transform.position = e.Point;
            _sparks.transform.rotation = Quaternion.LookRotation(
                e.Direction.sqrMagnitude > 1e-4f ? e.Direction : Vector3.up);
            _sparks.Emit(SparkCountFor(e.Severity));
        }

        private void Update()
        {
            if (_player == null || _player.Sim == null) return;

            // Boost flare: streams only while the sim itself says the boost is live.
            var flareEmission = _flare.emission;
            flareEmission.rateOverTime = _player.Sim.BoostMult > 1.02f ? 90f : 0f;

            // Tyre smoke: driven by the worst wheel's slip angle, gated on real speed.
            float maxSlip = 0f;
            float[] slip = _player.Sim.SlipAngleDeg;
            for (int i = 0; i < slip.Length; i++)
                maxSlip = Mathf.Max(maxSlip, Mathf.Abs(slip[i]));
            var smokeEmission = _smoke.emission;
            smokeEmission.rateOverTime = SmokeRateFor(maxSlip, _player.SpeedKmh);
        }
    }
}
