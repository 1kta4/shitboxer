namespace Shitboxer.Meta
{
    /// <summary>
    /// A one-shot request from the main menu to start a FRESH run with a chosen chassis, read by the
    /// RunDirector in the race scene the menu loads into. Static because it has to survive the scene load
    /// between menu and race; consumed exactly once so a later mid-run scene reload resumes the save
    /// instead of restarting. No request pending = RunDirector behaves as before (resume, else fresh).
    /// </summary>
    public static class RunLaunch
    {
        private static bool _requested;
        private static int _chassisId;
        private static int _stakeLevel;

        public static void RequestNewRun(int chassisId, int stakeLevel)
        {
            _requested = true;
            _chassisId = chassisId;
            _stakeLevel = stakeLevel;
        }

        /// <summary>True once if the menu asked for a fresh run; clears the request so it fires only once.</summary>
        public static bool ConsumeNewRun(out int chassisId, out int stakeLevel)
        {
            chassisId = _chassisId;
            stakeLevel = _stakeLevel;
            bool requested = _requested;
            _requested = false;
            return requested;
        }
    }
}
