using System.Collections;
using System.Collections.Generic;
using GameNetcodeStuff;
using LethalLib.Modules;
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
            Resetting = 4,
            Consuming = 5,
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

        // Store the current target player and last target chase location.
        private Vector3 targetLocation;
        private Quaternion targetRotation;

        // Momentary rotational force of the enemy when targeting.
        private float currentRotationSpeed = 0f;
        private readonly float maxRotationSpeed = 60f;

        // Momentary speed values of the enemy.
        private float currentSpeed = 0f;
        private readonly float maxSpeed = 1f;

        // Current eye color and intensity.
        private Color currentEyeColor = eyeColorDormant;
        private float currentEyeIntensity = 0;

        // Track the duration between starting a chase.
        private float activationTimer = 0f;
        private readonly float activationDuration = 1.5f;

        // Duration before the locker begins rotating towards the target.
        private readonly float activationSpinWindup = 0.45f;

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

        private Vector3 lastPosition = Vector3.zero;

        [Header("Locker")]
        // Store the current state publicly so I can switch it in the inspector.
        public LockerState State;
        public bool DebugToCamera = false;

        public AudioClip AudioClipPing;
        public AudioClip AudioClipActivate;
        public AudioClip AudioClipChase;
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

            targetLocation = Vector3.zero;

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

            switch (State)
            {
                case LockerState.Dormant:
                    // Get the closest player if possible.
                    PlayerControllerB closestPlayer = GetClosestPlayer(true, true, true);
                    if (closestPlayer != null)
                    {
                        // Check if the player has the pocket flashlight.
                        if (closestPlayer.pocketedFlashlight != null)
                        {
                            // If their flashlight is out and activate. Chase.
                            if (closestPlayer.pocketedFlashlight.isBeingUsed)
                            {
                                // Make sure to only chase when the player is shining their flashlight at the Locker.
                                Vector3 directionToLocker =
                                    transform.position - closestPlayer.transform.position;
                                float angle = Vector3.Angle(
                                    closestPlayer.transform.forward,
                                    directionToLocker
                                );

                                // Check if the Locker is in the middle 45 degree range of the players view (as in they're shining on it)
                                if (Mathf.Abs(angle) < 45)
                                {
                                    TargetServerRpc(closestPlayer.transform.position);
                                }
                            }
                        }
                    }
                    break;
            }
        }

        public override void Update()
        {
            base.Update();

            if (DebugToCamera)
            {
                DebugToCamera = false;

                TargetServerRpc(Camera.main.transform.position);
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

                    // Tilt the Locker up while chasing.
                    Quaternion targetRotationChasing =
                        targetRotation * Quaternion.Euler(Vector3.back * 8f);

                    transform.rotation = Quaternion.Slerp(
                        transform.rotation,
                        targetRotationChasing,
                        Time.deltaTime * 4
                    );

                    // Fade in our internal lights quickly.
                    internalLight.intensity = Mathf.Lerp(
                        internalLight.intensity,
                        40000,
                        Time.deltaTime
                    );

                    // Fade in the scrape lights during the chase sparks. Additionally flicker them.
                    foreach (Light light in scrapeLights)
                    {
                        light.intensity = Mathf.Lerp(
                            light.intensity + Random.Range(-30000, 30000),
                            40000,
                            Time.deltaTime * 2
                        );
                    }

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
                        Time.deltaTime * 10
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
                        light.intensity = Mathf.Lerp(light.intensity, 0, Time.deltaTime * 2);
                    }

                    break;

                default:
                    break;
            }

            eyeMaterial.SetColor("_EmissiveColor", currentEyeColor * currentEyeIntensity);
        }

        private void FixedUpdate()
        {
            // Handle logic and required state changes.
            switch (State)
            {
                case LockerState.Dormant:
                    if (playerScanned)
                    {
                        playerScannedTimer += Time.fixedDeltaTime;
                        if (playerScannedTimer > playerScannedDuration)
                        {
                            // Make sure to only chase when the player is scanning at the Locker.
                            Vector3 directionToLocker =
                                transform.position - playerScanning.transform.position;
                            float angle = Vector3.Angle(
                                playerScanning.transform.forward,
                                directionToLocker
                            );

                            // Check if the Locker was in the players field of view when the scan completes.
                            if (Mathf.Abs(angle) < playerScanning.gameplayCamera.fieldOfView)
                            {
                                // Play the ping return sound.
                                audioSource.PlayOneShot(AudioClipPing);

                                currentEyeColor = eyeColorScan;

                                TargetServerRpc(playerScanning.transform.position);
                            }

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
                    // Make sure to check that we can move and are not stuck in an infinite moving loop.
                    if (Vector3.Distance(lastPosition, transform.position) < 0.01)
                    {
                        SwitchState(LockerState.Resetting);
                    }

                    lastPosition = transform.position;

                    currentSpeed = Mathf.Lerp(currentSpeed, maxSpeed, Time.fixedDeltaTime);

                    // Move towards the target location.
                    transform.position = Vector3.MoveTowards(
                        transform.position,
                        targetLocation,
                        currentSpeed
                    );

                    if (Vector3.Distance(transform.position, targetLocation) < .1f)
                    {
                        SwitchState(LockerState.Resetting);
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

        public override void OnCollideWithPlayer(Collider player)
        {
            base.OnCollideWithPlayer(player);
            switch (State) // Handle collisions with a player during our dormant state.
            {
                case LockerState.Dormant: // We were touched by a player, target their position.
                    if (player.gameObject.GetComponent<PlayerControllerB>())
                    {
                        TargetServerRpc(player.transform.position);
                    }

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
                    PlayerControllerB playerController =
                        collider.gameObject.GetComponent<PlayerControllerB>();

                    // If we collided with a player controler, consume it.
                    if (playerController != null)
                    {
                        ConsumeServerRpc(playerController);
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
                consumeTimer = 0;
                resetTimer = 0;

                switch (state)
                {
                    case LockerState.Debug:
                        // Temporarily set the target.
                        TargetServerRpc(
                            new Vector3(
                                Random.Range(-25, 25),
                                transform.position.y,
                                Random.Range(-25, 25)
                            )
                        );

                        print($"Set current debug Locker target to: {targetLocation}");

                        break;

                    case LockerState.Dormant:
                        // Reset our audio source.
                        audioSource.loop = false;

                        // Reset speed values.
                        currentSpeed = 0;
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
                        audioSource.PlayOneShot(AudioClipActivate);

                        animationController.SetTrigger("Activate");

                        break;

                    case LockerState.Chasing:
                        // Make sure we reset the last position value.
                        lastPosition = Vector3.zero;

                        // Loop the chase audio.
                        audioSource.pitch = 1;
                        audioSource.clip = AudioClipChase;
                        audioSource.loop = true;
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

                    case LockerState.Resetting:
                    case LockerState.Consuming:
                        // Stop the previous looping chase audio.
                        audioSource.Stop();
                        audioSource.loop = false;

                        // Tilt the Locker forward on stopping.
                        transform.rotation =
                            targetRotation * Quaternion.Euler(Vector3.forward * 20f);

                        animationController.SetBool("Chasing", false);
                        animationController.SetTrigger("CloseDoors");

                        // End the scrape particle effects. Begin the blood effects.
                        foreach (VisualEffect vfx in visualEffects)
                        {
                            vfx.SendEvent(chaseVFXEndTrigger.name);
                        }

                        if (state == LockerState.Consuming) // Activate the consume specific effects.
                        {
                            foreach ( // Increase fear if the player witnessed the Locker consuming someone.
                                PlayerControllerB player in StartOfRound.Instance.allPlayerScripts)
                            {
                                if (
                                    Vector3.Distance(transform.position, player.transform.position)
                                    < 5
                                )
                                {
                                    player.JumpToFearLevel(1);
                                }
                            }

                            // Play the consuming sound effect.
                            audioSource.PlayOneShot(AudioClipConsume);
                        }
                        else
                        {
                            foreach ( // Increase fear if the Locker had a close encounter.
                                PlayerControllerB player in StartOfRound.Instance.allPlayerScripts)
                            {
                                if (
                                    Vector3.Distance(transform.position, player.transform.position)
                                    < 3
                                )
                                {
                                    player.JumpToFearLevel(.8f);
                                }
                            }

                            // Play reset audio clip.
                            audioSource.PlayOneShot(AudioClipReset);
                        }

                        break;

                    default:
                        break;
                }

                lastState = state;
            }
        }

        [ServerRpc(RequireOwnership = false)]
        public void TargetServerRpc(Vector3 position)
        {
            TargetClientRpc(position);
        }

        [ClientRpc]
        public void TargetClientRpc(Vector3 position)
        {
            // This is the only real function that requires networking syncing as it causes all state and animation changes.
            if (
                Mathf.Abs(position.y - transform.position.y) > 3
                || Mathf.Abs(position.y - transform.position.y) < 0
            ) // Don't target entities higher or lower.
                return;

            position.y = transform.position.y; // Make sure we don't change in elevation.

            // Only allowing targeting during the debug or dormant state.
            if (State == LockerState.Dormant || State == LockerState.Debug)
            {
                // Activate the locker and begin the attack sequence.
                targetLocation = position;

                // Set the target rotation.
                targetRotation = Quaternion.LookRotation(targetLocation - transform.position);

                // Rotate an additional 90 degree offset.
                targetRotation *= Quaternion.Euler(Vector3.up * 90);

                SwitchState(LockerState.Activating);
            }
        }

        [ServerRpc(RequireOwnership = false)]
        public void ConsumeServerRpc(PlayerControllerB player)
        {
            ConsumeClientRpc(player);
        }

        [ClientRpc]
        public void ConsumeClientRpc(PlayerControllerB player)
        {
            // The other function that requires networking to indicate a player has died and we should play the kill animation.
            if (State == LockerState.Chasing) // Only allow consuming during the chase state.
            {
                // Apply heavy bleeding.
                player.bleedingHeavily = true;

                // Kill the player.
                player.KillPlayer(Vector3.zero, false, CauseOfDeath.Crushing, 0);

                SwitchState(LockerState.Consuming);
            }
        }

        public void PlayerScan(PlayerControllerB player)
        {
            // Only allowing activation during the dormant state.
            if (State == LockerState.Dormant || State == LockerState.Debug)
            {
                float distance = Vector3.Distance(transform.position, player.transform.position);
                if (
                    distance < 90 // Make a raycast to the player checking if they're visible.
                    && !Physics.Linecast(
                        transform.position,
                        player.transform.position,
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
