using System.Collections;
using System.Collections.Generic;
using System.Linq;
using GameNetcodeStuff;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.VFX;

namespace Locker.MonoBehaviours;

public class LockerAI : EnemyAI
{
    // Reference these to better understand the currentBehaviourStateIndex.
    public enum State
    {
        Dormant,
        Activating,
        Chasing,
        Reactivating,
        Resetting,
        Consuming,
        Debug,
    }

    public static readonly int CreatureID = 176;

    // Make definitions for the eye colors.
    private static readonly Color eyeColorDormant = Color.black;
    private static readonly Color eyeColorScan = Color.cyan;
    private static readonly Color eyeColorDetect = new(1, .4f, 0);
    private static readonly Color eyeColorChase = Color.red;

    // Store components for easy access.
    private AudioSource audioSource;
    private Animator animationController;
    private Material eyeMaterial;
    private Light internalLight;
    private List<Light> scrapeLights;

    private DoorLock[] doors = [];
    private Turret[] turrets = [];
    private Landmine[] landmines = [];

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

    // State for easier transitions.
    private int observedState;

    // Store the current target position, rotation and client.
    private Vector3 targetPosition;
    private Quaternion targetRotation;

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
    private readonly float chaseMovementSpeed = 30;
    private readonly float chaseMovementAverageInitial = 1000f;
    private readonly float chaseMovementAverageMinimum = 0.01f;

    // Allow retargeting only every quarter of a second (maximum 5 calls).
    private readonly float lastTargetTimeframe = .2f;

    // Explosion effect parameters on death.
    private readonly float explosionDamage = 100f;
    private readonly float explosionMinRange = 5f;
    private readonly float explosionMaxRange = 6f;
    private readonly int explosionEnemyDamage = 6;

    [Header("Locker")]
    // You can still switch the state in editor if you look at currentBehaviourStateIndex i believe
    public bool DebugToCamera = false;

    public AudioClip AudioClipPing;
    public AudioClip AudioClipActivate;
    public AudioClip AudioClipChase;
    public AudioClip AudioClipReactivate;
    public AudioClip AudioClipReset;
    public AudioClip AudioClipConsume;

    private LineRenderer debugLine;

    public static List<LockerAI> activeLockers = [];

    public override void OnDestroy()
    {
        base.OnDestroy();

        // Remove this Locker from the list of existing ones.
        activeLockers.Remove(this);
    }

    public override void Start()
    {
        base.Start();

        // Add ourselves to the list of currently active Locker's.
        activeLockers.Add(this);

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
        scrapeLights = [];

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

        // Get all doors in the level. We use this later to check the distance of the Locker to them.
        doors = Object.FindObjectsOfType(typeof(DoorLock)) as DoorLock[];

        // Get all turrets in the level.
        turrets = Object.FindObjectsOfType(typeof(Turret)) as Turret[];

        // Get all landmines in the level.
        landmines = Object.FindObjectsOfType(typeof(Landmine)) as Landmine[];

        debugLine = gameObject.GetComponent<LineRenderer>();

        // Begin the initial state.
        SwitchState(State.Dormant);
    }

    public IEnumerator DrawPath()
    {
        if (!agent.enabled)
            yield break;

        yield return new WaitForEndOfFrame();

        // Set the array of positions to the amount of corners
        debugLine.positionCount = agent.path.corners.Length;

        debugLine.SetPosition(0, agent.transform.position); // Set the line's origin
        for (var i = 1; i < agent.path.corners.Length; i++)
        {
            debugLine.SetPosition(i, agent.path.corners[i]); // Go through each corner and set that to the line renderer's position
        }
    }

    public override void DoAIInterval()
    {
        base.DoAIInterval();
        if (isEnemyDead || StartOfRound.Instance.allPlayersDead)
            return;

        if (!debugLine)
        {
            return;
        }

        if (debugEnemyAI)
        {
            debugLine.enabled = true;
            StartCoroutine(DrawPath());
        }
        else
        {
            debugLine.enabled = false;
        }
    }

    public override void HitEnemy(
        int force = 1,
        PlayerControllerB playerWhoHit = null,
        bool playHitSFX = false,
        int hitID = -1
    )
    {
        // Target players if they hit the enemy.
        if (playerWhoHit != null)
        {
            TargetServerRpc(playerWhoHit.playerClientId, playerWhoHit.transform.position);
        }
    }

