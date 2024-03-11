using System.Collections.Generic;
using GameNetcodeStuff;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.VFX;

namespace Locker.MonoBehaviours
{
    public class LockerAI : EnemyAI
    {
        public enum LockerState : ushort
        {
            Debug = 0,
            Dormant = 1,
            Activating = 2,
            Chasing = 3,
            Reactivating = 4,
            Resetting = 5,
            Consuming = 6,
        }

        public static readonly int CreatureID = 176;

        // Make definitions for the eye colors.
        private static readonly Color eyeColorDormant = Color.black;
        private static readonly Color eyeColorScan = Color.cyan;
        private static readonly Color eyeColorDetect = new Color(1, .4f, 0);
        private static readonly Color eyeColorChase = Color.red;

        // Store components for easy access.
        private AudioSource audioSource;
        private Animator animationController;
        private Material eyeMaterial;
        private Light internalLight;
        private List<Light> scrapeLights;

        // Store the scrape/chase VFXs so they can be toggled during chase.
        private VisualEffect[] visualEffects;

        // The two chase triggers we will work with.
        static VFXExposedProperty chaseVFXBeginTrigger;
        static VFXExposedProperty chaseVFXEndTrigger;

        // Define trigger names for our scrape VFX.
        private static readonly string chaseVFXBeginTriggerName = "BeginChase";
        private static readonly string chaseVFXEndTriggerName = "EndChase";

        // The two consume triggers we will work with.
        static VFXExposedProperty consumeVFXBeginTrigger;
        static VFXExposedProperty consumeVFXEndTrigger;

        // Define trigger names for our blood/consume VFX.
        private static readonly string consumeVFXBeginTriggerName = "BeginConsume";
        private static readonly string consumeVFXEndTriggerName = "EndConsume";

        // Last state for easier transitions.
        private LockerState lastState;

        // Store the current target position, rotation and client.
        private Vector3 targetPosition;
        private Quaternion targetRotation;
        private ulong targetClientId;

        // Momentary rotational force of the enemy when targeting.
        private float currentRotationSpeed = 0f;
        private readonly float maxRotationSpeed = 90f;

        // Current eye color and intensity.
        private Color currentEyeColor = eyeColorDormant;
        private float currentEyeIntensity = 0;

        // Track the duration between starting a chase.
        private float activationTimer = 0f;
        private readonly float activationDuration = 1.5f;

        // Duration before the locker begins rotating towards the target.
        private readonly float activationSpinWindup = 0.45f;

        private float reactivationTimer = 0f;
        private readonly float reactivationDuration = 1f;

        // Track the duration between finishing a chase and consuming a player.
        private float consumeTimer = 0f;
        private readonly float consumeDuration = 2.2f;

        // Duration before the locker begins the blood gushing effect to sync up with audio.
        private readonly float consumeBloodWindup = 1f;
        private bool consumeBloodTriggered = false;

        // Track the duration between finishing a chase and consuming a player.
        private float resetTimer = 0f;
        private readonly float resetDuration = 1f;

        // These variables will keep track if the player has scanned and if we're performing the distance timeout.
        private PlayerControllerB playerScanning;
        private bool playerScanned = false;
        private float playerScannedTimer = 0f;
        private float playerScannedDuration = 0f;

        // Keep track of retargeting timings to not send an abundance of RPCs.
        private float lastTargetTime;

        // Overshoot values for specific interactions.
        private readonly float touchOvershoot = 1.75f;
        private readonly float scanOvershoot = 1f;
        private readonly float reactivationOvershoot = 1.25f;

        // Keep track of the average distance travelled during a chase to avoid getting stuck infinitely.
        private Vector3 lastChasePosition = Vector3.zero;
        private float chaseMovementAverage = 0f;
        private float chaseMovementAverageInitial = 100f;
        private float chaseMovementAverageMinimum = 0.1f;

        // Allow retargeting only every quarter of a second (maximum 5 calls).
        private readonly float lastTargetTimeframe = .2f;

        // Explosion effect parameters on death.
        private readonly float explosionDamage = 100f;
        private readonly float explosionMinRange = 5f;
        private readonly float explosionMaxRange = 6f;
        private readonly int explosionEnemyDamage = 6;

        [Header("Locker")]
        // Store the current state publicly so I can switch it in the inspector.
        public LockerState State;
        public bool DebugToCamera = false;

