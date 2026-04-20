using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace RoundsMidJoin
{
    /// <summary>
    /// RoundsMidJoin — allows players to leave and rejoin Rounds mid-game without
    /// ending the current game session.
    ///
    /// Compatible with RoundsWithFriends (soft dependency).
    /// </summary>
    [BepInPlugin(ModId, ModName, ModVersion)]
    // RoundsWithFriends is a soft dependency so our mod still loads without it.
    [BepInDependency("olavim.rounds.rwf", BepInDependency.DependencyFlags.SoftDependency)]
    // UnboundLib is used by many Rounds mods for state synchronisation helpers.
    [BepInDependency("com.willis.rounds.unbound", BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        public const string ModId      = "com.dandol328.rounds.midjoin";
        public const string ModName    = "RoundsMidJoin";
        public const string ModVersion = "0.1.0";

        internal static ManualLogSource ModLogger = null!;
        internal static Plugin          Instance  = null!;

        private Harmony _harmony = null!;

        private void Awake()
        {
            Instance  = this;
            ModLogger = Logger;

            _harmony = new Harmony(ModId);
            _harmony.PatchAll();

            ModLogger.LogInfo($"[{ModName}] v{ModVersion} loaded.");
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchAll(ModId);
        }
    }
}
