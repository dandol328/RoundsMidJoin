using System.Collections;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;

namespace RoundsMidJoin.Patches
{
    [HarmonyPatch(typeof(GM_ArmsRace))]
    internal static class GM_ArmsRacePatches
    {
        [HarmonyPostfix]
        [HarmonyPatch("StartGame")]
        private static void StartGame_Postfix()
        {
            MidJoinManager.ResetState();
            MidJoinManager.PublishMatchState(true);
            EnsureRoomAllowsJoins();
        }

        [HarmonyPostfix]
        [HarmonyPatch("RoundTransition")]
        private static void RoundTransition_Postfix()
        {
            if (!MidJoinManager.IsMatchInProgress || Plugin.Instance == null)
                return;

            Plugin.Instance.StartCoroutine(ActivatePendingPlayers());
        }

        [HarmonyPostfix]
        [HarmonyPatch("ResetMatch")]
        private static void ResetMatch_Postfix()
        {
            MidJoinManager.ResetState();
            MidJoinManager.PublishMatchState(false);
        }

        [HarmonyPrefix]
        [HarmonyPatch("PlayerJoined")]
        private static bool PlayerJoined_Prefix(Player player)
        {
            if (player == null || !MidJoinManager.IsMatchInProgress)
                return true;

            int actorNumber = player.data?.view?.Owner?.ActorNumber ?? -1;
            if (actorNumber <= 0 ||
                !MidJoinManager.IsPendingActor(actorNumber) ||
                MidJoinManager.IsActivating(actorNumber))
            {
                return true;
            }

            // The network object is registered immediately, but the game-mode
            // participant is held out of the active round until the transition.
            if (player.data != null)
                player.data.isPlaying = false;

            Plugin.ModLogger.LogInfo(
                $"[RoundsMidJoin] Holding actor #{actorNumber} until the next round.");
            return false;
        }

        private static IEnumerator ActivatePendingPlayers()
        {
            while (MidJoinManager.HasPendingJoins)
            {
                var photonPlayer = MidJoinManager.DequeuePendingJoin();
                Player? unityPlayer = null;

                for (int frame = 0; frame < 300 && unityPlayer == null; frame++)
                {
                    unityPlayer = MidJoinManager.FindUnityPlayer(photonPlayer);
                    if (unityPlayer == null)
                        yield return null;
                }

                if (unityPlayer == null)
                {
                    Plugin.ModLogger.LogWarning(
                        $"[RoundsMidJoin] Timed out waiting for '{photonPlayer.NickName}' " +
                        "to create a networked Player object.");
                    continue;
                }

                int actorNumber = photonPlayer.ActorNumber;
                MidJoinManager.BeginActivation(actorNumber);
                try
                {
                    if (unityPlayer.data != null)
                        unityPlayer.data.isPlaying = true;

                    if (GM_ArmsRace.instance != null)
                        GM_ArmsRace.instance.PlayerJoined(unityPlayer);
                }
                finally
                {
                    MidJoinManager.EndActivation(actorNumber);
                }

                Plugin.ModLogger.LogInfo(
                    $"[RoundsMidJoin] Activated '{photonPlayer.NickName}' for the new round.");
            }
        }

        private static void EnsureRoomAllowsJoins()
        {
            if (!PhotonNetwork.IsMasterClient || PhotonNetwork.CurrentRoom == null)
                return;

            PhotonNetwork.CurrentRoom.IsOpen = true;
            PhotonNetwork.CurrentRoom.IsVisible = true;
        }
    }
}