        public AudioClip AudioClipPing;
        public AudioClip AudioClipActivate;
        public AudioClip AudioClipChase;
        public AudioClip AudioClipReactivate;
        public AudioClip AudioClipReset;
        public AudioClip AudioClipConsume;

        public override void Start()
        {
            base.Start();

            // Assign our components.
            audioSource = GetComponent<AudioSource>();
            animationController = GetComponent<Animator>();

            // Get the materials in the child mesh.
            SkinnedMeshRenderer mesh = gameObject.GetComponentInChildren<SkinnedMeshRenderer>();
            foreach (Material material in mesh.materials)
            {
                // Get the eye material by name.
                if (material.name.ToLower().Contains("eye"))
                {
                    eyeMaterial = material;
                    eyeMaterial.SetColor("_EmissiveColor", currentEyeColor);
                    break;
                }
            }

            // Initialize our scrape lights array.
            scrapeLights = new List<Light> { };

            // Get the internal light component and scrape lights.
            Light[] lights = gameObject.GetComponentsInChildren<Light>();
            foreach (Light light in lights)
            {
                if (light.gameObject.name == "InternalLight")
                {
                    internalLight = light;
                }
                else // This is a scrape light.
                {
                    scrapeLights.Add(light);

                    // Make sure to disable these lights.
                    light.enabled = false;
                }
            }

            // Make sure to enable the Locker's main light but set the intensity to zero.
            internalLight.intensity = 0;
            internalLight.enabled = true;

            targetPosition = Vector3.zero;

            // Capture all visual effects that are a part of this.
            visualEffects = gameObject.GetComponentsInChildren<VisualEffect>();

            // Make sure to assign our constants.
            chaseVFXBeginTrigger.name = chaseVFXBeginTriggerName;
            chaseVFXEndTrigger.name = chaseVFXEndTriggerName;
            consumeVFXBeginTrigger.name = consumeVFXBeginTriggerName;
            consumeVFXEndTrigger.name = consumeVFXEndTriggerName;

            // Begin the initial state.
            SwitchState(LockerState.Dormant);
        }

        public override void DoAIInterval()
        {
            base.DoAIInterval();
        }

