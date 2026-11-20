using System.Collections;
using HarmonyLib;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

namespace RoundsMidJoin.Patches
{
    [HarmonyPatch(typeof(NetworkConnectionHandler))]
    internal static class NetworkHandlerPatches
    {
        [HarmonyPrefix]
        [HarmonyPatch("OnPlayerLeftRoom")]
        private static void OnPlayerLeftRoom_Prefix(Photon.Realtime.Player other)
        {
            if (other == null)
                return;

            var unityPlayer = MidJoinManager.FindUnityPlayer(other);
            MidJoinManager.HandlePlayerLeft(other);

            // Photon may destroy the remote Player object after this callback.
            // Remove it from the game's registry first, so team/alive checks never
            // retain a null or disconnected participant.
            if (Plugin.Instance != null)
                Plugin.Instance.StartCoroutine(RemovePlayerAfterCallback(unityPlayer));
        }

        [HarmonyPostfix]
        [HarmonyPatch("OnPlayerEnteredRoom")]
        private static void OnPlayerEnteredRoom_Postfix(Photon.Realtime.Player newPlayer)
        {
            if (newPlayer == null)
                return;

            MidJoinManager.HandlePlayerJoined(newPlayer);
            EnsureRoomAllowsJoins();
        }

        [HarmonyPostfix]
        [HarmonyPatch("OnJoinedRoom")]
        private static void OnJoinedRoom_Postfix()
        {
            MidJoinManager.RefreshMatchStateFromRoom();
            if (!MidJoinManager.IsMatchInProgress)
                return;

            MidJoinManager.QueueLocalJoin();
            if (Plugin.Instance != null)
                Plugin.Instance.StartCoroutine(CreateLatePlayerWhenReady());
        }

        private static IEnumerator RemovePlayerAfterCallback(Player? player)
        {
            yield return null;

            var manager = PlayerManager.instance;
            if (manager == null || player == null)
                yield break;

            if (!manager.players.Contains(player))
                yield break;

            if (player.data != null)
                player.data.isPlaying = false;

            // Treat a disconnect as a non-lethal elimination.  The normal
            // Arms-Race handler can then advance the round when the departing
            // player was the last active member of their team, without ending
            // the entire match.
            if (PhotonNetwork.IsMasterClient &&
                MidJoinManager.IsMatchInProgress &&
                GM_ArmsRace.instance != null)
            {
                GM_ArmsRace.instance.PlayerDied(player, 0);
            }

            manager.RemovePlayer(player);
            Plugin.ModLogger.LogInfo(
                "[RoundsMidJoin] Removed disconnected player from PlayerManager.");
        }

        private static IEnumerator CreateLatePlayerWhenReady()
        {
            for (int frame = 0; frame < 300; frame++)
            {
                if (!MidJoinManager.IsMatchInProgress)
                    yield break;

                var manager = PlayerManager.instance;
                var local = PhotonNetwork.LocalPlayer;
                if (manager != null && local != null &&
                    MidJoinManager.FindUnityPlayer(local) != null)
                    yield break;

                var assigner = PlayerAssigner.instance;
                if (assigner != null)
                {
                    // This is the game's normal networked player creation path.
                    // CreatePlayer requests the player/team IDs from the master.
                    var method = AccessTools.Method(
                        typeof(PlayerAssigner), "CreatePlayer");
                    var routine = method?.Invoke(
                        assigner, new object[] { null!, false }) as IEnumerator;
                    if (routine != null)
                    {
                        assigner.StartCoroutine(routine);
                        yield break;
                    }
                }

                yield return null;
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
