using Shitboxer.Race;
using Shitboxer.Vehicle;
using UnityEngine;

namespace Shitboxer.Fx
{
    /// <summary>
    /// The race's audio juice, all of it read-only over the sim exactly as the assembly doc promises:
    /// an RPM-pitched engine loop on the player, contact crunches off <see cref="VehicleCombat.OnImpact"/>
    /// (severity picks the clip), countdown beeps + the green-flag GO, a boost whoosh when the sim's
    /// BoostMult ramps, the crippled-at-half alarm (decision 15's threshold), and a verdict sting when
    /// the player's race ends. Every clip is synthesized once in Awake (<see cref="SfxSynth"/>) — no
    /// assets, no Resources, nothing for a headless build to miss. Installed at runtime by
    /// <see cref="FxBootstrap"/>, never serialized into a scene, so the scenes stay byte-for-byte
    /// reproducible by the builders.
    /// </summary>
    public sealed class RaceFxController : MonoBehaviour
    {
        // Pitch map: loop is authored at pitch 1 ≈ mid-range revs, then stretched with RPM. Clamped so
        // idle still sounds like an engine and the redline never chipmunks.
        private const float PitchReferenceRpm = 2800f;
        private const float MinPitch = 0.45f;
        private const float MaxPitch = 2.6f;

        /// <summary>The durability line under which the alarm fires — decision 15's crippled threshold,
        /// the same 0.5 the Fragile break and the pace curve read, so "badly hurt" SOUNDS where it bites.</summary>
        private const float CrippledLine = 0.5f;

        private const int SampleRate = 44100;

        private RaceManager _race;
        private VehicleController _player;
        private VehicleCombat _combat;

        private AudioSource _engine;
        private AudioSource _oneShot;

        private AudioClip _impactLight, _impactMid, _impactHeavy;
        private AudioClip _beep, _go, _whoosh, _boom, _stingUp, _stingDown, _alarm;

        // Edge-detection state: every cue fires on a crossing, never per-frame.
        private float _prevRaceTime = float.NegativeInfinity;
        private float _prevDurability = 1f;
        private float _prevBoost = 1f;
        private CarRaceState _prevState = CarRaceState.Racing;
        private float _lastImpactAt = float.NegativeInfinity;

        /// <summary>Wire the scene's referee and the car to voice. Called by FxBootstrap right after
        /// AddComponent, before this component's first Update.</summary>
        public void Bind(RaceManager race, VehicleController player)
        {
            _race = race;
            _player = player;
            _combat = player ? player.GetComponent<VehicleCombat>() : null;
            if (_combat != null) _combat.OnImpact += OnImpact;

            _prevDurability = player ? player.Durability : 1f;
            _prevState = CarRaceState.Racing;
            _prevRaceTime = race != null ? race.RaceTimeS : float.NegativeInfinity;
        }

        private void Awake()
        {
            _impactLight = Bake("sfx_impact_light", SfxSynth.Impact(SampleRate, 0.25f, seed: 7));
            _impactMid = Bake("sfx_impact_mid", SfxSynth.Impact(SampleRate, 0.6f, seed: 8));
            _impactHeavy = Bake("sfx_impact_heavy", SfxSynth.Impact(SampleRate, 1f, seed: 9));
            _beep = Bake("sfx_count_beep", SfxSynth.Beep(SampleRate, 620f, 0.14f));
            _go = Bake("sfx_count_go", SfxSynth.Beep(SampleRate, 930f, 0.3f, level: 0.4f));
            _whoosh = Bake("sfx_boost", SfxSynth.Whoosh(SampleRate));
            _boom = Bake("sfx_retire", SfxSynth.Boom(SampleRate));
            _stingUp = Bake("sfx_finish", SfxSynth.Sting(SampleRate, up: true));
            _stingDown = Bake("sfx_eliminated", SfxSynth.Sting(SampleRate, up: false));
            _alarm = Bake("sfx_crippled", SfxSynth.Alarm(SampleRate));

            _engine = gameObject.AddComponent<AudioSource>();
            _engine.clip = Bake("sfx_engine_loop", SfxSynth.EngineLoop(SampleRate));
            _engine.loop = true;
            _engine.playOnAwake = false;
            _engine.spatialBlend = 0f; // the player's own car — always in your ears, never panned away
            _engine.volume = 0.65f;

            _oneShot = gameObject.AddComponent<AudioSource>();
            _oneShot.playOnAwake = false;
            _oneShot.spatialBlend = 0f;
        }

