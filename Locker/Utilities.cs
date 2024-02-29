using GameNetcodeStuff;
using UnityEngine;

namespace Locker
{
    internal class Utilities
    {
        public static void ApplyLocalPlayerScreenshake(
            Vector3 position,
            float minDistance = 14f,
            float maxDistance = 25f,
            bool onlySmall = false
        )
        {
            float distance = Vector3.Distance(
                GameNetworkManager.Instance.localPlayerController.transform.position,
                position
            );

            if (distance < minDistance && !onlySmall)
            {
                HUDManager.Instance.ShakeCamera(ScreenShakeType.Big);
            }
            else if (distance < maxDistance)
            {
                HUDManager.Instance.ShakeCamera(ScreenShakeType.Small);
            }
        }

        public static void Explode(
            Vector3 position,
            float minRange = 5f,
            float maxRange = 6f,
            float damage = 25f,
            int enemyDamage = 6
        )
        {
            // Make sure we get to parent the explosion effect to something in the scene.
            Transform holder = null;

            if (
                RoundManager.Instance != null
                && RoundManager.Instance.mapPropsContainer != null
                && RoundManager.Instance.mapPropsContainer.transform != null
            )
            {
                holder = RoundManager.Instance.mapPropsContainer.transform;
            }

            // Instantiate the explosion prefab.
            UnityEngine
                .Object.Instantiate(
                    StartOfRound.Instance.explosionPrefab,
                    position,
                    Quaternion.Euler(-90f, 0f, 0f),
                    holder
                )
                .SetActive(value: true);

            // Apply screen shake to the local player.
            ApplyLocalPlayerScreenshake(position);

            Collider[] array = Physics.OverlapSphere(
                position,
                maxRange,
                2621448,
                QueryTriggerInteraction.Collide
            );

            PlayerControllerB player = null;
            for (int i = 0; i < array.Length; i++)
            {
                float distance = Vector3.Distance(position, array[i].transform.position);

                if (
                    distance > 4f
                    && Physics.Linecast(
                        position,
                        array[i].transform.position + Vector3.up * 0.3f,
                        256,
                        QueryTriggerInteraction.Ignore
                    )
                )
                {
                    continue;
                }

                // Damage players.
                if (array[i].gameObject.layer == 3)
                {
                    player = array[i].gameObject.GetComponent<PlayerControllerB>();
                    if (player != null && player.IsOwner)
                    {
                        float damageMultiplier =
                            1f - Mathf.Clamp01((distance - minRange) / (maxRange - minRange));

                        player.DamagePlayer(
                            (int)(damage * damageMultiplier),
                            causeOfDeath: CauseOfDeath.Blast
                        );
                    }
                }
                // Destroy mines.
                else if (array[i].gameObject.layer == 21)
                {
                    Landmine componentInChildren = array[i]
                        .gameObject.GetComponentInChildren<Landmine>();
                    if (
                        componentInChildren != null
                        && !componentInChildren.hasExploded
                        && distance < 6f
                    )
                    {
                        componentInChildren.StartCoroutine(
                            componentInChildren.TriggerOtherMineDelayed(componentInChildren)
                        );
                    }
                }
                // Apply damage to enemies.
                else if (array[i].gameObject.layer == 19)
                {
                    EnemyAICollisionDetect componentInChildren2 = array[i]
                        .gameObject.GetComponentInChildren<EnemyAICollisionDetect>();
                    if (
                        componentInChildren2 != null
                        && componentInChildren2.mainScript.IsOwner
                        && distance < 4.5f
                    )
                    {
                        componentInChildren2.mainScript.HitEnemyOnLocalClient(enemyDamage);
                    }
                }
            }

            // Select everything but room and colliders masks.
            int layerMaskId = ~LayerMask.GetMask("Room");
            layerMaskId = ~LayerMask.GetMask("Colliders");

            // Check if they're in range and apply force if they have a rigidbody component.
            array = Physics.OverlapSphere(position, 10f, layerMaskId);
            for (int j = 0; j < array.Length; j++)
            {
                Rigidbody component = array[j].GetComponent<Rigidbody>();
                if (component != null)
                {
                    component.AddExplosionForce(70f, position, 10f);
                }
            }
        }
    }
}
