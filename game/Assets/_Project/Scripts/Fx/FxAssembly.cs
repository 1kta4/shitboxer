namespace Shitboxer.Fx
{
    /// <summary>
    /// Marks the presentation / juice / audio assembly.
    ///
    /// Shitboxer.Fx references Vehicle + Race and is referenced by NOBODY. That one-way edge is a
    /// structural guarantee, not a convention: visual effects, screen shake, and sound can only ever
    /// READ simulation state (VehicleCombat.OnImpact, VehicleSim.EngineRpm, VehicleController.Durability,
    /// SpeedKmh, ...), never write it — so the physics core stays engine-loop-independent and
    /// headless-safe, which is the load-bearing constraint the future multiplayer plan rests on. A
    /// headless server build simply excludes this assembly. FxAssemblyGuardTests enforces the
    /// "referenced by nobody" rule so it can't quietly rot.
    ///
    /// Intentionally near-empty until the juice/audio waves land the first effect on the OnImpact
    /// seam. This marker exists only so the assembly compiles cleanly (no "assembly has no scripts"
    /// warning) before that code arrives.
    /// </summary>
    internal static class FxAssembly
    {
    }
}
