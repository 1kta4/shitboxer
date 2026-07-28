using Shitboxer.Race;

namespace Shitboxer.Meta
{
    /// <summary>
    /// A rival's CHARACTER as a career-long identity — how they respond to being raced by this player over
    /// many races, not how they drive one corner. Deliberately ORTHOGONAL to <see cref="BotPersonalityKind"/>
    /// (Neutral / Blocker / Diver / Cruiser), which is the on-track archetype the Race layer already
    /// understands: a rival has one of each, and the two do different jobs.
    ///
    /// <see cref="BotPersonalityKind"/> answers "how does this car race?" and is consumed every physics step.
    /// This answers "how does this driver LEARN?" — how fast they form an opinion of you, how much evidence
    /// they demand before acting on it, how long they hold a grudge, and whether a lesson comes out as
    /// aggression or as caution. It is read once per race, in Meta, and never reaches the brain.
    ///
    /// Kept in Meta because it is a property of the persistent career roster. The Race layer only ever
    /// receives the bounded result of applying it, never this enum.
    /// </summary>
    public enum RivalPersonality
    {
        /// <summary>Learns fast, and converts what it learns into ATTACK rather than defence.</summary>
        Aggressive = 0,
        /// <summary>Demands the most evidence, remembers longest, and responds most sharply once sure.</summary>
        Veteran,
        /// <summary>Patient and evidence-hungry, then surgical. The one that baits.</summary>
        Calculating,
        /// <summary>Jumps to conclusions off two samples, acts on them, and forgets just as fast.</summary>
        Rookie,
        /// <summary>Learns readily but expresses every lesson as space rather than escalation.</summary>
        Cautious,
        /// <summary>One punt colours everything; weights recent contact far past what the evidence supports.</summary>
        HotHeaded,
    }

    /// <summary>
    /// One persistent rival on the career roster: a stable identity that survives race reloads, track
    /// rotation, run death, and the whole career.
    ///
    /// WHY THIS EXISTS. Before this, a "rival" was a <c>BotDriver</c> baked into a scene file and identified
    /// by <c>transform.GetSiblingIndex()</c>. Since every race reloads the scene and the run rotates across
    /// three different tracks, nothing carried a rival from one race to the next — there was no "who" for a
    /// memory to attach to. This is that "who".
    ///
    /// <see cref="id"/> is the primary key for everything persistent and MUST NEVER be a slot or grid index.
    /// Keying memory by slot would silently transplant one rival's entire history onto another the moment the
    /// roster order changed. Same reasoning as <c>PartDef.Id</c>, which <c>RunSave</c> persists by string for
    /// exactly this reason.
    ///
    /// Flat and <c>[System.Serializable]</c> so <c>JsonUtility</c> and the inspector both handle it inline.
    /// </summary>
    [System.Serializable]
    public struct RivalDef
    {
        /// <summary>Stable primary key. Never a slot index; never renumbered. Lowercase, no spaces.</summary>
        public string id;

        /// <summary>Full name for the (future) collection screen and pre-race cards.</summary>
        public string displayName;

        /// <summary>Three-character tag for the HUD leaderboard, where there is no room for a full name.</summary>
        public string shortName;

        /// <summary>How this driver learns about the player across a career. See <see cref="RivalPersonality"/>.</summary>
        public RivalPersonality personality;

        /// <summary>
        /// On-track archetype handed straight to the Race layer. Orthogonal to <see cref="personality"/> —
        /// a Cautious driver can still be a Blocker (defends hard, but cleanly).
        /// </summary>
        public BotPersonalityKind drivingArchetype;

        public RivalDef(string id, string displayName, string shortName,
            RivalPersonality personality, BotPersonalityKind drivingArchetype)
        {
            this.id = id;
            this.displayName = displayName;
            this.shortName = shortName;
            this.personality = personality;
            this.drivingArchetype = drivingArchetype;
        }

        /// <summary>True when this entry has a usable primary key. Guards against a half-authored roster row.</summary>
        public bool IsValid => !string.IsNullOrEmpty(id);
    }
}
