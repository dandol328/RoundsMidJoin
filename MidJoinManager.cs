using System;
using System.Collections.Generic;
using ExitGames.Client.Photon;
using Photon.Pun;
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
        private static readonly HashSet<int> _activatingActors = new HashSet<int>();

        /// <summary>
        /// Player IDs that belonged to disconnected Unity players.  CardChoice uses
        /// these IDs rather than Photon actor numbers when selecting cards.
        /// </summary>
        private static readonly HashSet<int> _disconnectedPlayerIds = new HashSet<int>();

        /// <summary>
        /// True once a match has started; used to distinguish lobby joins from genuine
        /// late joins during active gameplay.
        /// </summary>
        private static bool _matchInProgress;

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

            var unityPlayer = FindUnityPlayer(photonPlayer);
            if (unityPlayer != null)
            {
                _disconnectedPlayerIds.Add(unityPlayer.playerID);
            }
        }

        /// <summary>
        /// Called when a new Photon player enters the room.
        /// If the game is already in progress they are queued for the next round.
        /// </summary>
        public static void HandlePlayerJoined(Photon.Realtime.Player photonPlayer)
        {
            Plugin.ModLogger.LogInfo(
                $"[RoundsMidJoin] Player '{photonPlayer.NickName}' (actor #{photonPlayer.ActorNumber}) joined.");

            bool wasDisconnected = _disconnectedActors.Remove(photonPlayer.ActorNumber);

            // Always queue true rejoins (same actor returning) and queue genuine late
            // joins only while a match is in progress.
            if (wasDisconnected || _matchInProgress)
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
            _disconnectedPlayerIds.Clear();
            _activatingActors.Clear();
            _matchInProgress = false;
            Plugin.ModLogger.LogInfo("[RoundsMidJoin] State reset for new game.");
        }

        /// <summary>Marks a match as in-progress so late joins can be queued.</summary>
        public static void MarkMatchStarted() => _matchInProgress = true;

        // ---------------------------------------------------------------------------
        // Pending-join helpers
        // ---------------------------------------------------------------------------

        /// <summary>True when at least one player is queued to join at the next safe point.</summary>
        public static bool HasPendingJoins => _pendingJoins.Count > 0;

        public static bool IsMatchInProgress => _matchInProgress;

        public static bool IsPlayerIdDisconnected(int playerId)
            => _disconnectedPlayerIds.Contains(playerId);

        public static bool IsPendingActor(int actorNumber)
        {
            foreach (var player in _pendingJoins)
            {
                if (player.ActorNumber == actorNumber)
                    return true;
            }

            return false;
        }

        public static void BeginActivation(int actorNumber)
            => _activatingActors.Add(actorNumber);

        public static void EndActivation(int actorNumber)
            => _activatingActors.Remove(actorNumber);

        public static bool IsActivating(int actorNumber)
            => _activatingActors.Contains(actorNumber);

        public static void QueueLocalJoin()
        {
            if (PhotonNetwork.LocalPlayer == null || !IsMatchInProgress)
                return;

            HandlePlayerJoined(PhotonNetwork.LocalPlayer);
        }

        public static void RefreshMatchStateFromRoom()
        {
            if (PhotonNetwork.CurrentRoom?.CustomProperties == null)
                return;

            object value;
            if (PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(
                    MatchStateProperty, out value) && value is bool)
            {
                _matchInProgress = (bool)value;
            }
        }

        public static void PublishMatchState(bool active)
        {
            _matchInProgress = active;
            if (!PhotonNetwork.IsMasterClient || PhotonNetwork.CurrentRoom == null)
                return;

            PhotonNetwork.CurrentRoom.SetCustomProperties(
                new Hashtable { { MatchStateProperty, active } });
        }

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

        private const string MatchStateProperty = "RoundsMidJoin.MatchInProgress";

        // ---------------------------------------------------------------------------
        // Card-count helpers (used for catch-up picking)
        // ---------------------------------------------------------------------------

        /// <summary>
        /// Calculates the integer average number of cards held by all currently
        /// active players, optionally excluding one player (e.g. the late joiner who
        /// is about to catch up).  Returns 0 when there are no qualifying players.
        /// </summary>
        public static int GetAverageCardCount(Player? excluding = null)
        {
            if (PlayerManager.instance == null) return 0;

            int total = 0;
            int count = 0;

            foreach (var p in PlayerManager.instance.players)
            {
                if (p == null || p == excluding) continue;
                total += GetCardCount(p);
                count++;
            }

            return count == 0 ? 0 : total / count;
        }

        /// <summary>
        /// Returns the number of cards currently held by <paramref name="player"/>.
        /// Falls back to 0 if the data cannot be read.
        /// </summary>
        public static int GetCardCount(Player player)
        {
            if (player?.data == null) return 0;
            try
            {
                // CharacterData.currentCards is the standard Rounds field that
                // tracks the list of cards a player has picked.
                return player.data.currentCards?.Count ?? 0;
            }
            catch (Exception ex)
            {
                Plugin.ModLogger.LogDebug($"[RoundsMidJoin] GetCardCount: {ex.Message}");
                return 0;
            }
        }
    }
}
