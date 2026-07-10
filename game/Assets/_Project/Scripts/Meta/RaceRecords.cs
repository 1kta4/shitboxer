using System;

namespace Shitboxer.Meta
{
    /// <summary>
    /// One track's best (fastest) recorded lap time. A flat [Serializable] struct so JsonUtility
    /// round-trips a <c>List&lt;LapRecord&gt;</c> directly (the persistence convention here shuns
    /// dictionaries — see <see cref="RunSave"/>). Records are cosmetic history only: they carry no
    /// gameplay or economy effect, so they never touch driving feel or balance.
    /// </summary>
    [Serializable]
    public struct LapRecord
    {
        /// <summary>Stable track identifier the lap was set on (e.g. a scene/track id string).</summary>
        public string trackId;

        /// <summary>Best lap time on that track, in seconds. Always &gt; 0 once stored.</summary>
        public float lapSeconds;
    }

    /// <summary>
    /// A compact summary of one finished run for the rolling run-history log (see
    /// <see cref="MetaProgress.RecordRun"/>). Flat + [Serializable] so a <c>List&lt;RunHistoryEntry&gt;</c>
    /// serialises straight through JsonUtility. Purely a record of what happened — it has no effect on
    /// any future run. The <see cref="timestamp"/> is supplied BY THE CALLER (never read from
    /// <c>DateTime.Now</c> inside logic), keeping construction pure and testable.
    /// </summary>
    [Serializable]
    public struct RunHistoryEntry
    {
        /// <summary>How many circuits the run cleared (0-based reached index folds in as the caller sees fit).</summary>
        public int circuitsCleared;

        /// <summary>The run's final wallet at the moment it ended (won or lost).</summary>
        public int finalMoney;

        /// <summary>The 0-based license stake / season the run was played at.</summary>
        public int stakeLevel;

        /// <summary>
        /// Caller-supplied timestamp (e.g. Unix seconds or DateTime ticks) marking when the run ended,
        /// or 0 when the caller has none. Passed in — pure logic never calls <c>DateTime.Now</c> itself.
        /// </summary>
        public long timestamp;
    }
}
