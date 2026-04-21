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
    /// will never arrive, freezing the session for everyone.  Two distinct sub-cases
    /// exist:
    ///
    ///   1. The player disconnects <em>while</em> cards are already on-screen for
    ///      their turn (mid-turn disconnect).  Handled by <see cref="HandleDisconnect"/>
    ///      which is called from <see cref="NetworkHandlerPatches"/> the moment the
    ///      leave event fires.
    ///
    ///   2. The player disconnects <em>before</em> their turn (they are queued but
    ///      not yet the active picker).  Handled by the <c>StartPicking</c> Prefix +
    ///      Postfix pair below.
    ///
    /// Solution for case 2
    /// -------------------
    /// Prefix: if the player whose turn it is has disconnected:
    ///   • Master client — returns <c>true</c> so the original method still runs and
    ///     spawns the card GameObjects into <c>spawnedCards</c>.  Without this step
    ///     <c>spawnedCards</c> would be empty and the auto-pick would be a no-op.
    ///   • Non-master clients — returns <c>false</c> to suppress the UI entirely.
    /// Postfix (master only): immediately calls <c>DoAutoPick()</c> once the cards
    /// are spawned, so the turn resolves without waiting for player input.
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
        /// The backing field is marked <c>volatile</c> as an extra safety net in case
        /// either path is ever dispatched from a different thread.
        /// </remarks>
        private static volatile Player? _currentPickingPlayer;
        internal static Player? CurrentPickingPlayer => _currentPickingPlayer;

        /// <summary>
        /// Intercepts the start of a player's card-pick turn.
        ///
        /// For a <em>live</em> player: records them as the current picker and
        /// returns <c>true</c> so the vanilla method runs normally.
        ///
        /// For a <em>disconnected</em> player:
        ///   • Master client — returns <c>true</c> so the original method still
        ///     executes and populates <c>spawnedCards</c>.  The matching
        ///     <see cref="StartPicking_Postfix"/> then immediately picks the first
        ///     card once it is available.
        ///   • Non-master clients — returns <c>false</c> to suppress the selection
        ///     UI entirely; they will receive the pick result via Photon RPC.
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
                _currentPickingPlayer = player;
                return true;
            }

            Plugin.ModLogger.LogInfo(
                "[RoundsMidJoin] Disconnected player's card-pick turn detected — auto-resolving.");

            _currentPickingPlayer = null;

            // Master client: allow the original to run so that cards are spawned,
            // then the Postfix will immediately pick.
            // Non-master clients: suppress the original — no UI should appear.
            return PhotonNetwork.IsMasterClient;
        }

        /// <summary>
        /// Fires after <c>StartPicking</c> on the master client.
        /// If the player whose turn just started is disconnected the cards have now
        /// been spawned, so we can safely call <see cref="DoAutoPick"/> to advance
        /// the selection phase without waiting for input that will never arrive.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch("StartPicking")]
        private static void StartPicking_Postfix(Player player)
        {
            if (player == null) return;
            if (!MidJoinManager.IsPlayerDisconnected(player)) return;
            if (!PhotonNetwork.IsMasterClient) return;

            DoAutoPick();
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
                    $"[RoundsMidJoin] HandleDisconnect owner-check threw: {ex}");
                return;
            }

            Plugin.ModLogger.LogInfo(
                "[RoundsMidJoin] Player disconnected during their card-pick turn — auto-resolving.");

            _currentPickingPlayer = null;
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