        private static AudioClip Bake(string name, float[] samples)
        {
            var clip = AudioClip.Create(name, samples.Length, 1, SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private void OnDestroy()
        {
            if (_combat != null) _combat.OnImpact -= OnImpact;
        }

        private void OnImpact(VehicleCombat.ImpactEvent e)
        {
            // A pile-up fires several impacts a frame; let the first (loudest path) own the moment.
            if (Time.time - _lastImpactAt < 0.06f) return;
            _lastImpactAt = Time.time;

            AudioClip clip = e.Severity < 0.35f ? _impactLight : e.Severity < 0.7f ? _impactMid : _impactHeavy;
            // Small severity-seeded pitch variation so repeated contact never machine-guns one sample.
            float pitch = 0.92f + 0.16f * Mathf.Abs(Mathf.Sin(e.Severity * 43.7f + Time.time));
            _oneShot.pitch = pitch;
            _oneShot.PlayOneShot(clip, 0.5f + 0.5f * e.Severity);
        }

        private void Update()
        {
            if (_player == null || _player.Sim == null) { if (_engine != null && _engine.isPlaying) _engine.Stop(); return; }

            // --- engine: pitch by RPM, running only while the world runs ---
            // The garage and the ESC menu both hold Time.timeScale at 0 (RunDirector's pause model),
            // and a retired car is a wreck, not an idling engine. Pause rather than Stop so resuming
            // picks the loop up mid-cycle instead of restarting it with a click.
            bool audible = Time.timeScale > 0f && _prevState != CarRaceState.Retired;
            if (!audible)
            {
                if (_engine.isPlaying) _engine.Pause();
            }
            else
            {
                if (!_engine.isPlaying) _engine.UnPause();
                if (!_engine.isPlaying) _engine.Play(); // first start (UnPause on a never-played source is a no-op)
                _engine.pitch = Mathf.Clamp(_player.Sim.EngineRpm / PitchReferenceRpm, MinPitch, MaxPitch);
            }

            // --- boost: whoosh on the rising edge of the sim's own multiplier ---
            float boost = _player.Sim.BoostMult;
            if (boost > 1.02f && _prevBoost <= 1.02f) Play(_whoosh, 0.7f);
            _prevBoost = boost;

            // --- crippled alarm: fires once per downward crossing of the 0.5 line ---
            float durability = _player.Durability;
            if (durability < CrippledLine && _prevDurability >= CrippledLine) Play(_alarm, 0.8f);
            _prevDurability = durability;

            if (_race == null) return;

            // --- countdown: one beep per whole second, GO on the green flag ---
            float t = _race.RaceTimeS;
            if (_prevRaceTime > float.NegativeInfinity)
            {
                if (t < 0f && Mathf.FloorToInt(t) != Mathf.FloorToInt(_prevRaceTime)) Play(_beep, 0.6f);
                if (t >= 0f && _prevRaceTime < 0f) Play(_go, 0.8f);
            }
            _prevRaceTime = t;

            // --- verdict: one sting on the player's terminal state transition ---
            RaceCarStatus me = _race.GetStatus(_player);
            if (me != null && me.State != _prevState)
            {
                switch (me.State)
                {
                    case CarRaceState.Finished: Play(_stingUp, 0.8f); break;
                    case CarRaceState.Eliminated: Play(_stingDown, 0.8f); break;
                    case CarRaceState.Retired: Play(_boom, 1f); break;
                }
                _prevState = me.State;
            }
        }

        private void Play(AudioClip clip, float volume)
        {
            if (clip == null || _oneShot == null) return;
            _oneShot.pitch = 1f;
            _oneShot.PlayOneShot(clip, volume);
        }
    }
}