        public override void Update()
        {
            base.Update();

            if (DebugToCamera)
            {
                DebugToCamera = false;

                TargetServerRpc(0, Camera.main.transform.position);
            }

            // Override in case I want to manually change states through the inspector.
            if (State != lastState)
            {
                SwitchState(State);

                lastState = State;
            }

            // Handle over-time state changes.
            switch (State)
            {
                case LockerState.Dormant:
                    // Fade out eye.
                    currentEyeColor = Color.Lerp(currentEyeColor, eyeColorDormant, Time.deltaTime);
                    currentEyeIntensity = Mathf.Lerp(currentEyeIntensity, 0, Time.deltaTime);

                    // Fade out our lights entirely.
                    internalLight.intensity = Mathf.Lerp(
                        internalLight.intensity,
                        0,
                        Time.deltaTime * 8
                    );

                    break;

                case LockerState.Activating:
                    // Make sure we only spin after the windup has completed.
                    if (activationTimer > activationSpinWindup)
                    {
                        // Increase the rotation speed over time.
                        currentRotationSpeed = Mathf.Lerp(
                            currentRotationSpeed,
                            maxRotationSpeed,
                            Time.deltaTime / Mathf.Abs(maxRotationSpeed - currentRotationSpeed)
                        );

                        transform.rotation = Quaternion.Slerp(
                            transform.rotation,
                            targetRotation,
                            Time.deltaTime * currentRotationSpeed * 4
                        );
                    }

                    // Activate the eye color and brightness.
                    currentEyeColor = Color.Lerp(currentEyeColor, eyeColorDetect, Time.deltaTime);
                    currentEyeIntensity = Mathf.Lerp(currentEyeIntensity, 100000, Time.deltaTime);

                    // Fade in our internal lights slowly.
                    internalLight.intensity = Mathf.Lerp(
                        internalLight.intensity,
                        40000,
                        Time.deltaTime / 4
                    );

                    break;

                case LockerState.Chasing:
                    currentEyeColor = Color.Lerp(currentEyeColor, eyeColorChase, Time.deltaTime);
                    currentEyeIntensity = Mathf.Lerp(
                        currentEyeIntensity,
                        500000,
                        Time.deltaTime * 2
                    );

                    // Make sure we moved.
                    if (transform.position != lastChasePosition)
                    {
                        // Face the direction it's moving and tilt the enemy up while chasing.
                        Quaternion targetRotationChasing =
                            Quaternion.LookRotation(transform.position - lastChasePosition)
                            * Quaternion.Euler(Vector3.up * 90)
                            * Quaternion.Euler(Vector3.back * 8f);

                        transform.rotation = Quaternion.Slerp(
                            transform.rotation,
                            targetRotationChasing,
                            Time.deltaTime * 4
                        );
                    }

                    // Fade in our internal lights quickly.
                    internalLight.intensity = Mathf.Lerp(
                        internalLight.intensity,
                        20000,
                        Time.deltaTime
                    );

                    // Fade in the scrape lights during the chase sparks. Additionally flicker them.
                    foreach (Light light in scrapeLights)
                    {
                        light.intensity = Mathf.Lerp(
                            light.intensity + Random.Range(-3000, 3000),
                            4000,
                            Time.deltaTime * 2
                        );
                    }

                    break;

                case LockerState.Reactivating:
                    // Increase the rotation speed over time.
                    currentRotationSpeed = Mathf.Lerp(
                        currentRotationSpeed,
                        maxRotationSpeed,
                        Time.deltaTime / Mathf.Abs(maxRotationSpeed - currentRotationSpeed)
                    );

                    transform.rotation = Quaternion.Slerp(
                        transform.rotation,
                        targetRotation,
                        Time.deltaTime * currentRotationSpeed * 6
                    );

                    break;

                case LockerState.Resetting:
                case LockerState.Consuming:
                    if (consumeTimer > consumeBloodWindup && !consumeBloodTriggered)
                    {
                        // Set the consuming VFX.
                        foreach (VisualEffect vfx in visualEffects)
                        {
                            vfx.SendEvent(consumeVFXBeginTrigger.name);
                        }

                        consumeBloodTriggered = true;
                    }

                    // Reset the rotation to the target after chase.
                    transform.rotation = Quaternion.Slerp(
                        transform.rotation,
                        targetRotation,
                        Time.deltaTime * 8
                    );

                    currentEyeColor = Color.Lerp(currentEyeColor, eyeColorChase, Time.deltaTime);
                    currentEyeIntensity = Mathf.Lerp(currentEyeIntensity, 0, Time.deltaTime);

                    // Fade out our lights.
                    internalLight.intensity = Mathf.Lerp(
                        internalLight.intensity,
                        0,
                        Time.deltaTime * 2
                    );

                    // Quickly fade out the scrape lights.
                    foreach (Light light in scrapeLights)
                    {
                        light.intensity = Mathf.Lerp(light.intensity, 0, Time.deltaTime * 8);
                    }

                    break;

                default:
                    break;
            }

            eyeMaterial.SetColor("_EmissiveColor", currentEyeColor * currentEyeIntensity);
        }

