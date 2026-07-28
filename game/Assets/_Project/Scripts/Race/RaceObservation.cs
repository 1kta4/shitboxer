namespace Shitboxer.Race
{
    /// <summary>Which side of the car ahead a pass was attempted down, relative to the CORNER.</summary>
    public enum PassSide : byte
    {
        /// <summary>No side information — the move happened on a straight. Carries zero preference signal.</summary>
        Straight = 0,
        /// <summary>Down the inside of the corner (the tight side).</summary>
        Inside,
        /// <summary>Around the outside.</summary>
        Outside,
    }

    /// <summary>
    /// How a pass attempt ended. The outcome classifier is what turns "the player tried something" into a
    /// success rate, so the normative choices here matter:
    ///
    /// <see cref="Punted"/> counts as an attempt AND a failure even when the attacker ends up ahead. If
    /// barging through counted as success, the model would learn that a rammy player is a GOOD passer, and
    /// rivals would answer with respect where wariness is what the behaviour actually warrants.
    /// </summary>
    public enum PassOutcome : byte
    {
        /// <summary>Clean, completed, held.</summary>
        Completed = 0,
        /// <summary>Backed out, or never got alongside.</summary>
        Aborted,
        /// <summary>Contact, attacker at fault.</summary>
        Punted,
        /// <summary>Contact, defender at fault.</summary>
        Blocked,
        /// <summary>Contact, blame roughly even.</summary>
        Clashed,
        /// <summary>No contact, but the attacker ran out of road or lost the corner.</summary>
        RanWide,
    }

    /// <summary>
    /// One car sampled on one physics step, already reduced to the TRACK frame. Plain data so a headless
    /// server can fill it and the observer core never touches a transform.
    /// </summary>
    public struct CarFrame
    {
        /// <summary>0 = the player; &gt;0 = a rival's RivalKey. Cars with key &lt; 0 are ignored.</summary>
        public int Key;
        /// <summary>Monotonic total distance raced (RaceCarStatus.TotalDistanceM) — already teleport-guarded.</summary>
        public float TotalDistanceM;
        /// <summary>Arc-length position around the loop, for corner lookups.</summary>
        public float ProgressM;
        /// <summary>Signed offset from the centreline, + = right of travel.</summary>
        public float LateralM;
        public float SpeedMps;
        /// <summary>Player pedal inputs, used by the braking and bluff detectors. Zero for bots is fine.</summary>
        public float Throttle;
        public float Brake;
        /// <summary>False for a car that has finished, been eliminated, or is otherwise not contesting.</summary>
        public bool Racing;
    }

    /// <summary>
    /// Everything one rival observed about the player across one race, rolled up. THE cross-assembly
    /// payload: Meta pulls this at race end and folds it into persistent memory.
    ///
    /// Counts, not rates. Normalisation happens in the memory layer, where the exposure denominators are
    /// decayed alongside the numerators — computing rates here would throw away the sample count that the
    /// confidence model needs.
    /// </summary>
    [System.Serializable]
    public struct RivalEncounterSummary
    {
        public int RivalKey;

        // --- Passes (player as attacker unless named otherwise) ---
        public int PlayerPassesOnRival;
        public int RivalPassesOnPlayer;
        public int PlayerPassesInside;
        public int PlayerPassesOutside;
        public int PlayerPassesStraight;
        public int PlayerPassesCompletedClean;
        public int PlayerAttemptsAborted;
        public int PlayerAttemptsRanWide;

        // --- Divebombs ---
        public int PlayerDiveAttempts;
        public int PlayerDivesConverted;
        /// <summary>Summed 0..1 dive scores, so the memory layer can take a mean rather than only a count.</summary>
        public float PlayerDiveScoreTotal;

        // --- Contact ---
        public int ContactsPlayerFault;
        public int ContactsRivalFault;
        public int ContactsMutual;
        public float PlayerFaultSeverityTotal;
        public float ContactSeverityTotal;

        // --- Defence / racecraft ---
        public int PlayerDefensiveMoves;
        /// <summary>Summed signed corner-relative defensive shift (m); + = covered the inside.</summary>
        public float PlayerDefendShiftTotal;
        public int PlayerYields;
        public int RivalYields;
        public int PlayerBluffs;

        // --- Exposure: the denominators that stop a 40-race player looking 4x more aggressive ---
        /// <summary>Seconds spent within proximity range of this rival. The confidence denominator.</summary>
        public float ProximitySeconds;
        /// <summary>Times a pass contest opened between these two, whatever came of it.</summary>
        public int Engagements;
        public float ClosestApproachM;
        /// <summary>Mean signed gap over the race, + = player ahead. The pace signal.</summary>
        public float MeanSignedGapM;
    }

    /// <summary>One race's worth of observation, per rival, plus the race-level facts memory needs.</summary>
    public struct RaceObservationSummary
    {
        public float RaceDurationS;
        public int PlayerFinishPosition;
        public int FieldSize;
        public RivalEncounterSummary[] Rivals;
    }
}