    public override void Update()
    {
        base.Update();
        if (isEnemyDead)
            return;

        if (DebugToCamera)
        {
            DebugToCamera = false;

            TargetServerRpc(0, Camera.main.transform.position);
        }

        // Handle over-time state changes.
        switch (currentBehaviourStateIndex)
        {
            case (int)State.Debug:
                break;

            case (int)State.Dormant:
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

            case (int)State.Activating:
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

            case (int)State.Chasing:
                currentEyeColor = Color.Lerp(currentEyeColor, eyeColorChase, Time.deltaTime);
                currentEyeIntensity = Mathf.Lerp(currentEyeIntensity, 500000, Time.deltaTime * 2);

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

            case (int)State.Reactivating:
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

            case (int)State.Resetting:
                // Quickly fade out the scrape lights.
                foreach (Light light in scrapeLights)
                {
                    light.intensity = Mathf.Lerp(light.intensity, 0, Time.deltaTime * 10);
                }
                break;

            case (int)State.Consuming:
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
                    light.intensity = Mathf.Lerp(light.intensity, 0, Time.deltaTime * 10);
                }
                break;

            default:
                // Shouldn't happen ever.
                break;
        }

        eyeMaterial.SetColor("_EmissiveColor", currentEyeColor * currentEyeIntensity);
    }

    public void FixedUpdate()
    {
        // Check if the state changed from the one previously observed (such as through enemy behavior RPC switching).
        ObserveState();

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
        switch (currentBehaviourStateIndex)
        {
            case (int)State.Dormant:
                // If a player is in touching range and not standing on the enemy, target them.
                if (closestPlayer != null)
                {
                    if (
                        Mathf.Abs(closestPlayer.transform.position.y - transform.position.y) + 2 > 2
                        && Vector3.Distance(closestPlayer.transform.position, transform.position)
                            < 1.5
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
                if (IsLocalPlayerClosestWithLight())
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

            case (int)State.Activating:
                activationTimer += Time.fixedDeltaTime;

                if (activationTimer > activationDuration)
                {
                    SwitchState(State.Chasing);
                }

                break;

            case (int)State.Chasing:
                // Commence a chase if the player is holding a light source or pointing a flashlight.
                if (IsLocalPlayerClosestWithLight())
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
                        Mathf.Abs(closestPlayer.transform.position.y - transform.position.y) + 2 > 2
                        && Vector3.Distance(closestPlayer.transform.position, transform.position)
                            < 2
                    )
                    {
                        ConsumeServerRpc(closestPlayer.playerClientId);

                        break;
                    }
                }

                // Check door's distance to the Locker while chasing.
                foreach (DoorLock door in doors)
                {
                    // Make sure we don't crash in case a door got removed by another system.
                    if (!door)
                    {
                        doors = doors.Where(val => val != door).ToArray();

                        break;
                    }

                    if ( // Check that we're in range of a door.
                        !door.GetComponent<Rigidbody>()
                        && Vector3.Distance(door.transform.position, transform.position) < 3f
                    )
                    {
                        Utilities.Explode(door.transform.position, 2, 4, 100, 0);

                        Destroy(door.transform.parent.gameObject);

                        // Remove the door from our array.
                        doors = doors.Where(val => val != door).ToArray();
                    }
                }

                // Check turret's distance to the Locker while chasing and destroy them if passed.
                foreach (Turret turret in turrets)
                {
                    // Looks like the given turret doesn't exist anymore. Let's reprocess the turret array.
                    if (!turret)
                    {
                        turrets = turrets.Where(val => val != turret).ToArray();

                        break;
                    }

                    if ( // Check that we're in range of a turret.
                        Vector3.Distance(turret.transform.position, transform.position) < 3f
                    )
                    {
                        Utilities.Explode(turret.transform.position, 2, 4, 100, 0);

                        Destroy(turret.transform.parent.gameObject);

                        // Remove the destroyed turret from our array.
                        turrets = turrets.Where(val => val != turret).ToArray();
                    }
                }

                // Check landmine's distance to the Locker while chasing and destroy them if passed.
                foreach (Landmine landmine in landmines)
                {
                    // Looks like the given landmine doesn't exist anymore. Let's reprocess the landmine array.
                    if (!landmine)
                    {
                        landmines = landmines.Where(val => val != landmine).ToArray();

                        break;
                    }

                    if ( // Check that we're in range of a landmine.
                        Vector3.Distance(landmine.transform.position, transform.position) < 3f
                    )
                    {
                        Utilities.Explode(landmine.transform.position, 2, 4, 100, 0);

                        Destroy(landmine.transform.parent.gameObject);

                        // Remove the destroyed landmine from our array.
                        landmines = landmines.Where(val => val != landmine).ToArray();
                    }
                }

                // Update the last movement distance change.
                chaseMovementAverage =
                    (chaseMovementAverage + Vector3.Distance(lastChasePosition, transform.position))
                    / 2;

                // Store our movement for the next check.
                lastChasePosition = transform.position;

                if (IsServer)
                {
                    if (
                        Vector3.Distance(transform.position, targetPosition) <= 1f
                        || chaseMovementAverage < chaseMovementAverageMinimum
                    )
                    {
                        if (chaseMovementAverage > chaseMovementAverageMinimum)
                        {
                            // Possibly trigger another lunge at the closest visible player.
                            if (
                                Random.Range(0f, 100f)
                                < Config.LockerMechanicsReactivationChance.Value
                            )
                            {
                                ReactivateServerRpc();

                                break;
                            }
                        }

                        ResetServerRpc();
                    }
                }

                break;

            case (int)State.Reactivating:
                reactivationTimer += Time.fixedDeltaTime;

                if (reactivationTimer > reactivationDuration)
                {
                    PlayerControllerB player = GetClosestPlayer(true);
                    if (player)
                    {
                        // Get the direction to the locker so we can overshoot the player's position.
                        Vector3 directionToLocker = transform.position - player.transform.position;

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

            case (int)State.Consuming:
                consumeTimer += Time.fixedDeltaTime;

                if (consumeTimer > consumeDuration)
                {
                    SwitchState(State.Dormant);
                }

                break;

            case (int)State.Resetting:
                resetTimer += Time.fixedDeltaTime;

                if (resetTimer > resetDuration)
                {
                    SwitchState(State.Dormant);
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

        switch (currentBehaviourStateIndex) // Handle collisions with entities during our chasing state.
        {
            // Are we in our chase phase?
            case (int)State.Chasing:
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

    public void OnTriggerEnter(Collider collider)
    {
        switch (currentBehaviourStateIndex) // Handle collisions with a player during our chasing state.
        {
            // Are we in our chase phase?
            case (int)State.Chasing:
                // Check if what we collided with has a player controller.
                PlayerControllerB player = collider.gameObject.GetComponent<PlayerControllerB>();

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

    // Wrapper method for switching the behavior state so we can use our named states.
    public void SwitchState(State state)
    {
        // Handle one-shot state changes. Inform the underlying state.
        SwitchToBehaviourState((int)state);
    }

    public void ObserveState()
    {
        // This method is called during updates to check if we have transitioned between states. This is to avoid desyncs in animations.
        if (currentBehaviourStateIndex == observedState)
            return;

        // Reset timers.
        activationTimer = 0;
        reactivationTimer = 0;
        consumeTimer = 0;
        resetTimer = 0;

        switch (currentBehaviourStateIndex)
        {
            case (int)State.Debug:
                // Temporarily set the target.
                TargetServerRpc(
                    0,
                    new Vector3(Random.Range(-25, 25), transform.position.y, Random.Range(-25, 25))
                );

                break;

            case (int)State.Dormant:
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

            case (int)State.Activating:
                // Loop the chase audio.
                audioSource.PlayOneShot(AudioClipActivate, Config.LockerVolumeAdjustment.Value);

                animationController.SetTrigger("Activate");

                break;

            case (int)State.Chasing:
                // Initiate moving to our destination.
                if (IsServer && agent.isOnNavMesh)
                    SetDestinationToPosition(targetPosition, true);

                agent.speed = chaseMovementSpeed;

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

            case (int)State.Reactivating:
            case (int)State.Resetting:
            case (int)State.Consuming:
                // Set the agent's speed to zero to stop.
                agent.speed = 0;

                // Reset rotation speed value.
                currentRotationSpeed = 0;

                // Stop the previous looping chase audio.
                audioSource.Stop();
                audioSource.loop = false;

                // Tilt the Locker forward on stopping.
                transform.rotation = transform.rotation * Quaternion.Euler(Vector3.forward * 10f);

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
                        Utilities.ApplyLocalPlayerScreenshake(transform.position, 4, 7f, false);

                        // Additionally jump in fear level if the player had a close encounter.
                        if (distance < 4f)
                        {
                            if (currentBehaviourStateIndex == (int)State.Consuming)
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

                if (currentBehaviourStateIndex == (int)State.Consuming) // Activate the consume specific effects.
                {
                    // Play the consuming sound effect.
                    audioSource.PlayOneShot(AudioClipConsume, Config.LockerVolumeAdjustment.Value);
                }
                else if (currentBehaviourStateIndex == (int)State.Reactivating)
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
                    audioSource.PlayOneShot(AudioClipReset, Config.LockerVolumeAdjustment.Value);
                }

                break;

            default:
                break;
        }

        observedState = currentBehaviourStateIndex;
    }

    private bool IsLocalPlayerClosestWithLight()
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
                currentBehaviourStateIndex == (int)State.Dormant
                || currentBehaviourStateIndex == (int)State.Debug
                || currentBehaviourStateIndex == (int)State.Chasing
                    && clientId == targetPlayer.playerClientId
                || currentBehaviourStateIndex == (int)State.Reactivating
            )
            {
                // Set the attack destination.
                targetPosition = position;

                // Set the target rotation. Make sure to not change the elevation for the rotation as to avoid sloping.
                targetRotation = Quaternion.LookRotation(
                    new Vector3(targetPosition.x, transform.position.y, targetPosition.z)
                        - transform.position
                );

                // Rotate an additional 90 degree offset.
                targetRotation *= Quaternion.Euler(Vector3.up * 90);

                // Syncronize the last target time.
                lastTargetTime = Time.time;

                // Store the current chase target identifier.
                targetPlayer = StartOfRound.Instance.allPlayerScripts[clientId];

                // Only allowing targeting during the debug or dormant state.
                if (
                    currentBehaviourStateIndex == (int)State.Dormant
                    || currentBehaviourStateIndex == (int)State.Debug
                )
                {
                    // Activate the enemy.
                    SwitchState(State.Activating);
                }
                else if (
                    currentBehaviourStateIndex == (int)State.Chasing
                    || currentBehaviourStateIndex == (int)State.Reactivating
                )
                {
                    // Make sure we go into another chase from the reactivation state.
                    if (currentBehaviourStateIndex == (int)State.Reactivating)
                    {
                        // Update the nav mesh destination if we're the host.
                        if (IsServer && agent.isOnNavMesh)
                        {
                            SetDestinationToPosition(targetPosition, true);
                        }

                        SwitchState(State.Chasing);
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
        if (IsServer && agent.isOnNavMesh)
        {
            SetDestinationToPosition(targetPosition, true);
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
                new Vector3(
                    closestPlayer.transform.position.x,
                    transform.position.y,
                    closestPlayer.transform.position.z
                ) - transform.position
            );

            // Rotate by 90 degrees so we're facing the right way.
            targetRotation *= Quaternion.Euler(Vector3.up * 90);
        }

        SwitchState(State.Reactivating);
    }

    [ServerRpc(RequireOwnership = false)]
    public void ConsumeServerRpc(ulong clientid)
    {
        // Stop movement.
        targetPosition = transform.position;

        // Update the nav mesh destination if we're the host.
        if (IsServer && agent.isOnNavMesh)
        {
            SetDestinationToPosition(targetPosition);
        }

        ConsumeClientRpc(clientid);
    }

    [ClientRpc]
    public void ConsumeClientRpc(ulong id)
    {
        // Kill the player. Need to wait for different actions for this to work.
        ((MonoBehaviour)this).StartCoroutine(KillPlayer(id));

        SwitchState(State.Consuming);
    }

    [ServerRpc(RequireOwnership = false)]
    public void ResetServerRpc()
    {
        // Update the nav mesh destination if we're the host.
        if (IsServer && agent.isOnNavMesh)
        {
            // Stop movement.
            targetPosition = transform.position;

            SetDestinationToPosition(targetPosition);
        }

        ResetClientRpc();
    }

    [ClientRpc]
    public void ResetClientRpc()
    {
        SwitchState(State.Resetting);
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

    public IEnumerator KillPlayer(ulong id)
    {
        PlayerControllerB player = StartOfRound.Instance.allPlayerScripts[id];
        if (player != null)
        {
            // Apply heavy bleeding.
            player.bleedingHeavily = true;

            // Delay for bleeding.
            yield return new WaitForSeconds(0.1f);

            // Kill the player.
            player.KillPlayer(Vector3.zero, true, CauseOfDeath.Crushing, 1);

            // Attach the body to the Locker!
            // First we need to get the start time for the kill to then remove the body later.
            float startTime = Time.timeSinceLevelLoad;

            // Wait until the body is available or it's been 3 seconds to fail out.
            yield return new WaitUntil(
                () => player.deadBody != null || Time.timeSinceLevelLoad - startTime > 3f
            );

            if (player.deadBody != null)
            {
                player.deadBody.attachedTo = eye.transform;
                player.deadBody.attachedLimb = player.deadBody.bodyParts[5];
                player.deadBody.matchPositionExactly = true;
            }

            // Delay for removing/disabling the body.
            yield return new WaitUntil(
                () => Time.timeSinceLevelLoad - startTime > consumeDuration * 0.75
            );

            if (player.deadBody != null)
            {
                player.deadBody.attachedTo = null;
                player.deadBody.attachedLimb = null;
                player.deadBody.matchPositionExactly = false;

                // Disable the body.
                player.deadBody.gameObject.SetActive(false);

                player.deadBody = null;
            }
        }

        yield break;
    }

    public void PlayerScan(PlayerControllerB player)
    {
        if (
            currentBehaviourStateIndex == (int)State.Dormant
            || currentBehaviourStateIndex == (int)State.Debug
            || currentBehaviourStateIndex == (int)State.Chasing
        )
        {
            float distance = Vector3.Distance(transform.position, player.transform.position);

            // Disallow scanning on top of the Locker triggering it.
            if (
                distance < 2
                && Mathf.Abs(player.transform.position.y - transform.position.y) + 2 > 2
            )
                return;

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