        private void FixedUpdate()
        {
            PlayerControllerB closestPlayer = null;
            try
            {
                closestPlayer = GetClosestPlayer(false, true, true);
            }
            catch (System.Exception ex)
            {
                // Some times the get closest player function throws a null pointer...
                if (ex is System.NullReferenceException) { }
            }

            // Handle logic and required state changes.
            switch (State)
            {
                case LockerState.Dormant:
                    // If a player is in touching range and not standing on the enemy, target them.
                    if (closestPlayer != null)
                    {
                        if (
                            Mathf.Abs(closestPlayer.transform.position.y - transform.position.y) + 2
                                > 2
                            && Vector3.Distance(
                                closestPlayer.transform.position,
                                transform.position
                            ) < 1.75
                        )
                        {
                            // Get the direction to the locker so we can overshoot the player's position.
                            Vector3 directionToLocker =
                                transform.position - closestPlayer.transform.position;

                            TargetServerRpc(
                                closestPlayer.playerClientId,
                                closestPlayer.transform.position
                                    - directionToLocker.normalized * touchOvershoot
                            );

                            break;
                        }
                    }

                    // Commence a chase if the player is holding a light source or pointing a flashlight.
                    if (isLocalPlayerClosestWithLight())
                    {
                        TargetServerRpc(
                            StartOfRound.Instance.localPlayerController.playerClientId,
                            StartOfRound.Instance.localPlayerController.transform.position
                        );
                    }

                    // Check if a player scanned.
                    if (playerScanned)
                    {
                        playerScannedTimer += Time.fixedDeltaTime;
                        if (playerScannedTimer > playerScannedDuration)
                        {
                            currentEyeColor = eyeColorScan;

                            // Play the ping return sound.
                            audioSource.PlayOneShot(
                                AudioClipPing,
                                1.5f * Config.LockerVolumeAdjustment.Value
                            );
                            playerScanning.JumpToFearLevel(.2f);

                            // Get the direction to the locker so we can overshoot the player's position.
                            Vector3 directionToLocker =
                                transform.position - playerScanning.transform.position;

                            TargetServerRpc(
                                playerScanning.playerClientId,
                                playerScanning.transform.position
                                    - directionToLocker.normalized * scanOvershoot
                            );

                            // We hit the ping. Now reset variables.
                            playerScanned = false;
                            playerScannedTimer = 0;
                            playerScannedDuration = 0;
                        }
                    }
                    break;

                case LockerState.Activating:
                    activationTimer += Time.fixedDeltaTime;

                    if (activationTimer > activationDuration)
                    {
                        SwitchState(LockerState.Chasing);
                    }

                    break;

                case LockerState.Chasing:
                    // Commence a chase if the player is holding a light source or pointing a flashlight.
                    if (isLocalPlayerClosestWithLight())
                    {
                        TargetServerRpc(
                            StartOfRound.Instance.localPlayerController.playerClientId,
                            StartOfRound.Instance.localPlayerController.transform.position
                        );
                    }

                    // Check if the local player scanned.
                    if (playerScanned)
                    {
                        playerScannedTimer += Time.fixedDeltaTime;
                        if (playerScannedTimer > playerScannedDuration)
                        {
                            // Play the ping return sound.
                            audioSource.PlayOneShot(
                                AudioClipPing,
                                1.5f * Config.LockerVolumeAdjustment.Value
                            );
                            playerScanning.JumpToFearLevel(.5f);

                            currentEyeColor = eyeColorScan;

                            TargetServerRpc(
                                playerScanning.playerClientId,
                                playerScanning.transform.position
                            );

                            // We hit the ping. Now reset variables.
                            playerScanned = false;
                            playerScannedTimer = 0;
                            playerScannedDuration = 0;
                        }
                    }

                    // Check if there's a player touching the locker while it's moving. Kill them.
                    if (closestPlayer != null)
                    {
                        if (
                            Mathf.Abs(closestPlayer.transform.position.y - transform.position.y) + 2
                                > 2
                            && Vector3.Distance(
                                closestPlayer.transform.position,
                                transform.position
                            ) < 2
                        )
                        {
                            ConsumeServerRpc(closestPlayer.playerClientId);

                            break;
                        }
                    }

                    // Get all doors in the level and check their distance to the locker while chasing.
                    DoorLock[] doors = Object.FindObjectsOfType(typeof(DoorLock)) as DoorLock[];

                    foreach (DoorLock door in doors)
                    {
                        if (!door.isDoorOpened) // Ignore open doors.
                        {
                            if ( // Check that we're in range of a door.
                                !door.GetComponent<Rigidbody>()
                                && Vector3.Distance(door.transform.position, transform.position)
                                    < 3f
                            )
                            {
                                if (IsServer)
                                {
                                    DestroyDoorEffectsServerRpc();

                                    Destroy(door.transform.parent.gameObject);
                                }
                            }
                        }
                    }

                    // Update the last movement distance change.
                    chaseMovementAverage =
                        (
                            chaseMovementAverage
                            + Vector3.Distance(lastChasePosition, transform.position)
                        ) / 2;

                    // Store our movement for the next check.
                    lastChasePosition = transform.position;

                    if (
                        Vector3.Distance(transform.position, targetPosition) <= 0.5f
                        || chaseMovementAverage < chaseMovementAverageMinimum
                    )
                    {
                        // Make sure the server decides whether to rechase or reset.
                        if (IsServer)
                        {
                            // Possibly trigger another lunge at the closest visible player.
                            if (
                                Random.Range(0f, 100f)
                                < Config.LockerMechanicsReactivationChance.Value
                            )
                            {
                                ReactivateServerRpc();
                            }
                            else
                            {
                                ResetServerRpc();
                            }
                        }
                    }

                    break;

                case LockerState.Reactivating:
                    reactivationTimer += Time.fixedDeltaTime;

                    if (reactivationTimer > reactivationDuration)
                    {
                        PlayerControllerB player = GetClosestPlayer(true);
                        if (player)
                        {
                            // Get the direction to the locker so we can overshoot the player's position.
                            Vector3 directionToLocker =
                                transform.position - player.transform.position;

                            TargetServerRpc(
                                player.playerClientId,
                                player.transform.position
                                    - directionToLocker.normalized * reactivationOvershoot
                            );
                        }
                        else
                        {
                            ResetServerRpc();
                        }
                    }

                    break;

                case LockerState.Consuming:
                    consumeTimer += Time.fixedDeltaTime;

                    if (consumeTimer > consumeDuration)
                    {
                        SwitchState(LockerState.Dormant);
                    }

                    break;

                case LockerState.Resetting:
                    resetTimer += Time.fixedDeltaTime;

                    if (resetTimer > resetDuration)
                    {
                        SwitchState(LockerState.Dormant);
                    }

                    break;

                default:
                    break;
            }
        }

