using System.Collections.Generic;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;

namespace RoundsMidJoin.Patches
{
    /// <summary>
    /// Resolves card picks when the current picker has left the Photon room.
    /// The installed game stores the picker as a player ID and runs this method
    /// from CardChoice.Update, so this hook covers both before-turn and mid-turn
    /// disconnects without relying on a nonexistent StartPicking API.
    /// </summary>
    [HarmonyPatch(typeof(CardChoice), "DoPlayerSelect")]
    internal static class CardChoicePatches
    {
        private static bool Prefix(
            CardChoice __instance,
            List<GameObject> ___spawnedCards,
            int ___pickrID)
        {
            if (!MidJoinManager.IsPlayerIdDisconnected(___pickrID))
                return true;

            if (PhotonNetwork.IsMasterClient &&
                ___spawnedCards != null &&
                ___spawnedCards.Count > 0 &&
                ___spawnedCards[0] != null)
            {
                Plugin.ModLogger.LogInfo(
                    "[RoundsMidJoin] Auto-picking for a disconnected card picker.");
                __instance.Pick(___spawnedCards[0], true);
            }

            // Do not display input/UI for a player who no longer exists.
            return false;
        }
    }
}
