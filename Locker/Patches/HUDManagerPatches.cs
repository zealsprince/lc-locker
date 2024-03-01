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
                LockerAI[] lockerAIs = GameObject.FindObjectsByType<LockerAI>(
                    FindObjectsSortMode.None
                );
                foreach (LockerAI locker in lockerAIs)
                {
                    locker.PlayerScan(GameNetworkManager.Instance.localPlayerController);
                }
            }
        }
    }
}
