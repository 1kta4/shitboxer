namespace Shitboxer.Vehicle
{
    /// <summary>Driver intent for one sim step. Plain data so a server can fill it from a packet.</summary>
    public struct VehicleInput
    {
        /// <summary>-1 (full left) .. +1 (full right).</summary>
        public float Steer;

        /// <summary>0..1.</summary>
        public float Throttle;

        /// <summary>0..1. Doubles as reverse when the car is (near) stopped.</summary>
        public float Brake;

        /// <summary>0..1.</summary>
        public float Handbrake;

        /// <summary>Momentary overtake-boost request (the KERS-style DraftBoost deploy button). The sim
        /// never reads this — boost is applied through <see cref="VehicleSim.BoostMult"/>, which the
        /// DraftBoost host sets — so leaving it false (the default) keeps driving feel byte-for-byte
        /// unchanged. Present so a headless server can fill it from a packet like the other fields.</summary>
        public bool Boost;
    }
}
