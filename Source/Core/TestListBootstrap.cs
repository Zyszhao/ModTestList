// TestList -- startup hook that installs the Harmony patches.
//
// RimWorld runs every [StaticConstructorOnStartup] type's static constructor while the
// long event "InitializingInterface" executes, i.e. once per process, before the first
// map is drawn. That is exactly the right moment to patch: the game's own types are
// already loaded and no window exists yet.
//
// TestListMod.EnsureHarmonyPatched() is idempotent and swallows its own exceptions, so a
// Harmony failure can only disable the overlay -- it can never break game startup. Never
// add throwing code to this constructor.
using HarmonyLib;
using Verse;

namespace TestList
{
    [StaticConstructorOnStartup]
    internal static class TestListBootstrap
    {
        static TestListBootstrap()
        {
            TestListMod.EnsureHarmonyPatched();
        }
    }
}
