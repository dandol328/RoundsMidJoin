using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using Photon.Pun;
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
        /// At the beginning of every new round, activate any players who joined
        /// mid-game and kick off sequential catch-up card-picking sessions so each
        /// late joiner can reach the average card level before participating.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch("StartRound")]
        private static void StartRound_Postfix()
        {
            if (!MidJoinManager.HasPendingJoins) return;

            Plugin.ModLogger.LogInfo(
                "[RoundsMidJoin] Round started — processing pending mid-game joins.");

            // Activate every pending player and build the list of catch-up sessions.
            var catchUpSessions = new List<(Player unityPlayer, int cardsNeeded)>();

            while (MidJoinManager.HasPendingJoins)
            {
                var photonPlayer = MidJoinManager.DequeuePendingJoin();
                var (unityPlayer, cardsNeeded) = HandleMidGameJoin(photonPlayer);
                if (unityPlayer != null && cardsNeeded > 0)
                    catchUpSessions.Add((unityPlayer, cardsNeeded));
            }

            // Run all catch-up sessions one after the other so that concurrent
            // joiners never trigger overlapping CardChoice.StartPicking calls.
            if (catchUpSessions.Count > 0)
                Plugin.Instance.StartCoroutine(RunAllCatchUpPicking(catchUpSessions));
        }

        // -----------------------------------------------------------------
        // Internal helpers
        // -----------------------------------------------------------------

        /// <summary>
        /// Attempts to bring a late-joining Photon player into the current match.
        ///
        /// Returns the activated Unity <see cref="Player"/> and the number of
        /// catch-up cards needed to reach the current average.  Both values are
        /// <c>null</c> / <c>0</c> when the player could not be found or spawned.
        /// </summary>
        private static (Player? unityPlayer, int cardsNeeded) HandleMidGameJoin(
            Photon.Realtime.Player photonPlayer)
        {
            Plugin.ModLogger.LogInfo(
                $"[RoundsMidJoin] Handling mid-game join for '{photonPlayer.NickName}'.");

            // If the Unity player object already exists (e.g. it was kept alive from
            // a previous disconnect) simply mark them as active again.
            var existing = MidJoinManager.FindUnityPlayer(photonPlayer);
            if (existing != null)
            {
                existing.gameObject.SetActive(true);

                int avgCards = MidJoinManager.GetAverageCardCount(excluding: existing);
                int myCards  = MidJoinManager.GetCardCount(existing);
                int catchUp  = Math.Max(0, avgCards - myCards);

                Plugin.ModLogger.LogInfo(
                    $"[RoundsMidJoin] Reactivated existing Unity Player. " +
                    $"Catch-up: avg={avgCards}, current={myCards}, picks={catchUp}.");

                return (existing, catchUp);
            }

            // Full mid-game spawn (for brand-new players with no prior Unity Player
            // object) requires Photon-network instantiation that is game-mode
            // specific.  This remains a roadmap item.
            Plugin.ModLogger.LogInfo(
                $"[RoundsMidJoin] No existing Unity Player found for '{photonPlayer.NickName}' — " +
                "full mid-game spawn not yet implemented (see roadmap).");

            return (null, 0);
        }

        /// <summary>
        /// Runs all queued catch-up picking sessions one after the other so that
        /// multiple simultaneous late joiners do not trigger concurrent
        /// <c>CardChoice.StartPicking</c> calls.
        /// </summary>
        private static IEnumerator RunAllCatchUpPicking(
            List<(Player unityPlayer, int cardsNeeded)> sessions)
        {
            foreach (var (player, cardsNeeded) in sessions)
                yield return Plugin.Instance.StartCoroutine(
                    DoCatchUpCardPickingCoroutine(player, cardsNeeded));
        }

        /// <summary>
        /// Drives a sequential batch card-pick session for <paramref name="player"/>,
        /// giving them <paramref name="cardsNeeded"/> picks via the existing
        /// <c>CardChoice</c> system so that card effects (health, stats, etc.) are
        /// applied identically to a normal post-round pick.
        ///
        /// Only the master client calls <c>StartPicking</c>; every other client sees
        /// the card-selection UI via the usual Photon RPC path.  A per-pick timeout
        /// (90 s) auto-selects the first available card so the session can never hang
        /// indefinitely.
        /// </summary>
        private static IEnumerator DoCatchUpCardPickingCoroutine(Player player, int cardsNeeded)
        {
            Plugin.ModLogger.LogInfo(
                $"[RoundsMidJoin] Catch-up picking started — " +
                $"{cardsNeeded} card(s) for '{player.data.view.Owner?.NickName}'.");

            const float PickTimeoutSeconds = 90f;
            const float UiInitDelaySeconds  = 0.5f;
            const float PostPickDelaySeconds = 0.3f;

            for (int i = 0; i < cardsNeeded; i++)
            {
                if (CardChoice.instance == null) break;

                Plugin.ModLogger.LogInfo(
                    $"[RoundsMidJoin] Presenting catch-up pick {i + 1}/{cardsNeeded}.");

                // Only the master client initiates the pick so the RPC is sent once.
                if (PhotonNetwork.IsMasterClient)
                    CardChoice.instance.StartPicking(player, 1);

                // Give the UI one frame to initialise before we start polling.
                yield return new WaitForSeconds(UiInitDelaySeconds);

                // Poll until the card-choice system is no longer waiting on this
                // player, or until the per-pick timeout elapses.
                float elapsed = 0f;
                while (elapsed < PickTimeoutSeconds)
                {
                    bool pickDone;
                    try
                    {
                        pickDone = CardChoice.instance?.currentPicker != player;
                    }
                    catch (Exception ex)
                    {
                        // currentPicker field may not be accessible under that exact name;
                        // treat as done and let the normal flow continue.
                        Plugin.ModLogger.LogDebug(
                            $"[RoundsMidJoin] Could not read currentPicker — {ex.Message}");
                        pickDone = true;
                    }

                    if (pickDone) break;

                    elapsed += Time.deltaTime;
                    yield return null;
                }

                if (elapsed >= PickTimeoutSeconds)
                {
                    Plugin.ModLogger.LogWarning(
                        $"[RoundsMidJoin] Pick {i + 1}/{cardsNeeded} timed out — " +
                        "auto-selecting card 0.");
                    if (PhotonNetwork.IsMasterClient)
                        CardChoice.instance?.Pick(0, sendRPC: true);

                    yield return new WaitForSeconds(PostPickDelaySeconds);
                }
            }

            Plugin.ModLogger.LogInfo("[RoundsMidJoin] Catch-up card picking complete.");
        }
    }
}
