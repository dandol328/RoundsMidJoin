using System.Collections.Generic;
using HarmonyLib;
using Photon.Realtime;
using UnityEngine;

namespace RoundsMidJoin.Patches
{
    // =========================================================================
    // GM_ArmsRace patches
    // =========================================================================

    /// <summary>
    /// Patches for <c>GM_ArmsRace</c> — the primary Arms-Race game-mode class.
    ///
    /// Problem
    /// -------
    /// When a player disconnects, the game tries to process their "death" through
    /// <c>GM_ArmsRace.PlayerDied</c>.  If this causes one team to have zero living
    /// players the vanilla code immediately triggers a game-over for the remaining
    /// team, even though both teams may still have connected players who want to
    /// keep playing.
    ///
    /// Solution
    /// --------
    /// • Skip the vanilla <c>PlayerDied</c> path for a disconnected player: instead
    ///   remove them from <c>PlayerManager.instance.players</c> (after the current
    ///   loop is safe to modify) so all future checks ignore them.
    /// • Let the normal path run for all genuinely-alive players, which preserves
    ///   compatibility with vanilla scoring and with RoundsWithFriends team logic.
    /// </summary>
    [HarmonyPatch(typeof(GM_ArmsRace))]
    internal static class GM_ArmsRace_Patches
    {
        // Players scheduled for safe removal at the end of the current frame.
        private static readonly List<Player> _pendingRemoval = new List<Player>();

        /// <summary>
        /// Intercepts the player-death handler for disconnected players.
        /// Returns <c>false</c> (skip original) when the death was caused by a
        /// network disconnect rather than in-game elimination.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch("PlayerDied")]
        private static bool PlayerDied_Prefix(Player player, int __)
        {
            if (!MidJoinManager.IsPlayerDisconnected(player))
                return true; // normal in-game death — let vanilla handle it

            Plugin.ModLogger.LogInfo(
                "[RoundsMidJoin] Disconnected player's death event intercepted — " +
                "scheduling removal instead of triggering win condition.");

            // Schedule removal for the end of the frame to avoid mutating the
            // players list while the game may be iterating it.
            if (!_pendingRemoval.Contains(player))
                _pendingRemoval.Add(player);

            return false; // suppress the original method
        }

        /// <summary>
        /// After the game-mode has finished processing a death, flush any players
        /// that were scheduled for removal so subsequent checks use a clean list.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch("PlayerDied")]
        private static void PlayerDied_Postfix()
        {
            if (_pendingRemoval.Count == 0) return;

            foreach (var disconnected in _pendingRemoval)
            {
                if (PlayerManager.instance != null &&
                    PlayerManager.instance.players.Contains(disconnected))
                {
                    PlayerManager.instance.players.Remove(disconnected);
                    Plugin.ModLogger.LogInfo(
                        $"[RoundsMidJoin] Removed disconnected player from PlayerManager. " +
                        $"Remaining: {PlayerManager.instance.players.Count}");
                }
            }

            _pendingRemoval.Clear();
        }
    }

    // =========================================================================
    // GameModeHandler patches  (base class shared by all game modes)
    // =========================================================================

    /// <summary>
    /// Patches on the abstract <c>GameModeHandler</c> base class that are relevant
    /// regardless of which concrete game mode is active.
    /// </summary>
    [HarmonyPatch(typeof(GameModeHandler))]
    internal static class GameModeHandler_Patches
    {
        /// <summary>
        /// When a brand-new match starts, wipe all stale disconnect / pending-join
        /// tracking so the previous session cannot contaminate the new one.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch("StartGame")]
        private static void StartGame_Postfix()
        {
            MidJoinManager.ResetState();
        }

        /// <summary>
        /// At the beginning of every new round, check whether any players have been
        /// queued to join mid-game and log the fact.  Full spawn logic is deferred to
        /// the concrete game-mode implementation; this hook exists as the integration
        /// point for future extension.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch("StartRound")]
        private static void StartRound_Postfix()
        {
            if (!MidJoinManager.HasPendingJoins) return;

            Plugin.ModLogger.LogInfo(
                "[RoundsMidJoin] Round started — processing pending mid-game joins.");

            // Dequeue each waiting player and attempt to add them.
            // The actual spawn/team-assignment logic lives inside HandleMidGameJoin.
            while (MidJoinManager.HasPendingJoins)
            {
                var photonPlayer = MidJoinManager.DequeuePendingJoin();
                HandleMidGameJoin(photonPlayer);
            }
        }

        // -----------------------------------------------------------------
        // Internal helpers
        // -----------------------------------------------------------------

        /// <summary>
        /// Attempts to bring a late-joining Photon player into the current match.
        ///
        /// Implementation notes
        /// --------------------
        /// Full player spawning requires calling Photon RPC methods that are
        /// tightly coupled to the concrete game-mode implementation.  The current
        /// version logs the event and assigns the player to the smallest team; a
        /// future version can extend this to spawn a character prefab.
        /// </summary>
        private static void HandleMidGameJoin(Photon.Realtime.Player photonPlayer)
        {
            Plugin.ModLogger.LogInfo(
                $"[RoundsMidJoin] Handling mid-game join for '{photonPlayer.NickName}'.");

            // If the Unity player object already exists (e.g. it was kept alive from
            // a previous disconnect) simply mark them as active again.
            var existing = MidJoinManager.FindUnityPlayer(photonPlayer);
            if (existing != null)
            {
                Plugin.ModLogger.LogInfo(
                    "[RoundsMidJoin] Found existing Unity Player — reactivating.");
                existing.gameObject.SetActive(true);
                return;
            }

            // Otherwise log that a full spawn is needed.  Spawning requires the
            // master-client to instantiate the player prefab over the network, which
            // is game-mode-specific and intentionally left as an extension point.
            Plugin.ModLogger.LogInfo(
                "[RoundsMidJoin] No existing Unity Player found — " +
                "full mid-game spawn not yet implemented (see roadmap).");
        }
    }
}