        public override void KillEnemy(bool destroy = false)
        {
            base.KillEnemy(destroy);

            if (IsServer)
            {
                ExplodeServerRpc();
            }
        }

        public override void OnCollideWithEnemy(Collider other, EnemyAI enemy)
        {
            base.OnCollideWithEnemy(other, enemy);

            switch (State) // Handle collisions with entities during our chasing state.
            {
                // Are we in our chase phase?
                case LockerState.Chasing:
                    if (enemy.enemyType.canDie)
                        if (!enemy.isEnemyDead)
                            enemy.KillEnemy();

                    // If we collided with another locker destroy this one too!
                    if (enemy.enemyType == enemyType)
                        KillEnemy(false);

                    break;

                default:
                    break;
            }
        }

        private void OnTriggerEnter(Collider collider)
        {
            switch (State) // Handle collisions with a player during our chasing state.
            {
                // Are we in our chase phase?
                case LockerState.Chasing:
                    // Check if what we collided with has a player controller.
                    PlayerControllerB player =
                        collider.gameObject.GetComponent<PlayerControllerB>();

                    // If we collided with a player controler, consume it.
                    if (player != null)
                    {
                        ConsumeServerRpc(player.playerClientId);
                    }

                    break;

                default:
                    break;
            }
        }

        public void SwitchState(LockerState state)
        {
            // Handle one-shot state changes.
            State = state;
            if (state != lastState)
            {
                // Reset timers.
                activationTimer = 0;
                reactivationTimer = 0;
                consumeTimer = 0;
                resetTimer = 0;

                switch (state)
                {
                    case LockerState.Debug:
                        // Temporarily set the target.
                        TargetServerRpc(
                            0,
                            new Vector3(
                                Random.Range(-25, 25),
                                transform.position.y,
                                Random.Range(-25, 25)
                            )
                        );

                        print($"Set current debug Locker target to: {targetPosition}");

                        break;

                    case LockerState.Dormant:
                        // Reset our audio source.
                        audioSource.loop = false;

                        // Reset rotation speed value.
                        currentRotationSpeed = 0;

                        // Make sure to disable the scrape lights.
                        foreach (Light light in scrapeLights)
                        {
                            light.intensity = 0;
                            light.enabled = false;
                        }

                        animationController.SetTrigger("Deactivate");
                        animationController.SetBool("Chasing", false);

                        // End all our VFXs.
                        foreach (VisualEffect vfx in visualEffects)
                        {
                            vfx.SendEvent(consumeVFXEndTrigger.name);
                            vfx.SendEvent(chaseVFXEndTrigger.name);
                        }

                        consumeBloodTriggered = false;

                        break;

                    case LockerState.Activating:
                        // Loop the chase audio.
                        audioSource.PlayOneShot(
                            AudioClipActivate,
                            Config.LockerVolumeAdjustment.Value
                        );

                        animationController.SetTrigger("Activate");

                        break;

                    case LockerState.Chasing:
                        // Initiate moving to our destination.
                        SetDestinationToPosition(targetPosition);

                        // Set default chasing calculations.
                        lastChasePosition = transform.position;
                        chaseMovementAverage = chaseMovementAverageInitial;

                        // Loop the chase audio.
                        audioSource.pitch = 1;
                        audioSource.clip = AudioClipChase;
                        audioSource.loop = true;
                        audioSource.volume = Config.LockerVolumeAdjustment.Value;
                        audioSource.Play();

                        // Flip open the doors for the chase to begin.
                        animationController.SetTrigger("OpenDoors");

                        // Make sure to enable the scrape lights.
                        foreach (Light light in scrapeLights)
                        {
                            light.enabled = true;
                        }

                        // Begin the chasing.
                        animationController.SetTrigger("Chase");
                        animationController.SetBool("Chasing", true);

                        // Trigger the scrape particle effects.
                        foreach (VisualEffect vfx in visualEffects)
                        {
                            vfx.SendEvent(chaseVFXBeginTrigger.name);
                        }

                        break;

                    case LockerState.Reactivating:
                    case LockerState.Resetting:
                    case LockerState.Consuming:
                        // Reset rotation speed value.
                        currentRotationSpeed = 0;

                        // Stop the previous looping chase audio.
                        audioSource.Stop();
                        audioSource.loop = false;

                        // Tilt the Locker forward on stopping.
                        transform.rotation =
                            transform.rotation * Quaternion.Euler(Vector3.forward * 10f);

                        animationController.SetBool("Chasing", false);
                        animationController.SetTrigger("CloseDoors");

                        // End the scrape particle effects. Begin the blood effects.
                        foreach (VisualEffect vfx in visualEffects)
                        {
                            vfx.SendEvent(chaseVFXEndTrigger.name);
                        }

                        if (StartOfRound.Instance != null)
                        {
                            // Make sure we don't perform this check for every player.
                            PlayerControllerB player = StartOfRound.Instance.localPlayerController;
                            float distance = Vector3.Distance(
                                transform.position,
                                player.transform.position
                            );

                            if (distance < 7f)
                            {
                                // Apply screen shake to make the closing more scary.
                                Utilities.ApplyLocalPlayerScreenshake(
                                    transform.position,
                                    4,
                                    7f,
                                    false
                                );

                                // Additionally jump in fear level if the player had a close encounter.
                                if (distance < 4f)
                                {
                                    if (state == LockerState.Consuming)
                                    {
                                        player.JumpToFearLevel(1f);
                                    }
                                    else
                                    {
                                        player.JumpToFearLevel(.7f);
                                    }
                                }
                            }
                        }

                        if (state == LockerState.Consuming) // Activate the consume specific effects.
                        {
                            // Play the consuming sound effect.
                            audioSource.PlayOneShot(
                                AudioClipConsume,
                                Config.LockerVolumeAdjustment.Value
                            );
                        }
                        else if (state == LockerState.Reactivating)
                        {
                            // Play reactivation audio clip.
                            audioSource.PlayOneShot(
                                AudioClipReactivate,
                                Config.LockerVolumeAdjustment.Value
                            );
                        }
                        else
                        {
                            // Play reset audio clip.
                            audioSource.PlayOneShot(
                                AudioClipReset,
                                Config.LockerVolumeAdjustment.Value
                            );
                        }

                        break;

                    default:
                        break;
                }

                lastState = state;
            }
        }

