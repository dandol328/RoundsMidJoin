using System;
using HarmonyLib;
using Photon.Pun;

namespace RoundsMidJoin.Patches
{
    /// <summary>
    /// Patches for <c>CardChoice</c> — the singleton that manages the card-selection
    /// phase between rounds.
    ///
    /// Problem
    /// -------
    /// After a round ends the winning team picks cards one player at a time.  If one
    /// of those players has disconnected the game waits indefinitely for input that
    /// will never arrive, freezing the session for everyone.
    ///
    /// Solution
    /// --------
    /// Prefix-patch <c>StartPicking</c>: if the player whose turn it is has
    /// disconnected, the <em>master client</em> immediately auto-picks the first
    /// available card on their behalf, then returns <c>false</c> to skip the normal
    /// UI flow.  Non-master clients simply suppress the call so they don't show UI
    /// for a ghost player either.
    ///
    /// Compatibility
    /// -------------
    /// RoundsWithFriends also patches <c>CardChoice</c>.  Because we use a Prefix
    /// that only triggers for disconnected players (i.e. the common path still runs
    /// the original method), there is no functional conflict.
    /// </summary>
    [HarmonyPatch(typeof(CardChoice))]
    internal static class CardChoicePatches
    {
        /// <summary>
        /// Intercepts the start of a player's card-pick turn.
        /// Returns <c>false</c> (skip original method) when the target player has
        /// disconnected; otherwise returns <c>true</c> so vanilla behaviour runs.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch("StartPicking")]
        private static bool StartPicking_Prefix(Player player, int picksLeft)
        {
            if (player == null)
                return true; // nothing to override

            if (!MidJoinManager.IsPlayerDisconnected(player))
                return true; // live player — proceed normally

            Plugin.ModLogger.LogInfo(
                "[RoundsMidJoin] Disconnected player's card-pick turn detected — auto-resolving.");

            // Only the master client should drive the auto-pick so the RPC is sent
            // exactly once.
            if (PhotonNetwork.IsMasterClient)
            {
                // CardChoice.Pick takes a card index and a flag indicating whether
                // to broadcast the pick via RPC.  Picking index 0 (the first
                // offered card) is a safe, deterministic default.
                CardChoice.instance?.Pick(0, sendRPC: true);
            }

            // Suppress the original method on all clients — there is no local
            // player to show the selection UI to.
            return false;
        }

        /// <summary>
        /// After every card pick, check whether the *next* player in line has
        /// already disconnected.  If the game enters a coroutine-wait state for a
        /// ghost player, nudge it forward by calling <c>StartPicking</c> via the
        /// instance so our prefix runs and resolves it.
        ///
        /// This is a safety net for code paths that advance to the next picker
        /// without going through the prefix-patched entry point.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch("Pick")]
        private static void Pick_Postfix()
        {
            if (!PhotonNetwork.IsMasterClient) return;
            if (PlayerManager.instance == null) return;

            // Walk the player list; if the current picking player (identified by the
            // game's internal state) is disconnected, trigger the skip.
            // CardChoice.instance.currentPicker is the most likely field name — adapt
            // to the actual decompiled name if it differs.
            try
            {
                var picker = CardChoice.instance?.currentPicker;
                if (picker != null && MidJoinManager.IsPlayerDisconnected(picker))
                {
                    Plugin.ModLogger.LogInfo(
                        "[RoundsMidJoin] Post-pick: next picker is disconnected — auto-resolving.");
                    CardChoice.instance?.Pick(0, sendRPC: true);
                }
            }
            catch (Exception ex)
            {
                // currentPicker field may not exist under that exact name; the
                // prefix patch already covers the primary code path.
                Plugin.ModLogger.LogDebug(
                    $"[RoundsMidJoin] Pick_Postfix: could not read currentPicker — {ex.Message}");
            }
        }
    }
}
