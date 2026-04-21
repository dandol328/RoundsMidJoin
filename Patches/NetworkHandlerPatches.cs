using HarmonyLib;
using Photon.Realtime;

namespace RoundsMidJoin.Patches
{
    /// <summary>
    /// Patches for <c>NetworkConnectionHandler</c> — the class that implements
    /// Photon's <c>IInRoomCallbacks</c> and dispatches join/leave events to the rest
    /// of the game.
    ///
    /// Strategy
    /// --------
    ///  • <c>OnPlayerLeftRoom</c>  — register the departing actor as disconnected
    ///    *before* any vanilla game logic runs.  This ensures that subsequent win-
    ///    condition checks and death handlers can see the player is gone and handle
    ///    them gracefully rather than triggering a premature game-over.
    ///
    ///  • <c>OnPlayerEnteredRoom</c> — queue the new player for mid-game join
    ///    *after* the vanilla handler has finished registering them in Photon.
    ///
    ///  • <c>OnMasterClientSwitched</c> — log the switch so server-authoritative
    ///    operations (e.g. auto-picking cards) are only run by the correct host.
    /// </summary>
    [HarmonyPatch(typeof(NetworkConnectionHandler))]
    internal static class NetworkHandlerPatches
    {
        /// <summary>
        /// Runs *before* the game's own leave handler.
        /// Registers the departing Photon player as disconnected so that game-mode
        /// patches can make decisions based on this state.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch("OnPlayerLeftRoom")]
        private static void OnPlayerLeftRoom_Prefix(Photon.Realtime.Player other)
        {
            MidJoinManager.HandlePlayerLeft(other);

            // If this player was in the middle of their card-pick turn, auto-pick
            // for them so the selection phase does not freeze.
            CardChoicePatches.HandleDisconnect(other);
        }

        /// <summary>
        /// Runs *after* the game's own join handler so the player is fully
        /// registered before we enqueue them for mid-game insertion.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch("OnPlayerEnteredRoom")]
        private static void OnPlayerEnteredRoom_Postfix(Photon.Realtime.Player newPlayer)
        {
            MidJoinManager.HandlePlayerJoined(newPlayer);
        }

        /// <summary>
        /// Logs master-client migrations so we can confirm that the new host is
        /// correctly taking over server-authoritative responsibilities (card auto-picks,
        /// round advancement, etc.).
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch("OnMasterClientSwitched")]
        private static void OnMasterClientSwitched_Postfix(Photon.Realtime.Player newMasterClient)
        {
            Plugin.ModLogger.LogInfo(
                $"[RoundsMidJoin] Master client switched to '{newMasterClient.NickName}' " +
                $"(actor #{newMasterClient.ActorNumber}).");
        }
    }
}