        private bool isLocalPlayerClosestWithLight()
        {
            // Why do all the effort of checking for all players and then just focus on the local one?
            // -- For some reason I can't check if other players have an item active unless it's the local one.

            // Let's get the player closest to us with a light equipped or flashlight pocketed.
            PlayerControllerB closestPlayerWithLight = null;

            // Start at infinite range and narrow down.
            float shortestDistance = Mathf.Infinity;

            // Make sure we get players only in a valid radius around us where they can shine on the enemy.
            PlayerControllerB[] visiblePlayers = GetAllPlayersInLineOfSight(360, 15);

            if (visiblePlayers == null || visiblePlayers.Length == 0)
            {
                // We can exit out of this case early.
                return false;
            }

            foreach (PlayerControllerB player in visiblePlayers)
            {
                // We need to keep track of multiple different sources of holding a flashlight in a single variable.
                bool usingLight = false;

                // Check the direction to the enemy.
                Vector3 directionToLocker = transform.position - player.transform.position;

                // Perform an angle check used for flashlight items being held as well as the pocket flashlight.
                float viewAngle = Vector3.Angle(player.transform.forward, directionToLocker);

                // Check if the player has the pocket flashlight.
                if (player.pocketedFlashlight != null)
                {
                    // If their pocket flashlight is active: They're a target.
                    if (player.pocketedFlashlight.isBeingUsed)
                    {
                        // Check if they're within a 30 degree vision angle (the equivalent of the flashlight ray)
                        if (Mathf.Abs(viewAngle) < 30)
                        {
                            usingLight = true;
                        }
                    }
                }

                // Additionally check if they have the light source equipped right now and we haven't detected a pocket light.
                GrabbableObject item = player.currentlyHeldObjectServer;
                if (!usingLight && player.isHoldingObject && item != null)
                {
                    // Check if we're holding a flashlight and should perform the angle check.
                    bool isFlashlight = item.GetType() == typeof(FlashlightItem);

                    // Check for lights on the actively held object.
                    Light[] lights = item.gameObject.GetComponentsInChildren<Light>();
                    foreach (Light light in lights)
                    {
                        // Check that the lights are active and have values beyond zero.
                        if (light.enabled && light.intensity > 0 && light.range > 0)
                        {
                            if (isFlashlight) // Additionally check the viewing angle since this is a flashlight.
                            {
                                if (Mathf.Abs(viewAngle) < 30)
                                {
                                    usingLight = true;
                                }
                            }
                            else
                            {
                                usingLight = true;
                            }
                        }
                    }
                }

                if (!usingLight)
                {
                    continue;
                }

                // Check the distance to this light emitting player.
                float distanceToPlayer = Vector3.Distance(
                    player.transform.position,
                    transform.position
                );

                if (distanceToPlayer < shortestDistance)
                {
                    shortestDistance = distanceToPlayer;

                    closestPlayerWithLight = player;
                }
            }

            // Check if a player was found after our checks.
            if (closestPlayerWithLight != null)
            {
                if (closestPlayerWithLight == StartOfRound.Instance.localPlayerController)
                {
                    return true;
                }
            }

            return false;
        }

