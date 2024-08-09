using HarmonyLib;
using Locker.MonoBehaviours;
using UnityEngine;

namespace Locker.Patches
{
    [HarmonyPatch(typeof(HUDManager))]
    internal class HUDManagerPatches
    {
        [HarmonyPostfix]
        [HarmonyPatch("PingScan_performed")]
        private static void NotifyScanLockers()
        {
            // Alert Lockers that a player scanned.
            if (GameNetworkManager.Instance.localPlayerController != null)
            {
                foreach (LockerAI locker in LockerAI.activeLockers)
                {
                    // Make sure the Locker does in fact exist just in case something removes it and doesn't clear the list.
                    locker?.PlayerScan(GameNetworkManager.Instance.localPlayerController);
                }
            }
        }
    }
}
