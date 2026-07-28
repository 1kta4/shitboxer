using UnityEngine;

namespace Shitboxer.Vehicle
{
    /// <summary>
    /// Host-side draft-time accumulator behind the "Draft Leech" payoff: each FixedUpdate it reads this
    /// car's <see cref="DraftSensor.IsDrafting"/> and integrates the seconds spent sitting in another
    /// car's tow into <see cref="DraftSeconds"/>. A race-end payout layer (RunDirector) reads that total
    /// and pays money proportional to it — but ONLY for a player who owns a part flagged DraftLeech, so a
    /// car with no such part never has its economy touched (RunDirector doesn't even read this component).
    ///
    /// The accumulation is a pure, engine-loop-independent sum (see <see cref="Accumulate"/>): a headless
    /// server integrates the identical value. Time lives HERE in the MonoBehaviour host only. The component
    /// is otherwise inert telemetry — it applies no forces and never touches the sim — so simply carrying
    /// one is byte-for-byte free to a car's driving feel.
    /// </summary>
    public sealed class DraftReward : MonoBehaviour
    {
        /// <summary>Total seconds this car has spent drafting since the last <see cref="Reset"/>.</summary>
        public float DraftSeconds { get; private set; }

        private DraftSensor _sensor;

        /// <summary>Adds the component if the car doesn't already have one — safe to call repeatedly.</summary>
        public static DraftReward GetOrAdd(GameObject go) =>
            go.TryGetComponent(out DraftReward existing) ? existing : go.AddComponent<DraftReward>();

        /// <summary>Race-start reset: clears the accumulated draft-seconds so each race starts from zero.</summary>
        public void Reset() => DraftSeconds = 0f;

        private void Awake() => _sensor = GetComponent<DraftSensor>();

        private void FixedUpdate()
        {
            if (_sensor == null) _sensor = GetComponent<DraftSensor>();
            bool drafting = _sensor && _sensor.IsDrafting;
            DraftSeconds = Accumulate(DraftSeconds, Time.fixedDeltaTime, drafting);
        }

        /// <summary>
        /// Pure accumulation core (the unit-test seam): adds <paramref name="dt"/> seconds to the running
        /// total ONLY while <paramref name="isDrafting"/>, and never otherwise. A non-positive or non-finite
        /// dt contributes nothing, so the total is monotonic non-decreasing and can never be corrupted by a
        /// bad frame. Engine-loop-independent — no Time/Input/scene access — so a headless server integrates
        /// the identical value the client does.
        /// </summary>
        public static float Accumulate(float seconds, float dt, bool isDrafting)
        {
            if (!isDrafting) return seconds;
            if (!(dt > 0f)) return seconds; // rejects zero, negatives and NaN — nothing integrates
            return seconds + dt;
        }
    }
}