        public PlayerControllerB GetClosestVisiblePlayer()
        {
            // Make sure we get players only in a valid radius around us where they can shine on the enemy.
            PlayerControllerB[] visiblePlayers = GetAllPlayersInLineOfSight(360, 30);

            // Get the closest visible player.
            float closestDistance = Mathf.Infinity;
            PlayerControllerB closestPlayer = null;

            if (visiblePlayers != null)
            {
                if (visiblePlayers.Length > 0)
                {
                    foreach (PlayerControllerB player in visiblePlayers)
                    {
                        float distance = Vector3.Distance(
                            transform.position,
                            player.transform.position
                        );
                        if (distance < closestDistance)
                        {
                            closestDistance = distance;
                            closestPlayer = player;
                        }
                    }
                }
            }
            else
            {
                return null;
            }

            return closestPlayer;
        }

        [ServerRpc(RequireOwnership = false)]
        public void TargetServerRpc(ulong clientId, Vector3 position)
        {
            TargetClientRpc(clientId, position);
        }

        [ClientRpc]
        public void TargetClientRpc(ulong clientId, Vector3 position)
        {
            // Make sure we haven't retargeted recently.
            if (Time.time - lastTargetTimeframe > lastTargetTime)
            {
                if (
                    State == LockerState.Dormant
                    || State == LockerState.Debug
                    || State == LockerState.Chasing && clientId == targetClientId
                    || State == LockerState.Reactivating
                )
                {
                    // Make sure we don't change in elevation.
                    position.y = transform.position.y;

                    // Set the attack destination.
                    targetPosition = position;

                    // Set the target rotation.
                    targetRotation = Quaternion.LookRotation(targetPosition - transform.position);

                    // Rotate an additional 90 degree offset.
                    targetRotation *= Quaternion.Euler(Vector3.up * 90);

                    // Syncronize the last target time.
                    lastTargetTime = Time.time;

                    // Store the current chase target identifier.
                    targetClientId = clientId;

                    // Only allowing targeting during the debug or dormant state.
                    if (State == LockerState.Dormant || State == LockerState.Debug)
                    {
                        // Activate the enemy.
                        SwitchState(LockerState.Activating);
                    }
                    else if (State == LockerState.Chasing || State == LockerState.Reactivating)
                    {
                        // Update the nav mesh destination if we're the host.
                        if (IsOwner)
                        {
                            SetDestinationToPosition(targetPosition, true);
                        }

                        // Make sure we go into another chase from the reactivation state.
                        if (State == LockerState.Reactivating)
                        {
                            SwitchState(LockerState.Chasing);
                        }
                    }
                }
            }
        }

