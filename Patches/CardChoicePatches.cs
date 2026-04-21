using System;
using System.Collections.Generic;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;

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
        private static readonly System.Reflection.FieldInfo SpawnedCardsField =
            AccessTools.Field(typeof(CardChoice), "spawnedCards");

        /// <summary>
        /// Tracks the Unity <see cref="Player"/> whose card-pick turn is currently
        /// active (i.e. <c>StartPicking</c> was called for them and they have not yet
        /// made a pick).  <c>null</c> when no pick session is in progress.
        /// </summary>
        /// <remarks>
        /// All writes happen on Unity's main thread: <c>StartPicking</c> is called by
        /// the game loop, and <c>OnPlayerLeftRoom</c> (which calls
        /// <see cref="HandleDisconnect"/>) is dispatched by Photon on the main thread.
        /// No additional synchronisation is needed.
        /// </remarks>
        internal static Player? CurrentPickingPlayer { get; private set; }

        /// <summary>
        /// Intercepts the start of a player's card-pick turn.
        /// Returns <c>false</c> (skip original method) when the target player has
        /// disconnected; otherwise records them as the current picker and returns
        /// <c>true</c> so vanilla behaviour runs.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch("StartPicking")]
        private static bool StartPicking_Prefix(Player player, int picksLeft)
        {
            if (player == null)
                return true; // nothing to override

            if (!MidJoinManager.IsPlayerDisconnected(player))
            {
                // Live player — record as the current picker and proceed normally.
                CurrentPickingPlayer = player;
                return true;
            }

            Plugin.ModLogger.LogInfo(
                "[RoundsMidJoin] Disconnected player's card-pick turn detected — auto-resolving.");

            CurrentPickingPlayer = null;
            DoAutoPick();

            // Suppress the original method on all clients — there is no local
            // player to show the selection UI to.
            return false;
        }

        /// <summary>
        /// Called from <see cref="NetworkHandlerPatches"/> when a Photon player
        /// leaves the room.  If that player's card-pick turn is currently active
        /// (i.e. cards are already showing for them) the master client immediately
        /// auto-picks so the session does not freeze waiting for input that will
        /// never arrive.
        /// </summary>
        internal static void HandleDisconnect(Photon.Realtime.Player photonPlayer)
        {
            if (CurrentPickingPlayer == null) return;
            if (CardChoice.instance == null) return;

            try
            {
                var owner = CurrentPickingPlayer.data?.view?.Owner;
                if (owner?.ActorNumber != photonPlayer.ActorNumber) return;
            }
            catch (Exception ex)
            {
                Plugin.ModLogger.LogWarning(
                    $"[RoundsMidJoin] HandleDisconnect owner-check threw: {ex.Message}");
                return;
            }

            Plugin.ModLogger.LogInfo(
                "[RoundsMidJoin] Player disconnected during their card-pick turn — auto-resolving.");

            CurrentPickingPlayer = null;
            DoAutoPick();
        }

        /// <summary>
        /// Instructs the master client to pick the first available card via
        /// <c>CardChoice.Pick</c> so the round can advance without waiting for a
        /// player who will never provide input.  Non-master clients are no-ops because
        /// the master's RPC will propagate the pick to everyone.
        /// </summary>
        private static void DoAutoPick()
        {
            if (!PhotonNetwork.IsMasterClient) return;

            var instance = CardChoice.instance;
            if (instance == null) return;

            var spawnedCards = (List<GameObject>)SpawnedCardsField.GetValue(instance);
            if (spawnedCards != null && spawnedCards.Count > 0)
                instance.Pick(spawnedCards[0], true);
        }
    }
}
