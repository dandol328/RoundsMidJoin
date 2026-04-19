using System;
using System.Collections.Generic;
using Photon.Realtime;
using UnityEngine;

namespace RoundsMidJoin
{
    /// <summary>
    /// Central manager that tracks which Photon actors have disconnected and which
    /// players are waiting to join mid-game.  All state is intentionally reset at the
    /// start of every new match so stale entries never interfere with future games.
    /// </summary>
    public static class MidJoinManager
    {
        // ---------------------------------------------------------------------------
        // State
        // ---------------------------------------------------------------------------

        /// <summary>Photon actor numbers of players who left the room during a match.</summary>
        private static readonly HashSet<int> _disconnectedActors = new HashSet<int>();

        /// <summary>Photon players who entered mid-game and are waiting to be added to a round.</summary>
        private static readonly Queue<Photon.Realtime.Player> _pendingJoins = new Queue<Photon.Realtime.Player>();

        // ---------------------------------------------------------------------------
        // Public API
        // ---------------------------------------------------------------------------

        /// <summary>
        /// Called when a Photon player leaves the room.
        /// Records the actor number so subsequent game-logic can identify them.
        /// </summary>
        public static void HandlePlayerLeft(Photon.Realtime.Player photonPlayer)
        {
            Plugin.ModLogger.LogInfo(
                $"[RoundsMidJoin] Player '{photonPlayer.NickName}' (actor #{photonPlayer.ActorNumber}) left.");

            _disconnectedActors.Add(photonPlayer.ActorNumber);
        }

        /// <summary>
        /// Called when a new Photon player enters the room.
        /// If the game is already in progress they are queued for the next round.
        /// </summary>
        public static void HandlePlayerJoined(Photon.Realtime.Player photonPlayer)
        {
            Plugin.ModLogger.LogInfo(
                $"[RoundsMidJoin] Player '{photonPlayer.NickName}' (actor #{photonPlayer.ActorNumber}) joined.");

            // Treat a rejoining player as fully reconnected.
            _disconnectedActors.Remove(photonPlayer.ActorNumber);
            _pendingJoins.Enqueue(photonPlayer);
        }

        /// <summary>Returns true when the given Photon actor number belongs to a disconnected player.</summary>
        public static bool IsActorDisconnected(int actorNumber)
            => _disconnectedActors.Contains(actorNumber);

        /// <summary>
        /// Returns true when the Unity <see cref="Player"/> component belongs to a
        /// disconnected Photon peer (i.e. they left without the game ending).
        /// </summary>
        public static bool IsPlayerDisconnected(Player unityPlayer)
        {
            if (unityPlayer == null) return false;
            try
            {
                var owner = unityPlayer.data?.view?.Owner;
                return owner != null && IsActorDisconnected(owner.ActorNumber);
            }
            catch (Exception ex)
            {
                Plugin.ModLogger.LogWarning($"[RoundsMidJoin] IsPlayerDisconnected check threw: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Resets all tracking state.  Called at the start of every new match so that
        /// leftover entries from a previous session cannot cause problems.
        /// </summary>
        public static void ResetState()
        {
            _disconnectedActors.Clear();
            _pendingJoins.Clear();
            Plugin.ModLogger.LogInfo("[RoundsMidJoin] State reset for new game.");
        }

        // ---------------------------------------------------------------------------
        // Pending-join helpers
        // ---------------------------------------------------------------------------

        /// <summary>True when at least one player is queued to join at the next safe point.</summary>
        public static bool HasPendingJoins => _pendingJoins.Count > 0;

        /// <summary>Removes and returns the next player waiting to enter the match.</summary>
        public static Photon.Realtime.Player DequeuePendingJoin() => _pendingJoins.Dequeue();

        // ---------------------------------------------------------------------------
        // Utility
        // ---------------------------------------------------------------------------

        /// <summary>
        /// Finds the Unity <see cref="Player"/> component whose PhotonView owner
        /// matches <paramref name="photonPlayer"/>.
        /// Returns <c>null</c> if no match is found.
        /// </summary>
        public static Player? FindUnityPlayer(Photon.Realtime.Player photonPlayer)
        {
            if (PlayerManager.instance == null) return null;

            foreach (var p in PlayerManager.instance.players)
            {
                try
                {
                    if (p?.data?.view?.Owner?.ActorNumber == photonPlayer.ActorNumber)
                        return p;
                }
                catch { /* null-safety — keep searching */ }
            }

            return null;
        }

        /// <summary>Number of players currently tracked as disconnected.</summary>
        public static int DisconnectedCount => _disconnectedActors.Count;
    }
}