        [ServerRpc(RequireOwnership = false)]
        public void ReactivateServerRpc()
        {
            // Stop movement.
            targetPosition = transform.position;

            // Update the nav mesh destination if we're the host.
            if (IsServer)
            {
                SetDestinationToPosition(targetPosition);
            }

            ReactivateClientRpc();
        }

        [ClientRpc]
        public void ReactivateClientRpc()
        {
            PlayerControllerB closestPlayer = GetClosestVisiblePlayer();
            if (closestPlayer != null)
            {
                // Reset the current rotation speed so that we can step it up for the reactivation.
                currentRotationSpeed = 0;

                targetRotation = Quaternion.LookRotation(
                    closestPlayer.transform.position - transform.position
                );

                // Rotate by 90 degrees so we're facing the right way.
                targetRotation *= Quaternion.Euler(Vector3.up * 90);
            }

            SwitchState(LockerState.Reactivating);
        }

        [ServerRpc(RequireOwnership = false)]
        public void ConsumeServerRpc(ulong clientid)
        {
            // Stop movement.
            targetPosition = transform.position;

            // Update the nav mesh destination if we're the host.
            if (IsServer)
            {
                SetDestinationToPosition(targetPosition);
            }

            ConsumeClientRpc(clientid);
        }

        [ClientRpc]
        public void ConsumeClientRpc(ulong id)
        {
            PlayerControllerB localPlayer = StartOfRound.Instance.localPlayerController;
            if (StartOfRound.Instance.localPlayerController.playerClientId == id)
            {
                // Apply heavy bleeding.
                localPlayer.bleedingHeavily = true;

                // Kill the player.
                localPlayer.KillPlayer(Vector3.zero, true, CauseOfDeath.Crushing, 1);
            }

            SwitchState(LockerState.Consuming);
        }

        [ServerRpc(RequireOwnership = false)]
        public void ResetServerRpc()
        {
            // Stop movement.
            targetPosition = transform.position;

            // Update the nav mesh destination if we're the host.
            if (IsServer)
            {
                SetDestinationToPosition(targetPosition);
            }

            ResetClientRpc();
        }

        [ClientRpc]
        public void ResetClientRpc()
        {
            SwitchState(LockerState.Resetting);
        }

        [ServerRpc(RequireOwnership = true)]
        public void ExplodeServerRpc()
        {
            ExplodeClientRpc();
        }

        [ClientRpc]
        public void ExplodeClientRpc()
        {
            Utilities.Explode(
                transform.position,
                explosionMinRange,
                explosionMaxRange,
                explosionDamage,
                explosionEnemyDamage
            );

            Destroy(gameObject);
        }

        [ServerRpc(RequireOwnership = true)]
        public void DestroyDoorEffectsServerRpc()
        {
            DestroyDoorEffectsClientRpc();
        }

        [ClientRpc]
        public void DestroyDoorEffectsClientRpc()
        {
            Utilities.Explode(transform.position, 2, 4, 100, 0);
        }

        public void PlayerScan(PlayerControllerB player)
        {
            if (
                State == LockerState.Dormant
                || State == LockerState.Debug
                || State == LockerState.Chasing
            )
            {
                float distance = Vector3.Distance(transform.position, player.transform.position);
                if (
                    // Make a raycast to the player and check that we didn't hit our room mask.
                    // We adjust the linecast slightly to the side to avoid thin obstacles and not check from the ground.
                    distance < 90
                    && !Physics.Linecast(
                        transform.position + Vector3.up * 2 + Vector3.right * .2f,
                        player.transform.position + Vector3.up * 2 + Vector3.right * .2f,
                        StartOfRound.Instance.collidersAndRoomMask
                    )
                )
                {
                    // Make sure we assign our local player that's scanning.
                    playerScanning = player;
                    if (playerScanned == false)
                    {
                        // Delay the targeting call until the scan hits the Locker. At 90m this is 3 seconds.
                        playerScanned = true;
                        playerScannedTimer = 0;
                        playerScannedDuration = distance / 30;
                    }
                    else if ((playerScannedDuration - playerScannedTimer) < distance / 30)
                    {
                        // The player scanned while they were closer and the timer has not reached yet.
                        playerScannedDuration = distance / 30;
                    }
                }
            }
        }
    }
}
