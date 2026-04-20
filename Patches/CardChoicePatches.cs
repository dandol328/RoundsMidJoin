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
                CardChoice.instance?.Pick(0);
            }

            // Suppress the original method on all clients — there is no local
            // player to show the selection UI to.
            return false;
        }
    }
}
