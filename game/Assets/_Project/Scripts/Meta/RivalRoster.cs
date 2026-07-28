using System.Collections.Generic;
using Shitboxer.Race;
using UnityEngine;

namespace Shitboxer.Meta
{
    /// <summary>
    /// The career-long pool of named rivals a run draws its field from. Mirrors <c>PartPool</c>: ONE asset
    /// holding a list, rather than one asset per rival.
    ///
    /// WHY ONE ASSET. <c>PartDef</c> needs per-asset granularity because <c>RunState</c> holds live
    /// references that <c>RunSave</c> round-trips by Id. Rivals are only ever referenced by string id out of
    /// <c>MetaProgress</c> — nothing holds a live <see cref="RivalDef"/> across a scene load — so there is no
    /// reference to dangle and nothing to gain from 24 separate files.
    ///
    /// WHY A FIXED POOL, not per-run generation. Rivalry only means something if rivals RECUR: the whole
    /// premise is that the same driver remembers you across runs. A roster generated per run would give every
    /// rival exactly one run of history and the memory layer would never accumulate. A fixed pool also bounds
    /// the save: at most <see cref="Rivals"/>.Count memories can ever exist.
    ///
    /// The <see cref="Default"/> roster is built into code so an un-rebuilt scene, a missing asset, or a
    /// fresh clone all still work — the same fallback discipline as <c>BotLimits.Default</c>.
    /// </summary>
    [CreateAssetMenu(menuName = "Shitboxer/Rival Roster", fileName = "RivalRoster")]
    public sealed class RivalRoster : ScriptableObject
    {
        [Tooltip("Every rival that can appear in a career. Ids are the primary key for persistent memory and must be stable forever — rename displayName freely, never id.")]
        [SerializeField] private List<RivalDef> rivals = new List<RivalDef>();

        /// <summary>The roster, or the built-in <see cref="Default"/> when the asset was left empty.</summary>
        public IReadOnlyList<RivalDef> Rivals => rivals != null && rivals.Count > 0 ? rivals : Default;

        /// <summary>Editor wiring only — replaces the whole list. Matches <c>RaceManager.Configure</c>'s shape.</summary>
        public void Configure(List<RivalDef> defs) => rivals = defs;

        /// <summary>Looks a rival up by primary key. False (and <c>default</c>) when the id is unknown.</summary>
        public bool TryGet(string id, out RivalDef def)
        {
            IReadOnlyList<RivalDef> list = Rivals;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].id == id) { def = list[i]; return true; }
            }
            def = default;
            return false;
        }

        // --- The built-in roster ---------------------------------------------------------------------------
        // 24 rivals = the full 6 x 4 cross of RivalPersonality x BotPersonalityKind, one of each. That is not
        // decoration: it guarantees the career pool spans the whole behaviour space evenly, so whichever 7 a
        // run draws, the player meets a mix of learners and a mix of driving styles rather than (say) four
        // Blockers who all learn identically. 24 also comfortably exceeds the 7-car grid, so consecutive runs
        // draw visibly different fields.

        private static RivalDef[] _default;

        /// <summary>
        /// The built-in roster, used whenever no asset is assigned or the assigned one is empty. Ids are
        /// permanent — changing one orphans that rival's accumulated memory.
        /// </summary>
        public static IReadOnlyList<RivalDef> Default => _default ??= new[]
        {
            // Aggressive — learns fast, converts it into attack.
            new RivalDef("dex_karro",      "Dex Karro",      "DEX", RivalPersonality.Aggressive,  BotPersonalityKind.Neutral),
            new RivalDef("marla_vane",     "Marla Vane",     "VAN", RivalPersonality.Aggressive,  BotPersonalityKind.Blocker),
            new RivalDef("rico_blunt",     "Rico Blunt",     "RIC", RivalPersonality.Aggressive,  BotPersonalityKind.Diver),
            new RivalDef("ash_pike",       "Ash Pike",       "PIK", RivalPersonality.Aggressive,  BotPersonalityKind.Cruiser),

            // Veteran — highest evidence bar, longest memory, sharpest response.
            new RivalDef("sal_ordonez",    "Sal Ordonez",    "ORD", RivalPersonality.Veteran,     BotPersonalityKind.Neutral),
            new RivalDef("gus_maddox",     "Gus Maddox",     "MAD", RivalPersonality.Veteran,     BotPersonalityKind.Blocker),
            new RivalDef("iris_kade",      "Iris Kade",      "KAD", RivalPersonality.Veteran,     BotPersonalityKind.Diver),
            new RivalDef("tom_reyes",      "Tom Reyes",      "REY", RivalPersonality.Veteran,     BotPersonalityKind.Cruiser),

            // Calculating — patient, then surgical. These are the ones that bait.
            new RivalDef("yuki_sato",      "Yuki Sato",      "SAT", RivalPersonality.Calculating, BotPersonalityKind.Neutral),
            new RivalDef("vera_kestrel",   "Vera Kestrel",   "KES", RivalPersonality.Calculating, BotPersonalityKind.Blocker),
            new RivalDef("nils_broch",     "Nils Broch",     "BRO", RivalPersonality.Calculating, BotPersonalityKind.Diver),
            new RivalDef("ada_lemoine",    "Ada Lemoine",    "LEM", RivalPersonality.Calculating, BotPersonalityKind.Cruiser),

            // Rookie — overreacts early, forgets fast.
            new RivalDef("benny_ott",      "Benny Ott",      "OTT", RivalPersonality.Rookie,      BotPersonalityKind.Neutral),
            new RivalDef("tam_fowler",     "Tam Fowler",     "FOW", RivalPersonality.Rookie,      BotPersonalityKind.Blocker),
            new RivalDef("kip_sandoval",   "Kip Sandoval",   "KIP", RivalPersonality.Rookie,      BotPersonalityKind.Diver),
            new RivalDef("junie_park",     "Junie Park",     "PRK", RivalPersonality.Rookie,      BotPersonalityKind.Cruiser),

            // Cautious — learns readily, expresses every lesson as space.
            new RivalDef("hal_brenner",    "Hal Brenner",    "BRE", RivalPersonality.Cautious,    BotPersonalityKind.Neutral),
            new RivalDef("petra_nyx",      "Petra Nyx",      "NYX", RivalPersonality.Cautious,    BotPersonalityKind.Blocker),
            new RivalDef("sim_delacroix",  "Sim Delacroix",  "DEL", RivalPersonality.Cautious,    BotPersonalityKind.Diver),
            new RivalDef("moss_tiller",    "Moss Tiller",    "TIL", RivalPersonality.Cautious,    BotPersonalityKind.Cruiser),

            // Hot-headed — one punt colours everything.
            new RivalDef("bru_castellan",  "Bru Castellan",  "CAS", RivalPersonality.HotHeaded,   BotPersonalityKind.Neutral),
            new RivalDef("wren_malloy",    "Wren Malloy",    "MAL", RivalPersonality.HotHeaded,   BotPersonalityKind.Blocker),
            new RivalDef("duke_halloran",  "Duke Halloran",  "HAL", RivalPersonality.HotHeaded,   BotPersonalityKind.Diver),
            new RivalDef("cleo_vance",     "Cleo Vance",     "VNC", RivalPersonality.HotHeaded,   BotPersonalityKind.Cruiser),
        };
    }
}
